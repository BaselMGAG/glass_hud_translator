using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Regions;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// Working out which pixels to capture, and what is wrong with the answer.
///
/// <para>
/// These rules lived in the App until now, where nothing could reach them: every one of them is a
/// decision about numbers, but they sat between a Win32 call that needs a running game and an
/// overlay that needs a window. Two of them had already shipped as bugs. The first three tests
/// below are those bugs.
/// </para>
/// </summary>
public class RegionResolverTests
{
    /// <summary>A region drawn as the bottom third of a 1920x1080 window, with provenance.</summary>
    private static RegionProfile Drawn(string at = "1920x1080", double scale = 1.0) =>
        new("dialogue", at, scale, 0.1, 0.66, 0.8, 0.28);

    /// <summary>A shipped starting rectangle: never captured at any size, so it claims nothing.</summary>
    private static RegionProfile Shipped() =>
        new("dialogue", "unknown", 1.0, 0.1, 0.66, 0.8, 0.28);

    private static readonly CaptureRegion OneScreen = new(0, 0, 1920, 1080);

    [Fact]
    public void ARegionOnAMonitorLeftOfThePrimaryIsCapturable()
    {
        // THE multi-monitor bug. A display to the left of the primary starts at a NEGATIVE
        // coordinate, and the check used to ask "does this fit inside a pixel buffer", which
        // requires an origin of at least zero because there is no pixel at -1. The right question
        // is "is this on the desktop", and the desktop's origin is wherever the monitors put it.
        var game = new CaptureRegion(-1920, 0, 1920, 1080);
        var desktop = new CaptureRegion(-1920, 0, 3840, 1080);

        var outcome = RegionResolver.Resolve(Drawn(), game, 1.0, desktop);

        Assert.NotNull(outcome.Region);
        Assert.Null(outcome.Failure);
        Assert.DoesNotContain(RegionProblem.TrimmedToDesktop, outcome.Warnings);
        Assert.True(outcome.Region!.Value.X < 0, "the region should still be on the left monitor");
    }

    [Fact]
    public void ARegionHangingOffTheDesktopIsCutBackRatherThanCaptured()
    {
        // Capturing the overhang BitBlts undefined pixels into OCR, which comes back as garbage
        // text and reads as the translation getting worse rather than as a display change.
        var game = new CaptureRegion(1200, 0, 1920, 1080);
        var desktop = OneScreen;

        var outcome = RegionResolver.Resolve(Drawn(), game, 1.0, desktop);

        Assert.NotNull(outcome.Region);
        Assert.Contains(RegionProblem.TrimmedToDesktop, outcome.Warnings);
        Assert.True(desktop.Contains(outcome.Region!.Value), "the trimmed region is still off screen");
    }

    [Fact]
    public void ARegionEntirelyOffTheDesktopIsRefusedRatherThanTrimmedToNothing()
    {
        var game = new CaptureRegion(4000, 2000, 1920, 1080);

        var outcome = RegionResolver.Resolve(Drawn(), game, 1.0, OneScreen);

        Assert.Null(outcome.Region);
        Assert.Equal(RegionProblem.EntirelyOffScreen, outcome.Failure);
    }

    [Fact]
    public void ARegionDrawnOnThisVerySizeSaysNothing()
    {
        var outcome = RegionResolver.Resolve(Drawn(), OneScreen, 1.0, OneScreen);

        Assert.Empty(outcome.Warnings);
        Assert.NotNull(outcome.Region);
    }

    [Fact]
    public void ADifferentWindowSizeIsWorthMentioning()
    {
        var smaller = new CaptureRegion(0, 0, 1280, 720);

        var outcome = RegionResolver.Resolve(Drawn(), smaller, 1.0, smaller);

        Assert.Contains(RegionProblem.LayoutChanged, outcome.Warnings);

        // Mentioned, not refused: the fractions still resolve to something plausible, and refusing
        // to capture would be a worse answer than capturing a rectangle that may be slightly off.
        Assert.NotNull(outcome.Region);
    }

    [Fact]
    public void ADifferentDisplayScaleIsWorthMentioningToo()
    {
        var outcome = RegionResolver.Resolve(Drawn(scale: 1.0), OneScreen, 1.25, OneScreen);

        Assert.Contains(RegionProblem.LayoutChanged, outcome.Warnings);
    }

    [Fact]
    public void AShippedRectangleNeverComplainsAboutTheLayout()
    {
        // Provenance is all or nothing. A starting rectangle from profile.json was never captured
        // at any particular size and its scale of 1.0 is a placeholder, so comparing it against a
        // real scale would warn on the first run of every bundled profile on a scaled display.
        var outcome = RegionResolver.Resolve(Shipped(), new CaptureRegion(0, 0, 1280, 720), 1.5, OneScreen);

        Assert.Empty(outcome.Warnings);
    }

    [Fact]
    public void TheSameFactsGiveTheSameAnswerTwice()
    {
        // Nothing here remembers what it has already said. "Warn once per layout" is the caller's
        // job, deliberately - a pure function that quietly stopped reporting the second time would
        // be untestable in exactly the way this type exists to avoid.
        var first = RegionResolver.Resolve(Drawn(), new CaptureRegion(0, 0, 1280, 720), 1.0, OneScreen);
        var second = RegionResolver.Resolve(Drawn(), new CaptureRegion(0, 0, 1280, 720), 1.0, OneScreen);

        Assert.Equal(first.Warnings, second.Warnings);
        Assert.Equal(first.Region, second.Region);
    }

    [Fact]
    public void AnUnknownDesktopSkipsTheBoundsCheckRatherThanRefusingEverything()
    {
        // Off Windows there is no desktop to be off the edge of, and the frame source is replaying
        // recorded PNGs anyway. Treating "unknown" as "empty, so everything is outside it" would
        // make the Mac build refuse every region it was asked for.
        var outcome = RegionResolver.Resolve(Drawn(), OneScreen, 1.0, CaptureRegion.Empty);

        Assert.NotNull(outcome.Region);
        Assert.Null(outcome.Failure);
    }

    [Fact]
    public void TheRegionFollowsTheWindowRatherThanTheScreen()
    {
        // The whole reason regions are stored as fractions: the game moves and the saved rectangle
        // still points at the dialogue box.
        var moved = new CaptureRegion(300, 120, 1920, 1080);
        var desktop = new CaptureRegion(0, 0, 3840, 2160);

        var still = RegionResolver.Resolve(Drawn(), OneScreen, 1.0, desktop).Region!.Value;
        var after = RegionResolver.Resolve(Drawn(), moved, 1.0, desktop).Region!.Value;

        Assert.Equal(still.Width, after.Width);
        Assert.Equal(still.Height, after.Height);
        Assert.Equal(still.X + 300, after.X);
        Assert.Equal(still.Y + 120, after.Y);
    }
}
