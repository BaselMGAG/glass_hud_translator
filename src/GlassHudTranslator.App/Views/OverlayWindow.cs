using GlassHudTranslator.Core.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace GlassHudTranslator.App.Views;

/// <summary>
/// The Arabic overlay: transparent, borderless, always on top, and never focused.
///
/// <para>
/// Deliberately does not set an explicit <c>LineHeight</c>. Arabic hangs marks below the baseline -
/// kasra, the dot under jeem, the two dots of final yeh - and any line height under roughly 1.9x
/// the font size clips them silently. Clipping the dots turns "ي" into "ى", which changes the word,
/// so this is a correctness issue rather than a cosmetic one. Measured in PROJECT_PLAN.md 7; use
/// <see cref="TextBlock.LineSpacing"/> to add air instead, because it adds to the natural height
/// rather than replacing it and so stays correct when the user changes the font size in Settings.
/// </para>
/// </summary>
public sealed class OverlayWindow : FloatingWindow
{
    /// <summary>Painted the instant a translation is requested, so the ~1 s wait is not dead air.</summary>
    public const string LoadingText = "جارٍ الترجمة...";

    private readonly Border _panel;
    private readonly TextBlock _speaker;
    private readonly TextBlock _body;
    private readonly TextBlock _warning;

    public OverlayWindow()
    {
        Title = "Glass HUD Translator overlay";
        SizeToContent = SizeToContent.Height;
        Width = 900;

        _speaker = new TextBlock
        {
            FontFamily = Fonts.Arabic,
            FontSize = 18,
            Foreground = new SolidColorBrush(Color.Parse("#e9cd8a")),
            FlowDirection = FlowDirection.RightToLeft,
            TextAlignment = TextAlignment.Right,
            IsVisible = false,
        };

        _body = new TextBlock
        {
            FontFamily = Fonts.Arabic,
            FontSize = 26,
            LineSpacing = 8,
            Foreground = Brushes.White,
            FlowDirection = FlowDirection.RightToLeft,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.Wrap,
        };

        _warning = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#f28b82")),
            FlowDirection = FlowDirection.RightToLeft,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        _panel = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0a0a0c"), 0.82),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(22, 14, 22, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new StackPanel
            {
                Spacing = 4,
                Children = { _speaker, _body, _warning },
            },
        };

