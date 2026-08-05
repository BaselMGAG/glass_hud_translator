using GlassHudTranslator.Core.Capture;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// Builds frames from raw BGRA, with no text rendering. Tests must not depend on a font being
/// installed: the CI test job runs on ubuntu, where the available typefaces differ from macOS and
/// any glyph-based fixture would drift or render blank.
/// </summary>
internal sealed class FrameBuilder
{
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _bgra;

    public FrameBuilder(int width, int height, Rgb fill)
    {
        _width = width;
        _height = height;
        _bgra = new byte[width * height * Frame.BytesPerPixel];
        Rect(0, 0, width, height, fill);
    }

    public FrameBuilder Rect(int x, int y, int width, int height, Rgb colour)
    {
        for (var row = y; row < y + height; row++)
        {
            if (row < 0 || row >= _height) continue;
            for (var col = x; col < x + width; col++)
            {
                if (col < 0 || col >= _width) continue;
                var p = (row * _width + col) * Frame.BytesPerPixel;
                _bgra[p + 0] = colour.B;
                _bgra[p + 1] = colour.G;
                _bgra[p + 2] = colour.R;
                _bgra[p + 3] = 255;
            }
        }

        return this;
    }

    public Frame Build() => new(_width, _height, _bgra);

    /// <summary>
    /// The FFXIV dialogue box is translucent, so whatever is behind it bleeds through. Change
    /// detection has to survive that (see FrameSignature docs), so fixtures model it explicitly.
    /// </summary>
    public static Rgb Blend(Rgb over, Rgb under, double overOpacity) => new(
        (byte)(over.R * overOpacity + under.R * (1 - overOpacity)),
        (byte)(over.G * overOpacity + under.G * (1 - overOpacity)),
        (byte)(over.B * overOpacity + under.B * (1 - overOpacity)));
}

internal readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb TextWhite = new(240, 240, 240);
    public static readonly Rgb BoxDark = new(20, 20, 25);

    public static readonly Rgb DarkScene = new(40, 40, 50);
    public static readonly Rgb BrightScene = new(200, 180, 120);
}
