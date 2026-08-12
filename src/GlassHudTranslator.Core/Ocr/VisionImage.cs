using GlassHudTranslator.Core.Capture;

namespace GlassHudTranslator.Core.Ocr;

/// <summary>
/// How big to send a crop to a vision model, and why it is not what Tesseract wants.
///
/// <para>
/// <b>The preprocessing that helps one reader hurts the other, and it is not a close call.</b>
/// Image restoration cuts a traditional engine's character error rate by about half and a vision
/// model's by a few percent, because they read differently: Tesseract wants clean separable glyphs,
/// a vision model wants the picture. Greyscale, auto-invert and the contrast stretch all throw away
/// colour and anti-aliasing that the model uses — and in this app's flagship game the colour is
/// load-bearing, because speaker names and item names are colour-coded. So the vision lane gets the
/// raw colour crop and none of <see cref="OcrPreprocessor"/>.
/// </para>
///
/// <para>
/// <b>Upscaling still helps, for a completely different reason.</b> Not more information — there is
/// none — but more patch tokens per glyph. Providers cut an image into fixed patches of roughly 28
/// to 32 pixels, so a 22-pixel-tall glyph is smaller than a single patch and the model is being
/// asked to read a letter that never had a token of its own. Scaling up buys resolution in the only
/// unit the model actually sees.
/// </para>
///
/// <para>
/// <b>But only up to the lane's cap, because past it the server throws the extra away.</b> Every
/// provider downscales to fit its own ceiling before charging, so sending 3x of an already-large
/// crop delivers exactly the same pixels as 1.5x and costs the same tokens — the upload is simply
/// wasted. The clamp is what stops a large capture region turning into a slow request that buys
/// nothing.
/// </para>
/// </summary>
public sealed record VisionImageOptions
{
    /// <summary>
    /// The longest edge, in pixels, worth sending on this lane. Beyond it the provider resizes
    /// anyway. A per-lane number because the ceilings genuinely differ, and it is read from
    /// configuration rather than hard-coded for the reason every model number here is.
    /// </summary>
    public int LongestEdge { get; init; } = 1568;

    /// <summary>
    /// Never shrink below the original. A crop that is already larger than the cap is sent as-is
    /// and left for the provider to resize with whatever filter it prefers — ours is not better,
    /// and doing it twice softens the glyph edges the model is trying to read.
    /// </summary>
    public double MaximumUpscale { get; init; } = 3.0;
}

/// <summary>What was actually sent, so the cost can be explained afterwards.</summary>
public sealed record VisionImage(byte[] Png, int Width, int Height, double Scale);

public static class VisionImagePrep
{
    /// <summary>
    /// Scales a crop for a vision model. Pure arithmetic on the dimensions, so the decision is
    /// testable without encoding anything.
    /// </summary>
    public static (int Width, int Height, double Scale) SizeFor(int width, int height, VisionImageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (width <= 0 || height <= 0) return (Math.Max(width, 0), Math.Max(height, 0), 1);

        var longest = Math.Max(width, height);
        var cap = Math.Max(1, options.LongestEdge);

        // Already at or over the cap: send it untouched. Scaling down here would be doing the
        // provider's job worse, and scaling up would be immediately undone.
        if (longest >= cap) return (width, height, 1);

        var scale = Math.Min(cap / (double)longest, Math.Max(1.0, options.MaximumUpscale));

        return ((int)Math.Round(width * scale), (int)Math.Round(height * scale), scale);
    }

    /// <summary>
    /// The image to send: the raw colour crop, scaled, PNG-encoded.
    ///
    /// <para>
    /// PNG rather than JPEG deliberately. The saving is irrelevant — every provider charges by
    /// patch count, which is a function of dimensions alone, so a smaller file costs exactly the
    /// same — while JPEG's ringing lands hardest on high-contrast edges, which is what text is.
    /// </para>
    /// </summary>
    public static VisionImage Prepare(Frame frame, VisionImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var opts = options ?? new VisionImageOptions();
        var (width, height, scale) = SizeFor(frame.Width, frame.Height, opts);

        var sized = scale > 1.0 ? frame.Resize(width, height) : frame;

        return new VisionImage(sized.ToPng(), sized.Width, sized.Height, scale);
    }
}
