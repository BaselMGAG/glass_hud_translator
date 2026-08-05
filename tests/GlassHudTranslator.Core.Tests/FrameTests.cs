using GlassHudTranslator.Core.Capture;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

public class FrameTests
{
    [Fact]
    public void PngRoundTrip_PreservesSizeAndPixels()
    {
        var original = new FrameBuilder(64, 32, Rgb.BoxDark)
            .Rect(10, 8, 20, 10, Rgb.TextWhite)
            .Build();

        using var stream = new MemoryStream(original.ToPng());
        var restored = Frame.FromPng(stream);

        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        Assert.Equal(original.Bgra, restored.Bgra);
    }

    [Fact]
    public void Crop_ExtractsTheRequestedRectangle()
    {
        var frame = new FrameBuilder(40, 40, Rgb.Black)
            .Rect(10, 10, 10, 10, Rgb.White)
            .Build();

        var cropped = frame.Crop(new CaptureRegion(10, 10, 10, 10));

        Assert.Equal(10, cropped.Width);
        Assert.Equal(10, cropped.Height);
        Assert.All(cropped.ToGreyscale(), g => Assert.Equal(255, g));
    }

    [Fact]
    public void Crop_RejectsRegionOutsideTheFrame()
    {
        var frame = new FrameBuilder(20, 20, Rgb.Black).Build();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => frame.Crop(new CaptureRegion(15, 15, 10, 10)));
    }

    [Fact]
    public void Resize_ProducesTheRequestedDimensions()
    {
        var frame = new FrameBuilder(128, 64, Rgb.BoxDark).Build();

        var resized = frame.Resize(FrameSignature.Width, FrameSignature.Height);

        Assert.Equal(FrameSignature.Width, resized.Width);
        Assert.Equal(FrameSignature.Height, resized.Height);
        Assert.Equal(FrameSignature.Width * FrameSignature.Height * Frame.BytesPerPixel, resized.Bgra.Length);
    }

    [Theory]
    [InlineData(255, 255, 255, 255)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(255, 0, 0, 76)]    // 0.299 * 255
    [InlineData(0, 255, 0, 149)]   // 0.587 * 255
    [InlineData(0, 0, 255, 29)]    // 0.114 * 255
    public void ToGreyscale_UsesRec601Luma(byte r, byte g, byte b, byte expected)
    {
        var frame = new FrameBuilder(4, 4, new Rgb(r, g, b)).Build();

        Assert.All(frame.ToGreyscale(), actual => Assert.Equal(expected, actual));
    }

    [Fact]
    public void CaptureRegion_FitsWithin_RejectsOverhangAndEmptyRegions()
    {
        Assert.True(new CaptureRegion(0, 0, 10, 10).FitsWithin(10, 10));
        Assert.False(new CaptureRegion(1, 0, 10, 10).FitsWithin(10, 10));
        Assert.False(new CaptureRegion(0, 0, 0, 10).FitsWithin(10, 10));
        Assert.False(CaptureRegion.Empty.FitsWithin(10, 10));
    }
}
