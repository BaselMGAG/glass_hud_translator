using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Regions;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The two coordinate spaces the app works in, and the conversion between them.
///
/// <para>
/// <b>Buffer space</b> — indices into a captured frame. Starts at (0,0) because there is no pixel
/// at index -1. <b>Desktop space</b> — where things are across every monitor. Its origin is
/// wherever the leftmost and topmost screens put it, which is negative whenever a monitor sits left
/// of or above the primary one, because the primary monitor's top-left is always (0,0).
/// </para>
///
/// <para>
/// Conflating the two is the bug these tests exist for: every "whole screen" call asked for
/// <c>SM_CXSCREEN</c>, which is the primary monitor alone, and the buffer-bounds check was used to
/// answer "is this on screen?" — so a rectangle on a left-hand second monitor was not merely
/// unsupported, it was actively refused three layers down in Core. None of it is visible on a
/// single laptop, which is where all the testing happened.
/// </para>
/// </summary>
public class CoordinateSpaceTests
{
    // ── buffer space: origin is always zero ───────────────────────────────────────────────

    [Fact]
    public void ABufferCheckStillRefusesANegativeOrigin()
    {
        // Correct and deliberate: Frame.Crop indexes an array. This is the behaviour that was right
        // all along and only wrong when borrowed to answer a different question.
        Assert.False(new CaptureRegion(-1, 0, 10, 10).FitsWithin(100, 100));
        Assert.False(new CaptureRegion(0, -1, 10, 10).FitsWithin(100, 100));
        Assert.True(new CaptureRegion(0, 0, 100, 100).FitsWithin(100, 100));
    }

    // ── desktop space: origin is wherever the monitors put it ─────────────────────────────

    /// <summary>
    /// A two-monitor desktop with the second screen to the LEFT of the primary. The virtual
    /// desktop then starts at x = -1920, and every rectangle on that screen has a negative X.
    /// </summary>
    private static readonly CaptureRegion LeftHandSetup = new(-1920, 0, 3840, 1080);

    [Fact]
    public void ARectangleOnALeftHandMonitorIsOnScreen()
    {
        // The exact case that used to be refused. A game window on the second monitor, dialogue box
        // near the bottom.
        var dialogue = new CaptureRegion(-1500, 700, 900, 200);

        Assert.True(LeftHandSetup.Contains(dialogue));

        // ...and this is what the old check said about it.
        Assert.False(dialogue.FitsWithin(LeftHandSetup.Width, LeftHandSetup.Height));
    }

    [Fact]
    public void ARectangleOnAMonitorAboveThePrimaryIsOnScreen()
    {
        var stacked = new CaptureRegion(0, -1080, 1920, 2160);

        Assert.True(stacked.Contains(new CaptureRegion(100, -900, 800, 300)));
    }

    [Theory]
    [InlineData(-1920, 0, 1920, 1080)]   // fully on the left screen
    [InlineData(0, 0, 1920, 1080)]       // fully on the primary
    [InlineData(-960, 0, 1920, 1080)]    // straddling both, which is legitimate
    public void RectanglesAnywhereOnTheDesktopAreAccepted(int x, int y, int w, int h)
    {
        Assert.True(LeftHandSetup.Contains(new CaptureRegion(x, y, w, h)));
    }

    [Theory]
    [InlineData(-2000, 0, 100, 100)]     // off the left edge
    [InlineData(1900, 0, 100, 100)]      // hangs off the right edge by 80px
    [InlineData(0, 1000, 100, 100)]      // hangs off the bottom
    [InlineData(0, -1, 100, 100)]        // above a desktop whose top is 0
    public void RectanglesOffTheDesktopAreRejected(int x, int y, int w, int h)
    {
        Assert.False(LeftHandSetup.Contains(new CaptureRegion(x, y, w, h)));
    }

    [Fact]
    public void AnEmptyRectangleIsNeitherContainedNorContaining()
    {
        Assert.False(LeftHandSetup.Contains(CaptureRegion.Empty));
        Assert.False(CaptureRegion.Empty.Contains(new CaptureRegion(0, 0, 10, 10)));
    }

    // ── the conversion between them ───────────────────────────────────────────────────────

    [Fact]
    public void ThePickersPixelsBecomeDesktopCoordinates()
    {
        // The picker draws on a still of the whole desktop, so its (0,0) is the desktop's origin.
        // A box dragged 420px into that still, on a desktop starting at -1920, is at -1500.
        var picked = new CaptureRegion(420, 700, 900, 200);

        var onDesktop = picked.Translate(LeftHandSetup.X, LeftHandSetup.Y);

        Assert.Equal(new CaptureRegion(-1500, 700, 900, 200), onDesktop);
        Assert.True(LeftHandSetup.Contains(onDesktop));
    }

