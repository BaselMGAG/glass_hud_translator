namespace GlassHudTranslator.Core.Capture;

/// <summary>
/// Replays PNGs from a directory, one per call, in filename order. This is what turns the Mac into
/// a real test environment: the whole pipeline runs deterministically against recorded FFXIV
/// frames without Windows anywhere (brief 9).
/// </summary>
public sealed class FolderFrameSource : IFrameSource
{
    private readonly string[] _files;
    private int _index = -1;

    public FolderFrameSource(string directory, bool wrap = false)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Frame directory not found: {directory}");

        _files = Directory.GetFiles(directory, "*.png").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        if (_files.Length == 0)
            throw new InvalidOperationException($"No .png files in {directory}");

        Wrap = wrap;
    }

    public bool Wrap { get; }

    public int Count => _files.Length;

    public string LastFrameLabel { get; private set; } = "<none>";

    public IReadOnlyList<string> Files => _files;

    public Task<Frame?> GetFrameAsync(CaptureRegion region, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var next = _index + 1;
        if (next >= _files.Length)
        {
            if (!Wrap) return Task.FromResult<Frame?>(null);
            next = 0;
        }

        _index = next;
        var path = _files[_index];
        LastFrameLabel = Path.GetFileName(path);

        var frame = Frame.FromFile(path);

        // Recorded frames are normally already cropped to the dialogue box, so a region is only
        // applied when it actually fits - that also lets full-screen captures be replayed.
        return Task.FromResult<Frame?>(
            region.FitsWithin(frame.Width, frame.Height) ? frame.Crop(region) : frame);
    }

    public void Reset() => _index = -1;

    public void Dispose() { }
}
