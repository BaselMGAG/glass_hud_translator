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

    /// <summary>
    /// How many readings a stretch may take before the gate stops reading and lets the deadline
    /// start again. It gives UP, not in: nothing is translated at this bound.
    ///
    /// <para>
    /// <b>This replaces a guarantee that turned out to be about the wrong thing.</b> The cap used
    /// to promise that a screen which never holds still is translated anyway, on the grounds that
    /// showing nothing is worse than showing one frame caught mid-change. That was a claim about
    /// PIXELS never settling, and it is no longer the question: the words decide now, and pixels
    /// that never settle are perfectly ordinary — it is a dialogue box over a windy field. The only
    /// content that reaches this bound is content whose TEXT does not read the same twice half a
    /// second apart, which is either a garble or something changing faster than a person can read.
    /// Neither is worth a request, and one of them is the worst answer the app can give.
    /// </para>
    ///
    /// <para>
    /// What it costs when it fires is a fallback to one reading per <see cref="Cap"/> rather than
    /// one per poll, so a region full of scenery settles into a low, bounded duty cycle instead of
    /// running Tesseract twice a second forever.
    /// </para>
    /// </summary>
    public int ReadsBeforeGivingUp { get; init; } = 4;

    /// <summary>
    /// How often the gate is being offered frames. Not used to pace anything — the loop owns that —
    /// but the scene-motion measurement is a window over TIME, and a queue of samples can only work
    /// out how much time it holds if it knows how far apart they are.
    ///
    /// <para>
    /// Defaults to the rate every mode now runs at. Two is the historic value and is what the unit
    /// tests that advance a fake clock by 500 ms are describing, so both are meaningful.
    /// </para>
    /// </summary>
    public double PollsPerSecond { get; init; } = 4;

    /// <summary>
    /// After this long of the pixels insisting nothing has changed, read the words anyway and check.
    ///
    /// <para>
    /// <b>A watchdog on the one decision that can silently swallow every remaining line.</b>
    /// <see cref="FrameVerdict.Unchanged"/> is an optimisation — it says "this is the frame already
    /// on the overlay" and skips everything, including the reading that would have caught it being
    /// wrong. Every other mistake this gate can make is self-correcting within a few seconds;
    /// this one is not. If the comparison is ever wrong in that direction, for any reason — a
    /// tolerance widened by a busy scene, a new line that happens to lay out like the old one, a
    /// frame committed that was never really shown — the app translates once and then reports
    /// nothing changed for as long as it is left running, which is precisely what "it stops
    /// translating after one line" looks like from the chair.
    /// </para>
    ///
    /// <para>
    /// The check costs one local reading and no tokens: if the words really are the same, the
    /// pipeline's repeat guard drops them before the cache is even consulted. So the worst this can
    /// be wrong by is bounded at fifteen seconds instead of forever, for a couple of Tesseract
    /// passes a minute while somebody is sitting still reading a line.
    /// </para>
    /// </summary>
    public TimeSpan VerifyUnchangedAfter { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The whole life of one reading stretch, however well its readings are going.
    ///
    /// <para>
    /// <see cref="ReadsBeforeGivingUp"/> bounds a stretch whose readings never AGREE — scenery,
    /// where every reading is a fresh garble. This bounds one whose readings keep GROWING, which
    /// the gate deliberately refuses to count as a failure, because a typewriter reveal is a growing
    /// prefix and giving up on one is giving up on the line. Without a second ceiling a screen that
    /// grows forever — a scrolling chat log, a karaoke caption — would hold the stretch open
    /// indefinitely, and a stretch is blind to the rest of the region for its whole length.
    /// </para>
    /// </summary>
    public TimeSpan LongestArrival { get; init; } = TimeSpan.FromSeconds(4);
}

/// <summary>What auto-watch should do with the frame it just captured.</summary>
public enum FrameVerdict
{
    /// <summary>Same as the last frame translated. Do nothing - not even OCR.</summary>
    Unchanged,

    /// <summary>Something moved, but it is still moving. Wait for the next poll.</summary>
    Settling,

    /// <summary>
    /// Read this frame and tell the gate what it said, via <see cref="FrameSettleGate.Confirm"/>.
    /// Do NOT translate it and do not treat it as displayed — the pixels have run out of things to
    /// say about this frame and the text has to answer instead.
    /// </summary>
    Read,

    /// <summary>Changed and then held still. This is the frame to translate.</summary>
    Ready,
}

