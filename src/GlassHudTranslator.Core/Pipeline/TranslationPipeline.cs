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
/// How one call through the pipeline differs from the ordinary one.
///
/// <para>
/// Two flags, and they are separate because the three callers want three combinations of them. A
/// hotkey press is a question the user asked and always deserves an answer, even a repeated one. A
/// poll is one of dozens a minute and must not pay for the same line twice. A snip is a question
/// about something outside the conversation entirely.
/// </para>
/// </summary>
public sealed record ProcessOptions
{
    /// <summary>
    /// A hotkey press, or the toolbar. Never suppressed — but it does become the line later polls
    /// are measured against, because it is now the line on the overlay.
    /// </summary>
    public static readonly ProcessOptions Manual = new();

    /// <summary>One tick of auto-watch. The only mode that can be dropped as a repeat.</summary>
    public static readonly ProcessOptions Polled = new() { SuppressRepeats = true };

    /// <summary>
    /// A one-shot translation of somewhere else on the screen — a snip. Outside the conversation
    /// in both directions: it must not be steered by the last three dialogue lines, and it must not
    /// steer the next one. It leaves no repeat reference either, or the dialogue the player is
    /// actually reading would be suppressed as a repeat of a menu tooltip.
    /// </summary>
    public static readonly ProcessOptions Isolated = new()
    {
        UseContext = false,
        RemembersLine = false,
    };

    /// <summary>
    /// Whether the rolling context is read into the prompt and written back afterwards. Both, or
    /// neither: reading without writing would let a snip inherit the conversation, and writing
    /// without reading would let it poison the next line while pretending not to be part of it.
    /// </summary>
    public bool UseContext { get; init; } = true;

    /// <summary>
    /// Whether a body within a few characters of the last one is dropped before it reaches the
    /// cache or a provider. See <see cref="Text.TextSimilarity"/> for why the frame-level gate
    /// cannot cover this.
    /// </summary>
    public bool SuppressRepeats { get; init; }

    /// <summary>
    /// Whether this call becomes the line the next poll is compared against.
    ///
    /// <para>
    /// Separate from <see cref="SuppressRepeats"/>, and a test is what forced them apart. A hotkey
    /// press must never be suppressed — it is a question and it gets an answer — but it does put a
    /// line on the overlay, so the poll half a second later, reading the same pixels with one comma
    /// misread, is a repeat of it. Folding the two flags into one meant that poll paid for a fresh
    /// translation: a different string is a different cache key, so the cache does not save it
    /// either. Being suppressible and being the reference are simply different questions.
    /// </para>
    /// </summary>
    public bool RemembersLine { get; init; } = true;
}

