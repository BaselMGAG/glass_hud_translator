namespace GlassHudTranslator.Core.Capture;

/// <summary>One poll, reduced to the three things that say what kind of content this is.</summary>
/// <param name="Changed">The frame differs from the one currently on the overlay.</param>
/// <param name="HasText">OCR found something worth translating. Null when OCR was not run —
/// which is most polls, because the frame gate answers first and costs nothing.</param>
/// <param name="TextChanged">The text differs from the last line shown. Null when not read.</param>
public readonly record struct RhythmSample(bool Changed, bool? HasText = null, bool? TextChanged = null);

/// <summary>What the watched region turns out to be.</summary>
public enum ContentKind
{
    /// <summary>Not enough evidence yet. Callers run the safe default until this clears.</summary>
    Unknown,

    /// <summary>Text that waits: it appears, stays, and leaves when the player says so.</summary>
    Dialogue,

    /// <summary>Text that passes: it appears whole, lives seconds, and leaves on its own.</summary>
    Moving,
}

/// <summary>
/// Works out, from cheap per-poll observations, whether the region is a dialogue box that waits to
/// be clicked or a caption over moving picture that leaves on its own — so the user does not have
/// to know which timings to ask for.
///
/// <para>
/// The two live at opposite ends of every number in <see cref="WatchPacing"/>, and picking wrong
/// is expensive in both directions: dialogue timings over video put the Arabic on screen after the
/// line has gone (measured at 4.6 seconds), and video timings over a typewriter reveal translate
/// half-written sentences and pay for each one. Neither of the projects this was measured against
/// attempts the detection at all — both make it a setting and leave the consequence with the user.
/// </para>
///
/// <para>
/// <b>The signals, weakest first, and why the order matters.</b>
/// </para>
///
/// <para>
/// <b>Motion is the obvious signal and the one that lies.</b> "The picture keeps changing" looks
/// like video and is also true of a dialogue box with an animated scene behind it, weather, or a
/// character idling — which is most games. Used alone it would call FFXIV a film. It is kept as a
/// tie-breaker and never decides on its own.
/// </para>
///
/// <para>
/// <b>Emptiness is the strong signal for moving text.</b> Captions live in gaps: between two lines
/// there is nothing in the rectangle at all. A dialogue box holds its text until the player
/// advances, so an empty read is rare and usually means the box closed. A region that is regularly
/// empty is not a dialogue box.
/// </para>
///
/// <para>
/// <b>Persistence is the strong signal for dialogue, and it is measured in SECONDS.</b> How long
/// the line on screen has stood there unchanged is the question being asked, phrased directly
/// rather than inferred from pixels — and it is measured on the TEXT, so a moving background cannot
/// fake it. The unit is the whole of it. A caption that is still up and a box that is waiting are
/// the same observation while both are on screen, so the threshold can only come from outside: the
/// subtitling houses cap a caption at <b>seven seconds</b>, and a line still up after eight is
/// therefore not a caption. Counted in polls instead, the same box scored differently depending on
/// what was happening behind it, because a poll over a static frame and a poll over video carry
/// wildly different amounts of evidence.
/// </para>
///
/// <para>
/// Both strong signals are text-level, which is what makes them robust: they are already paid for
/// by the OCR the pipeline was running anyway, and they see through exactly the animation that
/// defeats a pixel comparison. But they are <b>scarce</b>, and that has bitten once already: OCR
/// runs on one verdict in three, so a window holds only about five reads at the dialogue timings.
/// Any gate counting reads has to fit inside that budget or it never opens.
/// </para>
///
/// <para>
/// <b>Switching is deliberately reluctant.</b> After any switch the next one is refused for
/// <see cref="MinimumDwell"/>, because content genuinely alternates — a cutscene inside a game, a
/// paused video — and a classifier that follows every wobble is worse than either fixed mode: it
/// spends its life in the wrong one, arriving there late. Note what the dwell is and is not: it
/// limits how OFTEN the verdict may change, not how much evidence a change needs. Once it expires,
/// one poll can flip the mode. That is tolerable only because both signals are themselves
/// integrated over time or over a window, and it is the first thing to revisit if Auto is ever
/// reported as flapping.
/// </para>
/// </summary>
public sealed class ContentRhythm(TimeProvider? clock = null)
{
    /// <summary>
    /// Polls kept. At the dialogue rate that is about fifteen seconds and at the video rate about
    /// seven — deliberately a window of EVIDENCE rather than of time, because what matters is how
    /// many observations back the verdict, not how long ago they started.
    /// </summary>
    public const int Window = 30;

