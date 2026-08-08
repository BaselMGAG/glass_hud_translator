using System.Diagnostics;
using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Glossary;
using GlassHudTranslator.Core.Ocr;
using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Text;
using GlassHudTranslator.Core.Translation;

namespace GlassHudTranslator.Core.Pipeline;

/// <summary>
/// Where a frame came from. Two members because two sources exist - the live screen and the
/// recorded frames Replay pushes through. Clipboard, drag-and-drop and audio are all planned, and
/// each adds its member in the session that adds its source, not before.
/// </summary>
public enum SourceKind
{
    Screen,
    RecordedFrame,
}

/// <summary>
/// <paramref name="Result"/> is null when no translation was attempted - the region was empty, or
/// the body was under the minimum length. That used to be a fabricated fallback result, which read
/// as "translation failed" in code that inspected it; nothing failing is a different fact from
/// nothing being tried.
/// </summary>
public sealed record PipelineOutcome(
    string RawOcr,
    string Normalized,
    string? Speaker,
    string Body,
    IReadOnlyList<GlossaryTerm> GlossaryHits,
    TranslationResult? Result,
    float OcrConfidence,
    TimeSpan Total,
    string? RegionKey = null,
    SourceKind Source = SourceKind.Screen,
    int RejectedWordCount = 0)
{
    public bool ProducedText => Result is not null && !string.IsNullOrWhiteSpace(Result.Text);
}

