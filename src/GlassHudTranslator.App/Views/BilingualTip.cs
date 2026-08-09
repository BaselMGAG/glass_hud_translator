using GlassHudTranslator.Core.Config;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GlassHudTranslator.App.Views;

/// <summary>
/// A tooltip that says the same thing in Arabic and in English, at the same time, whichever
/// language the interface is set to.
///
/// <para>
/// The reasoning is the one already applied to the «Language · اللغة» control on the Providers tab,
/// and it applies more strongly here. A settings row at least has a label you can read; a toolbar
/// button is a shape and nothing else. An Arabic-only tip fails the English speaker who opened the
/// app to set it up for a friend, and an English-only tip fails the person the app exists for. So
/// both, every time — the cost is one extra line in a box nobody looks at for long.
/// </para>
///
/// <para>
/// Two <see cref="TextBlock"/>s rather than one string with a separator, and that is the load-bearing
/// detail. The bundled Arabic font contains no Latin at all, so a single control holding
/// "Translate now · ترجم الآن" would have to resolve half its characters through OS fallback — the
/// exact dependency the font is bundled to remove. Separate controls let the Arabic line use the
/// bundled font and the Latin line use the system default, each rendering what it actually has, and
/// no separator glyph is needed between them because they are on different lines.
/// </para>
///
/// <para>
/// The interface language leads. Both are present either way, but the one the user chose is the one
/// their eye lands on first.
/// </para>
/// </summary>
public static class BilingualTip
{
    /// <summary>
    /// <paramref name="hotkey"/> is machine text — "Ctrl+Shift+T" — so it gets its own
    /// left-to-right line rather than being folded into the Arabic sentence, where the modifiers
    /// would reorder and the binding would read backwards.
    /// </summary>
    public static Control For(UiText text, string english, string arabic, string? hotkey = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var panel = new StackPanel { Spacing = 3, MaxWidth = 320 };

        if (text.IsRightToLeft)
        {
            panel.Children.Add(ArabicLine(arabic, lead: true));
            panel.Children.Add(EnglishLine(english, lead: false));
        }
        else
        {
            panel.Children.Add(EnglishLine(english, lead: true));
            panel.Children.Add(ArabicLine(arabic, lead: false));
        }

        if (!string.IsNullOrWhiteSpace(hotkey)) panel.Children.Add(HotkeyLine(hotkey));

        return panel;
    }

    private static TextBlock ArabicLine(string arabic, bool lead) => new()
    {
        Text = arabic,
        FontFamily = Fonts.Arabic,
        FontSize = lead ? 14 : 13,

        // Never an explicit LineHeight on Arabic. Kasra, the dot under jeem and the two dots of a
        // final yeh all hang below the baseline, and clipping those two dots turns "ي" into "ى" -
        // a different letter, often a different word. LineSpacing adds to the natural height
        // instead of replacing it.
        LineSpacing = 2,
        Foreground = lead ? Brushes.White : Secondary,
        FlowDirection = FlowDirection.RightToLeft,
        TextAlignment = TextAlignment.Right,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private static TextBlock EnglishLine(string english, bool lead) => new()
    {
        Text = english,
        FontSize = lead ? 14 : 13,
        Foreground = lead ? Brushes.White : Secondary,
        FlowDirection = FlowDirection.LeftToRight,
        TextAlignment = TextAlignment.Left,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private static TextBlock HotkeyLine(string hotkey) => new()
    {
        Text = hotkey,
        FontSize = 12,
        Foreground = new SolidColorBrush(Color.Parse("#8ab4f8")),
        FlowDirection = FlowDirection.LeftToRight,
        TextAlignment = TextAlignment.Left,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// The second language is dimmer, not smaller past readability. It is the same information, so
    /// hiding it would defeat the point; it is the one you did not ask for, so it recedes.
    /// </summary>
    private static readonly IBrush Secondary = new SolidColorBrush(Color.Parse("#c8ccd0"));
}
