using System.Diagnostics;
using GamingTranslatorGlassHUD.Core.Capture;
using GamingTranslatorGlassHUD.Core.Config;
using GamingTranslatorGlassHUD.Core.Diagnostics;
using GamingTranslatorGlassHUD.Core.Glossary;
using GamingTranslatorGlassHUD.Core.Ocr;
using GamingTranslatorGlassHUD.Core.Pipeline;
using GamingTranslatorGlassHUD.Core.Profiles;
using GamingTranslatorGlassHUD.Core.Storage;
using GamingTranslatorGlassHUD.Core.Text;
using GamingTranslatorGlassHUD.Core.Translation;

// Headless pipeline harness - the main development loop on macOS. Runs recorded (or synthetic)
// frames through exactly the same TranslationPipeline the overlay uses, and prints what each stage
// produced, so OCR, normalisation, caching, glossary matching and routing can all be debugged
// without Windows or a running game.

var options = ReplayOptions.Parse(args);
if (options is null) return 1;

var repoRoot = FindRepoRoot();
var dataDir = Path.Combine(repoRoot, "data");
var profilesDir = Path.Combine(repoRoot, "profiles");
var framesDir = options.FramesDirectory ?? Path.Combine(repoRoot, "test-frames");

if (options.GenerateFrames || !Directory.Exists(framesDir) || !Directory.EnumerateFiles(framesDir, "*.png").Any())
{
    Console.WriteLine($"Generating synthetic frames into {framesDir}");
    Console.WriteLine("  (scaffolding only - replace with real captures from your game, see CODING_SESSIONS.md Session 0)");
    var written = SyntheticFrames.WriteCorpus(framesDir);
    Console.WriteLine($"  wrote {written.Count} frames + expected.json\n");
    if (options.GenerateFrames) return 0;
}

var profile = GameProfileStore.LoadOrFallback(profilesDir, options.Profile);
var models = ModelsConfig.Load(Path.Combine(dataDir, "models.json"));

Console.WriteLine($"profile     {profile.Id} ({profile.DisplayName})");
Console.WriteLine($"glossary    {profile.Glossary.Count} terms");
Console.WriteLine($"corrections {profile.Corrections.Count} rules");
Console.WriteLine($"available   {string.Join(", ", GameProfileStore.Discover(profilesDir))}");

var tesseract = TesseractCliEngine.Locate();
if (tesseract is null)
{
    Console.Error.WriteLine("tesseract not found. Install it with: brew install tesseract");
    return 1;
}

Console.WriteLine($"ocr         {tesseract}");

await using var db = options.NoCache
    ? await AppDatabase.OpenInMemoryAsync()
    : await AppDatabase.OpenAsync(AppPaths.Database);

var cache = new SqliteTranslationCache(db);
var log = new TranslationLog(db);
var ledger = new QuotaLedger(db);

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
ISecretStore secrets = options.Provider == "stub" ? new InMemorySecretStore() : new DevPlainFileSecretStore();
var lanes = BuildLanes(options, models, http, secrets);

Console.WriteLine($"providers   {string.Join(" -> ", lanes.Select(l => l.Provider.Name))}");
Console.WriteLine($"cache       {(options.NoCache ? "disabled (in-memory)" : AppPaths.Database)}\n");

var router = new ProviderRouter(lanes, log: message => Console.WriteLine($"  ! {message}"));
router.ProviderUsed += (name, ct) => ledger.RecordAsync(name, ct);

using var frames = new FolderFrameSource(framesDir);
using var ocr = new TesseractCliEngine(new TesseractOptions
{
    ExecutablePath = tesseract,
    Language = profile.SourceLanguage,
});
var pipeline = new TranslationPipeline(ocr, cache, new GlossaryMatcher(profile.Glossary), router,
    profile.Corrections, log)
{
    GameName = profile.DisplayName,
    StyleHint = profile.StyleHint,
};

Console.WriteLine($"Replaying {frames.Count} frames from {framesDir}\n");

var skipped = 0;
var processed = 0;
var totalWatch = Stopwatch.StartNew();
FrameSignature? previous = null;

while (await frames.GetFrameAsync(CaptureRegion.Empty, CancellationToken.None) is { } frame)
{
    var label = frames.LastFrameLabel;
    var signature = FrameSignature.Compute(frame);

    // The same gate the live loop uses: unchanged frames never reach OCR.
    if (!options.NoSkip && signature.LooksIdenticalTo(previous))
    {
        skipped++;
        Console.WriteLine($"-- {label}\n   SKIPPED (unchanged, {signature.DifferenceCount(previous!)} cells differ)\n");
        continue;
    }

    previous = signature;
    processed++;

    var outcome = await pipeline.ProcessAsync(frame, CancellationToken.None);
    Print(label, outcome);
}