    /// <summary>
    /// Reads needed before the empty fraction means anything. Below this, "no text seen" is far
    /// more likely to be a game that has not started talking yet than a caption gap.
    ///
    /// <para>
    /// Three, and the ceiling is arithmetic rather than taste. A read costs a whole settle cap,
    /// because <see cref="RhythmSample.HasText"/> is filled in on one verdict only, so the reads a
    /// window can hold is <c>Window / (PollsPerSecond * SettleCap)</c> — <b>five</b> at the dialogue
    /// timings and nine at the video ones. It was six, which is to say one more than the dialogue
    /// rate can ever produce, and since Auto starts on the dialogue timings the gate guarding every
    /// route out of them could not open: over a film the classifier sat at Unknown with every signal
    /// it had screaming video. There is a test on the arithmetic, because no behavioural test here
    /// could catch it — they all feed a read on every poll, which the poll loop never does.
    /// </para>
    /// </summary>
    public const int MinimumReads = 3;

    /// <summary>
    /// A line still on screen, unchanged, for longer than this is being waited on rather than
    /// passing through — <b>measured in seconds, and that is the whole correction</b>.
    ///
    /// <para>
    /// It used to be four consecutive polls, justified as "longer than any caption holds still".
    /// Both halves were wrong. The claim was invented: subtitling practice is unusually well
    /// documented and every house agrees on the shape, a caption standing for up to <b>seven
    /// seconds</b> (the ESIST Code and Netflix both cap it there, Karamitroglou at six for two
    /// lines) against a floor near one. Four of anything is inside that, not beyond it.
    /// </para>
    ///
    /// <para>
    /// The unit was worse. A poll is not a fixed amount of evidence: over a <i>static</i> box the
    /// gate answers Unchanged and the run grows once per poll, while over <i>moving picture</i> it
    /// answers Settling — which is no evidence and must not vote — so the run can only grow on a
    /// read, once per settle cap. That is six times slower at the dialogue timings. Counting both
    /// in "polls" and comparing against one threshold is the same defect as mixing device-independent
    /// pixels with physical ones: two quantities that are equal on the machine you tested and
    /// unequal on the user's. Seconds are what the threshold was always about.
    /// </para>
    ///
    /// <para>
    /// Eight, because it has to clear the seven-second ceiling with a margin and nothing else is
    /// competing for the space above it — a dialogue box waits as long as the player does. And the
    /// measurement is recency-correct for free: an empty read resets the run, so "still for eight
    /// seconds" already means "has not left in the last eight seconds", which is why persistence
    /// can be weighed before emptiness without a stale verdict outliving its evidence.
    /// </para>
    /// </summary>
    public static readonly TimeSpan LongerThanAnyCaption = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How long a verdict must stand before another can replace it. Twelve seconds is long enough
    /// to cover the cutscene-inside-a-dialogue case without thrashing, and short enough that a
    /// genuine change of content is followed within one line or two.
    /// </summary>
    public static readonly TimeSpan MinimumDwell = TimeSpan.FromSeconds(12);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Queue<RhythmSample> _samples = new();

    private DateTimeOffset _decidedAt;

    /// <summary>When the line currently on screen went up, or null if nothing is standing.</summary>
    private DateTimeOffset? _stillSince;

    /// <summary>The current verdict. <see cref="ContentKind.Unknown"/> until the evidence is in.</summary>
    public ContentKind Kind { get; private set; } = ContentKind.Unknown;

    /// <summary>Fraction of recent polls where the frame differed from what is on the overlay.</summary>
    public double MotionFraction => _samples.Count == 0
        ? 0
        : _samples.Count(s => s.Changed) / (double)_samples.Count;

    /// <summary>
    /// Fraction of OCR reads in the window that found nothing. Captions leave gaps; boxes do not.
    ///
    /// <para>
    /// Over the whole window rather than a recent slice, because reads are scarce — one per settle
    /// cap — and halving the window halves an evidence budget that is already only five at the
    /// dialogue timings. Staleness is handled where it belongs instead: persistence answers first
    /// and cannot be stale, since an empty read resets it.
    /// </para>
    /// </summary>
    public double EmptyFraction
    {
        get
        {
            var reads = Reads;
            return reads == 0 ? 0 : _samples.Count(s => s.HasText == false) / (double)reads;
        }
    }

    /// <summary>Reads in the window — the denominator, and the thing <see cref="MinimumReads"/> gates.</summary>
    private int Reads => _samples.Count(s => s.HasText is not null);

    /// <summary>
    /// How long the line currently on screen has been there unchanged, or zero if the region has
    /// just changed, just emptied, or has never been read. Surfaced because an adaptation nobody
    /// can see is indistinguishable from a bug.
    /// </summary>
    public TimeSpan StillFor => _stillSince is { } since ? _clock.GetUtcNow() - since : TimeSpan.Zero;

    /// <summary>
    /// Records one poll and re-decides. Called on every tick including the cheap ones — a poll
    /// that changed nothing is evidence about the content, and the commonest evidence there is.
    /// </summary>
    public ContentKind Observe(RhythmSample sample)
    {
        _samples.Enqueue(sample);
        while (_samples.Count > Window) _samples.Dequeue();

        Time(sample);
        Decide();
        return Kind;
    }

