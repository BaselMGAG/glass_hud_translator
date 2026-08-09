using GlassHudTranslator.Core.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GlassHudTranslator.App.Views;

/// <summary>
/// The shared parts of every window this app floats over a game: borderless, topmost, out of
/// Alt-Tab, out of the taskbar, and re-styled through the Win32 seam the moment it has a handle.
///
/// <para>
/// There are three of them now — the translation panel, the toolbar and the capture frame — and
/// they disagree only about whether clicks pass through. Everything else was being copied, and a
/// copied <c>OnOpened</c> that forgets to apply the styles produces a window that looks right on
/// macOS and is a plain, focus-stealing, self-capturing window on the machine that matters.
/// </para>
///
/// <para>
/// A note on transparency, because it is the one thing here that is not obvious. A window drawn
/// with fully transparent pixels is not merely invisible, it is invisible to the mouse: the
/// compositor hit-tests against alpha, so a click lands on whatever is behind it no matter what
/// the extended styles say. Anything that wants to be grabbed therefore paints its background at
/// alpha 1/255 — <see cref="BarelyThere"/> — which no eye can see and every hit test can.
/// </para>
/// </summary>
public abstract class FloatingWindow : Window
{
    /// <summary>
    /// One part in 255 of black: invisible, and enough to keep the window hit-testable. Used by
    /// every surface that has to be clickable but must not obscure the game.
    /// </summary>
    public static readonly IBrush BarelyThere = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

    protected FloatingWindow()
    {
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
    }

    /// <summary>Which of the four Win32 bits this particular window wants.</summary>
    protected abstract OverlayStyleOptions StyleOptions { get; }

    /// <summary>
    /// Whether this build could hide the window from screen capture, and what to do if not. Null
    /// when everything is fine.
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
    /// Re-reads <see cref="StyleOptions"/> and pushes it at the window. Call after changing
    /// anything the options depend on; it is safe before the window has a handle, and does nothing
    /// there rather than throwing.
    /// </summary>
    public void ApplyPlatformStyles()
    {
        if (TryGetPlatformHandle() is { } handle)
            CaptureExclusionWarning = PlatformServices.ApplyOverlayWindowStyles(handle.Handle, StyleOptions);
    }

    /// <summary>Puts this window back above everything, without moving, resizing or focusing it.</summary>
    public void ReassertTopmost()
    {
        if (TryGetPlatformHandle() is { } handle) PlatformServices.ReassertTopmost(handle.Handle);
    }

    /// <summary>
    /// Nudges a window back onto a monitor. A remembered position is only as good as the display
    /// layout it was saved under: unplug the second screen and a toolbar last seen at x=2400 is
    /// somewhere nobody can reach, which reads as the feature having disappeared.
    /// </summary>
    protected PixelPoint ClampToAScreen(PixelPoint desired, PixelSize size)
    {
        var screen = Screens.ScreenFromPoint(desired)
                     ?? Screens.ScreenFromPoint(new PixelPoint(desired.X + size.Width / 2, desired.Y))
                     ?? Screens.Primary;

        if (screen is null) return desired;

        var area = screen.WorkingArea;
        var x = Math.Clamp(desired.X, area.X, Math.Max(area.X, area.X + area.Width - size.Width));
        var y = Math.Clamp(desired.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - size.Height));

        return new PixelPoint(x, y);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyPlatformStyles();
    }
}
