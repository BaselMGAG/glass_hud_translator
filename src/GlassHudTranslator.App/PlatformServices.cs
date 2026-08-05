using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Ocr;
using GlassHudTranslator.Core.Platform;
using GlassHudTranslator.Core.Storage;

namespace GlassHudTranslator.App;

public sealed record GameWindowInfo(
    CaptureRegion ClientArea, string Title, double Scaling, bool CanCapture, string Message);

/// <summary>
/// The one file in this project allowed to contain <c>#if WINDOWS</c>.
///
/// <para>
/// If a second one appears, the seam has leaked and the macOS build has stopped being a faithful
/// rehearsal of the Windows build. Everything platform-specific is reached through here, which is
/// what allowed the entire Win32 layer to be written and type-checked on a Mac.
/// </para>
/// </summary>
public static class PlatformServices
{
    public static bool IsWindows =>
#if WINDOWS
        true;
#else
        false;
#endif

    public static string Description => IsWindows
        ? "Windows: BitBlt capture, RegisterHotKey, DPAPI-encrypted keys"
        : "macOS/Linux dev: recorded frames, no global hotkeys, PLAINTEXT keys";

    /// <summary>Per-monitor v2. Also declared in app.manifest; this is the belt to that braces.</summary>
    public static void InitialiseDpiAwareness()
    {
#if WINDOWS
        try
        {
            Interop.NativeMethods.SetProcessDpiAwarenessContext(
                Interop.NativeMethods.DpiAwarenessContextPerMonitorAwareV2);
        }
        catch (EntryPointNotFoundException)
        {
            // Windows older than 1703. The manifest still covers it.
        }
#endif
    }

    public static IFrameSource CreateFrameSource(string testFramesDirectory)
    {
#if WINDOWS
        _ = testFramesDirectory;
        return new Windows.Win32FrameSource();
#else
        return new FolderFrameSource(testFramesDirectory, wrap: true);
#endif
    }

    /// <summary>
    /// A still of the whole screen, so the region picker can be drawn on a frozen image rather than
    /// over a live game. Picking on a still is far easier: the dialogue stays put while you drag.
    /// </summary>
    public static Frame? CaptureFullScreen()
    {
#if WINDOWS
        var width = Interop.NativeMethods.GetSystemMetrics(Interop.NativeMethods.SmCxScreen);
        var height = Interop.NativeMethods.GetSystemMetrics(Interop.NativeMethods.SmCyScreen);
        if (width <= 0 || height <= 0) return null;

        using var source = new Windows.Win32FrameSource();
        return source.GetFrameAsync(new CaptureRegion(0, 0, width, height), CancellationToken.None)
            .GetAwaiter().GetResult();
#else
        return null;
#endif
    }

    public static IOcrEngine CreateOcrEngine(string language = "eng")
    {
        var options = new TesseractOptions { Language = language };
#if WINDOWS
        // Bundled natives so the user installs nothing, with an automatic fallback to a
        // tesseract.exe shipped alongside if those fail to load.
        return new Windows.TesseractNativeEngine(options);
#else
        return new TesseractCliEngine(options);
#endif
    }

    public static ISecretStore CreateSecretStore()
    {
#if WINDOWS
        return new Windows.DpapiSecretStore();
#else
        // ProtectedData throws off Windows, so without this branch the settings screen could not be
        // run or debugged on the development machine at all. It warns loudly on construction.
        return new DevPlainFileSecretStore();
#endif
    }

    public static IHotkeyService CreateHotkeyService()
    {
#if WINDOWS
        return new Windows.GlobalHotkeyService();
#else
        return new NullHotkeyService();
#endif
    }

    /// <summary>
    /// Click-through, never-focused, always-on-top, and excluded from its own captures. Returns a
    /// warning when that last part is unavailable, which is the case on Windows builds before 2004.
    /// </summary>
    public static string? ApplyOverlayWindowStyles(nint windowHandle)
    {
#if WINDOWS
        return Windows.OverlayWindowStyles.Apply(windowHandle).Warning;
#else
        _ = windowHandle;
        return null;
#endif
    }

    public static void ReassertTopmost(nint windowHandle)
    {
#if WINDOWS
        Windows.OverlayWindowStyles.Reassert(windowHandle);
#else
        _ = windowHandle;
#endif
    }

    /// <summary>
    /// Locates the window a region should be measured against, and reports whether it can be
    /// captured. Off Windows there is no window to find, so this returns null and the caller falls
    /// back to the primary screen.
    ///
    /// <para>
    /// A profile with no window titles means "anything on screen" - a browser, a PDF, a video
    /// player. In that case the region is measured against the whole screen rather than against
    /// whatever happens to be in front, which matters because the foreground window is often this
    /// app's own Settings window at the moment a hotkey is pressed.
    /// </para>
    /// </summary>
    public static GameWindowInfo? FindGameWindow(IReadOnlyList<string> titleFragments)
    {
#if WINDOWS
        if (titleFragments.Count == 0) return WholeScreen();

        var window = Windows.GameWindowLocator.Find(titleFragments)
                     ?? Windows.GameWindowLocator.Foreground();
        if (window is null) return null;

        var verdict = Windows.DisplayModeGuard.Check(window);
        return new GameWindowInfo(window.ClientArea, window.Title, window.Scaling,
            verdict.CanCapture, verdict.Message);
#else
        _ = titleFragments;
        return null;
#endif
    }

#if WINDOWS
    private static GameWindowInfo? WholeScreen()
    {
        var width = Interop.NativeMethods.GetSystemMetrics(Interop.NativeMethods.SmCxScreen);
        var height = Interop.NativeMethods.GetSystemMetrics(Interop.NativeMethods.SmCyScreen);
        if (width <= 0 || height <= 0) return null;

        return new GameWindowInfo(new CaptureRegion(0, 0, width, height), "Whole screen", 1.0,
            true, $"Whole screen — {width}x{height}");
    }
#endif
}
