using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace GamingTranslatorGlassHUD.App.Views;

/// <summary>
/// PROJECT_PLAN.md 7 - the go/no-go test that decides Avalonia vs WPF.
///
/// Renders the cases where Arabic layout most commonly breaks, under the conditions the real
/// overlay uses (white text, dark translucent panel, ~26px). Pass requires all four:
///   1. contextual letter joining (letters connect into words, not isolated glyph runs)
///   2. right-to-left flow
///   3. embedded Latin proper nouns running left-to-right inside the RTL line
///   4. sentence-final punctuation on the LEFT end of the line
///
/// The last row deliberately uses the default UI font, which has no Arabic coverage. If it looks
/// identical to the bundled-font rows, the bundled font is NOT loading and the OS is substituting.
/// </summary>
public sealed class ArabicRenderTestWindow : Window
{
    private const double PanelWidth = 1000;
    private const double PanelHeight = 1560;

    private readonly Border _root;
    private readonly string? _saveTo;
    private readonly bool _exitAfterSave;

    private static readonly (string Label, string Text)[] Cases =
    [
        ("1. Latin proper nouns mid-sentence, final period",
            "اذهب إلى Limsa Lominsa وتحدث مع Y'shtola."),

        ("2. HARD CASE - Latin proper noun at end, then period (brief 6)",
            "هذا هو مكان لقائنا مع Y'shtola."),

        ("3. Pure Arabic + diacritic - the live loading string",
            "جارٍ الترجمة..."),

        ("4. Western digits inside an RTL line",
            "لديك 3 جرعات و 12 قطعة ذهبية."),

        ("5. Mirrored punctuation and quotes",
            "«لقد عاد المحارب من النور.» — قال الثوري."),

        ("6. Two-line dialogue, as the overlay will actually show it",
            "لقد شعرت باضطراب في الأثير حول هذا المكان.\nيجب أن نتحرك بحذر يا صديقي."),
    ];

    public ArabicRenderTestWindow(string? saveTo, bool exitAfterSave)
    {
        _saveTo = saveTo;
        _exitAfterSave = exitAfterSave;

        Title = "GamingTranslatorGlassHUD - Arabic rendering go/no-go";
        Width = PanelWidth;
        Height = PanelHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _root = BuildRoot();
        Content = _root;
    }

    private static Border BuildRoot()
    {
        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(28, 22, 28, 22) };

        stack.Children.Add(new TextBlock
        {
            Text = "Arabic rendering go/no-go  ·  Avalonia 11.3 + HarfBuzz  ·  bundled Noto Sans Arabic",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#8ab4f8")),
        });

        // Eyeballing "does it look Arabic" cannot distinguish the bundled font from an OS
        // substitution - macOS would quietly fall back to Geeza Pro and still look fine, then the
        // same build would show boxes on a Windows machine without an Arabic font installed.
        // Ask the font manager what actually resolved.
        var resolved = ResolveFamilyName(Fonts.Arabic);
        var ok = resolved.Equals("Noto Sans Arabic", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"render-test: bundled font resolved to '{resolved}' (expected 'Noto Sans Arabic') -> {(ok ? "OK" : "SUBSTITUTED")}");
        stack.Children.Add(new TextBlock
        {
            Text = $"bundled font resolves to: \"{resolved}\"   →   {(ok ? "OK - embedded resource loaded" : "FAIL - OS substituted a different font")}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse(ok ? "#81c995" : "#f28b82")),
        });

