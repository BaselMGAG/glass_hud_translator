using System.Runtime.Versioning;
using GlassHudTranslator.Interop;

namespace GlassHudTranslator.Windows;

/// <summary>
/// Turns an ordinary window into an overlay: click-through, never focused, always on top, and
/// invisible to screen capture.
///
/// <para>
/// The last one is not cosmetic. Without WDA_EXCLUDEFROMCAPTURE our own BitBlt would include the
/// Arabic we just drew, the OCR would read it back, and the pipeline would translate its own
/// output on the next poll.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class OverlayWindowStyles
{
    public static OverlayStyleResult Apply(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle))
            return new OverlayStyleResult(false, false, "Not a valid window handle.");

        var current = (uint)NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle);

        var wanted = current
                     | NativeMethods.WsExLayered      // required before WS_EX_TRANSPARENT means anything
                     | NativeMethods.WsExTransparent  // clicks fall through to the game
                     | NativeMethods.WsExNoActivate   // clicking never pulls focus off the game
                     | NativeMethods.WsExToolWindow;  // keeps it out of Alt-Tab

        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, (IntPtr)wanted);

        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);

        // Fails on Windows builds before 2004 (20H1). Degrading to a visible-but-self-capturing
        // overlay is better than refusing to run, so this is reported rather than thrown.
        var excluded = NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WdaExcludeFromCapture);

        return new OverlayStyleResult(true, excluded, excluded
            ? null
            : "This Windows build does not support excluding a window from capture. Keep the "
              + "overlay outside the captured region, or it will read its own output back.");
    }

    /// <summary>Puts the window back on top after the game has been Alt-Tabbed away and back.</summary>
    public static void Reassert(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle)) return;

        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }
}

public sealed record OverlayStyleResult(bool StylesApplied, bool ExcludedFromCapture, string? Warning);