/// <summary>What the gate makes of a reading it asked for.</summary>
public enum ReadVerdict
{
    /// <summary>Nothing there, or not a frame this gate asked about. Drop it, silently and free.</summary>
    Nothing,

    /// <summary>Something is there but it has not proved it has stopped. Read again next poll.</summary>
    KeepReading,

    /// <summary>Twice the same. This is the line — translate it and commit the frame.</summary>
    Translate,
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
/// It compares SIGNATURES rather than OCR text, which is what keeps deciding-to-wait free: no OCR
/// pass at all, where the text-level equivalent would run Tesseract on every intermediate state to
/// discover it should have skipped them. That is true of the fast path and only of the fast path.
/// </para>
///
/// <para>
/// <b>Over a moving scene the signature cannot answer the question at all, and the gate now says
/// so instead of guessing.</b> Measured with the sentence pixel-identical between polls, a dialogue
/// box over mild foliage moves 3-6 cells of 1536, moderate motion 13-18, heavy 46-58 — while one
/// more revealed WORD moves 14-18. The two populations overlap, so no cell budget separates "the
/// text grew" from "the leaves moved"; a strict threshold never settles and a loose one hides a
/// whole word. Every release then comes from <see cref="SettleOptions.Cap"/>, which fires
/// mid-animation, and the frame that reaches OCR is whatever the screen happened to be doing.
/// That is the "auto translate does not switch to the next sentence" report.
/// </para>
///
/// <para>
/// So the pixels keep the job they are good at — deciding WHEN TO LOOK — and hand the one they
/// cannot do to the only instrument that can. A frame whose scene is too restless to settle, or one
/// that has run out of time, comes back as <see cref="FrameVerdict.Read"/>: OCR it, and tell the
/// gate what it said. <b>Two consecutive readings of the same words</b> is the release, and that
/// single test does four jobs at once. It rejects a garble, because a garbled capture produces a
/// DIFFERENT garble every time — this codebase's own rule, and the reason <c>VisionMemo</c> exists.
/// It rejects a typewriter reveal, because a reveal is a growing prefix and two consecutive reads
/// of one never match. It accepts a line that has stopped changing whatever the foliage behind it
/// is doing. And it is <see cref="Text.TextSimilarity"/>, already here and already calibrated for
/// exactly this jitter.
/// </para>
///
/// <para>
/// The asymmetry still holds, one notch up: a reading is a local Tesseract pass on a small crop,
/// and a translation is a metered request that also blocks the poll thread for seconds. So the gate
/// now spends polls to avoid readings, and readings to avoid requests, and never the reverse.
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

    /// <summary>
    /// The frame currently being read, if the gate has given up on the pixels for this change.
    /// Non-null is the whole of "we are in a reading stretch": while it is set every poll answers
    /// <see cref="FrameVerdict.Read"/> and the decision belongs to <see cref="Confirm"/>.
    /// </summary>
    private FrameSignature? _reading;

    /// <summary>The previous reading, which the next one has to agree with to be released.</summary>
    private string? _lastRead;

    /// <summary>Readings taken in this stretch. Bounds it, so a never-agreeing screen still ends.</summary>
    private int _reads;

    /// <summary>When the pixels first started insisting nothing had changed. Null while something is.</summary>
    private DateTimeOffset? _unchangedSince;

    /// <summary>When the current reading stretch opened. Bounds one that keeps growing.</summary>
    private DateTimeOffset _stretchStartedAt;

    /// <summary>Whether a one-to-three character growth has already been given the benefit of the doubt.</summary>
    private bool _smallGrowthDeferred;

    /// <summary>
    /// One lock, taken by every public member, because two threads genuinely reach this object.
    ///
    /// <para>
    /// The poll thread is inside <see cref="Offer"/> or <see cref="Confirm"/> while the UI thread
    /// can arrive at <see cref="Reset"/> and <see cref="Retune"/> through a mode change, and at
    /// <see cref="NowShowing"/> through a key press, and at <see cref="ForgetWhatIsOnScreen"/>
    /// through a snip. That is reachable today, not a hypothetical. The gate calls nothing external
    /// and takes no other lock, so it can never be held across I/O and there is no ordering hazard.
    /// </para>
    /// </summary>
    private readonly Lock _sync = new();

    /// <summary>
    /// What a reading with words on it but none of them legible is called, so that "illegible
    /// twice running" can agree with itself the way a sentence does.
    ///
    /// <para>
    /// Its own value rather than an early discard, and that is what preserves the second reader:
    /// the vision lane's flagship case is words seen and none read, and it deserves to be escalated
    /// once the screen has held STILL that way — but not on the single mid-change capture that
    /// produces the same symptom for a moment. The leading space keeps it out of the space of real
    /// bodies, which are trimmed.
    /// </para>
    /// </summary>
    private const string Illegible = " illegible";

