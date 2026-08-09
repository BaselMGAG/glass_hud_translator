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
    /// <summary>
    /// <paramref name="hideFromCapture"/> false lets recorders and streaming software see the
    /// overlay. It is not the default and it is not free: the exclusion exists so our own BitBlt
    /// cannot read the Arabic back and translate its own output. But a player reported that the
    /// overlay is invisible to the Nvidia app — «البرنامج بيمنع تصوير اي برنامج زي Nvidia app» —
    /// and someone who wants to record or stream what they are playing, with the translation in
    /// it, is asking for something reasonable that the app simply refused. The self-capture risk
    /// only bites where the overlay OVERLAPS the captured rectangle, which the position sliders
    /// already exist to prevent, so this is the user's call to make with the warning in front of
    /// them rather than ours to make for them.
    /// </summary>
    public static OverlayStyleResult Apply(IntPtr handle, bool hideFromCapture = true)
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

        if (!hideFromCapture)
        {
            // WDA_NONE, set explicitly rather than skipped: the affinity survives on the handle, so
            // a window that was excluded earlier in the session stays excluded unless it is
            // actively cleared.
            NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WdaNone);
            return new OverlayStyleResult(true, false, null);
        }

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