    [Fact]
    public void ARegionIsStoredRelativeToTheWindowItWasDrawnOn()
    {
        // A game window on the left-hand monitor. The stored rectangle must come out window-relative
        // and positive, or the fractions computed from it are nonsense.
        var gameWindow = new CaptureRegion(-1920, 0, 1920, 1080);
        var dialogueOnDesktop = new CaptureRegion(-1500, 756, 1075, 216);

        var relative = dialogueOnDesktop.RelativeTo(gameWindow);

        Assert.Equal(new CaptureRegion(420, 756, 1075, 216), relative);
        Assert.True(relative.FitsWithin(gameWindow.Width, gameWindow.Height));
    }

    [Fact]
    public void TheRoundTripThroughAWindowOnASecondMonitorSurvives()
    {
        // Picker pixels -> desktop -> window-relative -> fractions -> back to window pixels. This is
        // the whole chain, and it is the one that produced a rectangle nothing would accept.
        var desktop = LeftHandSetup;
        var gameWindow = new CaptureRegion(-1920, 0, 1920, 1080);
        var picked = new CaptureRegion(420, 756, 1075, 216);

        var relative = picked.Translate(desktop.X, desktop.Y).RelativeTo(gameWindow);
        var stored = RegionProfile.FromPixels("dialogue", relative,
            gameWindow.Width, gameWindow.Height, 1.0);
        var resolved = stored.Resolve(gameWindow.Width, gameWindow.Height);

        Assert.Equal(relative, resolved);
        Assert.True(resolved.FitsWithin(gameWindow.Width, gameWindow.Height));
    }

    /// <summary>
    /// The offset belongs to the frame, so it may only be applied to a rectangle picked on that
    /// frame. When there is no still, the picker falls back to scaling its own window coordinates —
    /// which are already desktop coordinates for the monitor it opened on. Translating those by the
    /// desktop origin moves them by a whole screen's width.
    /// </summary>
    [Fact]
    public void ARectangleNotPickedOnTheStillMustNotBeTranslated()
    {
        var alreadyInDesktopSpace = new CaptureRegion(400, 700, 1000, 200);

        // What the buggy version did: translate unconditionally.
        var wrong = alreadyInDesktopSpace.Translate(LeftHandSetup.X, LeftHandSetup.Y);

        Assert.Equal(-1520, wrong.X);
        Assert.NotEqual(alreadyInDesktopSpace, wrong);

        // A left-hand monitor puts the primary's own rectangles outside itself. On a desktop that
        // starts at 0 the mistake is invisible, which is why it needs a test rather than a look.
        Assert.False(new CaptureRegion(0, 0, 1920, 1080).Contains(wrong));
    }

    /// <summary>
    /// The screen-relative profile stores fractions of whatever it is told the client area is.
    /// Widening that to the union of every monitor silently relocates every region already saved.
    /// </summary>
    [Fact]
    public void WideningTheClientAreaToTheWholeDesktopWouldRelocateStoredRegions()
    {
        var stored = RegionProfile.Default("dialogue");   // 0.22 / 0.70 / 0.56 / 0.20

        var onOneMonitor = stored.Resolve(1920, 1080);
        var onTheUnion = stored.Resolve(3840, 1080);

        // Correct: a band across the bottom-centre of one screen.
        Assert.Equal(422, onOneMonitor.X);
        Assert.Equal(1075, onOneMonitor.Width);

        // What measuring against the bounding box produces: more than twice as wide, starting past
        // the middle of the first monitor and running across the seam into the second.
        Assert.Equal(2150, onTheUnion.Width);
        Assert.True(onTheUnion.X < 1920 && onTheUnion.X + onTheUnion.Width > 1920,
            "The region should straddle the monitor seam - that is the bug being guarded against.");
    }

    // ── clamping to a layout that changed underneath ──────────────────────────────────────

    [Fact]
    public void ARegionHangingOffTheEdgeIsTrimmedRatherThanUsedWhole()
    {
        // Unplug the second monitor and a region stored against it now runs past the edge of what
        // is left. Capturing it whole BitBlts undefined pixels into OCR, which surfaces as garbage
        // text and reads as the model getting worse, not as a geometry problem.
        var afterUnplug = new CaptureRegion(0, 0, 1920, 1080);
        var stored = new CaptureRegion(1600, 700, 800, 200);   // 480px past the right edge

        var trimmed = stored.ClampTo(afterUnplug);

        Assert.Equal(new CaptureRegion(1600, 700, 320, 200), trimmed);
        Assert.True(afterUnplug.Contains(trimmed));
    }

    [Fact]
    public void ARegionEntirelyOffScreenClampsToNothing()
    {
        // The whole second monitor is gone. There is no honest rectangle to read, so the caller
        // must be told rather than handed a sliver.
        var afterUnplug = new CaptureRegion(0, 0, 1920, 1080);
        var onTheVanishedMonitor = new CaptureRegion(-1500, 700, 900, 200);

        Assert.True(onTheVanishedMonitor.ClampTo(afterUnplug).IsEmpty);
    }

