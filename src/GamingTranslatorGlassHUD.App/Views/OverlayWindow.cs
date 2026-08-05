using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace GamingTranslatorGlassHUD.App.Views;

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
        Title = "GamingTranslatorGlassHUD overlay";
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

    private void Render(string? speaker, string text, string? warning, bool englishBody = false)
    {
        _speaker.Text = speaker;
        _speaker.IsVisible = !string.IsNullOrWhiteSpace(speaker);

        _body.Text = text;
        _body.FlowDirection = englishBody ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        _body.TextAlignment = englishBody ? TextAlignment.Left : TextAlignment.Right;

        _warning.Text = warning;
        _warning.IsVisible = warning is not null;

        if (!IsVisible) Show();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Click-through, no-activate, topmost, and excluded from its own captures. No-op off
        // Windows; Session 2 implements it.
        if (TryGetPlatformHandle() is { } handle)
            PlatformServices.ApplyOverlayWindowStyles(handle.Handle);
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
