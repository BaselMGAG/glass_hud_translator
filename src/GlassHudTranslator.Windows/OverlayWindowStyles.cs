using System.Runtime.Versioning;
using GlassHudTranslator.Core.Platform;
using GlassHudTranslator.Interop;

namespace GlassHudTranslator.Windows;

/// <summary>
/// Turns an ordinary window into one of the app's floating surfaces: always on top, out of Alt-Tab,
/// and — depending on what the caller asked for — click-through, non-activating, and invisible to
/// screen capture.
///
/// <para>
/// That last one is not cosmetic. Without WDA_EXCLUDEFROMCAPTURE our own BitBlt would include the
/// Arabic we just drew, the OCR would read it back, and the pipeline would translate its own
/// output on the next poll. It applies to the toolbar and the capture frame for the same reason:
/// an icon strip or a bright border sitting inside the captured rectangle is just more shapes for
/// Tesseract to guess at.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class OverlayWindowStyles
{
    /// <summary>
    /// Applies exactly the bits <paramref name="options"/> asks for, and clears the ones it does
    /// not.
    ///
    /// <para>
    /// Clearing matters. This used to OR four constants into the existing style and return, which
    /// is idempotent only as long as nobody ever wants a bit back off. The capture frame wants
    /// <c>WS_EX_TRANSPARENT</c> off while the user drags it and on again the moment they finish, so
    /// an OR-only version would let it eat every click for the rest of the session after one
    /// adjustment.
    /// </para>
    ///
    /// <para>
    /// <c>hideFromCapture</c> false lets recorders and streaming software see the window. It is not
    /// the default and it is not free — the exclusion is what stops our own capture reading the
    /// Arabic back — but a player reported the overlay being invisible to the Nvidia app,
    /// «البرنامج بيمنع تصوير اي برنامج زي Nvidia app», and someone who wants to record what they
    /// are playing with the translation in it is asking for something reasonable that the app
    /// simply refused. The self-capture risk only bites where the window OVERLAPS the captured
    /// rectangle, which the position sliders already exist to prevent, so it is the user's call to
    /// make with the warning in front of them rather than ours to make for them.
    /// </para>
    /// </summary>
    public static OverlayStyleResult Apply(IntPtr handle, OverlayStyleOptions? options = null)
    {
        var wants = options ?? OverlayStyleOptions.Panel;

        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle))
            return new OverlayStyleResult(false, false, "Not a valid window handle.");

        var current = (uint)NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle);

        // WS_EX_LAYERED is a prerequisite rather than a choice: WS_EX_TRANSPARENT means nothing
        // without it, and every one of these windows is drawn with per-pixel alpha regardless.
        var wanted = current | NativeMethods.WsExLayered | NativeMethods.WsExToolWindow;

        wanted = Set(wanted, NativeMethods.WsExTransparent, wants.ClickThrough);
        wanted = Set(wanted, NativeMethods.WsExNoActivate, wants.NoActivate);

        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, (IntPtr)wanted);

        // SWP_FRAMECHANGED so a style that changed after the window was created is recalculated
        // rather than taking effect at some later, arbitrary repaint. Without it a frame switched
        // out of click-through can stay unclickable until something else forces the issue.
        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate
            | NativeMethods.SwpFrameChanged);

        if (!wants.HideFromCapture)
        {
            // WDA_NONE, set explicitly rather than skipped: the affinity lives on the handle, so a
            // window excluded earlier in the session stays excluded unless it is actively cleared.
            NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WdaNone);
            return new OverlayStyleResult(true, false, null);
        }

        // Fails on Windows builds before 2004 (20H1). Degrading to a visible-but-self-capturing
        // window is better than refusing to run, so this is reported rather than thrown.
        var excluded = NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WdaExcludeFromCapture);

        return new OverlayStyleResult(true, excluded, excluded
            ? null
            : "This Windows build does not support excluding a window from capture. Keep the "
              + "overlay outside the captured region, or it will read its own output back.");
    }

    /// <summary>
    /// Puts the window back on top. Called when the game window is found somewhere new — Alt-Tabbed
    /// away and back, moved to another monitor, or resized — because another topmost window that
    /// appeared in between can otherwise sit above ours permanently, and a translation nobody can
    /// see is indistinguishable from no translation.
    /// </summary>
    public static void Reassert(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle)) return;

        NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    private static uint Set(uint style, uint bit, bool on) => on ? style | bit : style & ~bit;
}