    [Fact]
    public void ClampingSomethingAlreadyInsideChangesNothing()
    {
        var region = new CaptureRegion(400, 700, 1000, 200);

        Assert.Equal(region, region.ClampTo(new CaptureRegion(0, 0, 1920, 1080)));
    }

    [Fact]
    public void ClampingWorksAcrossANegativeOrigin()
    {
        // The case a naive implementation gets wrong: both the region and the bounds start left of
        // zero, so anything comparing against 0 rather than against the bounds is broken here.
        var trimmed = new CaptureRegion(-2200, 0, 900, 200).ClampTo(LeftHandSetup);

        Assert.Equal(new CaptureRegion(-1920, 0, 620, 200), trimmed);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(10, 10, 0, 50)]
    public void ClampingAgainstNothingYieldsNothing(int x, int y, int w, int h)
    {
        Assert.True(new CaptureRegion(0, 0, 100, 100).ClampTo(new CaptureRegion(x, y, w, h)).IsEmpty);
    }

    [Fact]
    public void TranslatingByTheOriginAndBackIsIdentity()
    {
        var region = new CaptureRegion(-1500, 700, 900, 200);

        Assert.Equal(region, region.RelativeTo(LeftHandSetup).Translate(LeftHandSetup.X, LeftHandSetup.Y));
    }

    [Fact]
    public void ASingleMonitorDesktopBehavesExactlyAsBefore()
    {
        // The regression guard. Most users have one screen and nothing about their experience may
        // change: with an origin of (0,0) the two questions give the same answer.
        var single = new CaptureRegion(0, 0, 1920, 1080);
        var region = new CaptureRegion(400, 700, 1000, 200);

        Assert.True(single.Contains(region));
        Assert.Equal(region.FitsWithin(single.Width, single.Height), single.Contains(region));
        Assert.Equal(region, region.Translate(single.X, single.Y));
    }

    // ── region provenance: stored, and until now never consulted ──────────────────────────

    [Fact]
    public void ARegionDrawnAtADifferentResolutionIsNoticed()
    {
        // Written on save, read back into the record, and consulted by nothing - so a rectangle
        // dragged at 2560x1440 was silently reused at 1920x1080. The symptom is a truncated capture,
        // which reads as "the translation got worse", not "my region is stale".
        var drawnAt1440P = RegionProfile.FromPixels("dialogue",
            new CaptureRegion(563, 1008, 1434, 288), 2560, 1440, 1.0);

        Assert.True(drawnAt1440P.MatchesLayout(2560, 1440, 1.0));
        Assert.False(drawnAt1440P.MatchesLayout(1920, 1080, 1.0));
    }

    [Fact]
    public void AChangeOfDisplayScalingIsNoticed()
    {
        var drawnAt125 = RegionProfile.FromPixels("dialogue",
            new CaptureRegion(400, 700, 1000, 200), 1920, 1080, 1.25);

        Assert.True(drawnAt125.MatchesLayout(1920, 1080, 1.25));
        Assert.False(drawnAt125.MatchesLayout(1920, 1080, 1.0));
    }

    [Fact]
    public void ScaleIsComparedWithTolerance()
    {
        // UiScale arrives as a DPI ratio - 120/96, 144/96 - and will not round-trip a double
        // exactly through SQLite. An exact comparison would report a mismatch on every launch.
        var drawn = RegionProfile.FromPixels("dialogue",
            new CaptureRegion(400, 700, 1000, 200), 1920, 1080, 120 / 96.0);

        Assert.True(drawn.MatchesLayout(1920, 1080, 1.2500000001));
        Assert.True(drawn.MatchesLayout(1920, 1080, 1.2499999999));
        Assert.False(drawn.MatchesLayout(1920, 1080, 1.5));
    }

    [Fact]
    public void AStartingRectangleFromAProfileNeverReportsAMismatch()
    {
        // profile.json rectangles are captured at no particular size, so there is nothing for them
        // to disagree with. Reporting "this was drawn at a different resolution" for a shipped
        // default would be noise on the first run of every bundled profile.
        var shipped = RegionProfile.Default("dialogue");

        Assert.Equal("unknown", shipped.Resolution);
        Assert.True(shipped.MatchesLayout(1920, 1080, 1.0));
        Assert.True(shipped.MatchesLayout(3840, 2160, 2.0));
    }

    [Fact]
    public void AMismatchNeverDiscardsTheRectangle()
    {
        // The fractions remain the user's best guess and are usually close. Treating a mismatch as
        // "throw it away and make them pick again" would be worse than the bug.
        var drawnAt1440P = RegionProfile.FromPixels("dialogue",
            new CaptureRegion(563, 1008, 1434, 288), 2560, 1440, 1.0);

        var resolved = drawnAt1440P.Resolve(1920, 1080);

        Assert.False(drawnAt1440P.MatchesLayout(1920, 1080, 1.0));
        Assert.False(resolved.IsEmpty);
        Assert.True(resolved.FitsWithin(1920, 1080));
    }
}
