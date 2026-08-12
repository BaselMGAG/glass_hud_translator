namespace GlassHudTranslator.Core.Capture;

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

    /// <summary>
    /// Why the last call returned null, or null if it did not. Optional — a source with only one
    /// way to fail has nothing to add.
    ///
    /// <para>
    /// On the interface rather than on the Win32 class because the CALLER is what has to say it out
    /// loud, and the caller only ever sees the seam. A capture that returns nothing throws no
    /// exception and writes no log line, so the app simply goes quiet — which from outside is
    /// identical to it deciding there was nothing to translate. Two separate support rounds were
    /// spent guessing which of three unrelated Win32 failures had happened.
    /// </para>
    /// </summary>
    string? LastFailure => null;
}
