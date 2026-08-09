namespace GlassHudTranslator.Core.Capture;

/// <summary>
/// What kind of text auto-watch is looking at. Not a cosmetic label - the two differ in every
/// number that matters, and running one under the other's settings is the difference between a
/// translation arriving before the line leaves the screen and after.
/// </summary>
public enum WatchMode
{
    /// <summary>
    /// Game dialogue. Advances when the player advances it, reveals character by character, and
    /// sits inside a box that damps whatever is happening behind it. Patient settings: the line
    /// will still be there in a second.
    /// </summary>
    Dialogue,

    /// <summary>
    /// Subtitles over moving picture. The line appears whole, lives two to four seconds, and is
    /// then gone whether or not anything was translated. Impatient settings, and a floor to stop
    /// the spend running away.
    /// </summary>
    Video,
}

/// <summary>
/// The numbers one <see cref="WatchMode"/> runs at.
///
/// <para>
/// The reason this exists as a type rather than a pile of constants: a four-minute cap is right for
/// a cutscene and absurd for a film, a three-second settle is right for a dialogue box and ruinous
/// over video, and there is no single set of numbers that is defensible for both. Every field here
/// was wrong for one of the two workloads before it was split.
/// </para>
/// </summary>
public sealed record WatchPacing
{
    /// <summary>How often the screen is sampled. Costs a BitBlt and a 64x24 thumbnail per poll.</summary>
    public required double PollsPerSecond { get; init; }

    public required int RequiredStillTicks { get; init; }

    /// <summary>Translate anyway after this, even if the frame never holds still.</summary>
    public required TimeSpan SettleCap { get; init; }

    /// <summary>
    /// The shortest gap allowed between two translations. A READABILITY bound before it is a
    /// spending one: Arabic arriving faster than about a second and a half apart cannot be read,
    /// so paying for it is paying for something nobody consumes.
    ///
    /// <para>
    /// It is deliberately not the quota guard. Pixel noise in a busy region does not cost requests
    /// — the text is unchanged, so the cache answers for nothing. What costs requests is DISTINCT
    /// text, and distinct text arriving every half second is not being read either.
    /// </para>
    /// </summary>
    public required TimeSpan MinimumInterval { get; init; }

    /// <summary>Say something on the overlay after this long, and after this many requests.</summary>
    public required TimeSpan WarnAfter { get; init; }

    public required int WarnAfterRequests { get; init; }

    /// <summary>Switch off after this long, or this many requests, whichever comes first.</summary>
    public required TimeSpan StopAfter { get; init; }

    public required int StopAfterRequests { get; init; }

    /// <summary>
    /// Both caps in both units, because time is a poor proxy for spend and spend is a poor proxy
    /// for "how long have I left this on". Four minutes of cutscene is a dozen requests; four
    /// minutes of film is eighty. Whichever arrives first is the one that meant something.
    /// </summary>
    public static WatchPacing For(WatchMode mode) => mode switch
    {
        // Two polls a second because the game's render thread must always win, and a settle of two
        // ticks at that rate is half a second - nobody perceives it. No floor: a player advancing
        // dialogue by hand cannot outrun one.
        WatchMode.Video => new WatchPacing
        {
            // Four, not two. The 2 fps default was chosen so a capture tick could not compete with
            // a game's render thread. Watching a video there is no game, so the headroom is real
            // and the poll rate is the cheapest latency there is to buy.
            PollsPerSecond = 4,
            RequiredStillTicks = 2,

            // The number that fixes the reported delay. Over moving picture the stillness test can
            // never pass - the whole region is video, and Otsu re-thresholds per frame - so EVERY
            // release comes from this cap. At 3 seconds the Arabic arrived after the caption it
            // translated had already gone.
            SettleCap = TimeSpan.FromMilliseconds(800),
            MinimumInterval = TimeSpan.FromMilliseconds(1500),

            // Measured in the length of the thing being watched. Ten minutes in is a fair moment to
            // mention what it is costing; forty-five is an episode, and 1,200 requests is about a
            // feature film and roughly a third of a day's free allowance.
            WarnAfter = TimeSpan.FromMinutes(10),
            WarnAfterRequests = 300,
            StopAfter = TimeSpan.FromMinutes(45),
            StopAfterRequests = 1200,
        },

        _ => new WatchPacing
        {
            PollsPerSecond = 2,
            RequiredStillTicks = 2,
            SettleCap = TimeSpan.FromSeconds(3),
            MinimumInterval = TimeSpan.Zero,

            // The two numbers this was asked for. The request ceilings sit well above anything a
            // cutscene produces, so in ordinary play it is the clock that speaks - they are there
            // for the case where the "dialogue" turns out to be an animated banner.
            WarnAfter = TimeSpan.FromMinutes(2),
            WarnAfterRequests = 120,
            StopAfter = TimeSpan.FromMinutes(4),
            StopAfterRequests = 400,
        },
    };

    public TimeSpan PollInterval => TimeSpan.FromSeconds(1.0 / Math.Max(0.5, PollsPerSecond));
}

/// <summary>Whether auto-watch should keep going, say something, or stop.</summary>
public enum WatchVerdict
{
    Run,

    /// <summary>Returned ONCE, on the poll that crosses the threshold. Not on every poll after.</summary>
    Warn,

    Stop,
}

