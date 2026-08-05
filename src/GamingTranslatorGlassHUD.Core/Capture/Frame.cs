using SkiaSharp;

namespace GamingTranslatorGlassHUD.Core.Capture;

/// <summary>
/// A captured rectangle as a plain BGRA8888 buffer, 4 bytes per pixel, no row padding.
///
/// Deliberately not an <c>Image</c> or an <c>SKBitmap</c>: this type crosses the
/// <see cref="IFrameSource"/> seam, and the Win32 implementation converts its HBITMAP exactly once
/// at that boundary. Everything downstream of capture is then platform-free and unit-testable on
/// macOS (PROJECT_PLAN.md 1.4).
/// </summary>
public sealed record Frame(int Width, int Height, byte[] Bgra)
{
    public const int BytesPerPixel = 4;

    public int Stride => Width * BytesPerPixel;

    public static Frame FromPng(Stream stream)
    {
        using var decoded = SKBitmap.Decode(stream)
            ?? throw new InvalidDataException("Stream did not decode as an image.");
        return FromSkBitmap(decoded);
    }

    public static Frame FromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return FromPng(stream);
    }

    public static Frame FromSkBitmap(SKBitmap source)
    {
        if (source.ColorType == SKColorType.Bgra8888 && source.RowBytes == source.Width * BytesPerPixel)
            return new Frame(source.Width, source.Height, source.Bytes);

        using var converted = source.Copy(SKColorType.Bgra8888)
            ?? throw new InvalidOperationException("Conversion to BGRA8888 failed.");
        return new Frame(converted.Width, converted.Height, converted.Bytes);
    }

    public byte[] ToPng()
    {
        using var image = SKImage.FromPixelCopy(ImageInfo, Bgra);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void SavePng(string path) => File.WriteAllBytes(path, ToPng());

    public Frame Crop(CaptureRegion region)
    {
        if (!region.FitsWithin(Width, Height))
            throw new ArgumentOutOfRangeException(nameof(region), $"{region} does not fit in {Width}x{Height}.");

        var cropped = new byte[region.Width * region.Height * BytesPerPixel];
        var rowBytes = region.Width * BytesPerPixel;
        for (var y = 0; y < region.Height; y++)
        {
            var sourceOffset = ((region.Y + y) * Width + region.X) * BytesPerPixel;
            Buffer.BlockCopy(Bgra, sourceOffset, cropped, y * rowBytes, rowBytes);
        }

        return new Frame(region.Width, region.Height, cropped);
    }

    /// <summary>High-quality resample. Used for the change-detection downsample and OCR upscaling.</summary>
    public Frame Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        using var source = ToSkBitmap();
        using var resized = source.Resize(new SKImageInfo(width, height, SKColorType.Bgra8888), SKFilterQuality.High)
            ?? throw new InvalidOperationException($"Resize to {width}x{height} failed.");
        return FromSkBitmap(resized);
    }

    /// <summary>
    /// Rec. 601 luma, one byte per pixel. The basis for both change detection and OCR
    /// preprocessing, so it lives here rather than being duplicated in each.
    /// </summary>
    public byte[] ToGreyscale()
    {
        var grey = new byte[Width * Height];
        for (var i = 0; i < grey.Length; i++)
        {
            var p = i * BytesPerPixel;
            // BGRA byte order.
            grey[i] = (byte)((Bgra[p + 2] * 299 + Bgra[p + 1] * 587 + Bgra[p] * 114) / 1000);
        }

        return grey;
    }

    public SKBitmap ToSkBitmap()
    {
        var bitmap = new SKBitmap();
        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(Bgra,
            System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            // InstallPixels copies nothing, so hand Skia its own copy and release the pin.
            using var borrowed = new SKBitmap();
            borrowed.InstallPixels(ImageInfo, pinned.AddrOfPinnedObject(), Stride);
            borrowed.CopyTo(bitmap, SKColorType.Bgra8888);
        }
        finally
        {
            pinned.Free();
        }

        return bitmap;
    }

    private SKImageInfo ImageInfo => new(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

    public override string ToString() => $"Frame {Width}x{Height}";
}
