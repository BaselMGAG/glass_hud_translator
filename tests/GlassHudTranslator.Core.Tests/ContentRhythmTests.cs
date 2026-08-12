using GlassHudTranslator.Core.Capture;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The detector behind <see cref="WatchMode.Auto"/>. Every test here is a shape of content the
/// classifier has to survive, and the awkward ones — animation behind a static box, a paused
/// video, a caption that lingers — are the point rather than the edge.
///
/// <para>
/// <b>Every test drives a clock, and that is not ceremony.</b> Persistence is measured in seconds
/// now, so a test that never advances time is a test where no line is ever old enough to be a
/// dialogue box. It also forces the sample streams to be honest about their own pacing, which is
/// what the earlier version of this file got wrong: it fed an OCR read on every single poll, and
/// the poll loop does that on no verdict at all.
/// </para>
/// </summary>
public class ContentRhythmTests
{
    /// <summary>One watched region, with a clock that advances one poll per observation.</summary>
    private sealed class Screen(TimeSpan poll)
    {
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

        /// <summary>How long one poll takes at this mode's rate. Helpers count in time, not ticks.</summary>
        public TimeSpan Poll => poll;

        public ContentRhythm Rhythm { get; private set; } = null!;

        public Screen Start()
        {
            Rhythm = new ContentRhythm(Clock);
            return this;
        }

        public void Observe(RhythmSample sample)
        {
            Rhythm.Observe(sample);
            Clock.Advance(poll);
        }

        public void Wait(TimeSpan howLong) => Clock.Advance(howLong);
    }

    /// <summary>The dialogue timings: two polls a second against a three second settle cap.</summary>
    private static Screen Dialogue() => new Screen(TimeSpan.FromMilliseconds(500)).Start();

    /// <summary>The video timings: four polls a second against an 800 ms cap.</summary>
    private static Screen Video() => new Screen(TimeSpan.FromMilliseconds(250)).Start();

