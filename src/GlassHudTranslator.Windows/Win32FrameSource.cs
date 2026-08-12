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

    /// <summary>
    /// Process-wide, because the screen DC is process-wide whether we like it or not.
    ///
    /// <para>
    /// <c>GetDC(NULL)</c> draws from a small system CACHE of common device contexts. It is not a
    /// private handle, it is not guaranteed to be the same handle twice, and the app has more than
    /// one thing that wants a picture of the screen — the poll loop on its own thread, and the
    /// region picker and the snip on the UI thread. Holding the acquire, the BitBlt and the release
    /// inside one lock is what makes those safe to interleave.
    /// </para>
    /// </summary>
    private static readonly Lock ScreenGate = new();

    private IntPtr _memoryDc;
    private IntPtr _bitmap;
    private int _bitmapWidth;
    private int _bitmapHeight;
    private int _layout;
    private bool _disposed;

    public string LastFrameLabel { get; private set; } = "<none>";

    /// <summary>
    /// Why the last capture came back with nothing, or null if it did not.
    ///
    /// <para>
    /// <b>Three different failures used to arrive as one silent null.</b> "The screen device
    /// context could not be acquired", "BitBlt refused" and "GetDIBits returned no scan lines" have
    /// nothing to do with each other — the first is a resource problem, the second usually means
    /// the source rectangle is not where we think it is, the third is a bitmap-state problem — and
    /// two support rounds were spent guessing between them because the app could only say "nothing
    /// was captured". A capture that returns nothing has to say which nothing it was.
    /// </para>
    /// </summary>
    public string? LastFailure { get; private set; }

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
        // Acquired here and released before returning, every single time, and NEVER cached.
        //
        // <b>This is the fix for a total outage that has now been diagnosed twice.</b> The screen DC
        // was held for the lifetime of the source, which is only safe if there is exactly one source
        // in the process - and there is not: the region picker and the snip each build one to grab a
        // still of the whole desktop, and disposing it called ReleaseDC on a handle the live session
        // was still holding. Every capture afterwards returned nothing, silently, until the app was
        // restarted: BitBlt on a released DC simply fails, and no exception is thrown and no log line
        // is written.
        //
        // Refcounting the holders was the first attempt and it is not sound. It assumes every holder
        // got the SAME handle, which GetDC(NULL) does not promise - it hands out whichever cached
        // context is free. When two holders get different handles the counter says "someone else is
        // still using it" and the handle is leaked instead, which exhausts the same small cache from
        // the other direction. Holding it for one BitBlt removes the question: nothing is shared
        // across calls, so nothing can be released out from under anything.
        lock (ScreenGate)
        {
            var screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
            {
                LastFailure = "GetDC(NULL) returned nothing - the system is out of cached device "
                    + "contexts, which usually means something in this process is leaking them.";
                return null;
            }

            try
            {
                return CaptureWith(screenDc, region);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    private Frame? CaptureWith(IntPtr screenDc, CaptureRegion region)
    {
        if (!EnsureResources(screenDc, region.Width, region.Height)) return null;

        var previous = NativeMethods.SelectObject(_memoryDc, _bitmap);
        var restored = false;
        try
        {
            // CAPTUREBLT is needed to include layered windows. Our own overlay is excluded
            // separately via WDA_EXCLUDEFROMCAPTURE, or it would read its own output back.
            var copied = NativeMethods.BitBlt(
                _memoryDc, 0, 0, region.Width, region.Height,
                screenDc, region.X, region.Y,
                NativeMethods.SrcCopy | NativeMethods.CaptureBlt);

            if (!copied)
            {
                LastFailure = $"BitBlt refused {region}. The commonest cause is exclusive "
                    + "fullscreen; the next commonest is a rectangle that is not on any monitor.";
                return null;
            }

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

            // Deselected FIRST, and that is a documented requirement rather than tidiness: GetDIBits
            // states that the bitmap must not be selected into a device context when it is called.
            // It was being called with the bitmap still selected into _memoryDc, which most drivers
            // tolerate and some do not - and when one does not, it returns zero scan lines, which
            // this method reports as "nothing was captured". A silent, driver-dependent, permanent
            // capture failure is exactly the shape of bug this project keeps paying for.
            NativeMethods.SelectObject(_memoryDc, previous);
            restored = true;

            var scanLines = NativeMethods.GetDIBits(
                _memoryDc, _bitmap, 0, (uint)region.Height, buffer, ref info, NativeMethods.DibRgbColors);

            if (scanLines == 0)
            {
                LastFailure = $"GetDIBits read no scan lines back from a {region.Width}x{region.Height} "
                    + "bitmap that BitBlt had just filled.";
                return null;
            }

            LastFailure = null;
            return new Frame(region.Width, region.Height, buffer);
        }
        finally
        {
            if (!restored) NativeMethods.SelectObject(_memoryDc, previous);
        }
    }

    private bool EnsureResources(IntPtr screenDc, int width, int height)
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

        if (_memoryDc != IntPtr.Zero && layout != _layout) ReleaseDeviceContexts();

        if (_memoryDc == IntPtr.Zero)
        {
            // Compatible with the screen, but not dependent on that particular handle staying alive:
            // CreateCompatibleDC returns a private context describing the same pixel format, and it
            // outlives the screen DC it was measured against.
            _memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (_memoryDc == IntPtr.Zero)
            {
                LastFailure = "CreateCompatibleDC failed - GDI handles are exhausted.";
                return false;
            }

            _layout = layout;
        }

        if (_bitmap != IntPtr.Zero && _bitmapWidth == width && _bitmapHeight == height) return true;

        if (_bitmap != IntPtr.Zero) NativeMethods.DeleteObject(_bitmap);

        // Unchecked before now. A failure here left a null handle that SelectObject would then be
        // handed on every subsequent capture, so one transient GDI exhaustion became a permanent
        // black screen for the rest of the session.
        _bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
        if (_bitmap == IntPtr.Zero)
        {
            _bitmapWidth = _bitmapHeight = 0;
            LastFailure = $"CreateCompatibleBitmap({width}x{height}) failed - GDI handles are exhausted.";
            return false;
        }

        _bitmapWidth = width;
        _bitmapHeight = height;
        return true;
    }

    /// <summary>
    /// Frees what this instance actually owns, which is now everything it holds.
    ///
    /// <para>
    /// There is no shared state left to be careful about. <c>CreateCompatibleBitmap</c> and
    /// <c>CreateCompatibleDC</c> return private handles, so both are freed unconditionally and a
    /// second frame source can come and go without touching this one — which is the property the
    /// holder counter was trying and failing to provide.
    /// </para>
    /// </summary>
    private void ReleaseDeviceContexts()
    {
        if (_bitmap != IntPtr.Zero) NativeMethods.DeleteObject(_bitmap);
        if (_memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(_memoryDc);

        _bitmap = _memoryDc = IntPtr.Zero;
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
