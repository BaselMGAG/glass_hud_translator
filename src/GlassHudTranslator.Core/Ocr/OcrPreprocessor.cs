using GlassHudTranslator.Core.Capture;

namespace GlassHudTranslator.Core.Ocr;

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

    /// <summary>
    /// A margin of blank page added around the crop before recognition, in source pixels.
    ///
    /// <para>
    /// Detectors and Tesseract alike degrade when a glyph touches the image edge — there is no
    /// background left for the layout analysis to measure a character against — and a tightly drawn
    /// capture region guarantees exactly that. It is the reason a box the user drew "just around
    /// the words" reads worse than one drawn a little wide, which is not advice anyone should have
    /// to be given.
    /// </para>
    ///
    /// <para>
    /// Eight source pixels, which becomes sixteen after the 2x upscale. Every box Tesseract reports
    /// has the padding subtracted back off it before it is divided down, so the geometry contract
    /// is unchanged: word boxes stay in the coordinates of the frame that was handed in. Set to 0
    /// to switch it off.
    /// </para>
    /// </summary>
    public int PadPixels { get; init; } = 8;

    /// <summary>
    /// The fraction of pixels ignored at each end of the histogram when stretching contrast.
    ///
    /// <para>
    /// This is the fix for the quiet failure in the old stretch, which used the true minimum and
    /// maximum. One specular highlight, one white UI border clipped into the corner of the region,
    /// one fully black pixel — any single outlier pins an end of the range at its limit and the
    /// rescale becomes a near no-op on the text, which is the part that needed it. A real frame
    /// almost always has such an outlier; the synthetic corpus almost never does, which is why this
    /// survived so long.
    /// </para>
    ///
    /// <para>
    /// Two percent each end, and pixels outside the chosen range are clamped rather than wrapped.
    /// </para>
    /// </summary>
    public double ContrastTrim { get; init; } = 0.02;
}

/// <summary>
/// Prepares a captured region for Tesseract. Budgeted at 5-15 ms on weak hardware (brief 3).
/// </summary>
public static class OcrPreprocessor
{
    /// <summary>
    /// Upscale, greyscale, stretch, invert if needed, optionally binarise, then pad.
    ///
    /// <para>
    /// The padding goes on LAST, and that ordering is the whole reason it is safe. After the
    /// auto-invert the image is dark text on a light field whichever way round it started, so a
    /// white margin is continuous with the background rather than a bright frame drawn around it —
    /// which is what padding before the invert would produce, and which would be worse than no
    /// padding at all.
    /// </para>
    /// </summary>
    public static Frame Prepare(Frame frame, OcrPreprocessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var opts = options ?? new OcrPreprocessOptions();

        var working = opts.UpscaleFactor > 1
            ? frame.Resize(frame.Width * opts.UpscaleFactor, frame.Height * opts.UpscaleFactor)
            : frame;

        var grey = working.ToGreyscale();

        if (opts.StretchContrast) StretchContrast(grey, opts.ContrastTrim);
        if (opts.AutoInvert && IsLightOnDark(grey)) Invert(grey);
        if (opts.Binarise) Binarise(grey);

        var padded = PaddingFor(opts);
        return padded > 0
            ? FromGreyscale(Pad(grey, working.Width, working.Height, padded),
                working.Width + padded * 2, working.Height + padded * 2)
            : FromGreyscale(grey, working.Width, working.Height);
    }

    /// <summary>
    /// How many pixels of margin the prepared image carries, in the UPSCALED space the engine sees
    /// — which is the space Tesseract's boxes come back in, so it is the number that has to be
    /// subtracted off them.
    /// </summary>
    public static int PaddingFor(OcrPreprocessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Math.Max(0, options.PadPixels) * Math.Max(1, options.UpscaleFactor);
    }

    /// <summary>
    /// Rescales the luma range onto 0-255, so a dim capture is not judged on its dimness — ignoring
    /// <paramref name="trim"/> of the pixels at each end so a single bright or dark outlier cannot
    /// decide the range for everything else.
    /// </summary>
    private static void StretchContrast(byte[] grey, double trim)
    {
        if (grey.Length == 0) return;

        Span<int> histogram = stackalloc int[256];
        foreach (var g in grey) histogram[g]++;

        var ignore = (int)(grey.Length * Math.Clamp(trim, 0, 0.2));

        var min = Percentile(histogram, ignore, fromLow: true);
        var max = Percentile(histogram, ignore, fromLow: false);

        var span = max - min;
        if (span < 8) return;   // flat image; stretching would only amplify noise

        for (var i = 0; i < grey.Length; i++)
        {
            // Clamped, not wrapped. The trimmed outliers are outside [min, max] by construction, so
            // without this the brightest highlight in the frame would arithmetic-overflow into
            // black - which is a far more damaging artefact than the dullness being corrected.
            var value = (grey[i] - min) * 255 / span;
            grey[i] = (byte)Math.Clamp(value, 0, 255);
        }
    }

    /// <summary>The first luma level with more than <paramref name="ignore"/> pixels beyond it.</summary>
    private static int Percentile(ReadOnlySpan<int> histogram, int ignore, bool fromLow)
    {
        var running = 0;

        for (var step = 0; step < 256; step++)
        {
            var level = fromLow ? step : 255 - step;
            running += histogram[level];
            if (running > ignore) return level;
        }

        return fromLow ? 0 : 255;
    }

    /// <summary>
    /// Surrounds the image with white. White rather than the mean or a mirror of the edge: by this
    /// point the image is dark-on-light, so white is blank page, and blank page is exactly what
    /// layout analysis wants to find outside a block of text.
    /// </summary>
    private static byte[] Pad(byte[] grey, int width, int height, int pad)
    {
        var paddedWidth = width + pad * 2;
        var padded = new byte[paddedWidth * (height + pad * 2)];
        Array.Fill(padded, (byte)255);

        for (var y = 0; y < height; y++)
            Array.Copy(grey, y * width, padded, (y + pad) * paddedWidth + pad, width);

        return padded;
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
