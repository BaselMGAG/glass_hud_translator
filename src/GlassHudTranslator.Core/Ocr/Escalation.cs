using GlassHudTranslator.Core.Text;

namespace GlassHudTranslator.Core.Ocr;

/// <summary>Why a frame was, or was not, sent for a second reading.</summary>
public enum EscalationReason
{
    /// <summary>The user has not switched it on. The default, and the only state that costs nothing.</summary>
    SwitchedOff,

    /// <summary>Nothing was on screen. Silence is the right answer and it is free.</summary>
    NothingThere,

    /// <summary>The local engine read it well. Most lines end here — that is the whole economy.</summary>
    GoodEnough,

    /// <summary>This same unreadable line was escalated moments ago and the answer is remembered.</summary>
    AlreadyAsked,

    /// <summary>Words were seen and none could be trusted. The case the feature exists for.</summary>
    Illegible,

    /// <summary>Text came back, but enough of it was thrown away to doubt the rest.</summary>
    TooMuchRejected,
}

/// <summary>
/// Whether to pay for a second opinion on this frame, and why.
/// </summary>
public sealed record EscalationDecision(bool Escalate, EscalationReason Why)
{
    public static readonly EscalationDecision No = new(false, EscalationReason.GoodEnough);

    public static EscalationDecision Not(EscalationReason why) => new(false, why);

    public static EscalationDecision Yes(EscalationReason why) => new(true, why);
}

/// <summary>
/// Decides when a local reading is bad enough to be worth asking a vision model about.
///
/// <para>
/// <b>This is the whole cost control, and two of its rules exist because they were attacked.</b>
/// Every escalation is a metered request against a free tier of a few hundred a day, and this app
/// polls twice a second — so a predicate that is even slightly too generous does not degrade
/// gracefully, it empties the day's allowance in under half an hour.
/// </para>
///
/// <para>
/// <b>Rejected words are the trigger, not mean confidence.</b> <c>OcrResult.Confidence</c> is a mean
/// over the words that SURVIVED the filter, so a frame where nine words of ten were thrown away
/// still reports a serene 90 — it cannot see the frames worth escalating. And confidence alone is
/// actively the wrong signal: this project's own history records "linkpearl" read perfectly at
/// confidence 39.2, because Tesseract scores unusual proper nouns down, and unusual proper nouns
/// are most of what a game glossary contains. Escalating on low confidence would route precisely
/// the proper nouns to the reader that is measurably worst at them — multimodal models lose about
/// 57 accuracy points on text that carries no semantics, against about 5 for a supervised
/// recogniser, because they read by knowing what the word probably is.
/// </para>
/// </summary>
public static class EscalationPolicy
{
    /// <summary>
    /// How many rejected words make "there is text here I cannot read" more likely than "there is
    /// nothing here".
    ///
    /// <para>
    /// This constant is the difference between a free idle screen and an empty daily quota, and the
    /// number comes from a case already written down in this repository: a UI border read as
    /// <c>|~</c> at confidence 8. One or two rejected fragments is what the EDGE of a capture region
    /// looks like on a frame with no dialogue on it at all — and a region drawn slightly wide over
    /// an animated scene produces that roughly twenty times a minute at the dialogue pacing, which
    /// is Gemini's entire free day in under half an hour, for a screen with nothing on it. Three
    /// says somebody wrote a sentence there.
    /// </para>
    /// </summary>
    public const int RejectedWordsThatMeanText = 3;

    /// <summary>
    /// Above this share of words surviving, the reading is treated as good enough to use.
    ///
    /// <para>
    /// A ratio rather than a confidence, for the reason above: the count of words thrown away is a
    /// fact about this frame, while the mean confidence is a fact about the words that happened to
    /// survive.
    /// </para>
    /// </summary>
    public const double AcceptableSurvivalRate = 0.75;

