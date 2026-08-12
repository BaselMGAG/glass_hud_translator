using System.Runtime.Versioning;
using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Interop;

namespace GlassHudTranslator.Windows;

/// <summary>
/// Captures a rectangle of the screen with GDI BitBlt.
///
/// <para>
/// Chosen over Windows.Graphics.Capture because the workload is one small rectangle at 2-3 fps:
/// BitBlt costs 1-3 ms, works in an unpackaged app, and draws no capture border. WGC would draw a
/// yellow border by default and removing it goes through a permission prompt, which is a lot of
/// ceremony for no gain at this scale.
/// </para>
///
/// <para>
/// The DC and bitmap are cached across calls and rebuilt when the region size changes or the
/// display layout does, since creating them is most of the cost. The pixel buffer is not cached - each call allocates a fresh
/// one, because <see cref="Frame"/> keeps the reference and reusing it would let the next capture
/// mutate a frame someone is still reading.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32FrameSource : IFrameSource
{
    private readonly Lock _gate = new();

    private IntPtr _screenDc;
    private IntPtr _memoryDc;
    private IntPtr _bitmap;
    private int _bitmapWidth;
    private int _bitmapHeight;
    private int _layout;
    private bool _disposed;

    public string LastFrameLabel { get; private set; } = "<none>";

    /// <summary>Milliseconds the last BitBlt-plus-GetDIBits took. Watched against the 50 ms budget.</summary>
    public double LastCaptureMs { get; private set; }

    public Task<Frame?> GetFrameAsync(CaptureRegion region, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (region.IsEmpty)
            return Task.FromResult<Frame?>(null);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var frame = Capture(region);
            LastCaptureMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            LastFrameLabel = region.ToString();

            return Task.FromResult(frame);
        }
    }

    private Frame? Capture(CaptureRegion region)
    {
        if (!EnsureResources(region.Width, region.Height)) return null;

        var previous = NativeMethods.SelectObject(_memoryDc, _bitmap);
        try
        {
            // CAPTUREBLT is needed to include layered windows. Our own overlay is excluded
            // separately via WDA_EXCLUDEFROMCAPTURE, or it would read its own output back.
            var copied = NativeMethods.BitBlt(
                _memoryDc, 0, 0, region.Width, region.Height,
                _screenDc, region.X, region.Y,
                NativeMethods.SrcCopy | NativeMethods.CaptureBlt);

            if (!copied) return null;

            var buffer = new byte[region.Width * region.Height * Frame.BytesPerPixel];
            var info = new NativeMethods.BitmapInfo
            {
                Header = new NativeMethods.BitmapInfoHeader
                {
                    Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                    Width = region.Width,

                    // Negative height requests a top-down DIB, which is the row order Frame uses.
                    // With a positive height every captured frame would arrive vertically mirrored.
                    Height = -region.Height,

                    Planes = 1,
                    BitCount = 32,
                    Compression = NativeMethods.BiRgb,
                },
            };

            var scanLines = NativeMethods.GetDIBits(
                _memoryDc, _bitmap, 0, (uint)region.Height, buffer, ref info, NativeMethods.DibRgbColors);

            return scanLines == 0 ? null : new Frame(region.Width, region.Height, buffer);
        }
        finally
        {
            NativeMethods.SelectObject(_memoryDc, previous);
        }
    }

    private bool EnsureResources(int width, int height)
    {
        // The desktop DC is rebuilt when the display layout changes.
        //
        // It used to be acquired once and kept for the lifetime of the source. A DC obtained before
        // a monitor was plugged in, unplugged, or had its resolution changed still describes the
        // old desktop, so BitBlt reads from geometry that no longer exists - which returns black
        // rather than failing. Black frames are the worst possible failure here, because the change
        // detector sees a stable image and skips, so the app goes quiet and looks like it has
        // simply stopped working. Re-acquiring costs a handful of microseconds and only happens
        // when the layout actually differs from the one the current DC was taken under.
        var layout = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen) * 397
                     ^ NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen) * 31
                     ^ NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen) * 7
                     ^ NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);

        if (_screenDc != IntPtr.Zero && layout != _layout) ReleaseDeviceContexts();

        if (_screenDc == IntPtr.Zero)
        {
            // A null HWND gives the DC for the entire virtual screen, including monitors whose
            // origin is negative - which is every layout with a display left of or above primary.
            _screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (_screenDc == IntPtr.Zero) return false;

            lock (ScreenDcGate) _screenDcHolders++;

            _memoryDc = NativeMethods.CreateCompatibleDC(_screenDc);
            if (_memoryDc == IntPtr.Zero)
            {
                ReleaseDeviceContexts();
                return false;
            }

            _layout = layout;
        }

        if (_bitmap != IntPtr.Zero && _bitmapWidth == width && _bitmapHeight == height) return true;

        if (_bitmap != IntPtr.Zero) NativeMethods.DeleteObject(_bitmap);

        // Unchecked before now. A failure here left a null handle that SelectObject would then be
        // handed on every subsequent capture, so one transient GDI exhaustion became a permanent
        // black screen for the rest of the session.
        _bitmap = NativeMethods.CreateCompatibleBitmap(_screenDc, width, height);
        if (_bitmap == IntPtr.Zero)
        {
            _bitmapWidth = _bitmapHeight = 0;
            return false;
        }

        _bitmapWidth = width;
        _bitmapHeight = height;
        return true;
    }

    /// <summary>
    /// How many live instances hold the screen DC.
    ///
    /// <para>
    /// <b>This counter exists because of a measured, total outage.</b> <c>GetDC(NULL)</c> does not
    /// hand out a private handle — the screen DC comes from a system CACHE, so two instances get
    /// the same one. When a second, short-lived frame source was created for the diagnostic report
    /// and then disposed, its <c>ReleaseDC</c> invalidated the handle the LIVE session was still
    /// holding, and every capture from that moment returned nothing: auto-watch stopped, and so did
    /// the translate hotkey. Nothing logged an error, because BitBlt on a released DC simply fails.
    /// </para>
    ///
    /// <para>
    /// Releasing only when the last holder goes makes a second instance harmless rather than
    /// catastrophic. The app should still have exactly one — the counter is the guard, not the
    /// design.
    /// </para>
    /// </summary>
    private static int _screenDcHolders;

    private static readonly Lock ScreenDcGate = new();

    private void ReleaseDeviceContexts()
    {
        // The bitmap and the memory DC are genuinely ours: CreateCompatibleBitmap and
        // CreateCompatibleDC return private handles, so they are freed unconditionally.
        if (_bitmap != IntPtr.Zero) NativeMethods.DeleteObject(_bitmap);
        if (_memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(_memoryDc);

        // The screen DC is shared, so it is released only by the last holder.
        if (_screenDc != IntPtr.Zero)
        {
            lock (ScreenDcGate)
            {
                if (--_screenDcHolders <= 0)
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, _screenDc);
                    _screenDcHolders = 0;
                }
            }
        }

        _bitmap = _memoryDc = _screenDc = IntPtr.Zero;
        _bitmapWidth = _bitmapHeight = 0;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            ReleaseDeviceContexts();
        }
    }
}
