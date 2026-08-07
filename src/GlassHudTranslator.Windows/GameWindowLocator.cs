using System.Runtime.Versioning;
using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Interop;

namespace GlassHudTranslator.Windows;

/// <summary>One entry in the "which window is your game?" list.</summary>
public sealed record OpenWindow(string Title, string ProcessName);

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
    /// <summary>
    /// Finds a visible, non-minimised window matching any of the given executable names or title
    /// fragments. Process names are checked first because they do not change while the program
    /// runs; either kind of match is accepted, so a profile written before process names existed
    /// still works on its title alone.
    /// </summary>
    public static GameWindow? Find(
        IReadOnlyList<string> titleFragments, IReadOnlyList<string>? processNames = null)
    {
        if (titleFragments.Count == 0 && (processNames is null || processNames.Count == 0)) return null;

        IntPtr found = IntPtr.Zero;
        string foundTitle = "";

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle)) return true;

            var title = TitleOf(handle);
            if (title.Length == 0) return true;

            var matches =
                (processNames is { Count: > 0 } && Matches(processNames, ProcessNameOf(handle)))
                || titleFragments.Any(f => title.Contains(f, StringComparison.OrdinalIgnoreCase));

            if (!matches) return true;

            found = handle;
            foundTitle = title;
            return false;   // stop enumerating
        }, IntPtr.Zero);

        return found == IntPtr.Zero ? null : Describe(found, foundTitle);

        static bool Matches(IReadOnlyList<string> wanted, string? actual) =>
            actual is { Length: > 0 } &&
            wanted.Any(w => string.Equals(Bare(w), actual, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every visible top-level window with a title, for the profile editor's "which window?" list.
    /// Picking from a list beats asking someone to type a window title, which is a concept the
    /// people this app is for have no reason to have met.
    /// </summary>
    public static IReadOnlyList<OpenWindow> ListOpen()
    {
        var windows = new List<OpenWindow>();

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle)) return true;

            var title = TitleOf(handle);
            if (title.Length == 0) return true;

            // Skip anything without a client area worth capturing. Tool windows, tooltips and
            // hidden shell windows all carry titles and would otherwise pad the list with entries
            // that cannot possibly be the game.
            if (!NativeMethods.GetClientRect(handle, out var client)) return true;
            if (client.Width < 200 || client.Height < 200) return true;

            windows.Add(new OpenWindow(title, ProcessNameOf(handle) ?? ""));
            return true;
        }, IntPtr.Zero);

        return windows
            .DistinctBy(w => (w.Title, w.ProcessName))
            .OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string? ProcessNameOf(IntPtr handle)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(handle, out var pid);
            if (pid == 0) return null;

            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException
                                      or System.ComponentModel.Win32Exception)
        {
            // The process can exit between enumerating the window and asking about it, and some
            // system-owned windows refuse the query outright. Neither is worth failing the list for.
            return null;
        }
    }

    /// <summary>Accepts "ffxiv_dx11.exe" or "ffxiv_dx11" - Process.ProcessName has no extension.</summary>
    private static string Bare(string processName) =>
        processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

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