        Content = _panel;
    }

    /// <summary>Body text size. Named to avoid shadowing the inherited TemplatedControl.FontSize.</summary>
    public double BodyFontSize
    {
        get => _body.FontSize;
        set
        {
            _body.FontSize = value;
            _speaker.FontSize = Math.Max(12, value * 0.7);
        }
    }

    public double PanelOpacity
    {
        get => _panel.Background is SolidColorBrush brush ? brush.Opacity : 1;
        set
        {
            if (_panel.Background is SolidColorBrush brush) brush.Opacity = value;
        }
    }

    /// <summary>
    /// A line that stays under the translation until it is cleared, across any number of new
    /// translations. Everything else on this panel is one-shot.
    ///
    /// <para>
    /// It exists because auto-watch had no way to tell anyone anything. Every message it produces —
    /// switched on, expired, stopped by an error — went to the Settings status line, which a player
    /// in a fullscreen game is not looking at. A player reported the symptom exactly: it stops, and
    /// «كل ما يحصل مشكله اخش علي الاعدادات نفسها» — every time something goes wrong he has to go
    /// into Settings to find out what. A notice about spending is worth nothing where it cannot be
    /// read.
    /// </para>
    /// </summary>
    public string? Notice
    {
        get => _notice;
        set
        {
            _notice = value;
            Dispatcher.UIThread.Post(() =>
            {
                // Only repaint the slot; a live translation must not be disturbed by a notice
                // arriving underneath it.
                if (_warning.IsVisible && _stickyShown != true) return;

                _warning.Text = value;
                _warning.IsVisible = value is not null;
                _stickyShown = value is not null;
            });
        }
    }

    private string? _notice;
    private bool _stickyShown;

    /// <summary>
    /// Whether the overlay hides itself from screen capture.
    ///
    /// <para>
    /// True is the safe default and the reason the flag exists at all: without it our own BitBlt
    /// includes the Arabic we just drew, OCR reads it back, and the pipeline translates its own
    /// output. False is for the player who wants to record or stream with the translation visible —
    /// reported as «البرنامج بيمنع تصوير اي برنامج زي Nvidia app», which is a real thing to want
    /// and something the app simply refused. Re-applied live, so the switch takes effect without a
    /// restart.
    /// </para>
    /// </summary>
    public bool HideFromCapture
    {
        get => _hideFromCapture;
        set
        {
            if (_hideFromCapture == value) return;

            _hideFromCapture = value;
            ApplyPlatformStyles();
        }
    }

    private bool _hideFromCapture = true;

    /// <summary>
    /// Click-through except while the user is deliberately moving it. The panel is normally the
    /// one surface that must never eat a click — the user is aiming at the game behind it, and it
    /// has no controls of its own to aim at — but a thing you are asked to drag has to be a thing
    /// the mouse can reach.
    /// </summary>
    protected override OverlayStyleOptions StyleOptions =>
        (Movable ? OverlayStyleOptions.Interactive : OverlayStyleOptions.Panel)
        with { HideFromCapture = _hideFromCapture };

    /// <summary>
    /// Whether the panel can be picked up and dragged. Off is the normal state and the safe one:
    /// every moment this is true is a moment clicks meant for the game land on us instead, which
    /// is why nothing turns it on by itself and why it is one visible toggle rather than a mode
    /// the app can end up in without being asked.
    /// </summary>
    public bool Movable
    {
        get => _movable;
        set
        {
            if (_movable == value) return;

            _movable = value;
            ApplyPlatformStyles();

            // A dashed edge while it is loose. Without it, "movable" and "pinned" look identical
            // and the only way to find out which one you are in is to click the game and lose the
            // click - the exact thing the mode exists to make deliberate.
            _panel.BorderBrush = new SolidColorBrush(Color.Parse("#8ab4f8"));
            _panel.BorderThickness = value ? new Thickness(2) : new Thickness(0);
            Cursor = value ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
        }
    }

    private bool _movable;

    /// <summary>
    /// Raised when a drag finishes, with the panel's new top-left in physical screen pixels. The
    /// window does not know how a position is stored - that is a pair of fractions of the game's
    /// free space, which lives in Core - so it reports where it landed and lets the App convert.
    /// </summary>
    public event Action<PixelPoint>? Moved;

    private PixelPoint _grabbedAt;
    private PixelPoint _windowWasAt;
    private bool _dragging;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!Movable || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _dragging = true;
        _grabbedAt = this.PointToScreen(e.GetPosition(this));
        _windowWasAt = Position;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;

        // Same arithmetic as the toolbar and the capture frame: PointToScreen re-evaluated against
        // the window's current position gives the true pointer location however far it has already
        // moved, and anchoring to the grab rather than the previous frame stops rounding
        // accumulating across a long drag.
        var now = this.PointToScreen(e.GetPosition(this));
        Position = new PixelPoint(
            _windowWasAt.X + (now.X - _grabbedAt.X),
            _windowWasAt.Y + (now.Y - _grabbedAt.Y));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;

        _dragging = false;
        e.Pointer.Capture(null);
        Moved?.Invoke(Position);
    }

    public void ShowLoading(string? speaker = null) =>
        Dispatcher.UIThread.Post(() => Render(speaker, LoadingText, warning: null));

    public void ShowTranslation(string? speaker, string arabic) =>
        Dispatcher.UIThread.Post(() => Render(speaker, arabic, warning: null));

    /// <summary>
    /// Every provider failed, so this is the OCR'd English rather than a translation. Marked
    /// clearly, and left-to-right, because rendering Latin text under RTL rules reads as a bug.
    /// Never blank, never crash (brief 2.7).
    /// </summary>
    public void ShowFallbackEnglish(string? speaker, string english) =>
        Dispatcher.UIThread.Post(() => Render(speaker, english,
            warning: "تعذّرت الترجمة — يُعرض النص الإنجليزي", englishBody: true));

    public void ShowMessage(string message) =>
        Dispatcher.UIThread.Post(() => Render(null, message, warning: null));

    /// <summary>
    /// Something went wrong. Shown on the overlay rather than only in Settings, because the overlay
    /// is where the user is looking - an earlier version reported failures to the Settings status
    /// line only, so the overlay sat on "loading" forever and the app looked hung.
    ///
    /// <para>
    /// <paramref name="rightToLeft"/> follows the interface language, and the caller passes
    /// <c>UiText.IsRightToLeft</c> rather than this deciding for itself. It used to hardcode
    /// left-to-right, which was right while every one of these messages was an English literal in
    /// the session code - and stopped being right in the same commit that moved them all into
    /// UiText and gave them Arabic translations. The result was Arabic laid out as though it were
    /// English, on the most visible surface in the app, in the change whose entire point was that
    /// the overlay should stop answering in English.
    /// </para>
    /// </summary>
    public void ShowError(string message, bool rightToLeft) => Dispatcher.UIThread.Post(() =>
        Render(null, message, warning: null, englishBody: !rightToLeft, isError: true));

    /// <summary>Hides the panel entirely. Used when there is nothing to say.</summary>
    public void Clear() => Dispatcher.UIThread.Post(Hide);

    /// <summary>
    /// How tall the panel actually is, for placing it. <see cref="Avalonia.Layout.Layoutable.Height"/>
    /// is NaN here because the window sizes itself to its content, so asking for it before the
    /// first layout pass yields a position computed from NaN - which silently becomes a coordinate
    /// far off any screen.
    /// </summary>
    public int PanelHeight => (int)Math.Ceiling(
        Bounds.Height > 0 ? Bounds.Height :
        _panel.Bounds.Height > 0 ? _panel.Bounds.Height : 160);

    /// <summary>
    /// True when the user has hidden the HUD by hotkey. Distinct from simply not being shown:
    /// translations keep running and keep being cached while hidden, they just are not drawn, so
    /// unhiding is instant and nothing was missed.
    /// </summary>
    public bool HiddenByUser { get; private set; }

    /// <summary>Returns the new visibility, so the caller can report it.</summary>
    public bool ToggleHidden()
    {
        HiddenByUser = !HiddenByUser;

        if (HiddenByUser) Hide();
        else if (!string.IsNullOrEmpty(_body.Text)) Show();

        return !HiddenByUser;
    }

    private void Render(string? speaker, string text, string? warning, bool englishBody = false,
        bool isError = false)
    {
        _body.Foreground = isError ? new SolidColorBrush(Color.Parse("#f28b82")) : Brushes.White;
        _body.FontSize = isError ? Math.Min(18, BodyFontSize) : BodyFontSize;

        _speaker.Text = speaker;
        _speaker.IsVisible = !string.IsNullOrWhiteSpace(speaker);

        _body.Text = text;
        _body.FlowDirection = englishBody ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        _body.TextAlignment = englishBody ? TextAlignment.Left : TextAlignment.Right;

        // A one-shot warning wins for this line; otherwise the sticky notice fills the slot. One
        // control, two lifetimes - a second TextBlock would push the panel taller for a line that
        // is usually absent.
        var shown = warning ?? _notice;
        _warning.Text = shown;
        _warning.IsVisible = shown is not null;
        _stickyShown = warning is null && _notice is not null;

        // Respect an explicit hide: a new line arriving must not pop the HUD back over the game.
        if (!IsVisible && !HiddenByUser) Show();
    }

    /// <summary>
    /// Snapshots the panel exactly as drawn. Used by --overlay-test to verify the real overlay
    /// rather than a mock of it - the shaping rules above are easy to regress silently, and a
    /// screenshot of the actual production control is the only check that catches that.
    /// </summary>
    public void SavePanelSnapshot(string path)
    {
        var width = (int)Width;
        var height = (int)Math.Ceiling(_panel.Bounds.Height <= 0 ? 160 : _panel.Bounds.Height);

        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(_panel);
        bitmap.Save(path);
    }
}