/// <summary>
/// One captured frame in, one translated line out. Shared by the overlay and by tools/Replay so
/// that what the harness exercises on the Mac is the same code that runs against the game.
/// </summary>
public sealed class TranslationPipeline(
    IOcrEngine ocr,
    ITranslationCache cache,
    GlossaryMatcher glossary,
    ProviderRouter router,
    OcrCorrections? corrections = null,
    TranslationLog? log = null,
    TimeProvider? clock = null)
{
    /// <summary>
    /// How many previous lines ride along as context. Three, and the number is a policy rather
    /// than a tuning knob: cached translations replay with NO context at all - the cache key
    /// deliberately hashes the body alone, because hit rate is the entire quota argument - so the
    /// window must stay small enough that a context-free replay of the same line is still an
    /// acceptable translation. Widen this and cache hits do not get better, they get relatively
    /// worse.
    /// </summary>
    public const int ContextWindow = 3;

    private readonly Queue<string> _context = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private DateTimeOffset _contextAt;

    private GlossaryMatcher _glossary = glossary;
    private OcrCorrections _corrections = corrections ?? OcrCorrections.Empty;

    public ArabicRegister Register { get; set; } = ArabicRegister.ModernStandard;

    /// <summary>From the active game profile. Both go into the system prompt.</summary>
    public string GameName { get; set; } = "a video game";

    public string? StyleHint { get; set; }

    /// <summary>
    /// Context older than this is a different scene. Nothing ever called ResetContext between
    /// conversations - the "cleared when the player leaves a conversation" promise had no caller -
    /// so the previous line from a chat an hour ago was quietly steering pronouns in the next one.
    /// Two minutes covers a slow reader on a long dialogue box and not much else.
    /// </summary>
    public TimeSpan ContextTtl { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Bodies shorter than this are not dialogue - a stray glyph, a UI border read as a character.
    /// The check lives here, before the cache and the router, because it used to live in the App
    /// after ProcessAsync returned: the "guard against burning a request" ran after the request
    /// had been burned, and the discarded line had already been cached and pushed into context.
    /// </summary>
    public int MinimumBodyCharacters { get; set; }

    /// <summary>
    /// Swaps everything a game profile owns, without rebuilding the pipeline. Switching between a
    /// game and the desktop is a thing people do several times an hour, and making that a restart
    /// would be enough friction that they simply would not bother.
    /// </summary>
    public void UseProfile(string gameName, string? styleHint, GlossaryMatcher matcher, OcrCorrections ocrCorrections)
    {
        GameName = gameName;
        StyleHint = styleHint;
        _glossary = matcher;
        _corrections = ocrCorrections;
        ResetContext();
    }

    /// <summary>Cleared on profile switch, and by the TTL when the dialogue has moved on.</summary>
    public void ResetContext()
    {
        lock (_context) _context.Clear();
    }

    public async Task<PipelineOutcome> ProcessAsync(
        Frame frame,
        string? regionKey = null,
        SourceKind source = SourceKind.Screen,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var started = Stopwatch.GetTimestamp();
        var requestedAt = DateTimeOffset.UtcNow;

        // Read once, at capture time, and carried through - not re-read at log time. UseProfile
        // runs on the UI thread and can land mid-request: the auto-watch worker sits inside
        // router.TranslateAsync for up to four seconds a lane, and a profile switch during that
        // window would otherwise file an FFXIV line under whichever game the user just picked.
        // regionKey is already a parameter for the same reason; this closes the other half.
        var game = GameName;
        var styleHint = StyleHint;

        var recognised = await ocr.RecognizeAsync(frame, ct).ConfigureAwait(false);
        var normalized = TextNormalizer.Normalize(recognised.RawText, _corrections);
        var (speaker, body) = DialogueParser.Parse(normalized);

        ExpireStaleContext();

        // Nothing to translate. Before the cache lookup as well as before the router: a too-short
        // body must not bump the lookup counters, must not be cached, and must not enter context.
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length < MinimumBodyCharacters)
        {
            return new PipelineOutcome(recognised.RawText, normalized, speaker, body, [], null,
                recognised.Confidence, Stopwatch.GetElapsedTime(started),
                regionKey, source, recognised.RejectedWordCount);
        }

        var key = CacheKey.For(body, Register);
        var cached = await cache.TryGetAsync(key, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            var hit = new TranslationResult(cached.Arabic, ProviderNames.Cache, cached.Model, true,
                Stopwatch.GetElapsedTime(started), TranslationLogOutcomes.Cached);
            PushContext(body);
            await LogAsync(recognised, normalized, speaker, hit, game, regionKey, ct).ConfigureAwait(false);

            return new PipelineOutcome(recognised.RawText, normalized, speaker, body, [], hit,
                recognised.Confidence, Stopwatch.GetElapsedTime(started),
                regionKey, source, recognised.RejectedWordCount);
        }

        var hits = _glossary.Match(body);
        var result = await router.TranslateAsync(
            new TranslationRequest(body, speaker, hits, SnapshotContext(), Register, requestedAt,
                game, styleHint), ct)
            .ConfigureAwait(false);

        // Only cache a genuine translation. Caching the English fallback would poison the entry
        // permanently - the next lookup would hit and never retry the provider.
        if (result.Outcome == TranslationLogOutcomes.Ok)
        {
            await cache.PutAsync(new CachedTranslation(key, body, result.Text, result.Provider,
                result.Model, false, DateTimeOffset.UtcNow, 0), ct).ConfigureAwait(false);
            PushContext(body);
        }

        await LogAsync(recognised, normalized, speaker, result, game, regionKey, ct).ConfigureAwait(false);

        return new PipelineOutcome(recognised.RawText, normalized, speaker, body, hits, result,
            recognised.Confidence, Stopwatch.GetElapsedTime(started),
            regionKey, source, recognised.RejectedWordCount);
    }

    /// <summary>
    /// The English source enters context, never the Arabic: the next request's "previous lines"
    /// speak the language the line being translated does. Cache hits push too - the player read
    /// that line either way, and the scene the next line sits in does not care where its
    /// predecessor's translation came from.
    ///
    /// <para>
    /// All queue access is under a lock, and that is not caution for its own sake: the hotkey
    /// handler, the auto-watch worker thread and the Settings test button all reach this one
    /// pipeline, and the App's <c>_busy</c> flag is a plain bool that two threads can pass
    /// together - the test button does not consult it at all. When the context was a single
    /// string reference a race tore nothing; a <see cref="Queue{T}"/> mutated from two threads
    /// corrupts.
    /// </para>
    /// </summary>
    private void PushContext(string body)
    {
        lock (_context)
        {
            // Not the same line twice running. Pressing the hotkey again on the dialogue box
            // already on screen is ordinary - the overlay has cleared, or the player wants another
            // look - and each press used to enqueue another copy. Three presses filled the whole
            // window with one sentence, evicting the actual conversation and telling the model the
            // previous three lines were all identical. Auto-watch reaches this too: a cursor blink
            // changes the frame enough to pass change detection while the text is unchanged.
            if (_context.Count == 0 || _context.Last() != body)
                _context.Enqueue(body);

            while (_context.Count > ContextWindow) _context.Dequeue();
            _contextAt = _clock.GetUtcNow();
        }
    }

    private void ExpireStaleContext()
    {
        lock (_context)
        {
            if (_context.Count > 0 && _clock.GetUtcNow() - _contextAt > ContextTtl)
                _context.Clear();
        }
    }

    private string[] SnapshotContext()
    {
        lock (_context) return [.. _context];
    }

    private Task LogAsync(OcrResult recognised, string normalized, string? speaker,
        TranslationResult result, string game, string? regionKey, CancellationToken ct) =>
        log?.AppendAsync(new TranslationLogEntry(
            DateTimeOffset.UtcNow, recognised.RawText, normalized, speaker,
            result.Provider, result.Model, result.Text, result.Latency,
            result.FromCache, result.Outcome, game, regionKey), ct) ?? Task.CompletedTask;
}
