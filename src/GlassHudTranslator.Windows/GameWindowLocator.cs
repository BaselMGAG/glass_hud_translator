using System.Runtime.Versioning;
using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Interop;

namespace GlassHudTranslator.Windows;

public sealed record GameWindow(IntPtr Handle, string Title, CaptureRegion ClientArea, uint Dpi)
{
    public double Scaling => Dpi / 96.0;

    public bool IsStillValid => NativeMethods.IsWindow(Handle) && !NativeMethods.IsIconic(Handle);
}

/// <summary>
/// Finds the game window and reports its client area in screen coordinates.
///
/// <para>
/// Region profiles are stored as fractions of this client area rather than as desktop pixels, so a
/// profile keeps working when the window is moved, and only needs redoing if the resolution or the
/// in-game UI scale changes.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class GameWindowLocator
{
    /// <summary>Finds a visible, non-minimised window whose title contains any of the given fragments.</summary>
    public static GameWindow? Find(IReadOnlyList<string> titleFragments)
    {
        if (titleFragments.Count == 0) return null;

        IntPtr found = IntPtr.Zero;
        string foundTitle = "";

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle)) return true;

            var title = TitleOf(handle);
            if (title.Length == 0) return true;

            foreach (var fragment in titleFragments)
            {
                if (!title.Contains(fragment, StringComparison.OrdinalIgnoreCase)) continue;

                found = handle;
                foundTitle = title;
                return false;   // stop enumerating
            }

            return true;
        }, IntPtr.Zero);

        return found == IntPtr.Zero ? null : Describe(found, foundTitle);
    }

    public static GameWindow? Foreground()
    {
        var handle = NativeMethods.GetForegroundWindow();
        return handle == IntPtr.Zero ? null : Describe(handle, TitleOf(handle));
    }

    private static GameWindow? Describe(IntPtr handle, string title)
    {
        if (!NativeMethods.GetClientRect(handle, out var client)) return null;

        // GetClientRect is window-relative, so the origin has to be mapped to the desktop before
        // it means anything to BitBlt.
        var origin = new NativeMethods.Point { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(handle, ref origin)) return null;

        var dpi = NativeMethods.GetDpiForWindow(handle);
        if (dpi == 0) dpi = 96;

        return new GameWindow(handle, title,
            new CaptureRegion(origin.X, origin.Y, client.Width, client.Height), dpi);
    }

    private static string TitleOf(IntPtr handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0) return "";

        var buffer = new char[length + 1];
        int written;
        unsafe
        {
            fixed (char* pinned = buffer)
                written = NativeMethods.GetWindowText(handle, (IntPtr)pinned, buffer.Length);
        }

        return written <= 0 ? "" : new string(buffer, 0, written);
    }
}

public sealed record DisplayModeVerdict(bool CanCapture, string Message);

/// <summary>
/// Exclusive fullscreen breaks both screen capture and always-on-top overlays. Detecting it and
/// saying so beats silently producing black frames, which looks like a broken app rather than a
/// wrong setting.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DisplayModeGuard
{
    public static DisplayModeVerdict Check(GameWindow window)
    {
        if (!NativeMethods.GetWindowRect(window.Handle, out var windowRect))
            return new DisplayModeVerdict(false, "Could not read the game window's position.");

        var screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SmCxScreen);
        var screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SmCyScreen);

        // Borderless windowed also covers the screen exactly, so geometry alone cannot separate the
        // two modes. What it can do is catch the case worth catching: a window claiming the whole
        // screen with no client area, which is what a lost exclusive-fullscreen device looks like.
        var coversScreen = windowRect.Width >= screenWidth && windowRect.Height >= screenHeight;
        var hasClientArea = window.ClientArea is { Width: > 0, Height: > 0 };

        if (coversScreen && !hasClientArea)
        {
            return new DisplayModeVerdict(false,
                "The game appears to be in exclusive fullscreen. Screen capture and always-on-top " +
                "overlays do not work in that mode. Switch the game to Borderless Windowed and try again.");
        }

        return hasClientArea
            ? new DisplayModeVerdict(true, $"{window.Title} — {window.ClientArea.Width}x{window.ClientArea.Height} " +
                                           $"at {window.Scaling:P0} scaling")
            : new DisplayModeVerdict(false, "The game window has no client area to capture.");
    }
}
