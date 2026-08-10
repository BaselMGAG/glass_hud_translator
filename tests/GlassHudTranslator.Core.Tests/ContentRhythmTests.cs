using GlassHudTranslator.Core.Capture;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The detector behind <see cref="WatchMode.Auto"/>. Every test here is a shape of content the
/// classifier has to survive, and the awkward ones — animation behind a static box, a paused
/// video, a player mashing through dialogue — are the point rather than the edge.
/// </summary>
public class ContentRhythmTests
{
    /// <summary>A dialogue box: the line goes up and nothing changes until the player advances.</summary>
    private static void WaitingLine(ContentRhythm rhythm, int polls)
    {
        rhythm.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: true));
        for (var i = 0; i < polls; i++) rhythm.Observe(new RhythmSample(Changed: false));
    }

    /// <summary>A caption: it appears, is read, then the region is empty until the next one.</summary>
    private static void PassingLine(ContentRhythm rhythm, int gapPolls = 2)
    {
        rhythm.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: true));
        for (var i = 0; i < gapPolls; i++)
            rhythm.Observe(new RhythmSample(Changed: true, HasText: false, TextChanged: null));
    }

    [Fact]
    public void NothingIsClaimedBeforeThereIsEvidence()
    {
        var rhythm = new ContentRhythm();

        rhythm.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: true));

        Assert.Equal(ContentKind.Unknown, rhythm.Kind);

        // And an undecided detector runs Dialogue, because being patient on a film costs a few
        // late lines while being impatient on a typewriter reveal costs a request per fragment.
        Assert.Equal(WatchMode.Dialogue, rhythm.Resolved);
    }

    [Fact]
    public void ALineThatSitsThereIsDialogue()
    {
        var rhythm = new ContentRhythm();
        WaitingLine(rhythm, polls: 10);

        Assert.Equal(ContentKind.Dialogue, rhythm.Kind);
        Assert.Equal(WatchMode.Dialogue, rhythm.Resolved);
    }

    [Fact]
    public void CaptionsWithGapsBetweenThemAreMoving()
    {
        var rhythm = new ContentRhythm();
        for (var i = 0; i < 6; i++) PassingLine(rhythm);

        Assert.Equal(ContentKind.Moving, rhythm.Kind);
        Assert.Equal(WatchMode.Video, rhythm.Resolved);
    }

    [Fact]
    public void AnimationBehindAStaticDialogueBoxIsStillDialogue()
    {
        // THE trap. Every frame differs, so a pixel comparison says video on every single poll:
        // weather, an idling character, a scrolling sky. The words are identical, which is what
        // the repeat gate reports, and the words are what decide.
        var rhythm = new ContentRhythm();

        rhythm.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: true));
        for (var i = 0; i < 10; i++)
            rhythm.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: false));

        Assert.Equal(ContentKind.Dialogue, rhythm.Kind);
    }

    [Fact]
    public void APausedVideoLooksLikeDialogueAndThatIsTheRightAnswer()
    {
        // It is indistinguishable from a dialogue box, and correctly so: nothing is going to
        // vanish while it is paused, so the patient timings are exactly what it wants.
        var rhythm = new ContentRhythm();
        WaitingLine(rhythm, polls: 12);

        Assert.Equal(WatchMode.Dialogue, rhythm.Resolved);
    }

    [Fact]
    public void ContinuousCaptionsWithNoGapsAreStillMoving()
    {
        // Dense dialogue in a film: one caption replaced directly by the next, region never empty,
        // nothing ever holds still. Motion is the only signal left, and it is allowed to decide
        // here because both readings want the impatient timings anyway.
        var rhythm = new ContentRhythm();

        for (var i = 0; i < 20; i++)
            rhythm.Observe(new RhythmSample(Changed: true, HasText: true, TextChanged: true));

        Assert.Equal(ContentKind.Moving, rhythm.Kind);
    }

    [Fact]
    public void SettlingPollsDoNotVoteEitherWay()
    {
        // A frame mid-change that was never read is genuinely no evidence. If these counted as
        // persistence, a caption gap over moving footage would read as a line sitting still.
        var rhythm = new ContentRhythm();

        for (var i = 0; i < 20; i++) rhythm.Observe(new RhythmSample(Changed: true));

        Assert.Equal(ContentKind.Unknown, rhythm.Kind);
        Assert.Equal(0, rhythm.LongestStillRun);
    }

    [Fact]
    public void AVerdictHasToOutlastTheDwellBeforeAnotherCanReplaceIt()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var rhythm = new ContentRhythm(clock);

        WaitingLine(rhythm, polls: 10);
        Assert.Equal(ContentKind.Dialogue, rhythm.Kind);

        // A burst of caption-shaped evidence immediately afterwards is refused: content genuinely
        // alternates, and a classifier that follows every wobble spends its life in the wrong mode
        // arriving there late.
        for (var i = 0; i < 8; i++) PassingLine(rhythm);
        Assert.Equal(ContentKind.Dialogue, rhythm.Kind);

        clock.Advance(ContentRhythm.MinimumDwell + TimeSpan.FromSeconds(1));
        PassingLine(rhythm);

        Assert.Equal(ContentKind.Moving, rhythm.Kind);
    }

    [Fact]
    public void TheFirstVerdictIsFreeBecauseThereIsNothingToFlapAgainst()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var rhythm = new ContentRhythm(clock);

        // No time advanced at all, and it still decides - the dwell guards CHANGES of mind.
        WaitingLine(rhythm, polls: 10);

        Assert.Equal(ContentKind.Dialogue, rhythm.Kind);
    }

    [Fact]
    public void ACutsceneInsideAGameIsFollowedOnceItLasts()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var rhythm = new ContentRhythm(clock);

        WaitingLine(rhythm, polls: 10);
        Assert.Equal(WatchMode.Dialogue, rhythm.Resolved);

        clock.Advance(ContentRhythm.MinimumDwell + TimeSpan.FromSeconds(1));
        for (var i = 0; i < 8; i++) PassingLine(rhythm);

        Assert.Equal(WatchMode.Video, rhythm.Resolved);

        // ...and back again when the cutscene ends and the dialogue box returns.
        clock.Advance(ContentRhythm.MinimumDwell + TimeSpan.FromSeconds(1));
        WaitingLine(rhythm, polls: 10);

        Assert.Equal(WatchMode.Dialogue, rhythm.Resolved);
    }

    [Fact]
    public void SwitchingAutoWatchOnAsksTheQuestionAgain()
    {
        var rhythm = new ContentRhythm();
        for (var i = 0; i < 6; i++) PassingLine(rhythm);
        Assert.Equal(ContentKind.Moving, rhythm.Kind);

        rhythm.Reset();

        // A verdict carried over is a verdict about a screen nobody is looking at any more.
        Assert.Equal(ContentKind.Unknown, rhythm.Kind);
        Assert.Equal(0, rhythm.LongestStillRun);
    }

    [Fact]
    public void OneQuietMomentIsNotEnoughToCallItCaptions()
    {
        // A game that has not started talking yet reads empty. Below the read floor that must not
        // become "this is a film", or every session would start in the wrong mode.
        var rhythm = new ContentRhythm();

        rhythm.Observe(new RhythmSample(Changed: true, HasText: false, TextChanged: null));
        rhythm.Observe(new RhythmSample(Changed: true, HasText: false, TextChanged: null));

        Assert.Equal(ContentKind.Unknown, rhythm.Kind);
    }

    [Fact]
    public void TheWindowForgetsSoALongPauseCannotArgueForever()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var rhythm = new ContentRhythm(clock);

        WaitingLine(rhythm, polls: 10);
        clock.Advance(ContentRhythm.MinimumDwell + TimeSpan.FromSeconds(1));

        // Enough caption evidence to fill the window twice over: the early stillness has aged out
        // and cannot keep voting.
        for (var i = 0; i < ContentRhythm.Window; i++) PassingLine(rhythm, gapPolls: 1);

        Assert.Equal(ContentKind.Moving, rhythm.Kind);
    }
}
