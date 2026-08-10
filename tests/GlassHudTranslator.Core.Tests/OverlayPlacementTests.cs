using GlassHudTranslator.Core.Capture;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

public class OverlayPlacementTests
{
    private static readonly CaptureRegion Game = new(0, 0, 1920, 1080);

    private const int PanelWidth = 900;
    private const int PanelHeight = 160;

    [Fact]
    public void HalfAndHalfIsCentred()
    {
        var (x, y) = OverlayPlacement.Within(Game, PanelWidth, PanelHeight, 0.5, 0.5);

        Assert.Equal((1920 - 900) / 2, x);
        Assert.Equal((1080 - 160) / 2, y);
    }

    [Fact]
    public void TheExtremesSitFlushWithTheEdgesAndNotPastThem()
    {
        var topLeft = OverlayPlacement.Within(Game, PanelWidth, PanelHeight, 0, 0);
        var bottomRight = OverlayPlacement.Within(Game, PanelWidth, PanelHeight, 1, 1);

        Assert.Equal((0, 0), topLeft);

        // The whole panel stays inside. Measuring the fraction against the window rather than
        // against the free space is what used to let it hang off the bottom.
        Assert.Equal((1920 - 900, 1080 - 160), bottomRight);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.85)]
    [InlineData(1.0)]
    public void ThePanelIsAlwaysWhollyInsideTheGameWindow(double fraction)
    {
        var (x, y) = OverlayPlacement.Within(Game, PanelWidth, PanelHeight, fraction, fraction);

        Assert.True(new CaptureRegion(x, y, PanelWidth, PanelHeight).FitsWithin(1920, 1080),
            $"At {fraction} the panel escaped the window: ({x},{y}).");
    }

    [Fact]
    public void PlacementIsRelativeToTheGameWherverItIs()
    {
        // Including a monitor left of the primary, where the desktop origin is negative. The
        // fractions are inside the game window, so nothing here should care.
        var offScreenLeft = new CaptureRegion(-1920, -200, 1920, 1080);

        var (x, y) = OverlayPlacement.Within(offScreenLeft, PanelWidth, PanelHeight, 0.5, 0.5);

        Assert.Equal(-1920 + (1920 - 900) / 2, x);
        Assert.Equal(-200 + (1080 - 160) / 2, y);
    }

    [Fact]
    public void APanelLargerThanTheWindowPinsToTheCornerRatherThanInverting()
    {
        // A small game window and a long wrapped line. Negative free space multiplied by the
        // fraction would walk the panel backwards, off the top-left, taking the start of the
        // sentence with it - so the panel is pinned instead and it is the tail that overflows.
        var small = new CaptureRegion(100, 100, 400, 100);

        var (x, y) = OverlayPlacement.Within(small, PanelWidth, PanelHeight, 1, 1);

        Assert.Equal(100, x);
        Assert.Equal(100, y);
    }

    [Theory]
    [InlineData(-3.0, 0.0)]
    [InlineData(9.0, 1.0)]
    [InlineData(double.NaN, 0.5)]
    public void AFractionFromAHandEditedSettingsFileIsClamped(double stored, double expected)
    {
        // config.json is a text file people open. A fraction outside 0-1 would put the panel off
        // the screen entirely, which is indistinguishable from the app having stopped working -
        // and NaN propagates through the arithmetic into a coordinate nothing can render.
        Assert.Equal(expected, OverlayPlacement.Clamp(stored));

        var (x, y) = OverlayPlacement.Within(Game, PanelWidth, PanelHeight, stored, stored);
        Assert.True(new CaptureRegion(x, y, PanelWidth, PanelHeight).FitsWithin(1920, 1080));
    }

    [Fact]
    public void TheDefaultsAreCentredAndLow()
    {
        var (x, y) = OverlayPlacement.Within(Game, PanelWidth, PanelHeight,
            OverlayPlacement.DefaultHorizontal, OverlayPlacement.DefaultVertical);

        Assert.Equal((1920 - 900) / 2, x);

        // Near where the old hardcoded 72%-of-height put it, which is just above FFXIV's dialogue
        // box - but now expressed as free space, so a taller panel rides up instead of off.
        Assert.InRange(y, (int)(1080 * 0.7), 1080 - PanelHeight);
    }

    // ── Device-independent pixels in, physical pixels out ─────────────────────────────────────
    // The App had this conversion and nothing could test it there. It was also wrong.

    [Fact]
    public void ScalingIsAppliedToThePanelSizeBeforePlacing()
    {
        // 900 DIPs at 125% is 1125 physical pixels. Flush right therefore starts 1125 from the
        // right edge, not 900 - which is the whole of the bug: at 100% the two agree, and every
        // machine this gets written on is at 100%.
        var (x, _) = OverlayPlacement.Within(Game, PanelWidth, PanelHeight, 1.25, 1, 0);

        Assert.Equal(1920 - 1125, x);
    }

    [Fact]
    public void AScaledPanelStillFitsEntirelyInsideTheGame()
    {
        foreach (var scaling in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            var (x, y) = OverlayPlacement.Within(Game, PanelWidth, PanelHeight, scaling, 1, 1);

            var physical = new CaptureRegion(x, y,
                (int)Math.Ceiling(PanelWidth * scaling), (int)Math.Ceiling(PanelHeight * scaling));

            Assert.True(physical.FitsWithin(1920, 1080),
                $"at {scaling:P0} the panel lands at {physical} and hangs off the screen");
        }
    }

    // ── dragging: the inverse has to land back where it started ───────────────────────────────

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.22, 0.85)]
    public void ADraggedPanelLandsWhereItWasLeftAndStaysThere(double horizontal, double vertical)
    {
        // The loop that matters: place from stored fractions, the user drags, the drop is read
        // back into fractions, and the next launch places from those. A fraction cannot survive a
        // trip through an integer pixel unchanged - freeX is about a thousand pixels, so the
        // recovered fraction differs in the fourth decimal - and that is fine. What is NOT fine is
        // creep: if each round trip moved the panel one pixel, a panel dragged to the corner would
        // walk across the screen over a few dozen sessions.
        //
        // So the invariant is idempotence in PIXELS, which is the unit the user actually sees.
        foreach (var scaling in new[] { 1.0, 1.25, 1.5 })
        {
            var first = OverlayPlacement.Within(
                Game, PanelWidth, PanelHeight, scaling, horizontal, vertical);

            var recovered = OverlayPlacement.FractionsWithin(
                Game, PanelWidth, PanelHeight, scaling, first.X, first.Y);

            var second = OverlayPlacement.Within(
                Game, PanelWidth, PanelHeight, scaling, recovered.Horizontal, recovered.Vertical);

            Assert.Equal(first, second);

            // And a third trip changes nothing either - the fixed point is reached immediately
            // rather than converged on.
            var again = OverlayPlacement.FractionsWithin(
                Game, PanelWidth, PanelHeight, scaling, second.X, second.Y);

            Assert.Equal(first,
                OverlayPlacement.Within(Game, PanelWidth, PanelHeight, scaling,
                    again.Horizontal, again.Vertical));
        }
    }

    [Fact]
    public void ADragOutsideTheGameIsClampedRatherThanStored()
    {
        // Windows will happily let a panel be dragged off the edge. Storing a fraction outside
        // 0-1 would put it off screen on the next launch, with nothing to explain why.
        var (horizontal, vertical) = OverlayPlacement.FractionsWithin(
            Game, PanelWidth, PanelHeight, 1.0, x: -400, y: 5000);

        Assert.Equal(0, horizontal);
        Assert.Equal(1, vertical);
    }

    [Fact]
    public void APanelWithNoRoomToMoveReportsCentredRatherThanDividingByZero()
    {
        // As wide as the game, or wider: every position maps to the same place, so there is no
        // meaningful fraction. Centred is where a panel that cannot move belongs.
        var narrow = new CaptureRegion(0, 0, PanelWidth, 1080);

        var (horizontal, _) = OverlayPlacement.FractionsWithin(
            narrow, PanelWidth, PanelHeight, 1.0, x: 0, y: 0);

        Assert.Equal(0.5, horizontal);
    }

    [Fact]
    public void TheGamesOriginIsSubtractedSoASecondMonitorDoesNotSkewIt()
    {
        // A game on a monitor to the left of the primary starts at a negative X. Reading the drag
        // without removing that origin would push every fraction to one end.
        var secondScreen = new CaptureRegion(-1920, 0, 1920, 1080);

        var (horizontal, _) = OverlayPlacement.FractionsWithin(
            secondScreen, PanelWidth, PanelHeight, 1.0,
            x: -1920 + (1920 - PanelWidth) / 2, y: 0);

        Assert.Equal(0.5, horizontal, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void AnUnusableScaleIsTreatedAsOneRatherThanCollapsingThePanel(double scaling)
    {
        // Screens.ScreenFromPoint can return null while a window is between monitors, and
        // RenderScaling is 0 before the first layout pass. Neither may become a coordinate.
        var scaled = OverlayPlacement.Within(Game, PanelWidth, PanelHeight, scaling, 0.5, 0.5);
        var plain = OverlayPlacement.Within(Game, PanelWidth, PanelHeight, 0.5, 0.5);

        Assert.Equal(plain, scaled);
    }
}
