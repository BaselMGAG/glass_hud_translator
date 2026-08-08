using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Glossary;
using GlassHudTranslator.Core.Ocr;
using GlassHudTranslator.Core.Pipeline;
using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Text;
using GlassHudTranslator.Core.Translation;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>Returns each scripted reading in turn; OCR is not under test here.</summary>
internal sealed class ScriptedOcr : IOcrEngine
{
    private readonly Queue<OcrResult> _script = new();

    public string Name => "scripted";

    public ScriptedOcr Reads(string text, float confidence = 95f, int rejected = 0)
    {
        _script.Enqueue(new OcrResult(text, confidence,
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, rejected));
        return this;
    }

    public Task<OcrResult> RecognizeAsync(Frame frame, CancellationToken ct) =>
        Task.FromResult(_script.Count > 0 ? _script.Dequeue() : OcrResult.Empty);

    public void Dispose() { }
}

/// <summary>In-memory cache that counts lookups, because "the guard ran before the cache" is a claim about counters.</summary>
internal sealed class MemoryCache : ITranslationCache
{
    public Dictionary<string, CachedTranslation> Rows { get; } = [];

    public int Lookups { get; private set; }

    public Task<CachedTranslation?> TryGetAsync(string key, CancellationToken ct)
    {
        Lookups++;
        return Task.FromResult(Rows.TryGetValue(key, out var row) ? row : null);
    }

    public Task PutAsync(CachedTranslation entry, CancellationToken ct)
    {
        Rows[entry.Key] = entry;
        return Task.CompletedTask;
    }

    public Task PutOverrideAsync(string key, string source, string arabic, CancellationToken ct)
    {
        Rows[key] = new CachedTranslation(key, source, arabic, "manual", "manual", true,
            DateTimeOffset.UtcNow, 0);
        return Task.CompletedTask;
    }

    public Task<CacheStats> GetStatsAsync(CancellationToken ct) =>
        Task.FromResult(new CacheStats(Rows.Count, 0, 0, Lookups));
}

public class PipelineContextTests
{
    private static readonly Frame AnyFrame = new FrameBuilder(8, 8, new Rgb(0, 0, 0)).Build();

    private static async Task<PipelineOutcome> Process(
        TranslationPipeline pipeline, ScriptedOcr ocr, string line)
    {
        ocr.Reads(line);
        return await pipeline.ProcessAsync(AnyFrame);
    }

