using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace GlassHudTranslator.App.Views;

/// <summary>How much of itself the frame is currently offering to the mouse.</summary>
public enum FrameMode
{
    /// <summary>Not drawn at all.</summary>
    Hidden,

    /// <summary>Drawn, and completely transparent to clicks. This is what it is for most of the time.</summary>
    Shown,

    /// <summary>Drawn with handles, and grabbable. Every click in the rectangle belongs to us.</summary>
    Adjustable,
}

/// <summary>
/// A thin outline around the rectangle being captured, which can be dragged and resized in place.
///
/// <para>
/// The hard problem this would normally have does not exist here, and it is worth saying why. A
/// visible border around the region you are OCR'ing is a border inside the pixels you are about to
/// OCR: Tesseract reads it as a run of strokes, and the global contrast stretch keys off it before
/// that. Every general solution is unpleasant — hide the border, capture, show it again, and hope
/// nobody sees the flicker. We do not need any of them, because this window carries
/// <c>WDA_EXCLUDEFROMCAPTURE</c> exactly as the translation panel does, so it is not merely
/// unlikely to appear in our BitBlt, it physically cannot. The border is also drawn OUTSIDE the
/// rectangle — the window is inflated and the stored region deflated back — so even without the
/// exclusion it would sit on the wrong side of the boundary.
/// </para>
///
/// <para>
/// Direct manipulation, with no confirm step and no keyboard. Dragging the frame IS the edit, and
/// it is saved on release: there is nothing to confirm because the thing being edited is already
/// showing its own result. That also means the window never needs focus, which is the one piece of
/// behaviour on this platform that cannot be rehearsed away from a Windows machine.
/// </para>
/// </summary>
public sealed class CaptureFrameWindow : FloatingWindow
{
    /// <summary>Two device-independent pixels of line, and it sits outside the captured rectangle.</summary>
    private const double BorderDips = 2;

    private const double GripDips = 14;

    /// <summary>Same floor as the region picker: a stray drag must not shrink a working region to nothing.</summary>
    private const int MinimumWidth = 40;
    private const int MinimumHeight = 20;

    private static readonly IBrush Line = new SolidColorBrush(Color.Parse("#8ab4f8"));
    private static readonly IBrush GripFill = new SolidColorBrush(Color.Parse("#8ab4f8"), 0.9);

    private readonly Border _outline;
    private readonly Canvas _grips = new() { IsVisible = false };
    private readonly Rectangle[] _corners = new Rectangle[4];

    private CaptureRegion _region;
    private FrameMode _mode = FrameMode.Hidden;

    private Corner _dragging = Corner.None;
    private bool _moving;
    private PixelPoint _grabbedAt;
    private CaptureRegion _regionWas;

    public CaptureFrameWindow()
    {
        Title = "Glass HUD Translator capture frame";
        ShowActivated = false;
        Background = Brushes.Transparent;

        _outline = new Border
        {
            BorderBrush = Line,
            BorderThickness = new Thickness(BorderDips),
            CornerRadius = new CornerRadius(2),
            Background = Brushes.Transparent,
        };

        for (var i = 0; i < _corners.Length; i++)
        {
            var grip = new Rectangle
            {
                Width = GripDips,
                Height = GripDips,
                Fill = GripFill,
                RadiusX = 2,
                RadiusY = 2,
                Cursor = new Cursor(i is 0 or 3
                    ? StandardCursorType.TopLeftCorner
                    : StandardCursorType.TopRightCorner),
            };

            _corners[i] = grip;
            _grips.Children.Add(grip);
        }

        Content = new Panel { Children = { _outline, _grips } };

        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
    }

    /// <summary>
    /// Raised when a drag finishes, with the new rectangle in physical screen pixels. The window
    /// deliberately does not know how a region is stored — that is a fractional profile measured
    /// against a game's client area, and it belongs with the code that already does it for the
    /// picker rather than being written a second time here.
    /// </summary>
    public event Action<CaptureRegion>? Adjusted;

    public FrameMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;

            _mode = value;
            _grips.IsVisible = value == FrameMode.Adjustable;

            // Zero alpha is not merely invisible, it is absent as far as hit-testing goes - so in
            // Shown mode the interior lets clicks through on its own, and in Adjustable mode it has
            // to be given the one part in 255 that makes it grabbable. The extended style is set to
            // match either way rather than relying on that alone.
            _outline.Background = value == FrameMode.Adjustable ? BarelyThere : Brushes.Transparent;

            ApplyPlatformStyles();

            // Nothing to outline is not the same as being switched off, and it must not become a
            // zero-by-zero window near the origin - which is what a Show() before the first Track()
            // produces, and which reads as the feature being broken rather than as there being no
            // region yet.
            if (value == FrameMode.Hidden || _region.Width <= 0 || _region.Height <= 0)
            {
                Hide();
                return;
            }

