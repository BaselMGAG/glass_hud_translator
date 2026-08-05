using System.Diagnostics;
using GamingTranslatorGlassHUD.App.Views;
using GamingTranslatorGlassHUD.Core.Capture;
using GamingTranslatorGlassHUD.Core.Config;
using GamingTranslatorGlassHUD.Core.Platform;
using GamingTranslatorGlassHUD.Core.Regions;
using GamingTranslatorGlassHUD.Core.Translation;

namespace GamingTranslatorGlassHUD.App;

/// <summary>
/// Drives the live loop: a hotkey or a poll produces a capture, the capture goes through the
/// pipeline, and the result lands on the overlay.
///
/// <para>
/// Manual trigger is the default mode. Continuous polling was where nearly all of the original
/// design's complexity lived, and it does not fit the API budget either - one request per dialogue
/// advance is 3-6 a minute, where polling once a second would exhaust a free tier in the first
/// scene. Auto-watch stays available as an explicit opt-in for cutscenes.
/// </para>
/// </summary>
public sealed class TranslationSession : IDisposable
{
    private readonly AppServices _services;
    private readonly OverlayWindow _overlay;
    private readonly AppSettings _settings;

    private readonly IFrameSource _frames;
    private CancellationTokenSource? _autoWatch;
    private FrameSignature? _lastSignature;
    private string? _lastSourceText;
    private string? _lastArabic;
    private bool _busy;

    public TranslationSession(AppServices services, OverlayWindow overlay, AppSettings settings, string framesDirectory)
    {
        _services = services;
        _overlay = overlay;
        _settings = settings;
        _frames = PlatformServices.CreateFrameSource(framesDirectory);
    }

    public bool IsAutoWatching => _autoWatch is not null;

    /// <summary>
    /// When set, every captured region is written here as a PNG. This is how a real frame corpus
    /// gets collected: play normally for twenty minutes and the folder fills with exactly the
    /// frames the OCR has to cope with, rather than screenshots someone took by hand.
    /// </summary>
    public string? SaveFramesDirectory { get; set; }

    private int _savedFrames;

    public event Action<string>? Status;

    /// <summary>The line currently on the overlay, so it can be corrected with the flag hotkey.</summary>
    public (string Source, string Arabic)? Current =>
        _lastSourceText is null || _lastArabic is null ? null : (_lastSourceText, _lastArabic);