/// <summary>
/// <paramref name="Result"/> is null when no translation was attempted - the region was empty, the
/// body was under the minimum length, or it was the line already on the overlay. That used to be a
/// fabricated fallback result, which read as "translation failed" in code that inspected it;
/// nothing failing is a different fact from nothing being tried.
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
    int RejectedWordCount = 0,
    bool Repeat = false,
    bool Ignored = false)
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
    TimeProvider? clock = null,

    /// <summary>
    /// The optional second reader. Null is the normal state — the feature is off by default and
    /// every path through here works without it, which is what makes it safe to be optional rather
    /// than a mode the whole pipeline has to know about.
    /// </summary>
    IVisionReader? vision = null)
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

    /// <summary>
    /// The last body a repeat-suppressing caller translated. Guarded by the same lock as the
    /// context queue - not because it is a queue, but because the hotkey handler, the auto-watch
    /// worker and the Settings test button all reach this one pipeline from three threads, and a
    /// torn read here suppresses a real line or pays for a repeated one.
    /// </summary>
    private string? _lastBody;

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
    /// Lines the user has said never to translate. Sits beside <see cref="MinimumBodyCharacters"/>
    /// because it is the same kind of rule and belongs in the same place: ahead of the cache lookup,
    /// so an ignored line costs no request, no cache row, no context slot and no quota.
    ///
    /// <para>
    /// This one is quota before it is polish. A hotbar label or a «Press E to continue» drifting
    /// into the capture region is a DISTINCT string on most frames, so the cache cannot absorb it —
    /// every appearance is a fresh key and a fresh request. It is the first thing since the cache
    /// itself that reduces spend rather than merely not increasing it.
    /// </para>
    /// </summary>
    public IgnoreList Ignored { get; set; } = IgnoreList.Empty;

    /// <summary>
    /// Whether the overlay shows tashkeel. Off by default.
    ///
    /// <para>
    /// Applied on the way OUT, and the cache deliberately keeps whatever the provider actually
    /// said. That is what makes this switch instant and free in both directions: flipping it
    /// re-presents every line already cached, rather than being a setting that only affects
    /// sentences you have not read yet. Turning it ON cannot invent marks a cached answer never
    /// had - the prompt asked for none - but the next uncached line will carry them, and that
    /// asymmetry is worth far less than not re-translating the whole session to change a display
    /// preference.
    /// </para>
    /// </summary>
    public bool Diacritics { get; set; }

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
        ResetRepeatGuard();
    }

    /// <summary>
    /// Forgets the last line, so the very next capture counts as new whatever it says.
    ///
    /// <para>
    /// Separate from <see cref="ResetContext"/> because switching auto-watch on has to do this and
    /// must not do that: the player may have been reading manually a moment ago, and throwing away
    /// three lines of conversation is a real cost paid for nothing. Toggling off and on again, on
    /// the other hand, has to translate whatever is on screen — otherwise the toggle looks broken.
    /// </para>
    /// </summary>
    public void ResetRepeatGuard()
    {
        lock (_context) _lastBody = null;
    }

    public async Task<PipelineOutcome> ProcessAsync(
        Frame frame,
        string? regionKey = null,
        SourceKind source = SourceKind.Screen,
        ProcessOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var how = options ?? ProcessOptions.Manual;

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

        // A second reading, when the first one is not worth using and the user has paid in.
        //
        // Placed HERE, before the guards rather than after them, and that placement is what makes
        // the whole thing work. The flagship case - words seen, none legible - produces an EMPTY
        // RawText, so an escalation sitting below the empty-body guard could never see the one
        // frame the feature exists for. Hoisting it above that guard would ordinarily be reckless,
        // because an empty region is the commonest frame there is and paying for each one would
        // empty a day's free tier in half an hour. What makes it safe is that the policy itself
        // refuses that case: nothing rejected means nothing was there, and only a handful of
        // rejected words means somebody wrote a sentence here that could not be read.
        var escalation = EscalationPolicy.Decide(
            recognised, vision is not null, _memo.Remembers(recognised.RawText));

        var reading = escalation.Escalate
            ? await ReadAgainAsync(frame, recognised, ct).ConfigureAwait(false)
            : null;

        if (escalation.Why == EscalationReason.AlreadyAsked
            && _memo.Recall(recognised.RawText) is { } remembered)
        {
            // Bought once, reused for as long as the same unreadable line is on screen.
            recognised = recognised with { RawText = remembered };
        }
        else if (reading is { Verdict: not ReadingVerdict.Rejected, Text.Length: > 0 })
        {
            _memo.Remember(recognised.RawText, reading.Text);
            recognised = recognised with { RawText = reading.Text, Words = [] };
        }

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

        // Same place, same reasoning, and reported separately - "you told me to skip this" is a
        // different fact from "there was nothing there", and only one of them is worth a word on
        // the overlay when the user pressed a key expecting an answer.
        if (Ignored.ShouldSkip(body))
        {
            return new PipelineOutcome(recognised.RawText, normalized, speaker, body, [], null,
                recognised.Confidence, Stopwatch.GetElapsedTime(started),
                regionKey, source, recognised.RejectedWordCount, Ignored: true);
        }

        // The second net, and the same rule as the one above it: ahead of the cache, ahead of the
        // router, ahead of everything with a side effect. A line already on the overlay, read again
        // with a comma turned into a full stop, is not a new line - but it is a new cache key, so
        // by the time anything downstream could notice, the request has been sent and paid for.
        if (how.SuppressRepeats && IsRepeatOfLastBody(body))
        {
            return new PipelineOutcome(recognised.RawText, normalized, speaker, body, [], null,
                recognised.Confidence, Stopwatch.GetElapsedTime(started),
                regionKey, source, recognised.RejectedWordCount, Repeat: true);
        }

        var key = CacheKey.For(body, Register);
        var cached = await cache.TryGetAsync(key, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            var hit = new TranslationResult(cached.Arabic, ProviderNames.Cache, cached.Model, true,
                Stopwatch.GetElapsedTime(started), TranslationLogOutcomes.Cached);
            Remember(body, how);
            await LogAsync(recognised.RawText, normalized, speaker, hit, game, regionKey, ct).ConfigureAwait(false);

            return new PipelineOutcome(recognised.RawText, normalized, speaker, body, [], Present(hit),
                recognised.Confidence, Stopwatch.GetElapsedTime(started),
                regionKey, source, recognised.RejectedWordCount);
        }

        var hits = _glossary.Match(body);
        var result = await router.TranslateAsync(
            new TranslationRequest(body, speaker, hits,
                how.UseContext ? SnapshotContext() : [], Register, requestedAt,
                game, styleHint, Diacritics), ct)
            .ConfigureAwait(false);

        // Only cache a genuine translation. Caching the English fallback would poison the entry
        // permanently - the next lookup would hit and never retry the provider.
        if (result.Outcome == TranslationLogOutcomes.Ok)
        {
            // Deliberately NOT under ct. By this point a provider has answered, which means the
            // request has been sent, counted against a daily quota and paid for - and cancellation
            // is ordinary here: auto-watch switching off, the app closing, a snip superseded. Every
            // one of those used to throw between the answer arriving and the row being written, so
            // the app discarded something it had already bought and paid for it again the next time
            // the player read that line. Storing it is a local write measured in microseconds.
            await cache.PutAsync(new CachedTranslation(key, body, result.Text, result.Provider,
                result.Model, false, DateTimeOffset.UtcNow, 0), CancellationToken.None)
                .ConfigureAwait(false);

            Remember(body, how);
        }

        await LogAsync(recognised.RawText, normalized, speaker, result, game, regionKey, ct).ConfigureAwait(false);

        return new PipelineOutcome(recognised.RawText, normalized, speaker, body, hits, Present(result),
            recognised.Confidence, Stopwatch.GetElapsedTime(started),
            regionKey, source, recognised.RejectedWordCount);
    }

    /// <summary>
    /// Translates text the caller already has, with no capture and no OCR. Two features share it,
    /// and they are the same operation with one flag between them.
    ///
    /// <para>
    /// <b>Retry</b> passes the body unchanged with <paramref name="fresh"/> true. Bypassing the
    /// cache is the entire point: the line is in there precisely because it was translated once,
    /// so a retry that consulted the cache would return the same answer the user is complaining
    /// about, instantly, forever. It costs a request, deliberately and visibly.
    /// </para>
    ///
    /// <para>
    /// <b>Edit and retranslate</b> passes a corrected body. That is a different string, so it is a
    /// different key and the cache is consulted normally — a user fixing «Y&#39;shtola» to
    /// «Y&#39;shtola» the way another line already spells it should get the cached answer for free.
    /// </para>
    ///
    /// <para>
    /// Neither participates in the repeat guard or the rolling context by default. A retry is not a
    /// new line in the conversation, it is the same line again; recording it would make the poll
    /// that follows treat the real dialogue as a repeat of it, which is the snip lesson in a new
    /// place. The caller can ask for context with <paramref name="options"/> if it ever wants it.
    /// </para>
    /// </summary>
    public async Task<PipelineOutcome> TranslateTextAsync(
        string body,
        string? speaker = null,
        bool fresh = false,
        string? regionKey = null,
        SourceKind source = SourceKind.Screen,
        ProcessOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var how = options ?? ProcessOptions.Isolated;
        var started = Stopwatch.GetTimestamp();
        var requestedAt = DateTimeOffset.UtcNow;
        var game = GameName;
        var styleHint = StyleHint;

        body = body.Trim();
        var key = CacheKey.For(body, Register);

        if (!fresh)
        {
            var cached = await cache.TryGetAsync(key, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                var hit = new TranslationResult(cached.Arabic, ProviderNames.Cache, cached.Model,
                    true, Stopwatch.GetElapsedTime(started), TranslationLogOutcomes.Cached);

                Remember(body, how);
                await LogAsync(body, body, speaker, hit, game, regionKey, ct).ConfigureAwait(false);

                return new PipelineOutcome(body, body, speaker, body, [], Present(hit),
                    OcrConfidence: 0, Stopwatch.GetElapsedTime(started), regionKey, source);
            }
        }

        var hits = _glossary.Match(body);
        var result = await router.TranslateAsync(
            new TranslationRequest(body, speaker, hits,
                how.UseContext ? SnapshotContext() : [], Register, requestedAt,
                game, styleHint, Diacritics), ct)
            .ConfigureAwait(false);

        if (result.Outcome == TranslationLogOutcomes.Ok)
        {
            // Same CancellationToken.None as the capture path, for the same reason: the request is
            // already spent by the time we get here. A retry overwrites the row it bypassed, so the
            // answer the user chose to pay for is the one served from then on.
            await cache.PutAsync(new CachedTranslation(key, body, result.Text, result.Provider,
                result.Model, false, DateTimeOffset.UtcNow, 0), CancellationToken.None)
                .ConfigureAwait(false);

            Remember(body, how);
        }

        await LogAsync(body, body, speaker, result, game, regionKey, ct).ConfigureAwait(false);

        return new PipelineOutcome(body, body, speaker, body, hits, Present(result),
            OcrConfidence: 0, Stopwatch.GetElapsedTime(started), regionKey, source);
    }

    /// <summary>
    /// Asks the vision lane, and judges what comes back. Never throws: this is an accuracy
    /// improvement bolted onto a pipeline whose contract is that it always produces something, so
    /// every failure here degrades to the local reading in silence.
    /// </summary>
    private async Task<VisionReading?> ReadAgainAsync(Frame frame, OcrResult local, CancellationToken ct)
    {
        if (vision is not { IsConfigured: true }) return null;

        try
        {
            var answer = await vision.ReadAsync(
                new VisionRequest(
                    VisionImagePrep.Prepare(frame),
                    local.RawText,
                    _glossary.Vocabulary(),
                    GameName),
                ct).ConfigureAwait(false);

            return ReadingJudge.Judge(local.RawText, answer.Text, answer.Arabic);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Same contract as the router: an optional second opinion must never be the reason a
            // line fails to appear.
            return null;
        }
    }

    /// <summary>
    /// Which unreadable lines have already been paid for. Cleared with the repeat guard, since both
    /// are answers about the line currently on screen.
    /// </summary>
    private readonly VisionMemo _memo = new();

    /// <summary>
    /// Records what was just shown, for the two things that need to remember it - the rolling
    /// context and the repeat guard - each only for the callers that participate in it.
    ///
    /// <para>
    /// A snip leaves neither trace: it is not part of the conversation, and it must not become the
    /// line the next poll is compared against, or the dialogue the player is actually reading would
    /// be suppressed as a repeat of a menu tooltip. Everything else records both - including a
    /// hotkey press, which cannot itself be suppressed but has just put a line on the overlay, so
    /// the poll that arrives half a second later reading the same pixels is a repeat OF it.
    /// </para>
    /// </summary>
    private void Remember(string body, ProcessOptions how)
    {
        if (how.UseContext) PushContext(body);
        if (how.RemembersLine) lock (_context) _lastBody = body;
    }

    /// <summary>
    /// <para>
    /// Deliberately does NOT update <c>_lastBody</c> on a match. The reference stays the last line
    /// actually shown, so a caption that drifts one character at a time - a scoreboard, a timer
    /// counting up inside the captured rectangle - eventually moves far enough from it to be
    /// translated. Advancing the reference on every near-match would let an arbitrarily large
    /// change through three characters at a time and never translate any of it.
    /// </para>
    /// </summary>
    private bool IsRepeatOfLastBody(string body)
    {
        lock (_context) return TextSimilarity.LooksLikeARepeat(body, _lastBody);
    }

    /// <summary>
    /// The last thing that happens to a translation before anyone sees it. Deliberately after the
    /// cache write and after the log, so both hold what the provider actually said - a display
    /// preference must not be baked into a row that outlives it.
    /// </summary>
    private TranslationResult Present(TranslationResult result) =>
        Diacritics ? result : result with { Text = ArabicText.WithoutDiacritics(result.Text) };

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

    private Task LogAsync(string rawOcr, string normalized, string? speaker,
        TranslationResult result, string game, string? regionKey, CancellationToken ct) =>
        log?.AppendAsync(new TranslationLogEntry(
            DateTimeOffset.UtcNow, rawOcr, normalized, speaker,
            result.Provider, result.Model, result.Text, result.Latency,
            result.FromCache, result.Outcome, game, regionKey), ct) ?? Task.CompletedTask;
}
