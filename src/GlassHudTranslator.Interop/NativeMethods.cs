using System.Runtime.InteropServices;

namespace GlassHudTranslator.Interop;

/// <summary>
/// P/Invoke surface. Declarations only - no logic, no state, no caching. Compiles on macOS because
/// these are attributes rather than calls, which is what lets the Windows layer be written and
/// type-checked without a Windows machine.
/// </summary>
public static partial class NativeMethods
{
    public const string User32 = "user32.dll";
    public const string Gdi32 = "gdi32.dll";
    public const string Kernel32 = "kernel32.dll";

    // ── Geometry ──────────────────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X, Y;
    }

    // ── Window discovery ──────────────────────────────────────────────────────────────────────

    [LibraryImport(User32, EntryPoint = "GetForegroundWindow")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport(User32, EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport(User32, EntryPoint = "IsIconic")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport(User32, EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hWnd);

    /// <summary>
    /// Takes a raw pointer to a caller-pinned UTF-16 buffer rather than a char[]. LibraryImport
    /// will not marshal char or char[] unless the entire assembly opts out of runtime marshalling,
    /// and doing that would also outlaw the MarshalAs(Bool) returns used throughout this file. A
    /// pointer sidesteps the whole question - see GameWindowLocator.TitleOf for the call side.
    /// </summary>
    [LibraryImport(User32, EntryPoint = "GetWindowTextW")]
    public static partial int GetWindowText(IntPtr hWnd, IntPtr text, int maxCount);

    [LibraryImport(User32, EntryPoint = "GetWindowTextLengthW")]
    public static partial int GetWindowTextLength(IntPtr hWnd);

    /// <summary>
    /// Owning process id for a window, so a profile can be bound to an executable name rather than
    /// to a title. Titles change while a program runs - a browser's is whatever page is open, and
    /// games append the zone or character name - but ffxiv_dx11.exe is ffxiv_dx11.exe forever.
    /// </summary>
    [LibraryImport(User32, EntryPoint = "GetWindowThreadProcessId")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport(User32, EntryPoint = "EnumWindows", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [LibraryImport(User32, EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [LibraryImport(User32, EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [LibraryImport(User32, EntryPoint = "ClientToScreen")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(IntPtr hWnd, ref Point point);

    // ── Screen capture ────────────────────────────────────────────────────────────────────────
    // GDI BitBlt rather than Windows.Graphics.Capture: one small rectangle at <= 3 fps costs 1-3 ms,
    // works unpackaged, and draws no capture border.

    public const uint SrcCopy = 0x00CC0020;
    public const uint CaptureBlt = 0x40000000;
    public const int BiRgb = 0;
    public const uint DibRgbColors = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;

        /// <summary>Negative gives a top-down DIB, matching how Frame stores its rows.</summary>
        public int Height;

        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [LibraryImport(User32, EntryPoint = "GetDC")]
    public static partial IntPtr GetDC(IntPtr hWnd);

    [LibraryImport(User32, EntryPoint = "ReleaseDC")]
    public static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [LibraryImport(Gdi32, EntryPoint = "CreateCompatibleDC")]
    public static partial IntPtr CreateCompatibleDC(IntPtr hDC);

    [LibraryImport(Gdi32, EntryPoint = "CreateCompatibleBitmap")]
    public static partial IntPtr CreateCompatibleBitmap(IntPtr hDC, int width, int height);

    [LibraryImport(Gdi32, EntryPoint = "SelectObject")]
    public static partial IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [LibraryImport(Gdi32, EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport(Gdi32, EntryPoint = "DeleteDC")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(IntPtr hDC);

    [LibraryImport(Gdi32, EntryPoint = "BitBlt", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(
        IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    [LibraryImport(Gdi32, EntryPoint = "GetDIBits")]
    public static partial int GetDIBits(
        IntPtr hDC, IntPtr hBitmap, uint startScan, uint scanLines,
        [Out] byte[] bits, ref BitmapInfo info, uint usage);

    // ── Hotkeys ───────────────────────────────────────────────────────────────────────────────
    // RegisterHotKey with a null window associates the hotkey with the calling THREAD, so WM_HOTKEY
    // arrives in that thread's message queue and no window class has to be registered at all.

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>Without this, holding a hotkey down repeats it at the key repeat rate.</summary>
    public const uint ModNoRepeat = 0x4000;

    public const uint WmHotkey = 0x0312;
    public const uint WmQuit = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    [LibraryImport(User32, EntryPoint = "RegisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport(User32, EntryPoint = "UnregisterHotKey")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport(User32, EntryPoint = "GetMessageW")]
    public static partial int GetMessage(out Msg msg, IntPtr hWnd, uint filterMin, uint filterMax);

    [LibraryImport(User32, EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport(Kernel32, EntryPoint = "GetCurrentThreadId")]
    public static partial uint GetCurrentThreadId();

    // ── Overlay window styles ─────────────────────────────────────────────────────────────────

    public const int GwlExStyle = -20;
    public const uint WsExTransparent = 0x00000020;   // clicks pass through to the game
    public const uint WsExToolWindow = 0x00000080;    // keeps it out of Alt-Tab
    public const uint WsExNoActivate = 0x08000000;    // never steals focus from the game
    public const uint WsExLayered = 0x00080000;

    public static readonly IntPtr HwndTopmost = new(-1);
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;

    /// <summary>
    /// Excludes the overlay from screen captures - including our own. Without it the pipeline reads
    /// its own Arabic output back on the next poll and translates that.
    /// </summary>
    public const uint WdaExcludeFromCapture = 0x00000011;
    public const uint WdaNone = 0x00000000;

    [LibraryImport(User32, EntryPoint = "GetWindowLongPtrW")]
    public static partial IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [LibraryImport(User32, EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

    [LibraryImport(User32, EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport(User32, EntryPoint = "SetWindowDisplayAffinity")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    // ── DPI ───────────────────────────────────────────────────────────────────────────────────
    // Region rectangles have to stay correct at 125% and 150% scaling.

    public static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    [LibraryImport(User32, EntryPoint = "SetProcessDpiAwarenessContext", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessDpiAwarenessContext(IntPtr context);

    [LibraryImport(User32, EntryPoint = "GetDpiForWindow")]
    public static partial uint GetDpiForWindow(IntPtr hWnd);

    [LibraryImport(User32, EntryPoint = "GetSystemMetrics")]
    public static partial int GetSystemMetrics(int index);

    public const int SmCxScreen = 0;
    public const int SmCyScreen = 1;

    /// <summary>
    /// The virtual desktop: the bounding box of every monitor, not just the primary one.
    ///
    /// <para>
    /// Its origin is frequently negative — a monitor placed to the left of or above the primary
    /// starts at a negative coordinate, because the primary monitor's top-left is always (0,0).
    /// Anything that asks for "the screen" with <see cref="SmCxScreen"/> silently means "the
    /// primary monitor", which is why a game on a second display could not be captured at all.
    /// </para>
    /// </summary>
    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;
    public const int SmCxVirtualScreen = 78;
    public const int SmCyVirtualScreen = 79;

    public const uint MonitorDefaultToNearest = 0x00000002;

    /// <summary>
    /// The monitor a window is on. Needed wherever "the screen" means the one display in question
    /// rather than the union of all of them — a game's fullscreen check, and the client area the
    /// screen-relative profile measures against. Using the union for either turns a rectangle into
    /// a band spanning the seam between two monitors.
    /// </summary>
    [LibraryImport(User32, EntryPoint = "MonitorFromWindow")]
    public static partial IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [LibraryImport(User32, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);
}
