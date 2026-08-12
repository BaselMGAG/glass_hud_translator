using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Ocr;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// How large a crop is sent to a vision model. Pure arithmetic, which is the point: it decides both
/// how legible the text is to the model and what the request costs, and neither should depend on a
/// number nobody can check.
/// </summary>
public class VisionImageTests
{
    private static readonly VisionImageOptions Cap1568 = new() { LongestEdge = 1568 };

    [Fact]
    public void ASmallCropIsScaledUpTowardsTheCap()
    {
        // The FFXIV subtitle strip: wide, short, and with glyphs smaller than one provider patch,
        // so the model is being asked to read letters that never got a token of their own.
        var (width, height, scale) = VisionImagePrep.SizeFor(1075, 216, Cap1568);

        Assert.True(scale > 1.0);
        Assert.Equal(1568, width);
        Assert.Equal((int)Math.Round(216 * (1568 / 1075.0)), height);
    }

    [Fact]
    public void TheAspectRatioIsKept()
    {
        var (width, height, _) = VisionImagePrep.SizeFor(800, 200, Cap1568);

        Assert.Equal(800 / 200.0, width / (double)height, 2);
    }

    [Fact]
    public void ACropAlreadyLargerThanTheCapIsSentUntouched()
    {
        // Scaling down here would be doing the provider's job worse, and it resizes anyway.
        var (width, height, scale) = VisionImagePrep.SizeFor(1920, 1080, Cap1568);

        Assert.Equal(1, scale);
        Assert.Equal(1920, width);
        Assert.Equal(1080, height);
    }

    [Fact]
    public void UpscalingStopsAtTheCeilingRatherThanChasingTheCap()
    {
        // A tiny crop would otherwise be blown up enormously for nothing: past the point where the
        // glyphs have their own patches, more pixels buy no more legibility and the upload is
        // simply slower.
        var tiny = new VisionImageOptions { LongestEdge = 4000, MaximumUpscale = 3.0 };

        var (_, _, scale) = VisionImagePrep.SizeFor(100, 40, tiny);

        Assert.Equal(3.0, scale);
    }

    [Fact]
    public void TheSizeSentIsNeverBeyondTheCap()
    {
        foreach (var (w, h) in new[] { (1075, 216), (400, 90), (1920, 130), (60, 20), (3000, 200) })
        {
            var (width, height, _) = VisionImagePrep.SizeFor(w, h, Cap1568);

            Assert.True(Math.Max(width, height) <= Math.Max(1568, Math.Max(w, h)),
                $"{w}x{h} was sent as {width}x{height}, past the cap for no benefit");
        }
    }

    [Fact]
    public void ADegenerateCropDoesNotThrow()
    {
        var (width, height, scale) = VisionImagePrep.SizeFor(0, 0, Cap1568);

        Assert.Equal(0, width);
        Assert.Equal(0, height);
        Assert.Equal(1, scale);
    }

    [Fact]
    public void ThePreparedImageIsPngAndCarriesWhatWasActuallySent()
    {
        // The dimensions come back because the cost is a function of them, and "why did that line
        // cost so much" is a question the diagnostics have to be able to answer.
        var frame = new FrameBuilder(200, 80, new Rgb(20, 20, 24)).Build();

        var prepared = VisionImagePrep.Prepare(frame, Cap1568);

        Assert.True(prepared.Png.Length > 0);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, prepared.Png[..4]);
        Assert.True(prepared.Width >= 200);
        Assert.Equal(200 / 80.0, prepared.Width / (double)prepared.Height, 1);
    }
}
