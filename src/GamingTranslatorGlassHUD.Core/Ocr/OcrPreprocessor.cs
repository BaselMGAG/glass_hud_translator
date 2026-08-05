using GamingTranslatorGlassHUD.Core.Capture;

namespace GamingTranslatorGlassHUD.Core.Ocr;

public sealed record OcrPreprocessOptions
{
    /// <summary>Tesseract is markedly more accurate on upscaled small text.</summary>
    public int UpscaleFactor { get; init; } = 2;

    public bool StretchContrast { get; init; } = true;

    /// <summary>
    /// FFXIV draws light text on a dark translucent box; Tesseract expects dark on light.
    /// Decided per frame from the image itself rather than assumed, so the same code path also
    /// handles the quest-accept window, which is lighter.
    /// </summary>
    public bool AutoInvert { get; init; } = true;

    /// <summary>
    /// Off by default. Binarising throws away the antialiasing Tesseract uses to disambiguate
    /// similar glyphs, and FFXIV text is already high contrast against its box. Session 3 measures
    /// whether turning it on helps on the real corpus rather than assuming it does.
    /// </summary>
    public bool Binarise { get; init; }
}

/// <summary>
/// Prepares a captured region for Tesseract. Budgeted at 5-15 ms on weak hardware (brief 3).
/// </summary>
public static class OcrPreprocessor
{
    public static Frame Prepare(Frame frame, OcrPreprocessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var opts = options ?? new OcrPreprocessOptions();

        var working = opts.UpscaleFactor > 1
            ? frame.Resize(frame.Width * opts.UpscaleFactor, frame.Height * opts.UpscaleFactor)
            : frame;

        var grey = working.ToGreyscale();

        if (opts.StretchContrast) StretchContrast(grey);
        if (opts.AutoInvert && IsLightOnDark(grey)) Invert(grey);
        if (opts.Binarise) Binarise(grey);

        return FromGreyscale(grey, working.Width, working.Height);
    }

    /// <summary>Rescales the luma range onto 0-255, so a dim capture is not judged on its dimness.</summary>
    private static void StretchContrast(byte[] grey)
    {
        byte min = 255, max = 0;
        foreach (var g in grey)
        {
            if (g < min) min = g;
            if (g > max) max = g;
        }

        var span = max - min;
        if (span < 8) return;   // flat image; stretching would only amplify noise

        for (var i = 0; i < grey.Length; i++)
            grey[i] = (byte)((grey[i] - min) * 255 / span);
    }

    /// <summary>
    /// True when the bright pixels are the minority - i.e. glyphs on a dark field. Uses the median
    /// rather than the mean so a bright scene bleeding through the box does not flip the decision.
    /// </summary>
    private static bool IsLightOnDark(byte[] grey)
    {
        Span<int> histogram = stackalloc int[256];
        foreach (var g in grey) histogram[g]++;

        var half = grey.Length / 2;
        var running = 0;
        for (var level = 0; level < 256; level++)
        {
            running += histogram[level];
            if (running >= half) return level < 128;
        }

        return false;
    }

    private static void Invert(byte[] grey)
    {
        for (var i = 0; i < grey.Length; i++)
            grey[i] = (byte)(255 - grey[i]);
    }

    private static void Binarise(byte[] grey)
    {
        var mean = 0L;
        foreach (var g in grey) mean += g;
        var threshold = (byte)(mean / grey.Length);

        for (var i = 0; i < grey.Length; i++)
            grey[i] = grey[i] > threshold ? (byte)255 : (byte)0;
    }

    private static Frame FromGreyscale(byte[] grey, int width, int height)
    {
        var bgra = new byte[grey.Length * Frame.BytesPerPixel];
        for (var i = 0; i < grey.Length; i++)
        {
            var p = i * Frame.BytesPerPixel;
            bgra[p] = bgra[p + 1] = bgra[p + 2] = grey[i];
            bgra[p + 3] = 255;
        }

        return new Frame(width, height, bgra);
    }
}