            Apply();
            Show();
        }
    }

    /// <summary>
    /// Click-through unless the user is adjusting it, and hidden from capture always. That second
    /// part is what makes a visible border around an OCR'd rectangle possible at all.
    /// </summary>
    protected override OverlayStyleOptions StyleOptions =>
        (_mode == FrameMode.Adjustable ? OverlayStyleOptions.Interactive : OverlayStyleOptions.Panel)
        with { HideFromCapture = true };

    /// <summary>Points the frame at a rectangle, in physical screen pixels.</summary>
    public void Track(CaptureRegion region)
    {
        // Mid-drag the pointer is the authority on where this rectangle is. Auto-watch re-resolves
        // the region twice a second, and letting one of those land during a drag would snap the
        // frame back to the stored position under the user's hand.
        if (_dragging != Corner.None || _moving) return;

        var first = _region.Width <= 0 || _region.Height <= 0;
        _region = region;

        if (_mode == FrameMode.Hidden) return;

        Apply();

        // The first rectangle it has ever had, arriving after the mode was already set. Without
        // this the frame stays hidden until something toggles it again.
        if (first && !IsVisible) Show();
    }

    /// <summary>The rectangle currently outlined, in physical screen pixels.</summary>
    public CaptureRegion Region => _region;

    // ── geometry ──────────────────────────────────────────────────────────────────────────────

    private void Apply()
    {
        if (_region.Width <= 0 || _region.Height <= 0) return;

        var scaling = ScalingAt(_region);
        var border = (int)Math.Ceiling(BorderDips * scaling);

        Position = new PixelPoint(_region.X - border, _region.Y - border);
        Width = (_region.Width + border * 2) / scaling;
        Height = (_region.Height + border * 2) / scaling;

        var inner = new Rect(0, 0, Math.Max(0, Width), Math.Max(0, Height));
        var half = GripDips / 2;

        Place(_corners[0], inner.X, inner.Y);
        Place(_corners[1], inner.Right, inner.Y);
        Place(_corners[2], inner.Right, inner.Bottom);
        Place(_corners[3], inner.X, inner.Bottom);

        void Place(Rectangle grip, double x, double y)
        {
            Canvas.SetLeft(grip, x - half);
            Canvas.SetTop(grip, y - half);
        }
    }

    private double ScalingAt(CaptureRegion region)
    {
        var centre = new PixelPoint(region.X + region.Width / 2, region.Y + region.Height / 2);
        var scaling = Screens.ScreenFromPoint(centre)?.Scaling ?? RenderScaling;

        return double.IsNaN(scaling) || scaling <= 0 ? 1.0 : scaling;
    }

    // ── dragging ──────────────────────────────────────────────────────────────────────────────

    private enum Corner { None, TopLeft, TopRight, BottomRight, BottomLeft }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mode != FrameMode.Adjustable) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _grabbedAt = this.PointToScreen(e.GetPosition(this));
        _regionWas = _region;
        _dragging = CornerUnder(e.GetPosition(this));
        _moving = _dragging == Corner.None;

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging == Corner.None && !_moving) return;

        var now = this.PointToScreen(e.GetPosition(this));
        var dx = now.X - _grabbedAt.X;
        var dy = now.Y - _grabbedAt.Y;

        _region = _moving
            ? _regionWas.Translate(dx, dy)
            : Resized(_regionWas, _dragging, dx, dy);

        Apply();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging == Corner.None && !_moving) return;

        _dragging = Corner.None;
        _moving = false;
        e.Pointer.Capture(null);

        Adjusted?.Invoke(_region);
    }

    /// <summary>
    /// Which grip the pointer is on, by proximity rather than by hit-testing the shapes. The grips
    /// are 14 pixels wide and a hand-held mouse is not that accurate; a generous corner zone is the
    /// difference between resizing and accidentally moving the whole thing.
    /// </summary>
    private Corner CornerUnder(Point point)
    {
        const double reach = GripDips;

        var right = Width - point.X;
        var bottom = Height - point.Y;

        return (point.X <= reach, point.Y <= reach, right <= reach, bottom <= reach) switch
        {
            (true, true, _, _) => Corner.TopLeft,
            (_, true, true, _) => Corner.TopRight,
            (_, _, true, true) => Corner.BottomRight,
            (true, _, _, true) => Corner.BottomLeft,
            _ => Corner.None,
        };
    }

    /// <summary>
    /// Moves one corner and leaves the opposite one where it is, refusing to go below the floor
    /// rather than inverting. Dragging a corner past its opposite would otherwise produce a
    /// negative width, which is not a small rectangle - it is a rectangle every geometric test
    /// quietly declines to match.
    /// </summary>
    private static CaptureRegion Resized(CaptureRegion from, Corner corner, int dx, int dy)
    {
        var left = from.X;
        var top = from.Y;
        var right = from.X + from.Width;
        var bottom = from.Y + from.Height;

        switch (corner)
        {
            case Corner.TopLeft: left += dx; top += dy; break;
            case Corner.TopRight: right += dx; top += dy; break;
            case Corner.BottomRight: right += dx; bottom += dy; break;
            case Corner.BottomLeft: left += dx; bottom += dy; break;
        }

        if (right - left < MinimumWidth)
        {
            if (corner is Corner.TopLeft or Corner.BottomLeft) left = right - MinimumWidth;
            else right = left + MinimumWidth;
        }

        if (bottom - top < MinimumHeight)
        {
            if (corner is Corner.TopLeft or Corner.TopRight) top = bottom - MinimumHeight;
            else bottom = top + MinimumHeight;
        }

        return new CaptureRegion(left, top, right - left, bottom - top);
    }
}