        foreach (var (label, text) in Cases)
        {
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#9aa0a6")),
                Margin = new Thickness(0, 6, 0, 0),
            });
            stack.Children.Add(ArabicRow(text, Fonts.Arabic));
        }

        // Below-baseline diagnostic. Arabic hangs marks under the baseline (kasra/kasratan, the two
        // dots of final yeh, the dot of jeem). A too-tight LineHeight silently clips them, which
        // reads as "the font is broken" when it is actually the line box.
        stack.Children.Add(new TextBlock
        {
            Text = "BELOW-BASELINE DIAGNOSTIC - kasratan on جارٍ, two dots on final ي, dot under ج. "
                 + "All three must be visible in every row below.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#fdd663")),
            Margin = new Thickness(0, 14, 0, 0),
        });

        const string BelowBaseline = "جارٍ الثوري صديقي بِسْمِ";
        const double ProbeSize = 26;
        foreach (var (caption, lineHeight, lineSpacing) in new (string, double, double)[]
                 {
                     ("LineHeight 40  (1.54x) - EXPECT CLIPPED", 40, 0),
                     ("LineHeight 44  (1.69x)", 44, 0),
                     ("LineHeight 48  (1.85x)", 48, 0),
                     ("LineHeight 52  (2.00x)", 52, 0),
                     ("LineHeight auto - EXPECT CORRECT", double.NaN, 0),
                     ("LineHeight auto + LineSpacing 12 - the safe way to add air", double.NaN, 12),
                 })
        {
            stack.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.Parse("#9aa0a6")),
                Margin = new Thickness(0, 4, 0, 0),
            });
            var tb = new TextBlock
            {
                Text = BelowBaseline,
                FontFamily = Fonts.Arabic,
                FontSize = ProbeSize,
                Foreground = Brushes.White,
                FlowDirection = FlowDirection.RightToLeft,
                TextAlignment = TextAlignment.Right,
                LineSpacing = lineSpacing,
            };
            if (!double.IsNaN(lineHeight)) tb.LineHeight = lineHeight;
            stack.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#0a0a0c"), 0.72),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 4, 16, 4),
                Child = tb,
            });
        }

        // Report the natural line height so the overlay can pick a safe explicit value.
        var natural = new FormattedText(BelowBaseline, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.RightToLeft, new Typeface(Fonts.Arabic), ProbeSize, Brushes.White);
        Console.WriteLine($"render-test: natural line height at {ProbeSize}px = {natural.Height:F1}px "
                        + $"({natural.Height / ProbeSize:F2}x font size)");
        stack.Children.Add(new TextBlock
        {
            Text = $"natural line height at {ProbeSize}px = {natural.Height:F1}px "
                 + $"({natural.Height / ProbeSize:F2}x). Any explicit LineHeight below this clips marks.",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#81c995")),
        });

        stack.Children.Add(new TextBlock
        {
            Text = $"CONTROL - default UI font ({ResolveFamilyName(FontFamily.Default)} + OS fallback). "
                 + "Should be a visibly different typeface from the rows above. On Windows without an "
                 + "Arabic font installed this row is what the overlay would look like unbundled.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#fdd663")),
            Margin = new Thickness(0, 10, 0, 0),
        });
        stack.Children.Add(ArabicRow("اذهب إلى Limsa Lominsa وتحدث مع Y'shtola.", FontFamily.Default));

        return new Border
        {
            Width = PanelWidth,
            Height = PanelHeight,
            Background = new SolidColorBrush(Color.Parse("#16161a")),
            Child = new ScrollViewer { Content = stack },
        };
    }

    /// <summary>
    /// What the font manager actually resolved, as opposed to what we asked for. This is the only
    /// reliable way to catch a silent OS substitution.
    /// </summary>
    private static string ResolveFamilyName(FontFamily family) =>
        FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out var glyphTypeface)
            ? glyphTypeface.FamilyName
            : "<unresolved>";

    /// <summary>One line under real overlay conditions: dark translucent panel, white text, RTL.</summary>
    private static Border ArabicRow(string text, FontFamily font) => new()
    {
        Background = new SolidColorBrush(Color.Parse("#0a0a0c"), 0.72),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(16, 10, 16, 12),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = new TextBlock
        {
            Text = text,
            FontFamily = font,
            FontSize = 26,
            // NOT LineHeight. An explicit line height below ~1.9x font size clips Arabic
            // below-baseline marks - see the diagnostic block below and CLAUDE.md.
            LineSpacing = 8,
            Foreground = Brushes.White,
            FlowDirection = FlowDirection.RightToLeft,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.Wrap,
        },
    };

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_saveTo is null) return;

        // Background priority: let layout and the first render pass settle before capturing.
        Dispatcher.UIThread.Post(SaveSnapshot, DispatcherPriority.Background);
    }

    private void SaveSnapshot()
    {
        try
        {
            var size = new PixelSize((int)PanelWidth, (int)PanelHeight);
            using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
            bitmap.Render(_root);
            bitmap.Save(_saveTo!);
            Console.WriteLine($"render-test: wrote {_saveTo}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"render-test: FAILED - {ex}");
        }

        if (_exitAfterSave && Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
