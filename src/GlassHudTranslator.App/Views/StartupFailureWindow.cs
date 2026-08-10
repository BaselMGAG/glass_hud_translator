using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GlassHudTranslator.App.Views;

/// <summary>
/// The window a startup failure appears on — a completely ordinary one, and that is its entire
/// design.
///
/// <para>
/// Startup errors used to go where every other error goes: the overlay. The overlay is transparent,
/// click-through, absent from the taskbar and from Alt-Tab — properties chosen so it can float over
/// a game, and which turn an error shown on it into an error shown to nobody. A real user reported
/// the result exactly: "nothing opens." The app was running, its explanation on screen, invisible.
/// So this window is everything the overlay refuses to be: decorations, a taskbar entry, focus, a
/// close button.
/// </para>
///
/// <para>
/// Both languages, always, stacked. A startup failure is the one moment the language preference
/// itself may be part of what failed to load, so the window does not choose — the same reasoning
/// as the toolbar's tooltips, applied to the message where guessing wrong costs most.
/// </para>
/// </summary>
public sealed class StartupFailureWindow : Window
{
    public StartupFailureWindow(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        Title = UiText.En.StartupFailedTitle;
        Width = 640;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // Explicit rather than themed, along with every foreground below. This window appears when
        // loading things has already failed once; it assumes as little as it can get away with.
        Background = new SolidColorBrush(Color.Parse("#101216"));

        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(24, 20) };

        stack.Children.Add(new TextBlock
        {
            Text = UiText.Ar.StartupFailedTitle,
            FontFamily = Fonts.Arabic,
            FontSize = 18,
            LineSpacing = 4,
            Foreground = new SolidColorBrush(Color.Parse("#f28b82")),
            FlowDirection = FlowDirection.RightToLeft,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.Wrap,
        });

        stack.Children.Add(new TextBlock
        {
            Text = UiText.Ar.StartupFailedBody,
            FontFamily = Fonts.Arabic,
            FontSize = 14,
            LineSpacing = 3,
            Foreground = new SolidColorBrush(Color.Parse("#e8eaed")),
            FlowDirection = FlowDirection.RightToLeft,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.Wrap,
        });

        stack.Children.Add(new TextBlock
        {
            Text = UiText.En.StartupFailedBody,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.Parse("#c8ccd0")),
            TextWrapping = TextWrapping.Wrap,
        });

        // Selectable, because the whole point is that this text leaves the machine. An error that
        // can only be retyped by hand arrives in the bug report abbreviated, and abbreviated by
        // the person least equipped to know which part mattered.
        stack.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#16181d")),
            BorderBrush = new SolidColorBrush(Color.Parse("#343943")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10),
            Child = new SelectableTextBlock
            {
                Text = error.ToString(),
                FontSize = 12,
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                Foreground = new SolidColorBrush(Color.Parse("#e8eaed")),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 260,
            },
        });

        if (StartupLog.Path is { } logPath)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"{UiText.En.StartupFailedLogAt}  ·  {UiText.Ar.StartupFailedLogAt}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#c8ccd0")),
                TextWrapping = TextWrapping.Wrap,
            });
            stack.Children.Add(new SelectableTextBlock
            {
                Text = logPath,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#8ab4f8")),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var close = new Button
        {
            Content = $"{UiText.En.StartupFailedClose} · {UiText.Ar.StartupFailedClose}",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(18, 6),
        };
        close.Click += (_, _) => Close();
        stack.Children.Add(close);

        Content = stack;
    }
}