totalWatch.Stop();

var stats = await cache.GetStatsAsync(CancellationToken.None);
Console.WriteLine("------------------------------------------------------------");
Console.WriteLine($"processed {processed}   skipped {skipped}   wall {totalWatch.Elapsed.TotalSeconds:F1}s");
Console.WriteLine($"cache     {stats.Entries} entries, {stats.Hits}/{stats.Lookups} hits ({stats.HitRate:P0})");

foreach (var snapshot in await ledger.SnapshotAsync(
             models.Enabled(includeDevOnly: true).Select(p => (p.Name, p.Rpd)).ToList(),
             CancellationToken.None))
{
    if (snapshot.Used > 0) Console.WriteLine($"quota     {snapshot}");
}

return 0;

static void Print(string label, PipelineOutcome outcome)
{
    Console.WriteLine($"-- {label}");
    Console.WriteLine($"   ocr        {Flatten(outcome.RawOcr)}   [conf {outcome.OcrConfidence:F0}]");
    Console.WriteLine($"   normalized {Flatten(outcome.Normalized)}");
    if (outcome.Speaker is not null) Console.WriteLine($"   speaker    {outcome.Speaker}");
    if (outcome.GlossaryHits.Count > 0)
        Console.WriteLine($"   glossary   {string.Join(", ", outcome.GlossaryHits.Select(g => g.En))}");

    var result = outcome.Result;
    var source = result.FromCache ? "CACHE" : $"{result.Provider}/{result.Model}";
    Console.WriteLine($"   -> {result.Text}");
    Console.WriteLine($"   {source}   {result.Outcome}   {outcome.Total.TotalMilliseconds:F0}ms\n");
}

// Not "|": that character is itself an OCR artefact we care about, and using it as the line
// separator here made real pipes in the output impossible to read.
static string Flatten(string text) => text.Replace("\n", " ⏎ ");

static List<(ITranslationProvider Provider, int Rpm)> BuildLanes(
    ReplayOptions options, ModelsConfig models, HttpClient http, ISecretStore secrets)
{
    if (options.Provider == "stub")
        return [(new StubProvider(TimeSpan.FromMilliseconds(120)), 600)];

    var lanes = new List<(ITranslationProvider, int)>();
    foreach (var config in models.Enabled(includeDevOnly: true))
    {
        if (options.Provider != "all" && config.Name != options.Provider) continue;

        lanes.Add((new OpenAiCompatibleProvider(config, http,
            () => config.Secret is null ? null : secrets.Get(config.Secret)), config.Rpm));
    }

    if (lanes.Count == 0)
    {
        Console.Error.WriteLine($"No provider named '{options.Provider}' in models.json. Falling back to stub.");
        return [(new StubProvider(TimeSpan.FromMilliseconds(120)), 600)];
    }

    return lanes;
}

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir is not null && !File.Exists(Path.Combine(dir, "GamingTranslatorGlassHUD.slnx")))
        dir = Path.GetDirectoryName(dir);

    return dir ?? Directory.GetCurrentDirectory();
}

internal sealed record ReplayOptions
{
    public string Provider { get; init; } = "stub";
    public string? Profile { get; init; }
    public string? FramesDirectory { get; init; }
    public bool NoCache { get; init; }
    public bool NoSkip { get; init; }
    public bool GenerateFrames { get; init; }

    public static ReplayOptions? Parse(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("""
                replay - headless GamingTranslatorGlassHUD pipeline harness

                  --provider <name>   stub (default) | gemini | groq | ollama | all
                  --profile <id>      game profile from profiles/ (default: first found)
                  --frames <dir>      frame directory (default: test-frames/)
                  --no-cache          use a throwaway in-memory database
                  --no-skip           run OCR on every frame, ignoring change detection
                  --generate-frames   (re)write the synthetic corpus and exit
                """);
            return null;
        }

        return new ReplayOptions
        {
            Provider = Option(args, "--provider") ?? "stub",
            Profile = Option(args, "--profile"),
            FramesDirectory = Option(args, "--frames"),
            NoCache = args.Contains("--no-cache"),
            NoSkip = args.Contains("--no-skip"),
            GenerateFrames = args.Contains("--generate-frames"),
        };
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
