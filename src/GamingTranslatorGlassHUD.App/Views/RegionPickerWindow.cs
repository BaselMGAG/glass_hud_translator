using GamingTranslatorGlassHUD.Core.Capture;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GamingTranslatorGlassHUD.App.Views;

/// <summary>
/// Region picker, drawn on a frozen screenshot of the screen.
///
/// <para>
/// The first version dimmed the live desktop and asked the user to drag over it. That is much
/// harder than it sounds while a game is running: the dialogue you are trying to frame keeps
/// advancing, and a translucent box over a moving scene is difficult to judge. Freezing a still
/// first means the text stays put while you drag, and it also makes the "test what the OCR reads
/// here" step possible - the same pixels can be re-read as often as you like.
/// </para>
/// </summary>
public sealed class RegionPickerWindow : Window
{
    private readonly Frame? _screenshot;
    private readonly Func<CaptureRegion, Task<string>>? _testOcr;
    private readonly Image _backdrop = new() { Stretch = Stretch.Uniform };
    private readonly Rectangle _selection;
    private readonly TextBlock _readout;
    private readonly TextBlock _ocrPreview;
    private readonly Canvas _canvas = new();

    private Point _origin;
    private bool _dragging;

    public RegionPickerWindow(string profileName, Frame? screenshot = null,
        Func<CaptureRegion, Task<string>>? testOcr = null)
    {
        _screenshot = screenshot;
        _testOcr = testOcr;

        Title = $"Select the {profileName} region";
        SystemDecorations = SystemDecorations.None;
        WindowState = WindowState.FullScreen;
        Topmost = true;
        Background = new SolidColorBrush(Colors.Black);

        if (screenshot is not null)
        {
            using var stream = new MemoryStream(screenshot.ToPng());
            _backdrop.Source = new Bitmap(stream);
        }

        _selection = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.Parse("#8ab4f8")),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.Parse("#8ab4f8"), 0.18),
            IsVisible = false,
        };

        _readout = new TextBlock { FontSize = 15, Foreground = Brushes.White };
        _ocrPreview = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#81c995")),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 760,
        };

        var hud = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0a0a0c"), 0.9),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 14, 20, 16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 28, 0, 0),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = screenshot is null
                            ? $"Drag a box over the {profileName} area.    Enter saves  ·  Esc cancels"
                            : $"Drag a box over the {profileName} text. This is a frozen screenshot, so "
                              + "nothing will move while you aim.    Space tests the OCR  ·  Enter saves  ·  Esc cancels",
                        FontSize = 15,
                        Foreground = Brushes.White,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 900,
                    },
                    _readout,
                    _ocrPreview,
                },
            },
        };

        _canvas.Children.Add(_selection);
        Content = new Panel { Children = { _backdrop, _canvas, hud } };
    }

    /// <summary>Null when the user cancelled. In physical screen pixels, ready for capture.</summary>
    public CaptureRegion? Result { get; private set; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _origin = e.GetPosition(this);
        _dragging = true;
        _selection.IsVisible = true;
        _ocrPreview.Text = "";
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) Draw(Normalise(_origin, e.GetPosition(this)));
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
            _readout.Text = "Too small - drag across the whole text box.";
            _selection.IsVisible = false;
            return;
        }

        Draw(rect);
    }

    private void Draw(Rect rect)
    {
        Canvas.SetLeft(_selection, rect.X);
        Canvas.SetTop(_selection, rect.Y);
        _selection.Width = rect.Width;
        _selection.Height = rect.Height;

        var region = ToScreenPixels(rect);
        _readout.Text = $"{region.Width} x {region.Height} px   at {region.X}, {region.Y}";
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Escape:
                Result = null;
                Close();
                break;

            case Key.Enter when _selection.IsVisible:
                Result = ToScreenPixels(CurrentRect());
                Close();
                break;

            case Key.Space when _selection.IsVisible && _testOcr is not null:
                _ocrPreview.Text = "Reading...";
                try
                {
                    var text = await _testOcr(ToScreenPixels(CurrentRect()));
                    _ocrPreview.Text = string.IsNullOrWhiteSpace(text)
                        ? "OCR read nothing here. Try covering more of the text, or less of the border."
                        : $"OCR reads:   {text.Replace("\n", "   ⏎   ")}";
                }
                catch (Exception ex)
                {
                    _ocrPreview.Text = $"OCR failed: {ex.Message}";
                }

                break;
        }
    }

    private Rect CurrentRect() => new(
        Canvas.GetLeft(_selection), Canvas.GetTop(_selection), _selection.Width, _selection.Height);

    /// <summary>
    /// Converts a rectangle in this window's device-independent pixels into physical screen pixels.
    ///
    /// <para>
    /// The screenshot is captured in physical pixels but displayed scaled to fit, so the ratio
    /// between the two has to be applied explicitly. Without this every saved region would be wrong
    /// at any display scaling other than 100%.
    /// </para>
    /// </summary>
    private CaptureRegion ToScreenPixels(Rect rect)
    {
        if (_screenshot is null || _backdrop.Bounds.Width <= 0)
        {
            var scaling = RenderScaling;
            return new CaptureRegion(
                (int)(rect.X * scaling), (int)(rect.Y * scaling),
                (int)(rect.Width * scaling), (int)(rect.Height * scaling));
        }

        // Stretch.Uniform can letterbox the image, so the offset matters as well as the scale.
        var displayed = _backdrop.Bounds;
        var scale = Math.Min(displayed.Width / _screenshot.Width, displayed.Height / _screenshot.Height);
        var offsetX = displayed.X + (displayed.Width - _screenshot.Width * scale) / 2;
        var offsetY = displayed.Y + (displayed.Height - _screenshot.Height * scale) / 2;

        return new CaptureRegion(
            (int)Math.Round((rect.X - offsetX) / scale),
            (int)Math.Round((rect.Y - offsetY) / scale),
            (int)Math.Round(rect.Width / scale),
            (int)Math.Round(rect.Height / scale));
    }

    private static Rect Normalise(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
