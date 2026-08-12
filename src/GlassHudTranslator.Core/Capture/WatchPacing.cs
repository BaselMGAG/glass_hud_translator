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

    /// <summary>
    /// Work it out. <see cref="ContentRhythm"/> watches how the region behaves — whether lines
    /// persist, whether it empties between them — and switches the timings itself, so a cutscene
    /// inside a game gets video pacing without anybody reaching for a menu mid-scene.
    ///
    /// <para>
    /// Never a <see cref="WatchPacing"/> of its own: it resolves to one of the two above on every
    /// poll. Asking <see cref="WatchPacing.For"/> for it returns the Dialogue numbers, which is
    /// what an undecided detector runs on anyway.
    /// </para>
    /// </summary>
    Auto,
}

/// <summary>
/// The order the modes are offered in, and the single place that order is written down.
///
/// <para>
/// It exists because there are two surfaces — the Settings dropdown and the toolbar button — and
/// they disagreed. The toolbar flipped between Dialogue and Video, so <see cref="WatchMode.Auto"/>
/// could not be reached from it at all, while the same toolbar drew an Auto icon for a state it had
/// no way of producing. A button that shows a mode it cannot select is worse than one that does not
/// offer it. Anything cycling modes goes through <see cref="After"/>.
/// </para>
/// </summary>
public static class WatchModes
{
    /// <summary>Dialogue, Video, Auto — least clever first, so the cycle reads as an escalation.</summary>
    public static readonly IReadOnlyList<WatchMode> InOrder =
        [WatchMode.Dialogue, WatchMode.Video, WatchMode.Auto];

    /// <summary>The next mode round the cycle, wrapping. Unknown values restart at the beginning.</summary>
    public static WatchMode After(WatchMode current)
    {
        var at = InOrder.ToList().IndexOf(current);
        return at < 0 ? InOrder[0] : InOrder[(at + 1) % InOrder.Count];
    }
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

    /// <summary>Stop waiting for the pixels after this and start reading the words instead.</summary>
    public required TimeSpan SettleCap { get; init; }

    /// <summary>
    /// How many readings that never agree the gate takes before it stops reading and waits out the
    /// deadline again. Required rather than defaulted for the reason every field here is: the two
    /// modes disagree about every number, and a new one that silently takes the same value in both
    /// has not been decided, only skipped.
    /// </summary>
    public required int ReadsBeforeGivingUp { get; init; }

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

            // The number that decides the delay, and it is the ONLY one worth touching. Over moving
            // picture the stillness test can never pass - the whole region is video, and Otsu
            // re-thresholds per frame - so EVERY release comes from this cap, which makes it a
            // straight wait rather than a deadline. At 3 seconds the Arabic arrived after the
            // caption had gone; at 800 ms it was still the largest item in the budget, bigger than
            // the OCR and comparable to the whole network round trip.
            //
            // 400 ms is the floor rather than a guess: it is MinimumSettleCap, already documented
            // as the point below which the cap "stops being a deadline and starts being a guarantee
            // of translating mid-change". The only thing the wait buys over video is not catching a
            // caption mid fade-in, and a subtitle fade is one to three frames - well under this.
            SettleCap = TimeSpan.FromMilliseconds(400),

            // Three, not four. Readings are 250 ms apart at this poll rate, so three of them is
            // 750 ms of a caption that is only allowed to live for about a second at its shortest -
            // a bound any looser stops being a bound within the life of the thing it is bounding.
            ReadsBeforeGivingUp = 3,

            // Was 1500, which is longer than a subtitle is allowed to be short: Netflix's floor is
            // 20 frames, five sixths of a second, so a conformant track can legally change faster
            // than we were willing to look. Every caption arriving inside the floor was not delayed,
            // it was DROPPED - the poll is skipped and the line is gone before the next one asks.
            // A second still reads as a floor and no longer refuses to keep up with the source.
            MinimumInterval = TimeSpan.FromMilliseconds(1000),

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

            // Four, at 500 ms apart, so two seconds of a region whose text never reads the same
            // way twice is enough to conclude it is not showing a sentence. A dialogue box holds
            // its line until the player advances it, so reaching this at all means the region is
            // looking at scenery.
            ReadsBeforeGivingUp = 4,

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
    private bool _stopped;

    public WatchPacing Pacing { get; private set; } = pacing;

    /// <summary>
    /// Swaps the timings mid-run, for <see cref="WatchMode.Auto"/> when the detector changes its
    /// mind about what it is watching.
    ///
    /// <para>
    /// Deliberately does NOT touch the clock, the request count or the cadence samples. The caps
    /// are measured from when the user switched auto-watch on, and a run that has already spent
    /// forty requests has spent them whatever the content turned out to be — resetting the
    /// accounting on a mode change would make an alternating scene an unbounded session, which is
    /// precisely the guard the caps exist to be.
    /// </para>
    /// </summary>
    public void Adapt(WatchPacing pacing)
    {
        ArgumentNullException.ThrowIfNull(pacing);
        Pacing = pacing;
    }

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
        _stopped = false;
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
        // Stop is TERMINAL. Without this, changing mode after a run has hit its cap revives it,
        // because the check is against the CURRENT mode's ceiling and video's is ten times
        // dialogue's - so flipping modes would be a way to run past every limit the app has, which
        // is precisely what the cap exists to prevent. The clock is not being reset, only the
        // threshold moved, and that is enough.
        if (_stopped) return WatchVerdict.Stop;

        var elapsed = Elapsed;

        if (!Unbounded && (elapsed >= Pacing.StopAfter || Requests >= Pacing.StopAfterRequests))
        {
            _stopped = true;
            return WatchVerdict.Stop;
        }

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

        // Every field of SettleOptions is either set here or is a default that this method is
        // content to reassert, and that is not a stylistic point. AutoWatch calls Retune(Settle())
        // on EVERY poll, so anything omitted here is not "left alone" - it is reset to its default
        // twice a second. A value handed to the gate's constructor survives every unit test written
        // against it and dies on the first poll in production.
        return new SettleOptions
        {
            RequiredStillTicks = Pacing.RequiredStillTicks,
            Cap = cap,
            ReadsBeforeGivingUp = Pacing.ReadsBeforeGivingUp,
        };
    }

    /// <summary>
    /// Below this the cap stops being a deadline and starts being a guarantee of translating
    /// mid-change, which costs a request to produce half a sentence.
    /// </summary>
    public static readonly TimeSpan MinimumSettleCap = TimeSpan.FromMilliseconds(400);
}