    /// <summary>How much consecutive polls have differed recently. The scene's own restlessness.</summary>
    private readonly Queue<int> _movement = new();

    /// <summary>
    /// How long a stretch of screen the noise floor is measured over, and how much of it must be
    /// seen before the measurement is believed at all.
    ///
    /// <para>
    /// <b>In SECONDS, and the two constants these replaced were in polls.</b> They were 16 and 8,
    /// which was eight seconds and four while the poll rate was two a second. Raising the dialogue
    /// rate to 4 fps to cut the delay halved both of them without anybody opening this file, and
    /// that is not a harmless loss of precision — the floor is a MINIMUM, so a shorter window has
    /// fewer chances to contain a still moment and the measured floor comes out systematically
    /// HIGHER. A high floor widens the stillness tolerance, and the stillness tolerance also widens
    /// the "is this the line already on screen" test. Wide enough, and a genuinely new line reads as
    /// the old one: the app translates once and then reports nothing changed for as long as it is
    /// left running.
    /// </para>
    ///
    /// <para>
    /// It is the third time this exact defect has been found in this codebase and the second time in
    /// one commit — <see cref="ContentRhythm.Window"/> was fixed in the same change and these were
    /// missed. <b>A quantity counted in polls is a quantity that silently changes meaning whenever
    /// the poll rate does.</b> Anything measuring the world belongs in units of the world.
    /// </para>
    /// </summary>
    private static readonly TimeSpan SceneMemory = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan SceneWarmUp = TimeSpan.FromSeconds(4);

    private int MovementWindow => SamplesIn(SceneMemory);

    private int MinimumSamples => SamplesIn(SceneWarmUp);

    private int SamplesIn(TimeSpan span) =>
        Math.Max(2, (int)Math.Round(span.TotalSeconds * _options.PollsPerSecond));

