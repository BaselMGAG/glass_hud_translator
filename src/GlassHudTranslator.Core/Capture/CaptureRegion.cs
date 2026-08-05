namespace GlassHudTranslator.Core.Capture;

/// <summary>
/// A rectangle in pixels. Region profiles are persisted as fractions of the FFXIV client rect
/// (brief 8) and resolved to one of these only at capture time, so they survive window moves.
/// </summary>
public readonly record struct CaptureRegion(int X, int Y, int Width, int Height)
{
    public static readonly CaptureRegion Empty = new(0, 0, 0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool FitsWithin(int imageWidth, int imageHeight) =>
        !IsEmpty && X >= 0 && Y >= 0 && X + Width <= imageWidth && Y + Height <= imageHeight;

    public override string ToString() => $"{Width}x{Height}+{X}+{Y}";
}
