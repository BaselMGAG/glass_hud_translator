namespace GlassHudTranslator.Core.Capture;

public sealed record SettleOptions
{
    /// <summary>
    /// How many consecutive polls must agree before the text counts as finished. Two, which at the
    /// default 2 fps means the line has held still for half a second - long enough to outlast a
    /// typewriter reveal, short enough that nobody perceives it as lag.
    /// </summary>
    public int RequiredStillTicks { get; init; } = 2;

    /// <summary>
    /// Translate anyway after this long, even if the screen has never stopped moving. Without it,
    /// a game whose subtitles animate continuously - a scrolling chat log, a karaoke-style caption -
    /// would settle never and translate never, which is a worse failure than translating a frame
    /// mid-change. Three seconds is roughly how long a line stays worth reading.
    /// </summary>
    public TimeSpan Cap { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How near-identical two consecutive polls must be to count as "stopped", and deliberately
    /// far stricter than <see cref="FrameSignature.DefaultChangeThreshold"/>, which answers the
    /// opposite question.
    ///
    /// <para>
    /// Six cells of 1536 is the tolerance for "this is not a new line", sized to absorb a
    /// translucent box drifting over a moving scene. Measured against a rendered 1100x230 dialogue
    /// box, six cells is also about three to six revealed characters — so reused as a stillness
    /// test it declares a slow reveal finished while it is still typing, which is the exact wrong
    /// answer arrived at more expensively. Two cells is inside the noise floor of a static frame
    /// (a scene change behind static text measures zero) and outside a poll's worth of new text.
    /// </para>
    /// </summary>
    public int MaxDifferingCells { get; init; } = 2;

    /// <summary>
    /// How much MORE than the scene's own restlessness a frame may differ and still count as still.
    ///
    /// <para>
    /// <b>The stillness tolerance cannot be a fixed number, and that is a measurement rather than an
    /// opinion.</b> With the text completely unchanged, a dialogue box over a scene with mild
    /// foliage moves 3-6 cells of 1536 between consecutive polls; moderate motion moves 13-18, and
    /// heavy motion 46-58. One more revealed WORD moves 14-18. So mild motion wants a tolerance
    /// near 8 and heavy motion near 60 — and at 60 a whole word is invisible, which is precisely
    /// the defect this gate exists to prevent. There is no single number that is right for both.
    /// </para>
    ///
    /// <para>
    /// So the tolerance is <c>MaxDifferingCells + floor + floor/4</c>: the strictness that is right
    /// for a still image, plus whatever this scene is doing, plus a quarter for its own variance.
    /// A flat addition would not do — it has to vanish when the scene is static, or a typewriter
    /// pausing on a full stop across one poll (3 to 6 cells) reads as finished, which is the very
    /// defect this gate was built for. Checked against every level measured:
    /// </para>
    ///
    /// <code>
    /// floor    tolerance   scene moves   one word moves
    ///     0            2       0            14-18   static: unchanged from before
    ///     4            7       3-6          19      mild foliage
    ///    14           19      13-18         32      moderate
    ///    50           64      46-58         73      heavy
    /// </code>
    ///
    /// <para>
    /// The scene stays under the tolerance and a revealed word stays over it at every level, which
    /// is the only property that matters and the reason for the quarter rather than a constant.
    /// </para>
    /// </summary>
    public int MotionVariance { get; init; } = 4;
}

/// <summary>What auto-watch should do with the frame it just captured.</summary>
public enum FrameVerdict
{
    /// <summary>Same as the last frame translated. Do nothing - not even OCR.</summary>
    Unchanged,

    /// <summary>Something moved, but it is still moving. Wait for the next poll.</summary>
    Settling,

