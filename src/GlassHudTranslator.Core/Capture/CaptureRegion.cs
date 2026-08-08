namespace GlassHudTranslator.Core.Capture;

/// <summary>
/// A rectangle in pixels. Region profiles are persisted as fractions of the FFXIV client rect
/// (brief 8) and resolved to one of these only at capture time, so they survive window moves.
/// </summary>
public readonly record struct CaptureRegion(int X, int Y, int Width, int Height)
{
    public static readonly CaptureRegion Empty = new(0, 0, 0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Whether this rectangle lies inside a pixel buffer of the given size.
    ///
    /// <para>
    /// Buffer coordinates only, which is why a negative origin is rejected: there is no pixel at
    /// index -1. Do not use this to ask whether a rectangle is on screen — the desktop can start at
    /// a negative coordinate when a monitor sits to the left of or above the primary one, and this
    /// would refuse every rectangle on it. That question is <see cref="Contains"/>.
    /// </para>
    /// </summary>
    public bool FitsWithin(int imageWidth, int imageHeight) =>
        !IsEmpty && X >= 0 && Y >= 0 && X + Width <= imageWidth && Y + Height <= imageHeight;

    /// <summary>
    /// Whether <paramref name="inner"/> lies inside this rectangle, wherever this one starts.
    ///
    /// <para>
    /// The screen-space counterpart of <see cref="FitsWithin"/>. The virtual desktop's origin is
    /// whatever the leftmost and topmost monitors put it at, frequently negative, so containment
    /// has to be expressed relative to an origin rather than to zero.
    /// </para>
    /// </summary>
    public bool Contains(CaptureRegion inner) =>
        !IsEmpty && !inner.IsEmpty
        && inner.X >= X && inner.Y >= Y
        && inner.X + inner.Width <= X + Width
        && inner.Y + inner.Height <= Y + Height;

    /// <summary>Moves the rectangle without resizing it.</summary>
    public CaptureRegion Translate(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>
    /// Re-expresses a screen rectangle in the coordinates of a buffer captured from
    /// <paramref name="origin"/>. This is the conversion that has to happen between "where it is on
    /// the desktop" and "where it is in the frame we grabbed", and doing it implicitly by assuming
    /// the origin is (0,0) is what confines the app to the primary monitor.
    /// </summary>
    public CaptureRegion RelativeTo(CaptureRegion origin) => Translate(-origin.X, -origin.Y);

    public override string ToString() => $"{Width}x{Height}+{X}+{Y}";
}
