using System.Runtime.InteropServices;

namespace GamingTranslatorGlassHUD.Interop;

/// <summary>
/// P/Invoke surface. Declarations only - no logic, no state, no caching. Session 2 fills this in;
/// see CODING_SESSIONS.md. It compiles on macOS because these are attributes, not calls.
/// </summary>
public static partial class NativeMethods
{
    public const string User32 = "user32.dll";
    public const string Gdi32 = "gdi32.dll";

    // Window discovery / geometry -------------------------------------------------------------

    [LibraryImport(User32, EntryPoint = "GetForegroundWindow")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport(User32, EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [LibraryImport(User32, EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    // Overlay window styles --------------------------------------------------------------------

    public const int GwlExStyle = -20;
    public const uint WsExTransparent = 0x00000020;   // click-through
    public const uint WsExToolWindow = 0x00000080;    // no alt-tab entry
    public const uint WsExNoActivate = 0x08000000;    // never steal focus from the game
    public const uint WsExLayered = 0x00080000;

    /// <summary>Overlay must not appear in its own captures, or the pipeline OCRs itself.</summary>
    public const uint WdaExcludeFromCapture = 0x00000011;

    [LibraryImport(User32, EntryPoint = "SetWindowDisplayAffinity")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    // Hotkeys ----------------------------------------------------------------------------------
    // RegisterHotKey, not WH_KEYBOARD_LL: low-level hooks are the pattern AV heuristics flag,
    // and RegisterHotKey already fires while FFXIV has focus (brief 2.6).

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModNoRepeat = 0x4000;

    [LibraryImport(User32, EntryPoint = "RegisterHotKey")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport(User32, EntryPoint = "UnregisterHotKey")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    // Capture ----------------------------------------------------------------------------------
    // GDI BitBlt, not Windows.Graphics.Capture: one small rect at <=3 fps is 1-3 ms, works
    // unpackaged, and draws no capture border (brief 2.4).

    public const uint SrcCopy = 0x00CC0020;

    [LibraryImport(User32, EntryPoint = "GetDC")]
    public static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport(User32, EntryPoint = "ReleaseDC")]
    public static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [LibraryImport(Gdi32, EntryPoint = "BitBlt")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(
        IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rop);
}
