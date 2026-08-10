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
/// <b>Persistence is the strong signal for dialogue.</b> How many polls a line survives unchanged
/// is exactly the question being asked, phrased directly rather than inferred from pixels — and it
/// is measured on the TEXT, so a moving background cannot fake it. A line that outlives several
/// seconds of polling is being waited on.
/// </para>
///
/// <para>
/// Both strong signals are text-level, which is what makes them robust: they are already paid for
/// by the OCR the pipeline was running anyway, and they see through exactly the animation that
/// defeats a pixel comparison.
/// </para>
///
/// <para>
/// <b>Switching is deliberately reluctant.</b> A verdict needs a clear majority of a rolling
/// window, and after any switch the next one is refused for <see cref="MinimumDwell"/>. Both exist
/// because content genuinely alternates — a cutscene inside a game, a paused video — and a
/// classifier that follows every wobble is worse than either fixed mode: it spends its life in the
/// wrong one, arriving there late.
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
    /// Reads with text needed before the empty fraction means anything. Below this, "no text seen"
    /// is far more likely to be a game that has not started talking yet than a caption gap.
    /// </summary>
    public const int MinimumReads = 6;

    /// <summary>
    /// A line surviving this many consecutive polls is being waited on rather than passing through.
    /// Four polls is two seconds of dialogue-rate watching and one second of video-rate: longer
    /// than any caption holds still, shorter than any dialogue box a reader is reading.
    /// </summary>
    public const int PersistentTicks = 4;

    /// <summary>
    /// Persistence is measured over the recent half of the window only, and that is what makes it
    /// a signal about NOW rather than a claim that ages badly.
    ///
    /// <para>
    /// Measured over the whole window it does not decay fast enough to be useful: when a game cuts
    /// to a cutscene, the dialogue box that was on screen a moment ago leaves a long run sitting
    /// at the head of the window, and that one stale run outvotes every caption observed since —
    /// so the switch waits for the entire window to roll over instead of following the content.
    /// Half the window keeps a genuine dialogue box comfortably above the threshold while letting
    /// a finished one stop arguing.
    /// </para>
    /// </summary>
    public const int RecentWindow = Window / 2;

    /// <summary>
    /// How long a verdict must stand before another can replace it. Twelve seconds is long enough
    /// to cover the cutscene-inside-a-dialogue case without thrashing, and short enough that a
    /// genuine change of content is followed within one line or two.
    /// </summary>
    public static readonly TimeSpan MinimumDwell = TimeSpan.FromSeconds(12);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Queue<RhythmSample> _samples = new();

    private DateTimeOffset _decidedAt;
    private int _longestRun;

    /// <summary>The current verdict. <see cref="ContentKind.Unknown"/> until the evidence is in.</summary>
    public ContentKind Kind { get; private set; } = ContentKind.Unknown;

    /// <summary>Fraction of recent polls where the frame differed from what is on the overlay.</summary>
    public double MotionFraction => _samples.Count == 0
        ? 0
        : _samples.Count(s => s.Changed) / (double)_samples.Count;

    /// <summary>Fraction of recent OCR reads that found nothing. Captions leave gaps; boxes do not.</summary>
    public double EmptyFraction
    {
        get
        {
            var reads = _samples.Count(s => s.HasText is not null);
            return reads == 0 ? 0 : _samples.Count(s => s.HasText == false) / (double)reads;
        }
    }

    /// <summary>The longest run of consecutive polls one line survived, within the window.</summary>
    public int LongestStillRun => _longestRun;

    /// <summary>
    /// Records one poll and re-decides. Called on every tick including the cheap ones — a poll
    /// that changed nothing is evidence about the content, and the commonest evidence there is.
    /// </summary>
    public ContentKind Observe(RhythmSample sample)
    {
        _samples.Enqueue(sample);
        while (_samples.Count > Window) _samples.Dequeue();

        // Recomputed across the window rather than carried as a high-water mark. That is not a
        // tidiness point: a mark that only ever rises means the first dialogue box of the session
        // argues for Dialogue an hour into a film, because nothing can ever lower it again. As a
        // pure function of the window it decays on its own as the evidence ages out. Thirty
        // samples per poll, against a poll that costs a screen grab.
        _longestRun = LongestRunIn(_samples.TakeLast(RecentWindow));

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
        _longestRun = 0;
        Kind = ContentKind.Unknown;
        _decidedAt = default;
    }

    /// <summary>
    /// The longest stretch of consecutive polls, inside the window, during which one line stayed
    /// up. Three cases, and the middle one is where the hard trap lives.
    ///
    /// <para>
    /// A NEW line, or an empty read, ends a run: the thing that was being waited on is gone.
    /// </para>
    ///
    /// <para>
    /// A poll that found the frame unchanged extends it — and so does a read that came back with
    /// the SAME text as before. That second clause is what defeats the animated-background
    /// dialogue box: weather, an idling character, a scrolling sky behind a text panel. Every
    /// frame differs, so pixels say "video" on every poll; the words are identical, so the text
    /// says somebody is reading this. The words win.
    /// </para>
    ///
    /// <para>
    /// Anything else is a frame mid-change that was never read — genuinely no evidence either way
    /// — and it leaves the run exactly as it is rather than voting with silence.
    /// </para>
    /// </summary>
    private static int LongestRunIn(IEnumerable<RhythmSample> samples)
    {
        var longest = 0;
        var run = 0;

        foreach (var sample in samples)
        {
            if (sample.TextChanged == true || sample.HasText == false) run = 0;
            else if (!sample.Changed || sample.TextChanged == false) run++;

            longest = Math.Max(longest, run);
        }

        return longest;
    }

    private void Decide()
    {
        if (_samples.Count < MinimumReads) return;

        var reads = _samples.Count(s => s.HasText is not null);
        var candidate = Weigh(reads);

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
        // Persistence first, and it is close to conclusive: nothing that passes through of its own
        // accord stays put this long, so a line that did is one somebody is being waited on for.
        if (_longestRun >= PersistentTicks) return ContentKind.Dialogue;

        // Then the gaps. Only once enough reads have happened for "empty" to be a fact about the
        // content rather than about a quiet moment.
        if (reads >= MinimumReads && EmptyFraction >= 0.25) return ContentKind.Moving;

        // Nothing ever holds still and nothing is ever empty: a caption over continuous footage,
        // or a region full of animation. Both want the impatient timings, so the motion signal is
        // allowed to decide here - where it is the only thing left and where being wrong costs
        // latency rather than money.
        if (MotionFraction >= 0.8 && reads >= MinimumReads) return ContentKind.Moving;

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
