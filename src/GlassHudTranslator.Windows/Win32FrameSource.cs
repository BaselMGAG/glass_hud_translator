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
/// The DC and bitmap are cached across calls and only rebuilt when the region size changes, since
/// creating them is most of the cost. The pixel buffer is not cached - each call allocates a fresh
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
        EnsureResources(region.Width, region.Height);

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

    private void EnsureResources(int width, int height)
    {
        if (_screenDc == IntPtr.Zero)
        {
            // A null HWND gives the DC for the entire virtual screen.
            _screenDc = NativeMethods.GetDC(IntPtr.Zero);
            _memoryDc = NativeMethods.CreateCompatibleDC(_screenDc);
        }

        if (_bitmap != IntPtr.Zero && _bitmapWidth == width && _bitmapHeight == height) return;

        if (_bitmap != IntPtr.Zero) NativeMethods.DeleteObject(_bitmap);

        _bitmap = NativeMethods.CreateCompatibleBitmap(_screenDc, width, height);
        _bitmapWidth = width;
        _bitmapHeight = height;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            if (_bitmap != IntPtr.Zero) NativeMethods.DeleteObject(_bitmap);
            if (_memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(_memoryDc);
            if (_screenDc != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, _screenDc);

            _bitmap = _memoryDc = _screenDc = IntPtr.Zero;
        }
    }
}