/// <summary>
/// One run of auto-watch: how long it has been on, what it has spent, and how fast the thing it is
/// watching actually changes.
///
/// <para>
/// The last of those is the part that improves with use. Everything else in this app is told its
/// timings by a constant somebody chose; this measures the content in front of it and adjusts. A
/// dialogue box that advances every eight seconds can afford a patient settle, and the same code
/// watching subtitles that change every three seconds cannot - and neither the game nor the user
/// should have to say which is which.
/// </para>
///
/// <para>
/// It is measurement and adaptation, not learning in the machine-learning sense: no model, no
/// training data, nothing kept between runs, and every decision is a clamp on a median that can be
/// read off the Diagnostics tab. That is deliberate. A tuning loop nobody can inspect is a tuning
/// loop nobody can debug, and this one has to be explainable to somebody who is not going to read
/// the source.
/// </para>
/// </summary>
public sealed class WatchSession(WatchPacing pacing, TimeProvider? clock = null)
{
    /// <summary>
    /// How many gaps between translations the cadence is taken over. Eight is enough for a median
    /// to mean something and short enough to follow a scene change within half a minute.
    /// </summary>
    public const int CadenceWindow = 8;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Queue<TimeSpan> _gaps = new();

    private DateTimeOffset _startedAt;
    private DateTimeOffset? _lastTranslation;
    private bool _warned;

    public WatchPacing Pacing { get; } = pacing;

    /// <summary>The Advanced-mode escape hatch: warn, but never switch off.</summary>
    public bool Unbounded { get; init; }

    public int Requests { get; private set; }

    public TimeSpan Elapsed => _clock.GetUtcNow() - _startedAt;

    /// <summary>
    /// The median gap between the last <see cref="CadenceWindow"/> translations, or null until
    /// there are at least three to take a median of. Median rather than mean because one pause
    /// while the player reads should not stretch the estimate for the next ten lines.
    /// </summary>
    public TimeSpan? Cadence
    {
        get
        {
            if (_gaps.Count < 3) return null;

            var sorted = _gaps.Order().ToArray();
            return sorted[sorted.Length / 2];
        }
    }

    /// <summary>
    /// True when the screen is producing new text faster than the floor allows, which means lines
    /// are being skipped. Worth saying out loud rather than silently dropping them - it is the
    /// difference between "this tool is unreliable" and "this content is faster than this tool".
    /// </summary>
    public bool OutrunningTheFloor =>
        Cadence is { } cadence && cadence < Pacing.MinimumInterval;

    public void Start()
    {
        _startedAt = _clock.GetUtcNow();
        _lastTranslation = null;
        _gaps.Clear();
        Requests = 0;
        _warned = false;
    }

    /// <summary>
    /// The floor, asked BEFORE the frame is offered to the settle gate. Order matters: the gate
    /// commits a frame the moment it calls it Ready, so asking afterwards would mark a frame as
    /// handled that was never translated, and the screen would sit unchanged forever.
    /// </summary>
    public bool MayTranslate()
    {
        if (Pacing.MinimumInterval <= TimeSpan.Zero) return true;
        if (_lastTranslation is not { } last) return true;

        return _clock.GetUtcNow() - last >= Pacing.MinimumInterval;
    }

    /// <summary>Records one translation actually shown, and folds its gap into the cadence.</summary>
    public void Translated()
    {
        var now = _clock.GetUtcNow();
        Requests++;

        if (_lastTranslation is { } last)
        {
            _gaps.Enqueue(now - last);
            while (_gaps.Count > CadenceWindow) _gaps.Dequeue();
        }

        _lastTranslation = now;
    }

    /// <summary>
    /// A translation that happened while this run was going but was not part of it — a hotkey
    /// press, or a snip of somewhere else on the screen.
    ///
    /// <para>
    /// Counted against the cap and against nothing else, and the split is the whole point. The cap
    /// is about total spend, so anything that costs a request has to move it. The cadence and the
    /// floor are about the rhythm of the content being watched, and a person pressing a key is not
    /// the content: folding a snip in as a gap tells the adaptive settle deadline that the dialogue
    /// just advanced when it did not, and one such sample can drag the median for the next eight
    /// lines.
    /// </para>
    /// </summary>
    public void CountedOutsideTheRhythm() => Requests++;

    /// <summary>
    /// Asked once per poll. Measured from when auto-watch was switched ON, never from the last
    /// change: the old idle timer resets on any movement, so over a playing video - or a game with
    /// animation inside the capture region - it can never fire at all. A cap that a moving screen
    /// disables is not a cap.
    /// </summary>
    public WatchVerdict Check()
    {
        var elapsed = Elapsed;

        if (!Unbounded && (elapsed >= Pacing.StopAfter || Requests >= Pacing.StopAfterRequests))
            return WatchVerdict.Stop;

        if (_warned) return WatchVerdict.Run;
        if (elapsed < Pacing.WarnAfter && Requests < Pacing.WarnAfterRequests) return WatchVerdict.Run;

        _warned = true;
        return WatchVerdict.Warn;
    }

    /// <summary>
    /// The settle options to run with, tightened by what the content has turned out to be.
    ///
    /// <para>
    /// The cap is the adaptive part. It is a deadline for "this frame is never going to hold
    /// still", and the right deadline depends entirely on how long the text stays: a third of the
    /// observed cadence means the translation lands early in the line's life rather than after it.
    /// It only ever tightens the mode's own cap - adaptation cannot make the app slower than the
    /// number a human chose, only faster.
    /// </para>
    /// </summary>
    public SettleOptions Settle()
    {
        var cap = Pacing.SettleCap;

        if (Cadence is { } cadence)
        {
            var third = cadence / 3;
            if (third < cap) cap = third;
            if (cap < MinimumSettleCap) cap = MinimumSettleCap;
        }

        return new SettleOptions
        {
            RequiredStillTicks = Pacing.RequiredStillTicks,
            Cap = cap,
        };
    }

    /// <summary>
    /// Below this the cap stops being a deadline and starts being a guarantee of translating
    /// mid-change, which costs a request to produce half a sentence.
    /// </summary>
    public static readonly TimeSpan MinimumSettleCap = TimeSpan.FromMilliseconds(400);
}
