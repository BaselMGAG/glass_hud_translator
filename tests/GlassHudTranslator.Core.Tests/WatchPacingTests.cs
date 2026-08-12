using GlassHudTranslator.Core.Capture;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The pacing policy: how long auto-watch may run, how often it may spend, and how it tightens
/// itself to the rhythm of whatever it turns out to be watching.
/// </summary>
public class WatchPacingTests
{
    [Fact]
    public void VideoIsImpatientAndDialogueIsNot()
    {
        var dialogue = WatchPacing.For(WatchMode.Dialogue);
        var video = WatchPacing.For(WatchMode.Video);

        // The number that fixes the reported delay. Over moving picture the stillness test can
        // never pass, so every release comes from this cap - at three seconds the Arabic arrived
        // after the subtitle it translated had already left the screen.
        Assert.True(video.SettleCap < dialogue.SettleCap);
        Assert.True(video.PollsPerSecond > dialogue.PollsPerSecond);

        // And the other half: without a floor, "translate as soon as it changes" over video is a
        // request per poll.
        Assert.True(video.MinimumInterval > TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, dialogue.MinimumInterval);
    }

    [Fact]
    public void DialogueKeepsTheTwoAndFourMinuteCapsItWasAskedFor()
    {
        var dialogue = WatchPacing.For(WatchMode.Dialogue);

        Assert.Equal(TimeSpan.FromMinutes(2), dialogue.WarnAfter);
        Assert.Equal(TimeSpan.FromMinutes(4), dialogue.StopAfter);
    }

    [Fact]
    public void VideoIsMeasuredInTheLengthOfWhatIsBeingWatched()
    {
        // Stopping a film every four minutes would be worse than not capping at all: the user
        // would simply switch the cap off, and then nothing guards anything.
        var video = WatchPacing.For(WatchMode.Video);

        Assert.True(video.StopAfter >= TimeSpan.FromMinutes(40));
        Assert.True(video.StopAfterRequests >= 1000);
    }

    // ── the cycle the toolbar button walks ────────────────────────────────────────────────────

    [Fact]
    public void EveryModeIsReachableByCyclingFromAnyOther()
    {
        // The defect this pins was found in real use: the toolbar flipped between Dialogue and
        // Video, so Auto could only be chosen from Settings - while the same button drew an Auto
        // icon it had no way of ever showing. Cycling from anywhere must reach everything.
        foreach (var start in Enum.GetValues<WatchMode>())
        {
            var seen = new HashSet<WatchMode>();
            var at = start;

            for (var i = 0; i < Enum.GetValues<WatchMode>().Length; i++)
            {
                at = WatchModes.After(at);
                seen.Add(at);
            }

            Assert.Equal(Enum.GetValues<WatchMode>().ToHashSet(), seen);
        }
    }

    [Fact]
    public void TheCycleOffersEveryModeTheEnumHas()
    {
        // Adding a fourth mode and forgetting the toolbar is the same mistake in a new coat, and
        // it would be invisible: the dropdown would grow and the button would silently skip it.
        Assert.Equal(Enum.GetValues<WatchMode>().ToHashSet(), WatchModes.InOrder.ToHashSet());
    }

    [Fact]
    public void CyclingWrapsRatherThanRunningOffTheEnd()
    {
        Assert.Equal(WatchModes.InOrder[0], WatchModes.After(WatchModes.InOrder[^1]));
    }

    // ── keeping up with a real subtitle track ─────────────────────────────────────────────────

    [Fact]
    public void VideoCanKeepUpWithTheShortestSubtitleTheStandardsAllow()
    {
        // Netflix's floor is 20 frames - five sixths of a second - and the ESIST Code's is one
        // second, so a conformant track can put a new caption up that fast. A floor above that is
        // not pacing, it is dropping every other line: the poll is skipped and the caption is gone
        // before the next one asks.
        var shortestLegalCaption = TimeSpan.FromSeconds(1);

        Assert.True(WatchPacing.For(WatchMode.Video).MinimumInterval <= shortestLegalCaption,
            "the floor refuses to look as often as a subtitle track is allowed to change");
    }

    [Fact]
    public void TheVideoSettleCapIsNeverBelowWhatTheAdaptiveFloorAllows()
    {
        // Over moving picture the stillness test cannot pass, so every release comes from the cap
        // and it is pure latency. That argues for making it small; MinimumSettleCap is how small,
        // and it is documented as the point below which a cap guarantees translating mid-change.
        // The two numbers are chosen in different places, so assert the relationship rather than
        // either value.
        Assert.True(WatchPacing.For(WatchMode.Video).SettleCap >= WatchSession.MinimumSettleCap,
            "the video cap is below the floor the adaptive tightening refuses to cross");
    }
}

