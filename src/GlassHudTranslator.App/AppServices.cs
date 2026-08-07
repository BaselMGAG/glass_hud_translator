using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Glossary;
using GlassHudTranslator.Core.Ocr;
using GlassHudTranslator.Core.Pipeline;
using GlassHudTranslator.Core.Profiles;
using GlassHudTranslator.Core.Platform;
using GlassHudTranslator.Core.Regions;
using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Text;
using GlassHudTranslator.Core.Translation;

namespace GlassHudTranslator.App;

/// <summary>
/// Composition root. Builds the same <see cref="TranslationPipeline"/> that tools/Replay drives, so
/// what was debugged headlessly on the Mac is what runs behind the overlay.
/// </summary>
public sealed class AppServices : IAsyncDisposable
{
    private readonly HttpClient _http;

    private AppServices(
        AppDatabase db, HttpClient http, SqliteTranslationCache cache, TranslationLog log,
        QuotaLedger quota, RegionProfileStore regions, ISecretStore secrets, ModelsConfig models,
        GlossaryStore glossary, OcrCorrections corrections, IOcrEngine ocr, IHotkeyService hotkeys,
        TranslationPipeline pipeline, List<string> routerLog, IReadOnlyList<string> laneNames,
        GameProfile profile, IReadOnlyList<string> availableProfiles)
    {
        Db = db;
        _http = http;
        Cache = cache;
        Log = log;
        Quota = quota;
        Regions = regions;
        Secrets = secrets;
        Models = models;
        Glossary = glossary;
        Corrections = corrections;
        Ocr = ocr;
        Hotkeys = hotkeys;
        Pipeline = pipeline;
        RouterLog = routerLog;
        LaneNames = laneNames;
        Profile = profile;
        AvailableProfiles = availableProfiles;
    }

    public AppDatabase Db { get; }
    public SqliteTranslationCache Cache { get; }
    public TranslationLog Log { get; }
    public QuotaLedger Quota { get; }
    public RegionProfileStore Regions { get; }
    public ISecretStore Secrets { get; }
    public ModelsConfig Models { get; }
    public GlossaryStore Glossary { get; }
    public OcrCorrections Corrections { get; }
    public IOcrEngine Ocr { get; }
    public IHotkeyService Hotkeys { get; }
    public TranslationPipeline Pipeline { get; }

    /// <summary>Router diagnostics, surfaced in Settings - a deleted model must not fail silently.</summary>
    public List<string> RouterLog { get; }

    public IReadOnlyList<string> LaneNames { get; }

    /// <summary>
    /// Shared with the update check, which sets its own headers per request and caps itself with a
    /// linked token rather than inheriting this client's translation-length timeout.
    /// </summary>
    public HttpClient Http => _http;

    /// <summary>The active game profile - regions, glossary, OCR fixes and prompt voice.</summary>
    public GameProfile Profile { get; private set; }

    /// <summary>Refreshed in place, so adding a game does not need a restart to appear.</summary>
    public IReadOnlyList<string> AvailableProfiles { get; private set; }

    /// <summary>
    /// Bundled profiles plus the user's own. Owns creating, editing and deleting them, and the rule
    /// that anything the user writes goes under their data directory rather than the app folder,
    /// which an update replaces wholesale.
    /// </summary>
    public ProfileLibrary Profiles { get; private init; } = null!;

    /// <summary>Re-reads both profile roots. Call after the profile editor writes anything.</summary>
    public IReadOnlyList<string> RefreshProfiles() => AvailableProfiles = Profiles.Discover();

    /// <summary>
    /// Loads a different game profile and applies it immediately - glossary, OCR corrections,
    /// prompt voice and the window it measures capture regions against. Capture regions themselves
    /// are stored per profile, so the previous profile's rectangle is waiting when you switch back.
    /// </summary>
    public bool SwitchProfile(string id)
    {
        if (id == Profile.Id) return false;

        ReloadProfile(id);
        return true;
    }

    /// <summary>
    /// Loads a profile unconditionally, including one already active. Needed after the editor
    /// writes: the id is unchanged but the name, voice, glossary and OCR fixes are not, and
    /// <see cref="SwitchProfile"/> deliberately short-circuits on an unchanged id.
    /// </summary>
    public void ReloadProfile(string id)
    {
        Profile = Profiles.LoadOrFallback(id);
        Pipeline.UseProfile(Profile.DisplayName, Profile.StyleHint,
            new GlossaryMatcher(Profile.Glossary), Profile.Corrections);
    }

    public static async Task<AppServices> CreateAsync(
        string dataDirectory, string profilesDirectory, string? preferredProfileId,
        bool useStubProvider, CancellationToken ct = default)
    {
        var db = await AppDatabase.OpenAsync(AppPaths.Database, ct).ConfigureAwait(false);
        var cache = new SqliteTranslationCache(db);
        var log = new TranslationLog(db);
        var quota = new QuotaLedger(db);
        var regions = new RegionProfileStore(db);

        var secrets = PlatformServices.CreateSecretStore();
        var models = ModelsConfig.Load(Path.Combine(dataDirectory, "models.json"));
        var profiles = new ProfileLibrary(profilesDirectory, AppPaths.UserProfiles);
        var profile = profiles.LoadOrFallback(preferredProfileId);
        var glossary = profile.Glossary;
        var corrections = profile.Corrections;
        var ocr = PlatformServices.CreateOcrEngine(profile.SourceLanguage);
        var hotkeys = PlatformServices.CreateHotkeyService();
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var routerLog = new List<string>();
        var lanes = BuildLanes(models, secrets, http, useStubProvider);

        var router = new ProviderRouter(lanes, log: message =>
        {
            routerLog.Add($"{DateTimeOffset.Now:HH:mm:ss}  {message}");
            if (routerLog.Count > 200) routerLog.RemoveAt(0);
            Console.WriteLine(message);
        });
        router.ProviderUsed += (name, token) => quota.RecordAsync(name, token);

        var pipeline = new TranslationPipeline(ocr, cache, new GlossaryMatcher(glossary), router,
            corrections, log)
        {
            GameName = profile.DisplayName,
            StyleHint = profile.StyleHint,
        };

        return new AppServices(db, http, cache, log, quota, regions, secrets, models, glossary,
            corrections, ocr, hotkeys, pipeline, routerLog,
            lanes.Select(l => l.Provider.Name).ToList(), profile,
            profiles.Discover())
        {
            Profiles = profiles,
        };
    }

    private static List<(ITranslationProvider Provider, int Rpm)> BuildLanes(
        ModelsConfig models, ISecretStore secrets, HttpClient http, bool useStubProvider)
    {
        if (useStubProvider) return [(new StubProvider(), 600)];

        // devOnly lanes (Ollama) are excluded on Windows: the target PC cannot run a local model
        // alongside the game, and the shipped app must not wait on a localhost port that is not
        // there (brief 2.7).
        return models.Enabled(includeDevOnly: !PlatformServices.IsWindows)
            .Select(config => (ProviderFactory.Create(config, http, secrets), config.Rpm))
            .ToList();
    }

    public async ValueTask DisposeAsync()
    {
        Hotkeys.Dispose();
        Ocr.Dispose();
        _http.Dispose();
        await Db.DisposeAsync().ConfigureAwait(false);
    }
}