    /// <summary>
    /// A dialogue box: the line goes up and the frame gate answers Unchanged from then on, so no
    /// OCR runs at all until the player advances it. <paramref name="holds"/> is real time.
    /// </summary>
    private static void WaitingLine(Screen screen, TimeSpan holds)
    {
        screen.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: true));
        StillThere(screen, holds);
    }

    /// <summary>
    /// Keeps the line already up on screen there for longer. Distinct from <see cref="WaitingLine"/>
    /// because announcing a line is announcing a NEW one, which resets the stillness clock — so a
    /// test that wants "the same line, six seconds later" must not call the other helper twice.
    /// </summary>
    private static void StillThere(Screen screen, TimeSpan holds)
    {
        for (var i = 0; i < holds / screen.Poll; i++)
            screen.Observe(new RhythmSample(Changed: false));
    }

    /// <summary>A caption: it appears, is read once, then the region is empty until the next one.</summary>
    private static void PassingLine(Screen screen, int gapPolls = 2)
    {
        screen.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: true));
        for (var i = 0; i < gapPolls; i++)
            screen.Observe(new RhythmSample(Changed: true, HasText: false, TextChanged: null));
    }

    /// <summary>
    /// A caption that is read more than once while it sits there. Subtitling practice puts the
    /// ceiling at six to seven seconds and the floor around one, so at the video poll rate a normal
    /// two-line caption is read several times over before it leaves — which makes the multi-read
    /// caption the common case, not the exotic one.
    /// </summary>
    private static void LingeringLine(Screen screen, int reads, int gapPolls = 2)
    {
        screen.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: true));
        for (var i = 0; i < reads; i++)
            screen.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: false));
        for (var i = 0; i < gapPolls; i++)
            screen.Observe(new RhythmSample(Changed: true, HasText: false, TextChanged: null));
    }

    /// <summary>
    /// The stream the PRODUCTION loop actually produces, which is not the one most helpers above
    /// feed.
    ///
    /// <para>
    /// <see cref="RhythmSample.HasText"/> is populated on exactly one verdict — Ready — so a read
    /// costs a whole settle cap. Every other poll carries nulls. Over moving picture the stillness
    /// test can never pass, so there are no Unchanged polls at all and the window fills with
    /// Settling: at the dialogue rate that is five polls of silence for every one that says
    /// anything. Feeding a read per poll hands the classifier six times the evidence it will ever
    /// have on a real screen.
    /// </para>
    /// </summary>
    private static void MovingPictureAsThePollLoopSeesIt(
        Screen screen, int reads, int pollsPerRead, int captionEveryNthRead = 4)
    {
        for (var read = 0; read < reads; read++)
        {
            for (var i = 0; i < pollsPerRead - 1; i++)
                screen.Observe(new RhythmSample(Changed: true));

            // Captions leave gaps, but a read only lands in one occasionally — the gap is about a
            // second and the reads are three seconds apart.
            var inAGap = read % captionEveryNthRead == captionEveryNthRead - 1;

            screen.Observe(inAGap
                ? new RhythmSample(Changed: true, HasText: false, TextChanged: null)
                : new RhythmSample(Changed: true, HasText: true, TextChanged: true));
        }
    }

    [Fact]
    public void NothingIsClaimedBeforeThereIsEvidence()
    {
        var screen = Dialogue();

        screen.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: true));

        Assert.Equal(ContentKind.Unknown, screen.Rhythm.Kind);

        // And an undecided detector runs Dialogue, because being patient on a film costs a few
        // late lines while being impatient on a typewriter reveal costs a request per fragment.
        Assert.Equal(WatchMode.Dialogue, screen.Rhythm.Resolved);
    }

    [Fact]
    public void ALineThatSitsThereIsDialogue()
    {
        var screen = Dialogue();
        WaitingLine(screen, holds: TimeSpan.FromSeconds(12));

        Assert.Equal(ContentKind.Dialogue, screen.Rhythm.Kind);
        Assert.Equal(WatchMode.Dialogue, screen.Rhythm.Resolved);
    }

    [Fact]
    public void ALineIsNotCalledDialogueUntilItHasOutlastedTheLongestLegalCaption()
    {
        // Seven seconds is the published maximum for a subtitle, so at six the honest answer is
        // still "could be either". Deciding earlier is what put a whole film into patient timings.
        var screen = Dialogue();
        WaitingLine(screen, holds: TimeSpan.FromSeconds(6));

        Assert.NotEqual(ContentKind.Dialogue, screen.Rhythm.Kind);

        StillThere(screen, holds: TimeSpan.FromSeconds(4));

        Assert.Equal(ContentKind.Dialogue, screen.Rhythm.Kind);
    }

    [Fact]
    public void CaptionsWithGapsBetweenThemAreMoving()
    {
        var screen = Video();
        for (var i = 0; i < 6; i++) PassingLine(screen);

        Assert.Equal(ContentKind.Moving, screen.Rhythm.Kind);
        Assert.Equal(WatchMode.Video, screen.Rhythm.Resolved);
    }

    [Fact]
    public void AnimationBehindAStaticDialogueBoxIsStillDialogue()
    {
        // THE trap. Every frame differs, so a pixel comparison says video on every single poll:
        // weather, an idling character, a scrolling sky. The words are identical, which is what
        // the repeat gate reports, and the words are what decide.
        var screen = Dialogue();

        screen.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: true));
        for (var i = 0; i < 24; i++)
            screen.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: false));

        Assert.Equal(ContentKind.Dialogue, screen.Rhythm.Kind);
    }

    [Fact]
    public void APausedVideoLooksLikeDialogueAndThatIsTheRightAnswer()
    {
        // It is indistinguishable from a dialogue box, and correctly so: nothing is going to
        // vanish while it is paused, so the patient timings are exactly what it wants.
        var screen = Dialogue();
        WaitingLine(screen, holds: TimeSpan.FromSeconds(12));

        Assert.Equal(WatchMode.Dialogue, screen.Rhythm.Resolved);
    }

    [Fact]
    public void ContinuousCaptionsWithNoGapsAreStillMoving()
    {
        // Dense dialogue in a film: one caption replaced directly by the next, region never empty,
        // nothing ever holds still. Motion is the only signal left, and it is allowed to decide
        // here because both readings want the impatient timings anyway.
        var screen = Video();

        for (var i = 0; i < ContentRhythm.Window + 4; i++)
            screen.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: true));

        Assert.Equal(ContentKind.Moving, screen.Rhythm.Kind);
    }

    [Fact]
    public void SettlingPollsDoNotVoteEitherWay()
    {
        // A frame mid-change that was never read is genuinely no evidence. If these started the
        // stillness clock, a caption gap over moving footage would read as a line sitting still.
        var screen = Video();

        for (var i = 0; i < 20; i++) screen.Observe(new RhythmSample(Changed: true));

        Assert.Equal(ContentKind.Unknown, screen.Rhythm.Kind);
        Assert.Equal(TimeSpan.Zero, screen.Rhythm.StillFor);
    }

    [Fact]
    public void AVerdictHasToOutlastTheDwellBeforeAnotherCanReplaceIt()
    {
        // Video pacing so the caption burst below fits inside the dwell in wall-clock terms; at
        // the dialogue rate eight captions take twelve seconds and would outlast it honestly.
        var screen = Video();

        WaitingLine(screen, holds: TimeSpan.FromSeconds(12));
        Assert.Equal(ContentKind.Dialogue, screen.Rhythm.Kind);

        // A burst of caption-shaped evidence immediately afterwards is refused: content genuinely
        // alternates, and a classifier that follows every wobble spends its life in the wrong mode
        // arriving there late.
        for (var i = 0; i < 6; i++) PassingLine(screen);
        Assert.Equal(ContentKind.Dialogue, screen.Rhythm.Kind);

        screen.Wait(ContentRhythm.MinimumDwell + TimeSpan.FromSeconds(1));
        PassingLine(screen);

        Assert.Equal(ContentKind.Moving, screen.Rhythm.Kind);
    }

    [Fact]
    public void TheFirstVerdictIsFreeBecauseThereIsNothingToFlapAgainst()
    {
        var screen = Dialogue();

        // No dwell has elapsed at all, and it still decides — the dwell guards CHANGES of mind.
        WaitingLine(screen, holds: TimeSpan.FromSeconds(12));

        Assert.Equal(ContentKind.Dialogue, screen.Rhythm.Kind);
    }

    [Fact]
    public void ACutsceneInsideAGameIsFollowedOnceItLasts()
    {
        var screen = Dialogue();

        WaitingLine(screen, holds: TimeSpan.FromSeconds(12));
        Assert.Equal(WatchMode.Dialogue, screen.Rhythm.Resolved);

        screen.Wait(ContentRhythm.MinimumDwell + TimeSpan.FromSeconds(1));
        for (var i = 0; i < 8; i++) PassingLine(screen);

        Assert.Equal(WatchMode.Video, screen.Rhythm.Resolved);

        // ...and back again when the cutscene ends and the dialogue box returns. The stale caption
        // gaps still sitting in the window must not outvote a line that is on screen right now.
        screen.Wait(ContentRhythm.MinimumDwell + TimeSpan.FromSeconds(1));
        WaitingLine(screen, holds: TimeSpan.FromSeconds(12));

        Assert.Equal(WatchMode.Dialogue, screen.Rhythm.Resolved);
    }

    [Fact]
    public void ALongCaptionThatStillLeavesGapsIsMoving()
    {
        // The case the subtitle standards say is ordinary: a two-line caption near the published
        // seven-second ceiling, read five or six times at the video rate before it goes. Every one
        // of those reads says "same text as last time", which is the dialogue signal exactly — and
        // yet the region empties between every caption, which is the strongest evidence for video
        // there is. Persistence must not be able to answer on its own here, and the only reason it
        // cannot is that its threshold is a duration taken from what a caption is allowed to do.
        var screen = Video();

        for (var i = 0; i < 6; i++) LingeringLine(screen, reads: 5);

        Assert.Equal(ContentKind.Moving, screen.Rhythm.Kind);
        Assert.Equal(WatchMode.Video, screen.Rhythm.Resolved);
    }

    [Fact]
    public void TheReadBudgetAtDialoguePacingIsEnoughToDecideWith()
    {
        // Auto starts on the dialogue timings, because Unknown resolves to Dialogue. So the FIRST
        // question the classifier is ever asked is asked at two polls a second against a three
        // second settle cap — one read per six polls. If the evidence needed to leave that state
        // cannot fit in the window at that rate, Auto can never reach Video at all: the mode would
        // be inoperative in precisely the workload it exists for, and every behavioural test here
        // would still pass, because a helper can hand out reads the poll loop would never produce.
        foreach (var mode in new[] { WatchMode.Dialogue, WatchMode.Video })
        {
            var pacing = WatchPacing.For(mode);
            var pollsPerRead = pacing.SettleCap.TotalSeconds * pacing.PollsPerSecond;
            var readsInWindow = ContentRhythm.Window / pollsPerRead;

            Assert.True(readsInWindow >= ContentRhythm.MinimumReads,
                $"At {mode} pacing a full window holds only {readsInWindow:0.0} reads, and " +
                $"MinimumReads is {ContentRhythm.MinimumReads}. The gate can never open.");

            // And the window has to be long enough in SECONDS as well as rich enough in reads,
            // because the motion signal is gated on a full window and persistence is measured
            // against a threshold in seconds. Window is counted in polls, so it silently shortens
            // every time the poll rate goes up — which is exactly how raising the dialogue rate to
            // cut the delay broke the read budget above without anyone editing ContentRhythm.
            var windowSeconds = ContentRhythm.Window / pacing.PollsPerSecond;

            Assert.True(windowSeconds >= ContentRhythm.LongerThanAnyCaption.TotalSeconds,
                $"At {mode} pacing a full window is only {windowSeconds:0.0}s, which is shorter "
                + $"than the {ContentRhythm.LongerThanAnyCaption.TotalSeconds:0}s a caption is "
                + "allowed to hold still for - so the weakest signal would decide before the "
                + "strongest one had matured.");
        }
    }

    [Fact]
    public void AFilmIsRecognisedFromTheEvidenceThePollLoopActuallyProduces()
    {
        var screen = Dialogue();

        // Dialogue pacing, because that is where Auto starts: 2 polls/sec against a 3 s cap, so
        // one read every six polls and Settling in between.
        MovingPictureAsThePollLoopSeesIt(screen, reads: 12, pollsPerRead: 6);

        Assert.Equal(WatchMode.Video, screen.Rhythm.Resolved);
    }

    [Fact]
    public void SwitchingAutoWatchOnAsksTheQuestionAgain()
    {
        var screen = Video();
        for (var i = 0; i < 6; i++) PassingLine(screen);
        Assert.Equal(ContentKind.Moving, screen.Rhythm.Kind);

        screen.Rhythm.Reset();

        // A verdict carried over is a verdict about a screen nobody is looking at any more.
        Assert.Equal(ContentKind.Unknown, screen.Rhythm.Kind);
        Assert.Equal(TimeSpan.Zero, screen.Rhythm.StillFor);
    }

    [Fact]
    public void OneQuietMomentIsNotEnoughToCallItCaptions()
    {
        // A game that has not started talking yet reads empty. Below the read floor that must not
        // become "this is a film", or every session would start in the wrong mode.
        var screen = Dialogue();

        screen.Observe(new RhythmSample(Changed: true, HasText: false, TextChanged: null));
        screen.Observe(new RhythmSample(Changed: true, HasText: false, TextChanged: null));

        Assert.Equal(ContentKind.Unknown, screen.Rhythm.Kind);
    }

    [Fact]
    public void TheWindowForgetsSoALongPauseCannotArgueForever()
    {
        var screen = Dialogue();

        WaitingLine(screen, holds: TimeSpan.FromSeconds(12));
        screen.Wait(ContentRhythm.MinimumDwell + TimeSpan.FromSeconds(1));

        // Enough caption evidence to fill the window twice over: the early stillness has aged out
        // and cannot keep voting.
        for (var i = 0; i < ContentRhythm.Window; i++) PassingLine(screen, gapPolls: 1);

        Assert.Equal(ContentKind.Moving, screen.Rhythm.Kind);
    }
}
