using GamingTranslatorGlassHUD.Core.Capture;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace GamingTranslatorGlassHUD.App.Views;

/// <summary>
/// Full-screen drag-a-rectangle picker.
///
/// <para>
/// Reports pixels; the caller converts to fractions of the FFXIV client rect before storing, so
/// the profile survives the window being moved (brief 8). Doing the conversion at the call site
/// rather than here keeps this window ignorant of where the game is, which is what lets it be
/// exercised on macOS.
/// </para>
/// </summary>
public sealed class RegionPickerWindow : Window
{
    private readonly Rectangle _selection;
    private readonly TextBlock _readout;
    private Point _origin;
    private bool _dragging;

    public RegionPickerWindow(string profileName)
    {
        Title = $"Select the {profileName} region";
        SystemDecorations = SystemDecorations.None;
        WindowState = WindowState.FullScreen;
        Topmost = true;
        Background = new SolidColorBrush(Colors.Black, 0.35);

        _selection = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.Parse("#8ab4f8")),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.Parse("#8ab4f8"), 0.15),
            IsVisible = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };

        _readout = new TextBlock
        {
            Text = $"Drag a rectangle over the {profileName} box.   Esc to cancel.",
            FontSize = 16,
            Foreground = Brushes.White,
            Margin = new Thickness(24),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };

        Content = new Canvas { Children = { _selection, _readout } };
    }

    /// <summary>Null when the user cancelled.</summary>
    public CaptureRegion? Result { get; private set; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _origin = e.GetPosition(this);
        _dragging = true;
        _selection.IsVisible = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;

        var current = e.GetPosition(this);
        var rect = Normalise(_origin, current);

        Canvas.SetLeft(_selection, rect.X);
        Canvas.SetTop(_selection, rect.Y);
        _selection.Width = rect.Width;
        _selection.Height = rect.Height;
        _readout.Text = $"{rect.Width:F0} x {rect.Height:F0}   at {rect.X:F0}, {rect.Y:F0}";
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;

        _dragging = false;
        var rect = Normalise(_origin, e.GetPosition(this));

        // A stray click should not wipe a working profile with a 2x2 region.
        if (rect.Width < 40 || rect.Height < 20)
        {
            _readout.Text = "Too small - drag across the whole dialogue box. Esc to cancel.";
            _selection.IsVisible = false;
            return;
        }

        Result = new CaptureRegion((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Result = null;
            Close();
        }
    }

    private static Rect Normalise(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