    public async Task TranslateNowAsync(CancellationToken ct = default)
    {
        if (_busy) return;
        _busy = true;

        try
        {
            var region = await ResolveRegionAsync(ct).ConfigureAwait(false);
            if (region is null) return;

            _overlay.ShowLoading();

            var frame = await _frames.GetFrameAsync(region.Value, ct).ConfigureAwait(false);
            if (frame is null)
            {
                Report("Nothing captured. Is the game running in borderless windowed mode?");
                return;
            }

            await ProcessAsync(frame, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception e)
        {
            Report($"Translation failed: {e.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    public void ToggleAutoWatch()
    {
        if (_autoWatch is not null)
        {
            StopAutoWatch("Auto-watch off.");
            return;
        }

        _autoWatch = new CancellationTokenSource();
        var token = _autoWatch.Token;

        var worker = new Thread(() => AutoWatchLoop(token))
        {
            IsBackground = true,
            Name = "auto-watch",

            // The game's render thread must always win. On weak hardware a capture-and-OCR tick
            // competing at normal priority is visible as dropped frames.
            Priority = ThreadPriority.BelowNormal,
        };
        worker.Start();

        Report($"Auto-watch on, {_settings.AutoWatchFps:0.#} fps. " +
               $"Stops itself after {_settings.AutoWatchExpirySeconds}s with no new text.");
    }

    private void AutoWatchLoop(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(1.0 / Math.Max(0.5, _settings.AutoWatchFps));
        var expiry = TimeSpan.FromSeconds(_settings.AutoWatchExpirySeconds);
        var lastChange = Stopwatch.GetTimestamp();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                Thread.Sleep(interval);
                if (ct.IsCancellationRequested) break;

                // A toggle left on during an AFK is the main way to leak API quota, so this expiry
                // is not optional.
                if (Stopwatch.GetElapsedTime(lastChange) > expiry)
                {
                    StopAutoWatch($"Auto-watch stopped after {expiry.TotalSeconds:0}s with no new text.");
                    return;
                }

                var region = ResolveRegionAsync(ct).GetAwaiter().GetResult();
                if (region is null) continue;

                var frame = _frames.GetFrameAsync(region.Value, ct).GetAwaiter().GetResult();
                if (frame is null) continue;

                // The cheap gate: most frames during dialogue are identical to the previous one and
                // must never reach OCR.
                var signature = FrameSignature.Compute(frame);
                if (signature.LooksIdenticalTo(_lastSignature)) continue;

                _lastSignature = signature;
                lastChange = Stopwatch.GetTimestamp();

                if (_busy) continue;
                _busy = true;
                try
                {
                    ProcessAsync(frame, ct).GetAwaiter().GetResult();
                }
                finally
                {
                    _busy = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (Exception e)
        {
            StopAutoWatch($"Auto-watch stopped: {e.Message}");
        }
    }

    private void StopAutoWatch(string message)
    {
        _autoWatch?.Cancel();
        _autoWatch?.Dispose();
        _autoWatch = null;
        Report(message);
    }

    private async Task ProcessAsync(Frame frame, CancellationToken ct)
    {
        SaveFrameIfRequested(frame);
        _services.Pipeline.Register = _settings.Register;

        var outcome = await _services.Pipeline.ProcessAsync(frame, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(outcome.Body))
        {
            Report("No text found in the capture region.");
            return;
        }

        _lastSourceText = outcome.Body;
        _lastArabic = outcome.Result.Text;

        if (outcome.Result.IsFallbackEnglish)
            _overlay.ShowFallbackEnglish(outcome.Speaker, outcome.Result.Text);
        else
            _overlay.ShowTranslation(outcome.Speaker, outcome.Result.Text);

        var source = outcome.Result.FromCache ? "cache" : $"{outcome.Result.Provider}/{outcome.Result.Model}";
        Report($"{source} · {outcome.Total.TotalMilliseconds:F0} ms · OCR confidence {outcome.OcrConfidence:F0}");
    }

    /// <summary>
    /// Turns the stored fractional profile into screen pixels against the game's current client
    /// area, which is what makes a saved region survive the window being moved.
    /// </summary>
    private async Task<CaptureRegion?> ResolveRegionAsync(CancellationToken ct)
    {
        var profile = await _services.Regions
            .LoadOrDefaultAsync(_settings.LastRegionProfile, ct).ConfigureAwait(false);

        var window = PlatformServices.FindGameWindow(_services.Profile.WindowTitles);
        if (window is null)
        {
            // No game window - either not running, or we are on macOS where the frame source
            // replays recorded PNGs and ignores the region anyway.
            return PlatformServices.IsWindows ? null : CaptureRegion.Empty;
        }

        if (!window.CanCapture)
        {
            Report(window.Message);
            return null;
        }

        var client = window.ClientArea;
        var relative = profile.Resolve(client.Width, client.Height);
        return new CaptureRegion(client.X + relative.X, client.Y + relative.Y, relative.Width, relative.Height);
    }

    private void SaveFrameIfRequested(Frame frame)
    {
        if (SaveFramesDirectory is null) return;

        try
        {
            Directory.CreateDirectory(SaveFramesDirectory);
            var name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{++_savedFrames:D3}.png";
            frame.SavePng(Path.Combine(SaveFramesDirectory, name));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Collecting frames is a convenience; never let it take down a play session.
            Report($"Could not save frame: {e.Message}");
        }
    }

    private void Report(string message) => Status?.Invoke(message);

    public void Dispose()
    {
        _autoWatch?.Cancel();
        _autoWatch?.Dispose();
        _frames.Dispose();
    }
}
