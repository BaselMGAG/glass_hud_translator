namespace GamingTranslatorGlassHUD.Core.Capture;

/// <summary>
/// The seam that keeps the pipeline testable off Windows. Win32FrameSource does BitBlt;
/// FolderFrameSource replays recorded PNGs. Everything downstream sees only <see cref="Frame"/>.
/// </summary>
public interface IFrameSource : IDisposable
{
    /// <summary>Human-readable identity of the last frame returned - a filename, or the region.</summary>
    string LastFrameLabel { get; }

    /// <summary>Returns null when no frame is available (source exhausted, or window not found).</summary>
    Task<Frame?> GetFrameAsync(CaptureRegion region, CancellationToken ct);
}