    /// <summary>
    /// <paramref name="alreadyAsked"/> is the per-line memo: true when this same unreadable text
    /// has just been escalated and the answer is still remembered. Without it the feature bills
    /// once per POLL rather than once per line — a dialogue box over an animated scene never
    /// settles, so the same sentence is re-read every few seconds, and every re-read is a fresh
    /// garble that by construction differs from the correction, so nothing downstream suppresses
    /// it. That is the v0.5.2 "paying four times over for one sentence" defect one layer up, where
    /// neither the settle gate nor the cache can see it.
    /// </summary>
    public static EscalationDecision Decide(OcrResult local, bool enabled, bool alreadyAsked = false)
    {
        ArgumentNullException.ThrowIfNull(local);

        if (!enabled) return EscalationDecision.Not(EscalationReason.SwitchedOff);

        var seen = local.WordCount + local.RejectedWordCount;
        var looksLikeText = local.RejectedWordCount >= RejectedWordsThatMeanText;

        // Nothing readable came back. Two very different situations wear this face, and the
        // distinction is one the codebase already writes down on RejectedWordCount: nothing there
        // (nothing seen, nothing rejected) against illegible (words seen, none trusted). The first
        // must stay free forever; the second is the flagship case.
        if (string.IsNullOrWhiteSpace(local.RawText))
        {
            return looksLikeText
                ? Ask(EscalationReason.Illegible, alreadyAsked)
                : EscalationDecision.Not(EscalationReason.NothingThere);
        }

        // Text came back, but with enough thrown away to doubt what survived.
        if (seen > 0 && local.WordCount / (double)seen < AcceptableSurvivalRate && looksLikeText)
            return Ask(EscalationReason.TooMuchRejected, alreadyAsked);

        return EscalationDecision.Not(EscalationReason.GoodEnough);
    }

    private static EscalationDecision Ask(EscalationReason why, bool alreadyAsked) =>
        alreadyAsked ? EscalationDecision.Not(EscalationReason.AlreadyAsked) : EscalationDecision.Yes(why);
}

/// <summary>What became of a second reading.</summary>
public enum ReadingVerdict
{
    /// <summary>The vision model agreed with the local engine. Nothing to change.</summary>
    Confirmed,

    /// <summary>It disagreed plausibly. The correction is adopted.</summary>
    Corrected,

    /// <summary>It returned something unrelated, or nothing. The local reading stands.</summary>
    Rejected,
}

/// <summary>
/// One second reading, and what was decided about it.
/// </summary>
/// <param name="Text">What the vision model read. Empty when it declined.</param>
/// <param name="Understudy">
/// An Arabic translation the vision model offered in the same breath.
///
/// <para>
/// <b>Null unless the reading was accepted, and that is a correctness rule rather than tidiness.</b>
/// It exists for one narrow case — the text router afterwards failing every lane, where today the
/// user sees English with a warning marker and an Arabic-only reader is simply stuck. But a
/// REJECTED reading is one just proved untrustworthy, and displaying its translation would take the
/// system's single most confident wrong answer and put it on screen as fluent Arabic, with no
/// English beside it for the reader to catch it by. Rejecting a reading has to reject its
/// translation with it, and the cheapest way to guarantee that is for the field not to survive the
/// verdict.
/// </para>
/// </param>
/// <param name="Agreement">
/// How much the two readings shared, 0 to 1, or null when there was no local reading to compare
/// against. Null is the honest answer for an illegible frame and must not be read as zero.
/// </param>
public sealed record VisionReading(string Text, string? Understudy, double? Agreement, ReadingVerdict Verdict);

/// <summary>
/// Decides whether to believe a second reading, by comparing it with the first.
///
/// <para>
/// <b>Agreement between two independent readers is the only confidence signal available here, and
/// it happens to be the best one published.</b> Every alternative fails on this app's lanes: token
/// logprobs are unavailable on three of the four providers, and are conceptually wrong anyway
/// because a model reading unreadable pixels emits high-probability tokens about its own
/// misreading. Sampling the same model twice measures worse than not doing it. Self-reported
/// confidence calibrates only on frontier models, which are exactly the ones a free-lane-first
/// router does not reach. Cross-checking against the traditional reading is what the measurement
/// literature settles on, and this codebase already owns a tested edit-distance implementation.
/// </para>
///
/// <para>
/// The defence this provides is specific. A vision model's mistake is FLUENT — a well-formed
/// sentence that was never on screen — where a Tesseract mistake is visible noise. Fluent wrong
/// Arabic is undetectable to the reader this app exists for, and would be cached permanently under
/// the corrected key. But a genuine correction of a garbled line still shares most of its
/// characters with the garble, because both are readings of the same pixels; an invented sentence
/// has no reason to share anything.
/// </para>
/// </summary>
public static class ReadingJudge
{
    /// <summary>Above this, the two readers are saying the same thing and nothing needs changing.</summary>
    public const double SameThing = 0.90;

    /// <summary>
    /// Below this, the second reading has no visible relationship to the pixels the first one saw,
    /// and is far likelier to be invention than correction.
    /// </summary>
    public const double Unrelated = 0.35;