    /// <summary>How many polls the current change has been settling for. Diagnostics only.</summary>
    public int StillTicks { get { lock (_sync) return _stillTicks; } }

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
            lock (_sync)
            {
                // Nothing is claimed until the scene has been watched for a few seconds. With one
                // or two samples the only thing measured may BE the change - a reveal's own
                // 14-to-18 cells become the "floor", the tolerance opens to swallow a whole word,
                // and the gate calls the second poll of a typewriter reveal finished. Until then
                // the static default stands, which is strict, and strict merely means waiting.
                if (_movement.Count < MinimumSamples) return 0;

                // The MINIMUM, not a mean or a median. The scene's restlessness is by definition
                // the smallest difference two consecutive polls can show, because text appearing
                // only ever ADDS to it - so a reveal, however many polls it spans, cannot drag this
                // upward. Every averaging estimator can, and does: sampled across a reveal, a
                // median rises far enough to hide the NEXT reveal, which is the gate defeating
                // itself one line later.
                return _movement.Min();
            }
        }
    }

    /// <summary>
    /// Reading stretches that ended with nothing translated, for Diagnostics. A line the gate
    /// genuinely gave up on is the one thing that looks, from the player's chair, exactly like the
    /// app skipping — so "did it skip?" stops being a matter of opinion.
    /// </summary>
    public int GaveUp { get; private set; }

    /// <summary>
    /// This frame is what the player is looking at now, put there by something other than the gate.
    ///
    /// <para>
    /// <b>The manual hotkey is the caller, and without it a key press costs the next automatic
    /// cycle for nothing.</b> A press translates the line and puts it on the overlay, but the gate
    /// was never told — so it still held the PREVIOUS frame as displayed, called the very next poll
    /// a change, settled it, spent a reading on it, and handed the pipeline a line it had just
    /// shown, which the repeat guard then threw away. All correct, all wasted, and on a machine
    /// where a reading costs a few hundred milliseconds it is wasted in the one place the user is
    /// already unhappy about the delay.
    /// </para>
    ///
    /// <para>
    /// Deliberately NOT <see cref="Reset"/>: the scene measurements stay, and so does everything
    /// about the current change except the fact that it is now finished.
    /// </para>
    /// </summary>
    public void NowShowing(FrameSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        lock (_sync) NowShowingCore(signature);
    }

    private void NowShowingCore(FrameSignature signature)
    {
        _translated = signature;
        _pending = null;
        _stillTicks = 0;
        _reading = null;
        _lastRead = null;
        _reads = 0;
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
        lock (_sync) return OfferCore(signature);
    }

    private FrameVerdict OfferCore(FrameSignature signature)
    {
        // Sampled on EVERY poll, INCLUDING the polls of an open reading stretch - the sample used
        // to sit below the early return, so the scene estimate simply stopped being taken for the
        // whole of one. That was invisible while a stretch was four polls and is not now that a
        // growing line can hold one open for seconds. The samples taken during a stretch are the
        // good ones anyway: the text is not changing, so what they measure is exactly the scene's
        // own restlessness. SceneMovement is a Min, so more samples can only lower it, which is the
        // strict direction.
        if (_lastPoll is not null)
        {
            _movement.Enqueue(signature.DifferenceCount(_lastPoll));
            while (_movement.Count > MovementWindow) _movement.Dequeue();
        }

        _lastPoll = signature;

        // Once a reading stretch has begun, the pixels have already been asked and have already
        // failed to answer. Handing the decision back to them mid-stretch would abandon a reading
        // half-taken every time the scene twitched, which over a moving scene is every poll.
        if (_reading is not null)
        {
            _reading = signature;
            return FrameVerdict.Read;
        }

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

            var settledAt = _clock.GetUtcNow();
            _unchangedSince ??= settledAt;

            // The watchdog. Long enough on one answer and the answer gets checked against the words
            // rather than believed indefinitely - see SettleOptions.VerifyUnchangedAfter for why
            // this one decision is worth a periodic reading and none of the others are.
            if (settledAt - _unchangedSince >= _options.VerifyUnchangedAfter)
            {
                _unchangedSince = settledAt;
                return StartReading(signature);
            }

            return FrameVerdict.Unchanged;
        }

        _unchangedSince = null;

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

        // The free path, and it survives untouched for the screen it was written for. When the
        // scene is measurably quiet the learned tolerance IS the strict one, so this is the same
        // decision the gate has always made: two identical polls, translate, no OCR spent deciding.
        if (settled && SceneMovement <= _options.MaxDifferingCells)
        {
            _translated = signature;
            _pending = null;
            _stillTicks = 0;
            return FrameVerdict.Ready;
        }

        // Everything else asks the text. Two cases arrive here and they mean different things —
        // "the pixels look still but this scene is too restless for that to prove anything" and
        // "this screen has never stopped moving at all" — and the answer to both is the same one:
        // stop guessing from a 1536-cell thumbnail and read the words.
        return StartReading(signature);
    }

    /// <summary>Opens a reading stretch. One place, so its state cannot be half-initialised.</summary>
    private FrameVerdict StartReading(FrameSignature signature)
    {
        _reading = signature;
        _lastRead = null;
        _reads = 0;
        _smallGrowthDeferred = false;
        _stretchStartedAt = _clock.GetUtcNow();
        return FrameVerdict.Read;
    }

    /// <summary>
    /// Answers a <see cref="FrameVerdict.Read"/> with what the frame actually said.
    ///
    /// <para>
    /// <paramref name="wordsSeenButIllegible"/> is the distinction <c>IOcrEngine</c> already
    /// documents and the escalation policy already depends on: an empty body because the region is
    /// blank and an empty body because ten words were seen and every one was thrown away are
    /// different facts, and only the second is worth reading again.
    /// </para>
    /// </summary>
    public ReadVerdict Confirm(string? body, bool wordsSeenButIllegible)
    {
        lock (_sync) return ConfirmCore(body, wordsSeenButIllegible);
    }

    private ReadVerdict ConfirmCore(string? body, bool wordsSeenButIllegible)
    {
        // Not a frame this gate asked about. A manual press, a snip, a retry - all of them reach
        // the pipeline without a reading stretch open, and none of them is the gate's business.
        if (_reading is null) return ReadVerdict.Nothing;

        var reading = string.IsNullOrWhiteSpace(body)
            ? (wordsSeenButIllegible ? Illegible : "")
            : body.Trim();

        // Nothing on screen. The commonest frame there is, free to establish, and it must not start
        // a countdown to translating nothing.
        if (reading.Length == 0) return Discard(theRegionIsEmpty: true);

        if (_lastRead is null)
        {
            _reads = 1;
            _lastRead = reading;
            return ReadVerdict.KeepReading;
        }

        switch (Compare(reading, _lastRead))
        {
            case Reading.Same:
                return Release();

            case Reading.StillArriving:
                // <b>Deliberately does NOT move _reads.</b> The give-up bound exists for readings
                // that never AGREE - scenery, whose every reading is a fresh garble - and a reveal
                // always eventually agrees with itself. Counting a growing line as a failure is
                // what made a long line exhaust the budget, give up, and restart the whole
                // three-second cap: the multi-second inconsistency, arriving as a penalty for the
                // line being long.
                _lastRead = reading;
                break;

            default:
                _reads++;
                _lastRead = reading;
                break;
        }

        // Two ceilings, bounding two different things. _reads is untouched above and still bounds a
        // region whose words never agree. The CLOCK bounds a stretch that keeps growing, which the
        // rule above now refuses to end - so a scrolling chat log or a karaoke caption cannot hold
        // the gate open indefinitely. A stretch is blind to the rest of the region for its whole
        // length, and that is what stops either ceiling being generous.
        if (_reads >= _options.ReadsBeforeGivingUp
            || _clock.GetUtcNow() - _stretchStartedAt >= _options.LongestArrival)
            return Discard(theRegionIsEmpty: false);

        return ReadVerdict.KeepReading;
    }

    /// <summary>What one reading says about the one before it.</summary>
    private enum Reading
    {
        /// <summary>The same words. This is the line.</summary>
        Same,

        /// <summary>The same words plus more on the end. The line has not finished appearing.</summary>
        StillArriving,

        /// <summary>Neither. Two readings of something that could not be read the same way twice.</summary>
        Different,
    }

    /// <summary>
    /// How alike two readings of one screen must be before they count as the same words.
    ///
    /// <para>
    /// <b>Measured against real readings off a real screen, not chosen.</b> Three consecutive
    /// captures of one video caption, from a support trace, scored 0.79 and 0.88 against each other;
    /// four consecutive garbles off a region with no readable text scored 0.29, 0.35 and 0.34. Those
    /// are the two populations this has to sit between, and it sits in the middle of the gap rather
    /// than at the edge of either.
    /// </para>
    ///
    /// <para>
    /// It was <see cref="Ocr.ReadingJudge.SameThing"/> (0.90), which is the right number for the
    /// question it was borrowed from — is this vision model's reading the same line the local engine
    /// saw — and the wrong one here, because that comparison is between two readers looking at the
    /// same pixels while this one is between two readers looking at the screen a third of a second
    /// apart, with the picture behind the words moving. At 0.90 no real caption ever agreed with
    /// itself, so video mode translated nothing at all.
    /// </para>
    /// </summary>
    public const double SameText = 0.65;

    /// <summary>
    /// How near-exact a prefix has to be before a longer reading counts as the SAME line still
    /// appearing rather than a second look at a finished one. Near-exact on purpose: this is the
    /// only thing standing between a typewriter reveal and being translated half-written.
    /// </summary>
    private const double PrefixIsExact = 0.95;

    /// <summary>Characters of growth below which two readings are just two readings.</summary>
    private const int MeaningfulGrowth = 4;

    /// <summary>
    /// Whether two readings are the same words.
    ///
    /// <para>
    /// <b>The reveal is separated by SHAPE, not by degree, and that is the whole of this method.</b>
    /// Measured on the same data: a typewriter reveal scores 0.71 and 0.87 between consecutive
    /// readings and a jittering caption scores 0.79 and 0.88 — the two OVERLAP, so no similarity
    /// threshold can tell them apart and the previous version's attempt to do it with one number was
    /// always going to fail one of them. What separates them completely is that a reveal is a
    /// GROWING PREFIX: the shorter reading scores 1.00 against the longer one's opening, where the
    /// jittering caption scores 0.88 because its noise is scattered through the middle.
    /// </para>
    /// </summary>
    private Reading Compare(string current, string previous)
    {
        var (shorter, longer) = current.Length <= previous.Length
            ? (current, previous)
            : (previous, current);

        // <b>THE PREFIX TEST RUNS FIRST, and that ordering is the entire fix.</b>
        //
        // It used to run second, behind LooksLikeARepeat, whose budget is min(3, shorter/4)
        // ABSOLUTE edits. So the last partial reading of a reveal and the finished line - which
        // differ by one to three characters whenever the reveal happens to end near a reading -
        // scored as THE SAME WORDS. The half-written sentence was released, translated, and written
        // to the cache permanently; the finished sentence then arrived and was thrown away by the
        // pipeline's own repeat guard as a repeat of the fragment. Measured on four real FFXIV
        // lines: every one released one character short, at a prefix agreement of exactly 1.00.
        //
        // That is a fluent, confident, WRONG Arabic line shown to somebody who cannot check it
        // against the English - the single answer this file is most emphatic about never giving -
        // and it is also why the app looked inconsistent rather than uniformly slow: whether it
        // bit depended on where the reveal happened to be when two readings landed.
        //
        // Shape sees what degree cannot, so shape is asked first.
        if (longer.Length > shorter.Length
            && shorter.Length > 0
            && Ocr.ReadingJudge.Agreement(shorter, longer[..shorter.Length]) >= PrefixIsExact)
        {
            // DIRECTION decides which fact this is. The newer reading being the longer one is a
            // line still appearing. The newer one being SHORTER is the tail having gone missing,
            // which is a disagreement and nothing else - a reveal never runs backwards.
            var growing = current.Length > previous.Length;
            var growth = longer.Length - shorter.Length;

            if (growing && growth >= MeaningfulGrowth)
            {
                _smallGrowthDeferred = false;
                return Reading.StillArriving;
            }

            // One to three characters is genuinely ambiguous: it is the end of a reveal, and it is
            // equally the reader finding a full stop it missed last time. No threshold separates
            // those, so a THIRD reading does - defer once, and if the next pair says the same thing,
            // believe it. Costs one poll on the last reading of a reveal and buys the finished
            // sentence instead of the sentence minus its last word. It cannot loop: a pair already
            // deferred falls through, and the shrinking direction always falls through.
            if (growing && !_smallGrowthDeferred)
            {
                _smallGrowthDeferred = true;
                return Reading.StillArriving;
            }

            if (!growing && growth >= MeaningfulGrowth) return Reading.Different;
        }

        if (Text.TextSimilarity.LooksLikeARepeat(current, previous)) return Reading.Same;

        return Ocr.ReadingJudge.Agreement(current, previous) >= SameText
            ? Reading.Same
            : Reading.Different;
    }

    private ReadVerdict Release()
    {
        _smallGrowthDeferred = false;
        _translated = _reading;
        _reading = null;
        _lastRead = null;
        _reads = 0;
        _pending = null;
        _stillTicks = 0;
        return ReadVerdict.Translate;
    }

    /// <summary>
    /// Nothing worth translating in this stretch, so forget it — including what was on the overlay.
    ///
    /// <para>
    /// <b>Clearing <c>_translated</c> is what fixes "it does not come back".</b> A dialogue box that
    /// closes and reopens on the same line, and a caption that repeats after a gap, both used to be
    /// invisible: the region went empty, the overlay was cleared, and when the words returned they
    /// were within a few cells of the frame still recorded as displayed — so the gate answered
    /// <see cref="FrameVerdict.Unchanged"/> and the player sat looking at nothing while the text was
    /// plainly on screen. An empty region is proof that whatever was there has gone, which makes
    /// the next thing to appear new whatever it says.
    /// </para>
    ///
    /// <para>
    /// It costs at most one cache hit, because a line translated once this session is already
    /// stored — the same trade the snip rules make, for the same reason.
    /// </para>
    ///
    /// <para>
    /// Only on an EMPTY region, and the distinction is the point: running out of readings on text
    /// that never agreed with itself proves nothing about what is on the overlay, so forgetting
    /// there would re-translate a perfectly good line every time a burst of unreadable frames went
    /// past behind it.
    /// </para>
    /// </summary>
    private ReadVerdict Discard(bool theRegionIsEmpty)
    {
        if (theRegionIsEmpty) _translated = null;
        else GaveUp++;

        _smallGrowthDeferred = false;

        _reading = null;
        _lastRead = null;
        _reads = 0;
        _pending = null;
        _stillTicks = 0;

        // A fresh cap rather than an immediate retry: the change this stretch was about turned out
        // to be nothing, so the next one deserves the whole deadline to prove otherwise.
        _movingSince = _clock.GetUtcNow();
        return ReadVerdict.Nothing;
    }

    /// <summary>
    /// Forgets everything. Called when auto-watch is switched on, so that the first frame of a new
    /// session is always a change - otherwise turning it off and straight back on would sit on
    /// Unchanged until the player advanced the dialogue.
    /// </summary>
    public void Reset()
    {
        lock (_sync) ResetCore();
    }

    private void ResetCore()
    {
        _translated = null;
        _pending = null;
        _stillTicks = 0;
        _reading = null;
        _lastRead = null;
        _reads = 0;
        _unchangedSince = null;

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

        lock (_sync) _options = options;
    }
}