public class WatchSessionTests
{
    private static (WatchSession Session, FakeTimeProvider Clock) Fresh(WatchMode mode = WatchMode.Dialogue)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var session = new WatchSession(WatchPacing.For(mode), clock);
        session.Start();
        return (session, clock);
    }

    [Fact]
    public void TheCapIsMeasuredFromSwitchOnAndNothingResetsIt()
    {
        // The whole point. The old guard was an idle timer that reset on any movement, so over a
        // playing video - or a game with animation in the capture region - it could never fire at
        // all. Translating constantly must bring the cap CLOSER, not push it away.
        var (session, clock) = Fresh();

        for (var minute = 0; minute < 4; minute++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            session.Translated();
        }

        Assert.Equal(WatchVerdict.Stop, session.Check());
    }

    [Fact]
    public void TheWarningIsGivenOnceAndNotOnEveryPollAfterIt()
    {
        var (session, clock) = Fresh();

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(WatchVerdict.Warn, session.Check());

        // Otherwise a sticky overlay notice would be rewritten twice a second for two minutes.
        Assert.Equal(WatchVerdict.Run, session.Check());
        Assert.Equal(WatchVerdict.Run, session.Check());
    }

    [Fact]
    public void SpendingFastEnoughTripsTheCapBeforeTheClockDoes()
    {
        // Four minutes of cutscene is a dozen requests; four minutes of film is eighty. Time is a
        // poor proxy for spend, so both are counted and the first one to arrive wins.
        var (session, _) = Fresh(WatchMode.Video);

        for (var i = 0; i < WatchPacing.For(WatchMode.Video).StopAfterRequests; i++)
            session.Translated();

        Assert.Equal(WatchVerdict.Stop, session.Check());
    }

    [Fact]
    public void UnboundedStillWarnsButNeverStops()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var session = new WatchSession(WatchPacing.For(WatchMode.Dialogue), clock) { Unbounded = true };
        session.Start();

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(WatchVerdict.Warn, session.Check());

        clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal(WatchVerdict.Run, session.Check());
    }

    [Fact]
    public void TheFloorHoldsBackTheNextTranslationAndThenReleasesIt()
    {
        var (session, clock) = Fresh(WatchMode.Video);
        var floor = WatchPacing.For(WatchMode.Video).MinimumInterval;

        Assert.True(session.MayTranslate());   // nothing has been shown yet
        session.Translated();

        Assert.False(session.MayTranslate());
        clock.Advance(floor - TimeSpan.FromMilliseconds(1));
        Assert.False(session.MayTranslate());

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(session.MayTranslate());
    }

    [Fact]
    public void DialogueHasNoFloorBecauseNobodyCanOutrunOneByHand()
    {
        var (session, _) = Fresh();

        session.Translated();
        Assert.True(session.MayTranslate());
    }

    // ── the adaptive part ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CadenceStaysUnknownUntilThereIsSomethingToTakeAMedianOf()
    {
        var (session, clock) = Fresh();

        session.Translated();
        Assert.Null(session.Cadence);

        clock.Advance(TimeSpan.FromSeconds(4));
        session.Translated();
        Assert.Null(session.Cadence);
    }

    [Fact]
    public void CadenceIsTheMedianSoOnePauseDoesNotSkewIt()
    {
        // A player who stops to read for half a minute must not make the next ten lines patient.
        var (session, clock) = Fresh();

        foreach (var gap in new[] { 3, 3, 40, 3, 3 })
        {
            session.Translated();
            clock.Advance(TimeSpan.FromSeconds(gap));
        }

        session.Translated();

        Assert.Equal(TimeSpan.FromSeconds(3), session.Cadence);
    }

    [Fact]
    public void AFastRhythmTightensTheDeadline()
    {
        // Subtitles every three seconds. A three-second deadline means the translation lands as the
        // line leaves; a third of the cadence means it lands while the line is still there.
        var (session, clock) = Fresh();
        var before = session.Settle().Cap;

        for (var i = 0; i < 5; i++)
        {
            session.Translated();
            clock.Advance(TimeSpan.FromSeconds(3));
        }

        var after = session.Settle().Cap;

        Assert.Equal(WatchPacing.For(WatchMode.Dialogue).SettleCap, before);
        Assert.Equal(TimeSpan.FromSeconds(1), after);
    }

    [Fact]
    public void ASlowRhythmNeverMakesItSlowerThanTheModeAllows()
    {
        // Adaptation may only tighten. A human picked the mode's cap as the longest defensible
        // wait, and no measurement should be able to argue the app into being lazier than that.
        var (session, clock) = Fresh();

        for (var i = 0; i < 5; i++)
        {
            session.Translated();
            clock.Advance(TimeSpan.FromSeconds(60));
        }

        Assert.Equal(WatchPacing.For(WatchMode.Dialogue).SettleCap, session.Settle().Cap);
    }

    [Fact]
    public void TheDeadlineHasAFloorOfItsOwn()
    {
        // Below this a deadline stops meaning "it is never going to hold still" and starts
        // guaranteeing a translation of a half-drawn frame, which costs a request for half a line.
        var (session, clock) = Fresh();

        for (var i = 0; i < 5; i++)
        {
            session.Translated();
            clock.Advance(TimeSpan.FromMilliseconds(300));
        }

        Assert.Equal(WatchSession.MinimumSettleCap, session.Settle().Cap);
    }

    [Fact]
    public void ItSaysWhenTheContentIsFasterThanTheGapAllows()
    {
        // Skipping lines silently reads as "this tool is unreliable". Saying so reads as "this
        // content is faster than this setting", which is true and is something the user can act on.
        var (session, clock) = Fresh(WatchMode.Video);

        for (var i = 0; i < 5; i++)
        {
            session.Translated();
            clock.Advance(TimeSpan.FromMilliseconds(600));
        }

        Assert.True(session.OutrunningTheFloor);
    }

    [Fact]
    public void ANormalSubtitleTrackIsNotReportedAsOutrunningAnything()
    {
        var (session, clock) = Fresh(WatchMode.Video);

        for (var i = 0; i < 5; i++)
        {
            session.Translated();
            clock.Advance(TimeSpan.FromSeconds(3.5));
        }

        Assert.False(session.OutrunningTheFloor);
    }

    [Fact]
    public void StartingAgainForgetsTheLastRun()
    {
        var (session, clock) = Fresh();

        for (var i = 0; i < 5; i++)
        {
            session.Translated();
            clock.Advance(TimeSpan.FromSeconds(3));
        }

        clock.Advance(TimeSpan.FromMinutes(10));
        session.Start();

        Assert.Null(session.Cadence);
        Assert.Equal(0, session.Requests);
        Assert.Equal(WatchVerdict.Run, session.Check());
    }

    // ── changing mode while a run is in progress ──────────────────────────────────────────────

    [Fact]
    public void ChangingModeMidRunSwapsTheTimingsImmediately()
    {
        // Reported from real use as "changing between game dialogue, video or auto breaks
        // everything": the pacing is read once when a run starts, so a mode chosen afterwards did
        // nothing at all until auto-watch was switched off and on - which is indistinguishable,
        // from the outside, from the switch being broken.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var session = new WatchSession(WatchPacing.For(WatchMode.Dialogue), clock);
        session.Start();

        Assert.Equal(WatchPacing.For(WatchMode.Dialogue).SettleCap, session.Pacing.SettleCap);

        session.Adapt(WatchPacing.For(WatchMode.Video));

        Assert.Equal(WatchPacing.For(WatchMode.Video).SettleCap, session.Pacing.SettleCap);
        Assert.Equal(WatchPacing.For(WatchMode.Video).PollInterval, session.Pacing.PollInterval);
    }

    [Fact]
    public void ChangingModeDoesNotHandBackTheSessionCaps()
    {
        // THE reason a mode change adapts rather than restarts. The caps are measured from when
        // the user switched auto-watch on, so resetting them here would make flipping modes a way
        // to hold the app open past every limit it has - and the cap exists precisely because a
        // toggle left on is the failure it is guarding against.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var session = new WatchSession(WatchPacing.For(WatchMode.Dialogue), clock);
        session.Start();

        for (var i = 0; i < 40; i++) session.Translated();
        clock.Advance(TimeSpan.FromMinutes(3));

        var spentBefore = session.Requests;
        var elapsedBefore = session.Elapsed;

        session.Adapt(WatchPacing.For(WatchMode.Video));

        Assert.Equal(spentBefore, session.Requests);
        Assert.Equal(elapsedBefore, session.Elapsed);
    }

    [Fact]
    public void ChangingModeCannotReviveARunThatHasAlreadyHitItsCap()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var session = new WatchSession(WatchPacing.For(WatchMode.Dialogue), clock);
        session.Start();

        clock.Advance(WatchPacing.For(WatchMode.Dialogue).StopAfter + TimeSpan.FromMinutes(1));
        Assert.Equal(WatchVerdict.Stop, session.Check());

        // Video's clock cap is far longer than Dialogue's, so switching to it is the obvious way
        // to try to buy more time. It must not work.
        session.Adapt(WatchPacing.For(WatchMode.Video));

        Assert.Equal(WatchVerdict.Stop, session.Check());
    }
}


public class SettleGateRetuneTests
{
    [Fact]
    public void RetuningChangesThePaceWithoutForgettingTheScreen()
    {
        // The adaptation retunes the gate on every poll. If that also cleared what is on the
        // overlay, every poll would re-translate the line already showing.
        var frame = new FrameBuilder(200, 80, Rgb.DarkScene).Rect(20, 20, 120, 30, Rgb.TextWhite).Build();
        var signature = FrameSignature.Compute(frame);

        var gate = new FrameSettleGate();
        gate.Offer(signature);
        Assert.Equal(FrameVerdict.Ready, gate.Offer(signature));

        gate.Retune(new SettleOptions { RequiredStillTicks = 1, Cap = TimeSpan.FromSeconds(1) });

        Assert.Equal(FrameVerdict.Unchanged, gate.Offer(signature));
    }
}