    /// <summary>The sentinel a model is asked to return rather than inventing text.</summary>
    public const string NothingToRead = "<EMPTY>";

    public static VisionReading Judge(string? local, string? vision, string? understudy = null)
    {
        var read = (vision ?? "").Trim();

        // Declining is a real answer and has to be distinguishable from a failed call, which is the
        // same distinction PipelineOutcome.Result being null already draws on the other side.
        if (read.Length == 0 || read.Equals(NothingToRead, StringComparison.OrdinalIgnoreCase))
            return new VisionReading("", null, null, ReadingVerdict.Rejected);

        var first = (local ?? "").Trim();

        // No local reading to compare against. This is only reachable when the frame was escalated
        // as ILLEGIBLE, which means the local engine did see glyphs it could not resolve - so there
        // genuinely is text there and a reading is worth adopting. There is simply no agreement to
        // measure, and saying null is more honest than inventing a zero that would read as total
        // disagreement.
        if (first.Length == 0)
            return new VisionReading(read, understudy, null, ReadingVerdict.Corrected);

        var agreement = Agreement(first, read);

        var verdict = agreement >= SameThing ? ReadingVerdict.Confirmed
            : agreement >= Unrelated ? ReadingVerdict.Corrected
            : ReadingVerdict.Rejected;

        return new VisionReading(
            verdict == ReadingVerdict.Rejected ? "" : read,
            verdict == ReadingVerdict.Rejected ? null : understudy,
            agreement,
            verdict);
    }

    /// <summary>
    /// Shared content as a fraction, 1 for identical and 0 for nothing in common. Normalised by the
    /// LONGER string, so a short invented sentence cannot score well against a long garbled one
    /// simply by being short.
    /// </summary>
    public static double Agreement(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;

        var longest = Math.Max(a.Length, b.Length);
        if (longest == 0) return 1;

        // The whole distance, not a capped one: this is a measurement rather than a threshold test,
        // and the cheap early exit that serves the repeat guard would flatten every wide difference
        // onto the same number.
        var distance = TextSimilarity.DistanceAtMost(a, b, longest, ignoreCase: true) ?? longest;

        return 1.0 - (distance / (double)longest);
    }
}

/// <summary>
/// Remembers which unreadable lines have already been paid for, and what came back.
///
/// <para>
/// <b>Without this the feature bills once per POLL rather than once per line, and nothing else in
/// the app can catch it.</b> A dialogue box over an animated scene never satisfies the settle gate,
/// so the same sentence is re-read every few seconds; each re-read is a slightly different garble.
/// The cache cannot help, because it is keyed on the CORRECTED text and the incoming garble is not
/// that. The repeat guard cannot help, because it too remembers what was translated. So every poll
/// looks like a brand-new unreadable line and buys a brand-new reading — which is the "paying four
/// times over for one sentence" defect from v0.5.2 reappearing one layer up, where neither of the
/// two nets that were built for it can see it.
/// </para>
///
/// <para>
/// Matched with the same jitter tolerance as everything else that compares two OCR readings, and
/// for the same reason: consecutive reads of one unchanged line differ by a character or two, so
/// exact matching would remember nothing and the memo would be inert.
/// </para>
/// </summary>
public sealed class VisionMemo(int capacity = 8)
{
    private readonly Queue<(string Local, string Corrected)> _seen = new();

    /// <summary>Whether this garble has been sent already and the answer is still held.</summary>
    public bool Remembers(string? local) => Recall(local) is not null;

    /// <summary>
    /// What came back last time this line was unreadable, or null if it has not been asked.
    ///
    /// <para>
    /// An empty local reading never matches. Two illegible frames both reading as nothing are not
    /// evidence of being the same line — they are evidence of being unreadable, which is exactly
    /// the case where reusing a remembered answer would put the previous sentence's words on
    /// screen over this sentence's pixels.
    /// </para>
    /// </summary>
    public string? Recall(string? local)
    {
        if (string.IsNullOrWhiteSpace(local)) return null;

        foreach (var (seen, corrected) in _seen)
        {
            if (TextSimilarity.LooksLikeARepeat(local, seen)) return corrected;
        }

        return null;
    }

    public void Remember(string local, string corrected)
    {
        if (string.IsNullOrWhiteSpace(local)) return;

        _seen.Enqueue((local, corrected));
        while (_seen.Count > Math.Max(1, capacity)) _seen.Dequeue();
    }

    /// <summary>Auto-watch switching on, or a snip: the next stretch is a fresh question.</summary>
    public void Clear() => _seen.Clear();
}
