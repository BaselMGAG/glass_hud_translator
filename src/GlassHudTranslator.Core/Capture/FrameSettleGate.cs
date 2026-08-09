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
    private int _stillTicks;
    private DateTimeOffset _movingSince;

    /// <summary>How many polls the current change has been settling for. Diagnostics only.</summary>
    public int StillTicks => _stillTicks;

    /// <summary>
    /// Offers one captured frame's signature. Call this on every poll, including the ones that
    /// change nothing - the gate needs to see the still frames to know the line has finished.
    /// </summary>
    public FrameVerdict Offer(FrameSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        // Identical to what is already on the overlay. The overwhelmingly common case during
        // dialogue, and the reason this is checked first.
        if (signature.LooksIdenticalTo(_translated))
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

        if (signature.LooksIdenticalTo(_pending, _options.MaxDifferingCells))
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