    /// <summary>Changed and then held still. This is the frame to translate.</summary>
    Ready,
}

/// <summary>
/// Stops auto-watch translating the same line four times while it types itself out.
///
/// <para>
/// FFXIV reveals dialogue character by character. Auto-watch polls twice a second and translated
/// any frame that differed from the previous one, so a sentence that takes two seconds to appear
/// produced four or five captures, four or five DIFFERENT strings, four or five cache misses and
/// four or five API requests - to show the player four or five progressively less wrong versions of
/// one sentence. That is the behaviour behind "it translates the same frame more than once until it
/// adjusts", and on a metered free tier it is also four wasted requests out of every five.
/// </para>
///
/// <para>
/// The asymmetry that makes this safe is the same one <see cref="Ocr.StableOcrReader"/> was written
/// around: another poll is free - it is a BitBlt and a 64x24 thumbnail - and another translation is
/// not. So the gate spends polls to avoid requests, never the reverse.
/// </para>
///
/// <para>
/// It compares SIGNATURES rather than OCR text, which is what keeps it free: deciding to wait costs
/// no OCR pass at all, where the text-level equivalent would run Tesseract on every intermediate
/// state to discover it should have skipped them.
/// </para>
/// </summary>
public sealed class FrameSettleGate(SettleOptions? options = null, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private SettleOptions _options = options ?? new SettleOptions();

    private FrameSignature? _translated;
    private FrameSignature? _pending;
    private FrameSignature? _lastPoll;
    private int _stillTicks;
    private DateTimeOffset _movingSince;

    /// <summary>How much consecutive polls have differed recently. The scene's own restlessness.</summary>
    private readonly Queue<int> _movement = new();

    /// <summary>Samples the noise floor is taken over. Eight seconds at the dialogue rate.</summary>
    private const int MovementWindow = 16;

    /// <summary>Samples before the measurement is believed. Four seconds at the dialogue rate.</summary>
    private const int MinimumSamples = 8;

    /// <summary>How many polls the current change has been settling for. Diagnostics only.</summary>
    public int StillTicks => _stillTicks;

    /// <summary>
    /// How much the scene moves on its own, in cells, as a median over recent polls.
    ///
    /// <para>
    /// A MEDIAN rather than a mean, because the samples are a mixture of two populations: most
    /// polls are a line sitting still over a moving scene, and a few are the line actually
    /// changing. The median is dominated by the first, which is the one being measured; a mean
    /// would be dragged upward by every reveal and would then hide the next one.
    /// </para>
    /// </summary>
    public int SceneMovement
    {
        get
        {
            // Nothing is claimed until the scene has been watched for a few seconds. With one or
            // two samples the only thing measured may BE the change - a reveal's own 14-to-18 cells
            // become the "floor", the tolerance opens to swallow a whole word, and the gate calls
            // the second poll of a typewriter reveal finished. Until then the static default
            // stands, which is strict, and strict merely means waiting for the cap.
            if (_movement.Count < MinimumSamples) return 0;

            // The MINIMUM, not a mean or a median. The scene's restlessness is by definition the
            // smallest difference two consecutive polls can show, because text appearing only ever
            // ADDS to it - so a reveal, however many polls it spans, cannot drag this upward. Every
            // averaging estimator can, and does: sampled across a reveal, a median rises far enough
            // to hide the NEXT reveal, which is the gate defeating itself one line later.
            return _movement.Min();
        }
    }

    /// <summary>What "still" means on this screen right now.</summary>
    private int StillnessTolerance
    {
        get
        {
            var floor = SceneMovement;
            return _options.MaxDifferingCells + floor + floor / Math.Max(1, _options.MotionVariance);
        }
    }

    /// <summary>
    /// Offers one captured frame's signature. Call this on every poll, including the ones that
    /// change nothing - the gate needs to see the still frames to know the line has finished.
    /// </summary>
    public FrameVerdict Offer(FrameSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        // Sampled on EVERY poll, and read back as a low percentile rather than a median. Sampling
        // only the polls that are provably still would be cleaner, but those are unreachable when
        // the scene moves more than the current tolerance allows - the gate would need the floor in
        // order to learn the floor. A quarter-percentile is dragged upward by nothing a reveal can
        // do, because a reveal is a handful of polls and a line sitting on screen is dozens.
        if (_lastPoll is not null)
        {
            _movement.Enqueue(signature.DifferenceCount(_lastPoll));
            while (_movement.Count > MovementWindow) _movement.Dequeue();
        }

        _lastPoll = signature;

        // Identical to what is already on the overlay. The overwhelmingly common case during
        // dialogue, and the reason this is checked first. Deliberately still the default threshold
        // and NOT the learned one: this asks "is this a new line", the loosest of the questions,
        // and widening it further would let a genuinely new line be mistaken for the old one.
        // Never STRICTER than it was: this asks "is this a different line", and the six-cell
        // default was chosen for that question. The learned tolerance only ever widens it, which is
        // what a scene moving more than six cells a poll requires - without that, a line sitting
        // perfectly still over busy foliage reads as a new line on every single poll.
        if (signature.LooksIdenticalTo(
                _translated, Math.Max(FrameSignature.DefaultChangeThreshold, StillnessTolerance)))
        {
            _pending = null;
            _stillTicks = 0;
            return FrameVerdict.Unchanged;
        }

        var now = _clock.GetUtcNow();

        if (_pending is null)
        {
            // First frame of a new change. Start the clock that the cap is measured against, so a
            // screen that never stops moving is still translated on schedule rather than never.
            _pending = signature;
            _stillTicks = 1;
            _movingSince = now;
            return Verdict(signature, now);
        }

        if (signature.LooksIdenticalTo(_pending, StillnessTolerance))
        {
            _stillTicks++;
        }
        else
        {
            // Still moving. The newest frame becomes the candidate and the count restarts, but
            // _movingSince deliberately does NOT - it measures the whole change, which is what
            // makes the cap a bound on how long the player waits.
            _pending = signature;
            _stillTicks = 1;
        }

        return Verdict(signature, now);
    }

    private FrameVerdict Verdict(FrameSignature signature, DateTimeOffset now)
    {
        var settled = _stillTicks >= _options.RequiredStillTicks;
        var outOfTime = now - _movingSince >= _options.Cap;
        if (!settled && !outOfTime) return FrameVerdict.Settling;

        _translated = signature;
        _pending = null;
        _stillTicks = 0;
        return FrameVerdict.Ready;
    }

    /// <summary>
    /// Forgets everything. Called when auto-watch is switched on, so that the first frame of a new
    /// session is always a change - otherwise turning it off and straight back on would sit on
    /// Unchanged until the player advanced the dialogue.
    /// </summary>
    public void Reset()
    {
        _translated = null;
        _pending = null;
        _stillTicks = 0;

        // The movement samples are kept deliberately. They describe the SCENE, which has not
        // changed just because auto-watch was toggled - and throwing them away would put the gate
        // back on the static-frame default for the first eight seconds of every run, which is the
        // window where getting it wrong is most visible.
        _lastPoll = null;
    }

    /// <summary>
    /// Swaps the timings mid-run, without forgetting what is already on the overlay.
    ///
    /// <para>
    /// Needed because the cap is adaptive now: <see cref="WatchSession"/> measures how fast the
    /// content actually changes and tightens the deadline to match, which it cannot do to an
    /// object whose options were fixed at construction. Deliberately does NOT reset the frame
    /// state - a retune is a change of pace, not a change of screen, and clearing
    /// <c>_translated</c> here would make the very next poll re-translate the line already shown.
    /// </para>
    /// </summary>
    public void Retune(SettleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }
}
