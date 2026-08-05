using GamingTranslatorGlassHUD.Core.Capture;
using GamingTranslatorGlassHUD.Core.Ocr;
using GamingTranslatorGlassHUD.Core.Platform;
using GamingTranslatorGlassHUD.Core.Storage;

namespace GamingTranslatorGlassHUD.App;

/// <summary>
/// The one file in this project allowed to contain <c>#if WINDOWS</c>.
///
/// <para>
/// If a second one appears, the seam has leaked and the Mac build has stopped being a faithful
/// rehearsal of the Windows build. Everything platform-specific is reached through here, so
/// Session 2 fills in the Windows branches without touching any view.
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

    /// <summary>Shown in Settings so it is obvious which implementations are live.</summary>
    public static string Description => IsWindows
        ? "Windows: BitBlt capture, RegisterHotKey, DPAPI secrets"
        : "macOS dev: recorded frames, no hotkeys, PLAINTEXT secrets";

    public static IFrameSource CreateFrameSource(string testFramesDirectory)
    {
#if WINDOWS
        // Session 2: return new GamingTranslatorGlassHUD.Windows.Win32FrameSource();
        return new FolderFrameSource(testFramesDirectory, wrap: true);
#else
        return new FolderFrameSource(testFramesDirectory, wrap: true);
#endif
    }

    public static IOcrEngine CreateOcrEngine(string language = "eng")
    {
        var options = new TesseractOptions { Language = language };
#if WINDOWS
        // Session 2: return new GamingTranslatorGlassHUD.Windows.TesseractNativeEngine(options);
        return new TesseractCliEngine(options);
#else
        return new TesseractCliEngine(options);
#endif
    }

    public static ISecretStore CreateSecretStore()
    {
#if WINDOWS
        // Session 2: return new GamingTranslatorGlassHUD.Windows.DpapiSecretStore();
        return new DevPlainFileSecretStore();
#else
        // ProtectedData throws off Windows, so the settings screen would be undebuggable without
        // this branch (PROJECT_PLAN.md 1.3). It warns loudly on construction.
        return new DevPlainFileSecretStore();
#endif
    }

    public static IHotkeyService CreateHotkeyService()
    {
#if WINDOWS
        // Session 2: return new GamingTranslatorGlassHUD.Windows.GlobalHotkeyService();
        return new NullHotkeyService();
#else
        return new NullHotkeyService();
#endif
    }

    /// <summary>
    /// Click-through, no-activate, always-on-top, and excluded from its own captures. No-op off
    /// Windows - the overlay is still usable for layout work, it simply does not float over a game.
    /// </summary>
    public static void ApplyOverlayWindowStyles(nint windowHandle)
    {
#if WINDOWS
        // Session 2: GamingTranslatorGlassHUD.Windows.OverlayWindowStyles.Apply(windowHandle);
#endif
        _ = windowHandle;
    }
}