    /// <summary>
    /// Forgets everything. Auto-watch switching on, or a snip, means the next stretch is a fresh
    /// question — and a verdict carried over from the last session is a verdict about a screen
    /// nobody is looking at any more.
    /// </summary>
    public void Reset()
    {
        _samples.Clear();
        _stillSince = null;
        Kind = ContentKind.Unknown;
        _decidedAt = default;
    }

    /// <summary>
    /// Starts, extends or ends the clock on the line currently standing. Three cases, and the
    /// middle one is where the hard trap lives.
    ///
    /// <para>
    /// A NEW line, or an empty read, ends it: the thing that was being waited on is gone.
    /// </para>
    ///
    /// <para>
    /// A poll that found the frame unchanged starts it — and so does a read that came back with
    /// the SAME text as before. That second clause is what defeats the animated-background
    /// dialogue box: weather, an idling character, a scrolling sky behind a text panel. Every
    /// frame differs, so pixels say "video" on every poll; the words are identical, so the text
    /// says somebody is reading this. The words win.
    /// </para>
    ///
    /// <para>
    /// Anything else is a frame mid-change that was never read — genuinely no evidence either way
    /// — and it leaves the clock running rather than voting with silence. That is the change of
    /// unit doing real work: as a poll count, a stretch of Settling froze the run while the line
    /// sat there, so the same dialogue box scored differently depending on what was happening
    /// behind it. Time keeps running whether or not a poll had anything to say.
    /// </para>
    /// </summary>
    private void Time(RhythmSample sample)
    {
        if (sample.TextChanged == true || sample.HasText == false) _stillSince = null;
        else if (!sample.Changed || sample.TextChanged == false) _stillSince ??= _clock.GetUtcNow();
    }

    private void Decide()
    {
        if (_samples.Count < MinimumReads) return;

        var candidate = Weigh(Reads);

        if (candidate == ContentKind.Unknown || candidate == Kind) return;

        // Hysteresis. The first verdict is free - there is nothing to flap against - but every
        // one after it has to outlast the dwell, so alternating content settles on whichever it
        // spends most of its time being instead of chasing each transition.
        var now = _clock.GetUtcNow();
        if (Kind != ContentKind.Unknown && now - _decidedAt < MinimumDwell) return;

        Kind = candidate;
        _decidedAt = now;
    }

    private ContentKind Weigh(int reads)
    {
        // Persistence first, and it is safe there ONLY because it is measured in seconds against a
        // number taken from what subtitles are actually allowed to do. A caption may stand for
        // seven; a line still up after eight is not one. Nothing that leaves of its own accord
        // stays this long, so a line that did is one somebody is being waited on for.
        //
        // It is also the fresher of the two claims by construction, which is what earns it the
        // first look: an empty read resets the clock, so "still for eight seconds" already says
        // "has not left in eight seconds". A stale verdict cannot survive here the way a stale
        // fraction can.
        if (StillFor >= LongerThanAnyCaption) return ContentKind.Dialogue;

        // Then the gaps, for a region whose lines do leave. A box holds its text until the player
        // advances it, so an empty read means the box CLOSED - rarer than a line change, which is
        // what makes it the strong claim. Only once enough reads have happened for "empty" to be a
        // fact about the content rather than about a quiet moment before anyone has spoken.
        if (reads >= MinimumReads && EmptyFraction >= 0.25) return ContentKind.Moving;

        // Nothing ever holds still and nothing is ever empty: a caption over continuous footage,
        // or a region full of animation. Both want the impatient timings, so the motion signal is
        // allowed to decide here - where it is the only thing left and where being wrong costs
        // latency rather than money.
        //
        // It waits for a FULL window, and that is what keeps it last rather than merely written
        // last. Motion accumulates on every poll while persistence needs seconds to mature, so the
        // weakest signal is also the fastest one and it will win any race it is allowed to enter:
        // an animated background behind a static dialogue box reaches "moving on every poll" in two
        // seconds and gets called a film, four seconds before the line it is sitting behind is old
        // enough to speak for itself. A full window at the dialogue rate is fifteen seconds, which
        // is comfortably longer than LongerThanAnyCaption - so persistence always answers first
        // when it has an answer.
        if (_samples.Count >= Window && MotionFraction >= 0.8 && reads >= MinimumReads)
            return ContentKind.Moving;

        return ContentKind.Unknown;
    }

    /// <summary>
    /// The mode to run at right now. Unknown resolves to <see cref="WatchMode.Dialogue"/>, and that
    /// asymmetry is deliberate: being patient on a film costs a few late lines at the start, being
    /// impatient on a typewriter reveal costs a request per half-written sentence, permanently. The
    /// cheap mistake is the one to make while still deciding.
    /// </summary>
    public WatchMode Resolved => Kind == ContentKind.Moving ? WatchMode.Video : WatchMode.Dialogue;
}
