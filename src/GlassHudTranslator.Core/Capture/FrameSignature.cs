namespace GlassHudTranslator.Core.Capture;

/// <summary>
/// A binarised 64x24 thumbnail of a frame, used to answer one question cheaply: has the dialogue
/// text changed since the last poll? Brief 3 makes this the difference between ~120 ms and ~15 ms
/// per tick, because 85-90% of frames during dialogue are unchanged.
///
/// <para>
/// Deviates from PROJECT_PLAN.md 4, which specified a 64-bit perceptual hash compared by Hamming
/// distance. Two problems with that, both discovered while implementing:
/// </para>
/// <list type="number">
/// <item>64 bits over a whole dialogue box is far too coarse. One changed word covers a few percent
/// of the image and would frequently flip no bits at all - and a false "unchanged" means a missed
/// translation, which is the expensive direction to be wrong in. A false "changed" only costs one
/// 80 ms OCR.</item>
/// <item>The FFXIV dialogue box is semi-transparent, so the scene behind it bleeds through. Any
/// comparison of raw grey levels drifts continuously while the player moves the camera, which
/// would defeat the optimisation entirely. Binarising first makes the signature depend on the
/// near-white glyphs rather than on whatever is happening behind them.</item>
/// </list>
/// <para>
/// The threshold is picked per frame by Otsu's method rather than being a constant, because the
/// frame corpus deliberately covers bright zones and dark zones behind the same box.
/// </para>
/// </summary>
public sealed class FrameSignature
{
    public const int Width = 64;
    public const int Height = 24;
    public const int CellCount = Width * Height;

    /// <summary>
    /// Cells that must differ before two frames count as different. Provisional - Session 3 tunes
    /// it against the real corpus by measuring the OCR-skip rate, which should sit at 85%+.
    /// </summary>
    public const int DefaultChangeThreshold = 6;

    /// <summary>
    /// Fraction trimmed from each edge before sampling. A hand-dragged region always includes some
    /// raw scene outside the translucent box, and that margin is the least stable part of the
    /// frame: it tracks the scene directly instead of being damped by the box, so walking from a
    /// dark zone into a bright one flips it wholesale and swamps the signature. The text sits well
    /// inside the box padding, so discarding the outer 8% costs nothing and removes the problem.
    /// </summary>
    private const double EdgeInset = 0.08;

    private readonly byte[] _cells;

    private FrameSignature(byte[] cells, byte otsuThreshold)
    {
        _cells = cells;
        OtsuThreshold = otsuThreshold;
        Hash = Fnv1A(cells);
    }

    /// <summary>Stable 64-bit identity, for logs and diagnostics only - never for comparison.</summary>
    public ulong Hash { get; }

    /// <summary>The luma cut chosen for this frame. Useful when diagnosing a bad capture.</summary>
    public byte OtsuThreshold { get; }

    /// <summary>Fraction of cells that are "ink". Near 0 or near 1 means the capture is suspect.</summary>
    public double InkRatio
    {
        get
        {
            var ink = 0;
            foreach (var c in _cells) ink += c;
            return (double)ink / CellCount;
        }
    }

    public static FrameSignature Compute(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var grey = Interior(frame).Resize(Width, Height).ToGreyscale();
        var threshold = OtsuOf(grey);

        var cells = new byte[CellCount];
        for (var i = 0; i < CellCount; i++)
            cells[i] = grey[i] > threshold ? (byte)1 : (byte)0;

        return new FrameSignature(cells, threshold);
    }

    public int DifferenceCount(FrameSignature other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var differences = 0;
        for (var i = 0; i < CellCount; i++)
            if (_cells[i] != other._cells[i])
                differences++;

        return differences;
    }

    /// <summary>A null <paramref name="previous"/> (first frame) always counts as changed.</summary>
    public bool LooksIdenticalTo(FrameSignature? previous, int maxDifferingCells = DefaultChangeThreshold) =>
        previous is not null && DifferenceCount(previous) <= maxDifferingCells;

    /// <summary>
    /// The frame minus its outer <see cref="EdgeInset"/> on each side. Falls back to the whole
    /// frame when it is too small for the trim to leave anything useful.
    /// </summary>
    private static Frame Interior(Frame frame)
    {
        var insetX = (int)(frame.Width * EdgeInset);
        var insetY = (int)(frame.Height * EdgeInset);
        var region = new CaptureRegion(
            insetX, insetY, frame.Width - insetX * 2, frame.Height - insetY * 2);

        return region.FitsWithin(frame.Width, frame.Height) && region.Width >= Width && region.Height >= Height
            ? frame.Crop(region)
            : frame;
    }

    /// <summary>Otsu's method: the luma cut that maximises between-class variance.</summary>
    private static byte OtsuOf(byte[] grey)
    {
        Span<int> histogram = stackalloc int[256];
        foreach (var g in grey) histogram[g]++;

        long weightedTotal = 0;
        for (var i = 0; i < 256; i++) weightedTotal += (long)i * histogram[i];

        long backgroundWeighted = 0;
        var backgroundCount = 0;
        var bestVariance = -1.0;
        byte best = 128;

        for (var t = 0; t < 256; t++)
        {
            backgroundCount += histogram[t];
            if (backgroundCount == 0) continue;

            var foregroundCount = grey.Length - backgroundCount;
            if (foregroundCount == 0) break;

            backgroundWeighted += (long)t * histogram[t];
            var backgroundMean = (double)backgroundWeighted / backgroundCount;
            var foregroundMean = (double)(weightedTotal - backgroundWeighted) / foregroundCount;

            var delta = backgroundMean - foregroundMean;
            var variance = (double)backgroundCount * foregroundCount * delta * delta;
            if (variance > bestVariance)
            {
                bestVariance = variance;
                best = (byte)t;
            }
        }

        return best;
    }

    private static ulong Fnv1A(byte[] data)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in data)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    public override string ToString() =>
        $"sig {Hash:x16} ink={InkRatio:P1} otsu={OtsuThreshold}";
}