    [Fact]
    public async Task ContextRollsOldestFirstAndCapsAtThreeLines()
    {
        var provider = new FakeProvider("fake");
        for (var i = 0; i < 5; i++) provider.Returns("ترجمة");
        var ocr = new ScriptedOcr();
        var pipeline = new TranslationPipeline(
            ocr, new MemoryCache(), new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]));

        string[] lines =
        [
            "The first line spoken.",
            "The second line spoken.",
            "The third line spoken.",
            "The fourth line spoken.",
            "The fifth line spoken.",
        ];
        foreach (var line in lines) await Process(pipeline, ocr, line);

        // The fifth request carries lines two to four - three lines, oldest first, and the first
        // line has aged out of the window rather than the newest being withheld.
        var last = provider.Requests[^1];
        Assert.Equal(
            ["The second line spoken.", "The third line spoken.", "The fourth line spoken."],
            last.ContextLines);
    }

    [Fact]
    public async Task ACacheHitStillEntersContext()
    {
        var provider = new FakeProvider("fake").Returns("ترجمة");
        var cache = new MemoryCache();
        var ocr = new ScriptedOcr();
        var pipeline = new TranslationPipeline(
            ocr, cache, new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]));

        var body = "Come with me.";
        cache.Rows[CacheKey.For(body, ArabicRegister.ModernStandard)] =
            new CachedTranslation("k", body, "تعال معي.", "gemini", "m", false, DateTimeOffset.UtcNow, 0);

        var hit = await Process(pipeline, ocr, body);
        Assert.True(hit.Result!.FromCache);

        await Process(pipeline, ocr, "And after that, the aetheryte.");

        // The player read the cached line either way; the next line's scene includes it.
        Assert.Equal(["Come with me."], provider.Requests[^1].ContextLines);
    }

    [Fact]
    public async Task ReReadingTheSameLineDoesNotFillTheWindowWithIt()
    {
        var provider = new FakeProvider("fake").Returns("أ").Returns("ب");
        var cache = new MemoryCache();
        var ocr = new ScriptedOcr();
        var pipeline = new TranslationPipeline(
            ocr, cache, new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]));

        // Pressing the hotkey three times on one dialogue box: a translation, then two cache hits.
        var repeated = "Come with me to the aetheryte.";
        await Process(pipeline, ocr, repeated);
        await Process(pipeline, ocr, repeated);
        await Process(pipeline, ocr, repeated);
        await Process(pipeline, ocr, "A brand new line of dialogue.");

        // One entry, not three. Three copies would evict the real conversation and tell the model
        // the previous three lines were the same sentence.
        Assert.Equal([repeated], provider.Requests[^1].ContextLines);
    }

    [Fact]
    public async Task AFallbackDoesNotEnterContext()
    {
        var provider = new FakeProvider("fake");
        provider.Fails(ProviderFailure.Fatal);   // first line: every lane exhausted -> English fallback
        provider.Returns("ترجمة");               // second line translates
        var ocr = new ScriptedOcr();
        var pipeline = new TranslationPipeline(
            ocr, new MemoryCache(), new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]));

        var fallback = await Process(pipeline, ocr, "This line never translated.");
        Assert.True(fallback.Result!.IsFallbackEnglish);

        await Process(pipeline, ocr, "This line did.");

        // An untranslated line is not context: the model would be told the previous line said
        // something the player never saw in Arabic.
        Assert.Empty(provider.Requests[^1].ContextLines);
    }

    [Fact]
    public async Task ContextExpiresWhenTheDialogueHasMovedOn()
    {
        var clock = new FakeTimeProvider();
        var provider = new FakeProvider("fake").Returns("أ").Returns("ب").Returns("ج");
        var ocr = new ScriptedOcr();
        var pipeline = new TranslationPipeline(
            ocr, new MemoryCache(), new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]), clock: clock);

        await Process(pipeline, ocr, "A line from the last conversation.");

        clock.Advance(TimeSpan.FromSeconds(30));
        await Process(pipeline, ocr, "Still the same conversation.");
        Assert.Equal(["A line from the last conversation."], provider.Requests[^1].ContextLines);

        clock.Advance(TimeSpan.FromMinutes(5));
        await Process(pipeline, ocr, "A different scene entirely.");

        // Five minutes of silence is a different scene. Nothing ever called ResetContext between
        // conversations, so without the TTL this stale line would steer pronouns forever.
        Assert.Empty(provider.Requests[^1].ContextLines);
    }

    [Fact]
    public async Task SwitchingProfileClearsContext()
    {
        var provider = new FakeProvider("fake").Returns("أ").Returns("ب");
        var ocr = new ScriptedOcr();
        var pipeline = new TranslationPipeline(
            ocr, new MemoryCache(), new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]));

        await Process(pipeline, ocr, "A line from the first game.");
        pipeline.UseProfile("Another Game", null, new GlossaryMatcher(GlossaryStore.Empty),
            OcrCorrections.Empty);
        await Process(pipeline, ocr, "A line from the second game.");

        Assert.Empty(provider.Requests[^1].ContextLines);
    }
}

public class PipelineGuardTests
{
    private static readonly Frame AnyFrame = new FrameBuilder(8, 8, new Rgb(0, 0, 0)).Build();

