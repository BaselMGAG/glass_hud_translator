using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Ocr;
using GlassHudTranslator.Core.Platform;
using GlassHudTranslator.Core.Storage;

namespace GlassHudTranslator.App;

public sealed record GameWindowInfo(
    CaptureRegion ClientArea, string Title, double Scaling, bool CanCapture, string Message);

/// <summary>A window the user could point a game profile at. Platform-neutral by design.</summary>
public sealed record OpenWindowInfo(string Title, string ProcessName);

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

    /// <summary>
    /// The error dialog of last resort: a native message box, shown when the UI toolkit itself is
    /// the thing that failed to start. Plain user32, no Avalonia, no fonts of ours — if this cannot
    /// appear, nothing can, and the startup log is the remaining evidence.
    /// </summary>
    public static void ShowFatalError(string title, string message)
    {
#if WINDOWS
        try
        {
            Interop.NativeMethods.MessageBox(IntPtr.Zero, message, title,
                Interop.NativeMethods.MbOk | Interop.NativeMethods.MbIconError
                | Interop.NativeMethods.MbTopmost);
        }
        catch (Exception)
        {
            // Even the message box can fail if the Interop assembly is what was quarantined.
            // The caller has already written the log; there is nothing further to do.
        }
#else
        Console.Error.WriteLine($"{title}: {message}");
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
        var desktop = VirtualDesktop();
        if (desktop.IsEmpty) return null;

        using var source = new Windows.Win32FrameSource();
        return source.GetFrameAsync(desktop, CancellationToken.None).GetAwaiter().GetResult();
#else
        return null;
#endif
    }

    /// <summary>
    /// The bounding box of every monitor, in desktop coordinates — so its origin is negative when a
    /// monitor sits left of or above the primary one.
    ///
    /// <para>
    /// Everything used to ask for <c>SM_CXSCREEN</c>, which is the primary monitor and nothing else.
    /// A game on a second display was therefore outside every frame the app grabbed, and a rectangle
    /// on a left-hand monitor was rejected outright by the buffer-bounds check three layers down in
    /// Core. Off Windows this is empty and callers fall back as they did before.
    /// </para>
    /// </summary>
    public static CaptureRegion VirtualDesktop()
    {
#if WINDOWS
        var x = Interop.NativeMethods.GetSystemMetrics(Interop.NativeMethods.SmXVirtualScreen);
        var y = Interop.NativeMethods.GetSystemMetrics(Interop.NativeMethods.SmYVirtualScreen);
        var width = Interop.NativeMethods.GetSystemMetrics(Interop.NativeMethods.SmCxVirtualScreen);
        var height = Interop.NativeMethods.GetSystemMetrics(Interop.NativeMethods.SmCyVirtualScreen);

        // The virtual metrics are zero on some remote-session and single-adapter configurations.
        // Falling back to the primary monitor is exactly the old behaviour, which is correct there.
        if (width <= 0 || height <= 0)
        {
            width = Interop.NativeMethods.GetSystemMetrics(Interop.NativeMethods.SmCxScreen);
            height = Interop.NativeMethods.GetSystemMetrics(Interop.NativeMethods.SmCyScreen);
            return width > 0 && height > 0 ? new CaptureRegion(0, 0, width, height) : CaptureRegion.Empty;
        }

        return new CaptureRegion(x, y, width, height);
#else
        return CaptureRegion.Empty;
#endif
    }

    public static IOcrEngine CreateOcrEngine(string language = "eng") =>
        CreateOcrEngine(new TesseractOptions { Language = language });

    /// <summary>
    /// The same engines with the caller's own options. Exists for the region-proposal pass, which
    /// reads a whole frame once and wants very different settings from the per-line loop: no 2x
    /// upscale (a full desktop doubled is tens of millions of pixels for a layout question), and
    /// automatic page segmentation, because a full screen is not "a uniform block of text".
    /// </summary>
    public static IOcrEngine CreateOcrEngine(TesseractOptions options)
    {
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
    /// Always-on-top and out of Alt-Tab, plus whichever of click-through, never-focused and
    /// excluded-from-capture <paramref name="options"/> asks for. Returns a warning when the
    /// exclusion is unavailable, which is the case on Windows builds before 2004.
    /// </summary>
    public static string? ApplyOverlayWindowStyles(nint windowHandle, OverlayStyleOptions? options = null)
    {
#if WINDOWS
        return Windows.OverlayWindowStyles.Apply(windowHandle, options).Warning;
#else
        _ = windowHandle;
        _ = options;
        return null;
#endif
    }

    /// <summary>
    /// Puts a floating window back above everything. Called when the game window turns up somewhere
    /// new, because another topmost window that appeared meanwhile can otherwise sit over ours for
    /// the rest of the session — and a translation nobody can see is indistinguishable from none.
    /// </summary>
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
    public static GameWindowInfo? FindGameWindow(
        IReadOnlyList<string> titleFragments, IReadOnlyList<string>? processNames = null)
    {
#if WINDOWS
        if (titleFragments.Count == 0 && (processNames is null || processNames.Count == 0))
            return WholeScreen();

        var window = Windows.GameWindowLocator.Find(titleFragments, processNames)
                     ?? Windows.GameWindowLocator.Foreground();
        if (window is null) return null;

        var verdict = Windows.DisplayModeGuard.Check(window);
        return new GameWindowInfo(window.ClientArea, window.Title, window.Scaling,
            verdict.CanCapture, verdict.Message);
#else
        _ = titleFragments;
        _ = processNames;
        return null;
#endif
    }

    /// <summary>
    /// Visible windows the user could point a profile at.
    ///
    /// <para>
    /// Empty off Windows, which is not a gap so much as the seam working: the profile editor is
    /// built and tested on the Mac like everything else, and falls back to a text box for the
    /// window title there. Only the convenience of picking from a list needs a Win32 call.
    /// </para>
    /// </summary>
    public static IReadOnlyList<OpenWindowInfo> ListOpenWindows()
    {
#if WINDOWS
        return Windows.GameWindowLocator.ListOpen()
            .Where(w => !w.ProcessName.Equals("GlassHudTranslator", StringComparison.OrdinalIgnoreCase))
            .Select(w => new OpenWindowInfo(w.Title, w.ProcessName))
            .ToList();
#else
        return [];
#endif
    }

#if WINDOWS
    /// <summary>
    /// The client area the screen-relative profile measures against: ONE monitor — the one the user
    /// is looking at — not the union of all of them.
    ///
    /// <para>
    /// This deliberately does not return the virtual desktop, and the distinction is not academic.
    /// Regions are stored as fractions of whatever is returned here, so widening it to the bounding
    /// box would silently relocate every region a user has already saved: a rectangle stored as
    /// "22% from the left, 56% wide" against a 1920-wide screen becomes a 2150px band straddling
    /// the seam between two monitors, half of it reading the wrong display. Following the
    /// foreground window keeps the frame one monitor wide, so existing fractions stay valid and a
    /// browser on the second display works — which was the point.
    /// </para>
    /// </summary>
    private static GameWindowInfo? WholeScreen()
    {
        var monitor = MonitorUnderForegroundWindow() ?? VirtualDesktop();
        if (monitor.IsEmpty) return null;

        return new GameWindowInfo(monitor, "Whole screen", 1.0, true,
            $"Whole screen — {monitor.Width}x{monitor.Height} at {monitor.X},{monitor.Y}");
    }

    private static CaptureRegion? MonitorUnderForegroundWindow()
    {
        // Not GetForegroundWindow: our own Settings, wizard, picker and toolbar are foreground
        // windows too, and on a two-screen setup one of them being in front moved the whole capture
        // region to the monitor OUR window was on. The general profile - the one you would use to
        // watch a video - stores its region against the whole screen, so that is the entire frame
        // landing on the wrong display.
        var window = Windows.GameWindowLocator.ForegroundNotOurs();
        if (window == IntPtr.Zero) return null;

        var handle = Interop.NativeMethods.MonitorFromWindow(
            window, Interop.NativeMethods.MonitorDefaultToNearest);
        if (handle == IntPtr.Zero) return null;

        var info = new Interop.NativeMethods.MonitorInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Interop.NativeMethods.MonitorInfo>(),
        };

        if (!Interop.NativeMethods.GetMonitorInfo(handle, ref info)) return null;

        var bounds = info.Monitor;
        return bounds.Width <= 0 || bounds.Height <= 0
            ? null
            : new CaptureRegion(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
    }
#endif
}
