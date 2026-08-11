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

/// <summary>
/// The two features that let a user do something about a line rather than just watch it: phrases
/// that are never translated, and translating text the caller already has.
/// </summary>
public class PipelineRecourseTests
{
    private static readonly Frame AnyFrame = new FrameBuilder(8, 8, new Rgb(0, 0, 0)).Build();

    private static TranslationPipeline Build(
        ScriptedOcr ocr, MemoryCache cache, FakeProvider provider) =>
        new(ocr, cache, new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]));

    [Fact]
    public async Task AnIgnoredLineCostsNothingAtAll()
    {
        // The whole claim, and every clause of it matters. The guard sits ahead of the cache, so an
        // ignored line must not reach the provider (no request, no quota), must not be looked up
        // (the hit rate is a diagnostic and a line nobody asked about would skew it), and must not
        // be stored. Putting the check anywhere later would let each of those happen in turn and
        // suppress only the display - which is the exact defect the too-short guard already had
        // once, when it lived in the App and ran on the returned outcome.
        var ocr = new ScriptedOcr();
        var cache = new MemoryCache();
        var provider = new FakeProvider("fake").Returns("ترجمة");
        var pipeline = Build(ocr, cache, provider);
        pipeline.Ignored = new IgnoreList(["Press E to continue"]);

        ocr.Reads("Press E to continue");
        var outcome = await pipeline.ProcessAsync(AnyFrame);

        Assert.True(outcome.Ignored);
        Assert.Null(outcome.Result);
        Assert.Empty(provider.Requests);
        Assert.Equal(0, cache.Lookups);
        Assert.Empty(cache.Rows);
    }

    [Fact]
    public async Task AnIgnoredLineIsReportedSeparatelyFromAnEmptyOne()
    {
        // Both leave Result null, which is correct - nothing was attempted either way - but they
        // are different answers to somebody who pressed a key: one means "your rule caught this",
        // the other means "there was nothing there". Only the first should ever be reassuring.
        var ocr = new ScriptedOcr();
        var pipeline = Build(ocr, new MemoryCache(), new FakeProvider("fake").Returns("ترجمة"));
        pipeline.Ignored = new IgnoreList(["Open map"]);

        ocr.Reads("");
        Assert.False((await pipeline.ProcessAsync(AnyFrame)).Ignored);

        ocr.Reads("Open map");
        Assert.True((await pipeline.ProcessAsync(AnyFrame)).Ignored);
    }

    [Fact]
    public async Task AnIgnoredLineDoesNotEnterTheRollingContext()
    {
        // It was never on screen as far as the conversation is concerned, so quoting it back to the
        // model as a previous line would spend tokens describing a hotbar label as dialogue.
        var ocr = new ScriptedOcr();
        var provider = new FakeProvider("fake");
        provider.Returns("ترجمة");
        provider.Returns("ترجمة");
        var pipeline = Build(ocr, new MemoryCache(), provider);
        pipeline.Ignored = new IgnoreList(["Press E to continue"]);

        ocr.Reads("The Scions stand ready.");
        await pipeline.ProcessAsync(AnyFrame);

        ocr.Reads("Press E to continue");
        await pipeline.ProcessAsync(AnyFrame);

        ocr.Reads("We must reach Limsa before nightfall.");
        await pipeline.ProcessAsync(AnyFrame);

        Assert.Equal(["The Scions stand ready."], provider.Requests[^1].ContextLines);
    }

    [Fact]
    public async Task RetryBypassesTheCacheOrItWouldReturnTheAnswerBeingComplainedAbout()
    {
        // The line is in the cache BECAUSE it was translated once. A retry that consulted the cache
        // would hand back the same words instantly and forever, which is the one behaviour a retry
        // button must never have.
        var cache = new MemoryCache();
        var provider = new FakeProvider("fake").Returns("ترجمة ثانية");
        var pipeline = Build(new ScriptedOcr(), cache, provider);

        var body = "Come with me.";
        var key = CacheKey.For(body, ArabicRegister.ModernStandard);
        cache.Rows[key] = new CachedTranslation(key, body, "ترجمة أولى", "gemini", "m", false,
            DateTimeOffset.UtcNow, 0);

        var outcome = await pipeline.TranslateTextAsync(body, fresh: true);

        Assert.Equal("ترجمة ثانية", outcome.Result!.Text);
        Assert.False(outcome.Result.FromCache);
        Assert.Single(provider.Requests);

        // And the new answer replaces the old one, so the line the user paid to improve is the one
        // served from now on rather than being discarded the moment it leaves the screen.
        Assert.Equal("ترجمة ثانية", cache.Rows[key].Arabic);
    }

    [Fact]
    public async Task EditingTheTextIsANewLineSoTheCacheIsConsultedNormally()
    {
        // A user fixing a misread word to the spelling another line already uses should get that
        // line's answer for free. Only retry pays.
        var cache = new MemoryCache();
        var provider = new FakeProvider("fake").Returns("لا ينبغي طلب هذا");
        var pipeline = Build(new ScriptedOcr(), cache, provider);

        var corrected = "Come with me.";
        var key = CacheKey.For(corrected, ArabicRegister.ModernStandard);
        cache.Rows[key] = new CachedTranslation(key, corrected, "تعال معي.", "gemini", "m", false,
            DateTimeOffset.UtcNow, 0);

        var outcome = await pipeline.TranslateTextAsync(corrected);

        Assert.Equal("تعال معي.", outcome.Result!.Text);
        Assert.True(outcome.Result.FromCache);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task ARetryDoesNotBecomeTheLineLaterPollsAreComparedAgainst()
    {
        // Same lesson the snip taught. A retry is the line the user is already looking at, said
        // again - so recording it as "the last line shown" would make the very next poll of the
        // real dialogue box look like a repeat of it and suppress the thing being read.
        var ocr = new ScriptedOcr();
        var cache = new MemoryCache();
        var provider = new FakeProvider("fake");
        provider.Returns("ترجمة");
        provider.Returns("ترجمة");
        var pipeline = Build(ocr, cache, provider);

        ocr.Reads("The Scions stand ready.");
        await pipeline.ProcessAsync(AnyFrame, options: ProcessOptions.Polled);

        await pipeline.TranslateTextAsync("The Scions stand ready.", fresh: true);

        // The poll that follows reads the same pixels. It is a repeat of the ORIGINAL line, and it
        // must still be recognised as one - the retry must not have disturbed that reference.
        ocr.Reads("The Scions stand ready.");
        var poll = await pipeline.ProcessAsync(AnyFrame, options: ProcessOptions.Polled);

        Assert.True(poll.Repeat);
    }

    [Fact]
    public async Task RetryingWhenTheProviderFailsDoesNotCacheTheFallback()
    {
        // Same rule as the capture path: caching the English fallback would poison the row
        // permanently, and a retry is exactly when a user is most likely to hit a failing provider.
        var cache = new MemoryCache();
        var provider = new FakeProvider("fake").Fails(ProviderFailure.Transient, times: 6);
        var pipeline = Build(new ScriptedOcr(), cache, provider);

        await pipeline.TranslateTextAsync("Come with me.", fresh: true);

        Assert.Empty(cache.Rows);
    }
}
