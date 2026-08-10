using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Regions;
using Ocr = GlassHudTranslator.Core.Ocr;
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

    /// <summary>
    /// Reads a crop THIS window already holds, rather than being handed a rectangle and going back
    /// to the screen for it.
    ///
    /// <para>
    /// That distinction is the whole of a bug that shipped for months. This window is full-screen,
    /// topmost and has no capture exclusion, so a live re-capture of the same rectangle grabs the
    /// picker's own rendering of the frozen still — plus the instruction panel and the blue
    /// selection box sitting over the very text being tested. On a single monitor the scale works
    /// out 1:1 and the result looks plausible, which is why nobody noticed. On two monitors the
    /// still spans the whole desktop while this window covers one screen, and "test what the OCR
    /// reads here" reported on entirely different pixels.
    /// </para>
    ///
    /// <para>
    /// Returns the whole <see cref="Ocr.OcrResult"/> rather than the text, because the confidence
    /// is the half of the answer the user cannot judge by eye: text that reads correctly at 45%
    /// will misread on the next frame, and "did I pick right?" deserves the number.
    /// </para>
    /// </summary>
    private readonly Func<Frame, Task<Ocr.OcrResult>>? _testOcr;
    private readonly Image _backdrop = new() { Stretch = Stretch.Uniform };
    private readonly Rectangle _selection;
    private readonly TextBlock _readout;
    private readonly TextBlock _ocrPreview;
    private readonly Canvas _canvas = new();
    private readonly UiText _text;

    private Point _origin;
    private bool _dragging;

    /// <summary>
    /// One-shot mode: the box is committed the moment the button comes up, rather than waiting for
    /// Enter.
    ///
    /// <para>
    /// The same window rather than a second one, because the two differ in exactly one gesture and
    /// everything else — the frozen still, the letterbox arithmetic, the too-small guard, the
    /// off-screen check — is the part that took the effort and the part that is easy to get subtly
    /// wrong twice.
    /// </para>
    /// </summary>
    private readonly bool _snip;

    public RegionPickerWindow(string profileName, Frame? screenshot = null,
        Func<Frame, Task<Ocr.OcrResult>>? testOcr = null, UiText? text = null, bool snip = false)
    {
        _screenshot = screenshot;
        _testOcr = testOcr;
        _text = text ?? UiText.En;
        _snip = snip;

        // The region's display name, not its stored key: this window is full-screen over the game
        // and its instructions are the only thing on it, so a lone English word in them is loud.
        var region = _text.RegionName(profileName);

        Title = snip ? _text.SnipTitle : string.Format(_text.SelectRegionTitle, region);
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
                        Text = snip
                            ? _text.SnipHint
                            : string.Format(
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

    // ── proposals ─────────────────────────────────────────────────────────────────────────────

    private readonly List<(RegionCandidate Candidate, Rect WindowRect)> _proposals = [];

    /// <summary>
    /// Draws rectangles the app believes contain text, each labelled with what it thinks the block
    /// is and how confidently it read it. <paramref name="candidates"/> are in the STILL's pixel
    /// coordinates — the same space <see cref="ToScreenPixels"/> maps into.
    ///
    /// <para>
    /// This is the roadmap's "is this the dialogue?" moment: the answer to "where is the text?"
    /// becomes a picture with a question mark instead of an instruction to drag a box over
    /// something. Arrives late and asynchronously — a full-frame OCR takes a second or two — so
    /// the window is fully usable before, during and without it, and a user who has already drawn
    /// their own box loses nothing. Clicking a suggestion adopts it; the outlines never intercept
    /// the pointer, so drawing straight through one works exactly as before.
    /// </para>
    /// </summary>
    public void ShowProposals(IReadOnlyList<RegionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (_screenshot is null) return;

        // Two or three, never a list. The finder ranks; past the third the proposals stop being
        // suggestions and start being clutter over the picture the user is trying to read.
        foreach (var candidate in candidates.Take(3))
        {
            var rect = FromScreenPixels(candidate.Bounds);
            if (rect.Width < 8 || rect.Height < 8) continue;

            var outline = new Rectangle
            {
                Width = rect.Width,
                Height = rect.Height,
                Stroke = new SolidColorBrush(Color.Parse("#81c995")),
                StrokeThickness = 2,
                StrokeDashArray = [5, 4],
                Fill = new SolidColorBrush(Color.Parse("#81c995"), 0.06),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(outline, rect.X);
            Canvas.SetTop(outline, rect.Y);

            var label = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#0a0a0c"), 0.85),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = string.Format(_text.SuggestionLabel, KindName(candidate.Kind),
                        candidate.WordCount, Math.Round(candidate.MeanConfidence)),
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.Parse("#81c995")),
                },
            };
            Canvas.SetLeft(label, rect.X);
            Canvas.SetTop(label, Math.Max(0, rect.Y - 26));

            _canvas.Children.Add(outline);
            _canvas.Children.Add(label);
            _proposals.Add((candidate, rect));
        }

        if (_proposals.Count > 0 && string.IsNullOrEmpty(_ocrPreview.Text))
            _ocrPreview.Text = _text.PickerSuggestionHint;
    }

    /// <summary>
    /// The finder's kinds, in the interface language. SidePanel borrows the quest label — a quest
    /// log is what that shape almost always is — and Unknown is honest about knowing nothing.
    /// </summary>
    private string KindName(TextRegionKind kind) => kind switch
    {
        TextRegionKind.Dialogue => _text.RegionDialogue,
        TextRegionKind.Subtitle => _text.RegionSubtitle,
        TextRegionKind.SidePanel => _text.RegionQuest,
        _ => _text.RegionTextBlock,
    };

    /// <summary>
    /// A click that was too small to be a drag, landing inside a proposal, adopts it. Geometric
    /// rather than control hit-testing, so the outlines can stay transparent to the pointer and a
    /// drag that starts inside one still draws a fresh box.
    /// </summary>
    private bool TryAdoptProposal(Point point)
    {
        foreach (var (candidate, rect) in _proposals)
        {
            if (!rect.Contains(point)) continue;

            Draw(rect);
            _selection.IsVisible = true;
            _ocrPreview.Text = string.Format(_text.SuggestionLabel, KindName(candidate.Kind),
                candidate.WordCount, Math.Round(candidate.MeanConfidence))
                + "   —   " + _text.SuggestionAdopted;

            if (_snip) Commit();
            return true;
        }

        return false;
    }

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

        // A stray click should not wipe a working profile with a 2x2 region - but a click ON a
        // proposal is not stray, it is the answer to the question the proposal asks.
        if (rect.Width < 40 || rect.Height < 20)
        {
            if (TryAdoptProposal(e.GetPosition(this))) return;

            _readout.Text = _text.PickerTooSmall;
            _selection.IsVisible = false;
            return;
        }

        Draw(rect);

        // A snip is over when the button comes up. Asking for Enter as well would mean the user has
        // drawn the box, seen it, and still has to confirm the thing they just drew - and the whole
        // point of this mode is that it is faster than picking a region.
        if (_snip) Commit();
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
                Commit();
                break;

            case Key.Space when _selection.IsVisible && _testOcr is not null:
                // No still means no crop to read. Off Windows that is the normal state, and the
                // honest answer is the one the old code arrived at by accident.
                if (_screenshot is null)
                {
                    _ocrPreview.Text = _text.CaptureWindowsOnly;
                    break;
                }

                var candidateRect = ToScreenPixels(CurrentRect());
                if (!candidateRect.FitsWithin(_screenshot.Width, _screenshot.Height))
                {
                    _ocrPreview.Text = _text.SelectionOffScreen;
                    break;
                }

                _ocrPreview.Text = _text.OcrReading;
                try
                {
                    // The crop comes from the still this window is already displaying, so what the
                    // OCR reads is exactly what the user can see inside the blue box.
                    var read = await _testOcr(_screenshot.Crop(candidateRect));
                    _ocrPreview.Text = string.IsNullOrWhiteSpace(read.RawText)
                        ? _text.OcrReadNothing
                        : $"{_text.OcrReads}   {read.RawText.Replace("\n", "   ⏎   ")}   "
                          + $"({string.Format(_text.OcrConfidence, Math.Round(read.Confidence))})";
                }
                catch (Exception ex)
                {
                    _ocrPreview.Text = $"{_text.OcrFailed} {ex.Message}";
                }

                break;
        }
    }

    /// <summary>
    /// Accepts the current selection, or refuses it and says why.
    ///
    /// <para>
    /// The still can be wider than the monitor this window opened on — it covers every screen — so
    /// <c>Stretch.Uniform</c> letterboxes it, and a drag that starts in the black band maps outside
    /// the image entirely. Committing that produces a negative fraction and a region that resolves
    /// to nothing, with no way for the user to tell why.
    /// </para>
    /// </summary>
    private void Commit()
    {
        var candidate = ToScreenPixels(CurrentRect());

        if (_screenshot is not null && !candidate.FitsWithin(_screenshot.Width, _screenshot.Height))
        {
            _readout.Text = _text.SelectionOffScreen;
            _selection.IsVisible = false;
            return;
        }

        Result = candidate;
        Close();
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

    /// <summary>
    /// The inverse of <see cref="ToScreenPixels"/>: a rectangle in the still's pixels, mapped into
    /// this window's coordinates for drawing. The letterbox offset and scale are the same numbers
    /// applied the other way round, and they must be — a proposal drawn with any other arithmetic
    /// than the save uses would highlight one rectangle and store another.
    /// </summary>
    private Rect FromScreenPixels(CaptureRegion region)
    {
        if (_screenshot is null || _backdrop.Bounds.Width <= 0)
        {
            var scaling = RenderScaling;
            return scaling <= 0
                ? default
                : new Rect(region.X / scaling, region.Y / scaling,
                    region.Width / scaling, region.Height / scaling);
        }

        var displayed = _backdrop.Bounds;
        var scale = Math.Min(displayed.Width / _screenshot.Width, displayed.Height / _screenshot.Height);
        var offsetX = displayed.X + (displayed.Width - _screenshot.Width * scale) / 2;
        var offsetY = displayed.Y + (displayed.Height - _screenshot.Height * scale) / 2;

        return new Rect(
            region.X * scale + offsetX, region.Y * scale + offsetY,
            region.Width * scale, region.Height * scale);
    }

    private static Rect Normalise(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
