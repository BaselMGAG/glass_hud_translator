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
