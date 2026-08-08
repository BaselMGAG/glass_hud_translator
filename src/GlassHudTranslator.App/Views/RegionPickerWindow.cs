using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Config;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GlassHudTranslator.App.Views;

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
    private readonly UiText _text;

    private Point _origin;
    private bool _dragging;

    public RegionPickerWindow(string profileName, Frame? screenshot = null,
        Func<CaptureRegion, Task<string>>? testOcr = null, UiText? text = null)
    {
        _screenshot = screenshot;
        _testOcr = testOcr;
        _text = text ?? UiText.En;

        // The region's display name, not its stored key: this window is full-screen over the game
        // and its instructions are the only thing on it, so a lone English word in them is loud.
        var region = _text.RegionName(profileName);

        Title = string.Format(_text.SelectRegionTitle, region);
        SystemDecorations = SystemDecorations.None;
        WindowState = WindowState.FullScreen;
        Topmost = true;
        Background = new SolidColorBrush(Colors.Black);

        // Bundled font at the window, for the reason NOTICE gives: a Windows install with no Arabic
        // font draws all of this as empty boxes, and this window's instructions are the only thing
        // telling the user what to do. Deliberately no FlowDirection here - the selection lives on a
        // Canvas, and mirroring the window would mirror its coordinates and save the wrong
        // rectangle. Only the instruction panel below is mirrored.
        FontFamily = _text.IsRightToLeft ? Fonts.Arabic : FontFamily.Default;

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

        // Both carry machine output - pixel measurements and the raw English the OCR read - so they
        // stay left-to-right even when the panel around them is mirrored.
        _readout = new TextBlock
        {
            FontSize = 15,
            Foreground = Brushes.White,
            FlowDirection = FlowDirection.LeftToRight,
        };
        _ocrPreview = new TextBlock
        {
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.Parse("#81c995")),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 760,
            FlowDirection = FlowDirection.LeftToRight,
        };

        var hud = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0a0a0c"), 0.9),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 14, 20, 16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 28, 0, 0),
            FlowDirection = _text.IsRightToLeft
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight,
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = string.Format(
                            screenshot is null ? _text.PickerHintPlain : _text.PickerHintFrozen,
                            region),
                        FontSize = 16,
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
            _readout.Text = _text.PickerTooSmall;
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
                var candidate = ToScreenPixels(CurrentRect());

                // The still can be wider than the monitor this window opened on - it covers every
                // screen - so Stretch.Uniform letterboxes it, and a drag that starts in the black
                // band maps outside the image entirely. Saving that produces a negative fraction
                // and a region that resolves to nothing, with no way for the user to tell why.
                if (_screenshot is not null
                    && !candidate.FitsWithin(_screenshot.Width, _screenshot.Height))
                {
                    _readout.Text = _text.SelectionOffScreen;
                    break;
                }

                Result = candidate;
                Close();
                break;

            case Key.Space when _selection.IsVisible && _testOcr is not null:
                _ocrPreview.Text = _text.OcrReading;
                try
                {
                    var text = await _testOcr(ToScreenPixels(CurrentRect()));
                    _ocrPreview.Text = string.IsNullOrWhiteSpace(text)
                        ? _text.OcrReadNothing
                        : $"{_text.OcrReads}   {text.Replace("\n", "   ⏎   ")}";
                }
                catch (Exception ex)
                {
                    _ocrPreview.Text = $"{_text.OcrFailed} {ex.Message}";
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
