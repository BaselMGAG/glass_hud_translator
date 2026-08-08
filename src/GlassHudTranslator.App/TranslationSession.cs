using System.Diagnostics;
using GlassHudTranslator.App.Views;
using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Pipeline;
using GlassHudTranslator.Core.Platform;
using GlassHudTranslator.Core.Regions;
using GlassHudTranslator.Core.Translation;

namespace GlassHudTranslator.App;

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

    /// <summary>
    /// Re-read from settings on every use rather than captured once: the interface language can be
    /// switched while a session is live, and the overlay is the one surface where an English
    /// sentence at the moment something breaks is worst - it is what the user is looking at.
    /// </summary>
    private UiText Text => UiText.For(_settings.Language);
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
            if (region is null) return;   // ResolveRegionAsync has already explained why

            _overlay.ShowLoading();

            var frame = await _frames.GetFrameAsync(region.Value, ct).ConfigureAwait(false);
            if (frame is null)
            {
                Fail(Text.NothingCaptured);
                return;
            }

            await ProcessAsync(frame, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _overlay.Clear();
        }
        catch (Exception e)
        {
            // Every exit path has to leave the overlay in a defined state. Reporting only to the
            // Settings status line left it showing "loading" forever, which reads as a hang.
            Fail(string.Format(Text.TranslationFailed, e.Message));
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
            StopAutoWatch(Text.AutoWatchOff);
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

        Report(string.Format(Text.AutoWatchOn, _settings.AutoWatchExpirySeconds));
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
                    StopAutoWatch(string.Format(Text.AutoWatchExpired, expiry.TotalSeconds.ToString("0")));
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
            StopAutoWatch(string.Format(Text.AutoWatchStopped, e.Message));
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

        // In the pipeline rather than checked here afterwards: it used to be an after-the-fact
        // guard, which meant the "too short to translate" line had already been translated, paid
        // for, and cached by the time it was discarded.
        _services.Pipeline.MinimumBodyCharacters = _settings.MinimumCharactersToTranslate;

        var outcome = await _services.Pipeline
            .ProcessAsync(frame, _settings.LastRegionProfile, SourceKind.Screen, ct)
            .ConfigureAwait(false);

        // Null result: nothing was attempted - an empty dialogue box, or a stray glyph or UI
        // border that OCR'd to a character or two, which is not dialogue.
        if (outcome.Result is not { } result)
        {
            Fail(outcome.Body.Trim().Length == 0
                ? Text.NoTextInRegion
                : string.Format(Text.TooShortToTranslate, outcome.Body.Trim()));
            return;
        }

        _lastSourceText = outcome.Body;
        _lastArabic = result.Text;

        if (result.IsFallbackEnglish)
            _overlay.ShowFallbackEnglish(outcome.Speaker, result.Text);
        else
            _overlay.ShowTranslation(outcome.Speaker, result.Text);

        var source = result.FromCache ? "cache" : $"{result.Provider}/{result.Model}";
        Report($"{source} · {outcome.Total.TotalMilliseconds:F0} ms · OCR confidence {outcome.OcrConfidence:F0}");
    }

    /// <summary>
    /// Turns the stored fractional profile into screen pixels against the game's current client
    /// area, which is what makes a saved region survive the window being moved.
    /// </summary>
    private async Task<CaptureRegion?> ResolveRegionAsync(CancellationToken ct)
    {
        var profile = await _services.Regions
            .LoadOrDefaultAsync(_services.Profile.Id, _settings.LastRegionProfile, ct).ConfigureAwait(false);

        var window = PlatformServices.FindGameWindow(_services.Profile.WindowTitles, _services.Profile.ProcessNames);
        if (window is null)
        {
            // No game window - either not running, or we are on macOS where the frame source
            // replays recorded PNGs and ignores the region anyway.
            if (!PlatformServices.IsWindows) return CaptureRegion.Empty;

            Fail(string.Format(Text.GameWindowNotFound, _services.Profile.DisplayName));
            return null;
        }

        if (!window.CanCapture)
        {
            Fail(window.Message);
            return null;
        }

        var client = window.ClientArea;

        // The overlay follows the game. Raised only on a change, because auto-watch resolves a
        // region twice a second and moving a window on every tick would fight the compositor.
        if (_lastAnchor != client)
        {
            _lastAnchor = client;
            GameWindowLocated?.Invoke(client);
        }

        // Said once per (profile, region, layout) rather than every frame - auto-watch runs at 2 fps
        // and a warning repeated 120 times a minute is noise the user learns to ignore.
        if (!profile.MatchesLayout(client.Width, client.Height, window.Scaling)
            && _layoutWarnedFor != LayoutKey(profile, client))
        {
            _layoutWarnedFor = LayoutKey(profile, client);
            Report(Text.RegionLayoutChanged);
        }

        var relative = profile.Resolve(client.Width, client.Height);
        var region = relative.Translate(client.X, client.Y);

        // The display layout can change under a stored region - a monitor unplugged, the game moved
        // to a smaller screen. Capturing the overhang would BitBlt undefined pixels into OCR, which
        // surfaces as garbage text and reads as the model getting worse.
        var desktop = PlatformServices.VirtualDesktop();
        if (desktop.IsEmpty || desktop.Contains(region)) return region;

        var trimmed = region.ClampTo(desktop);
        if (trimmed.IsEmpty)
        {
            Fail(Text.RegionOffScreenTrimmed);
            return null;
        }

        if (_trimmedWarnedFor != LayoutKey(profile, client))
        {
            _trimmedWarnedFor = LayoutKey(profile, client);
            Report(Text.RegionOffScreenTrimmed);
        }

        return trimmed;
    }

    /// <summary>Identifies one region drawn against one window size, for once-only warnings.</summary>
    private string LayoutKey(RegionProfile profile, CaptureRegion client) =>
        $"{_services.Profile.Id}/{profile.Name}/{client.Width}x{client.Height}";

    private string? _layoutWarnedFor;
    private string? _trimmedWarnedFor;

    /// <summary>
    /// Raised when the game's window is located and has moved or resized since last time, so the
    /// overlay can follow it. Marshalled to the UI thread by the subscriber, like <see cref="Status"/>:
    /// auto-watch resolves regions on a background thread.
    /// </summary>
    public event Action<CaptureRegion>? GameWindowLocated;

    /// <summary>Where the overlay should sit, or null when there is no game window to follow.</summary>
    public CaptureRegion? OverlayAnchor()
    {
        var window = PlatformServices.FindGameWindow(
            _services.Profile.WindowTitles, _services.Profile.ProcessNames);

        return window?.ClientArea is { Width: > 0, Height: > 0 } client ? client : null;
    }

    private CaptureRegion? _lastAnchor;

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
            Report(string.Format(Text.CouldNotSaveFrame, e.Message));
        }
    }

    private void Report(string message) => Status?.Invoke(message);

    /// <summary>Reports to both the Settings status line and the overlay the user is looking at.</summary>
    private void Fail(string message)
    {
        Report(message);
        _overlay.ShowError(message);
    }

    public void Dispose()
    {
        _autoWatch?.Cancel();
        _autoWatch?.Dispose();
        _frames.Dispose();
    }
}
