using Avalonia;
using Avalonia.Controls;
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
public sealed class OverlayWindow : Window
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
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
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
    /// Whether this build could hide the overlay from its own screen capture, and what to do if
    /// not. Null when everything is fine.
    ///
    /// <para>
    /// Recorded rather than discarded, which it was until a player reported the overlay covering
    /// the game's English text and asked whether that was why translation had stopped working. It
    /// is exactly why it would: with the exclusion unavailable, the capture includes the Arabic
    /// panel, OCR reads that back, and the pipeline translates its own output. The app already
    /// knew this had happened and had nowhere to say it.
    /// </para>
    /// </summary>
    public string? CaptureExclusionWarning { get; private set; }

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

        _warning.Text = warning;
        _warning.IsVisible = warning is not null;

        // Respect an explicit hide: a new line arriving must not pop the HUD back over the game.
        if (!IsVisible && !HiddenByUser) Show();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Click-through, no-activate, topmost, and excluded from its own captures. No-op off
        // Windows; Session 2 implements it.
        if (TryGetPlatformHandle() is { } handle)
            CaptureExclusionWarning = PlatformServices.ApplyOverlayWindowStyles(handle.Handle);
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