    [Fact]
    public async Task AShortBodyCostsNothingAtAll()
    {
        var provider = new FakeProvider("fake").Returns("ترجمة");
        var cache = new MemoryCache();
        var ocr = new ScriptedOcr().Reads("OK").Reads("A real line of dialogue.");
        var pipeline = new TranslationPipeline(
            ocr, cache, new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]))
        {
            MinimumBodyCharacters = 4,
        };

        var outcome = await pipeline.ProcessAsync(AnyFrame);

        // The whole point of moving the guard into the pipeline: the discarded line must not have
        // touched the cache (which would distort hit rate), the router (which would spend quota),
        // or the context (which would feed "OK" to the next real line as its predecessor).
        Assert.Null(outcome.Result);
        Assert.False(outcome.ProducedText);
        Assert.Equal(0, cache.Lookups);
        Assert.Empty(provider.Calls);

        await pipeline.ProcessAsync(AnyFrame);
        Assert.Empty(provider.Requests[^1].ContextLines);
    }

    [Fact]
    public async Task AnEmptyFrameProducesNoResultRatherThanAFakeOne()
    {
        var provider = new FakeProvider("fake");
        var ocr = new ScriptedOcr().Reads("");
        var pipeline = new TranslationPipeline(
            ocr, new MemoryCache(), new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]));

        var outcome = await pipeline.ProcessAsync(AnyFrame);

        Assert.Null(outcome.Result);
        Assert.False(outcome.ProducedText);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task TheOutcomeCarriesItsProvenance()
    {
        var provider = new FakeProvider("fake").Returns("ترجمة");
        var ocr = new ScriptedOcr().Reads("A line worth translating.", confidence: 88f, rejected: 2);
        var pipeline = new TranslationPipeline(
            ocr, new MemoryCache(), new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]));

        var outcome = await pipeline.ProcessAsync(AnyFrame, "dialogue", SourceKind.RecordedFrame);

        Assert.Equal("dialogue", outcome.RegionKey);
        Assert.Equal(SourceKind.RecordedFrame, outcome.Source);
        Assert.Equal(2, outcome.RejectedWordCount);
    }

    [Fact]
    public async Task TheLogRecordsWhichGameAndRegionProducedEachRow()
    {
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var provider = new FakeProvider("fake").Returns("ترجمة");
        var ocr = new ScriptedOcr().Reads("A line worth translating.");
        var pipeline = new TranslationPipeline(
            ocr, new MemoryCache(), new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]), log: new TranslationLog(db))
        {
            GameName = "Final Fantasy XIV",
        };

        await pipeline.ProcessAsync(AnyFrame, "dialogue", SourceKind.Screen);

        Assert.Equal("Final Fantasy XIV", await db.ScalarAsync(
            "SELECT game FROM translation_log LIMIT 1;", CancellationToken.None));
        Assert.Equal("dialogue", await db.ScalarAsync(
            "SELECT region FROM translation_log LIMIT 1;", CancellationToken.None));
    }

    [Fact]
    public async Task ALineIsFiledUnderTheGameItWasCapturedFromEvenIfTheProfileChangesMidRequest()
    {
        await using var db = await AppDatabase.OpenInMemoryAsync();

        // A profile switch lands while the request is in flight - the UI thread calling
        // UseProfile while the auto-watch worker waits on a lane, which can take seconds.
        TranslationPipeline? pipeline = null;
        var provider = new SwitchingProvider(() =>
            pipeline!.UseProfile("Baldur's Gate 3", null,
                new GlossaryMatcher(GlossaryStore.Empty), OcrCorrections.Empty));

        pipeline = new TranslationPipeline(
            new ScriptedOcr().Reads("A line of Eorzean dialogue."),
            new MemoryCache(), new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]), log: new TranslationLog(db))
        {
            GameName = "Final Fantasy XIV",
        };

        await pipeline.ProcessAsync(AnyFrame, "dialogue", SourceKind.Screen);

        // The row must name the game the frame came from. Filing it under the game the user
        // switched to mid-flight corrupts the per-game statistics the column exists for.
        Assert.Equal("Final Fantasy XIV", await db.ScalarAsync(
            "SELECT game FROM translation_log LIMIT 1;", CancellationToken.None));
    }
}

/// <summary>Runs a side effect while the "provider call" is in flight, to interleave deterministically.</summary>
internal sealed class SwitchingProvider(Action duringCall) : ITranslationProvider
{
    public string Name => "switching";

    public IReadOnlyList<string> Models { get; } = ["m1"];

    public Task<string> TranslateAsync(TranslationRequest request, string model, CancellationToken ct)
    {
        duringCall();
        return Task.FromResult("ترجمة");
    }
}
