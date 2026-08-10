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

    /// <summary>
    /// Decides which polled frames are worth translating. Owns the "has this changed" question that
    /// used to be a bare signature comparison here - which answered yes on every frame of a
    /// typewriter reveal and so translated one sentence four times over.
    /// </summary>
    private readonly FrameSettleGate _settle = new();

    /// <summary>
    /// The pacing and the caps for the run currently in progress, and the only thing that measures
    /// how fast the watched content actually changes. Null when auto-watch is off.
    /// </summary>
    private WatchSession? _watch;

    /// <summary>
    /// Works out what the watched region actually is, for <see cref="WatchMode.Auto"/>. Fed on
    /// every poll including the cheap ones — a poll that changed nothing is the commonest evidence
    /// there is, and the one that says a line is being waited on.
    /// </summary>
    private readonly ContentRhythm _rhythm = new();

    /// <summary>
    /// Consecutive polls that threw. One bad frame used to end the whole run: the try/catch was
    /// around the entire loop, so a single OCR failure on an unfamiliar font stopped auto-watch
    /// permanently — and said so only on the Settings status line, which nobody playing a game is
    /// looking at. Reported from Wuthering Waves as «كلام مش عارف يترجمه فا يقف خالص»: text it
    /// cannot translate, and it stops dead.
    /// </summary>
    private int _consecutiveFailures;

    /// <summary>
    /// Consecutive polls that read nothing at all. A region that has stopped matching the game —
    /// the window resized, the HUD layout changed, the wrong rectangle was saved — looks exactly
    /// like a quiet moment, and the app used to say "no text in the capture region" once per poll
    /// and leave the user to guess. After enough of them in a row it says the useful thing instead.
    /// </summary>
    private int _consecutiveEmpty;

    /// <summary>How many empty reads in a row before the region itself becomes the suspect.</summary>
    private const int EmptyReadsBeforeBlamingTheRegion = 12;

    /// <summary>How many polls in a row may throw before the run is genuinely abandoned.</summary>
    private const int FailuresBeforeGivingUp = 5;

    private string? _lastSourceText;
    private string? _lastArabic;
    private bool _busy;

    /// <summary>
    /// Mean OCR confidence of the last frame that actually contained text. Fed to the health
    /// check, where "the region reads poorly" is a diagnosis the user can act on and a raw number
    /// in a status line was not.
    /// </summary>
    public float? LastOcrConfidence { get; private set; }

    public TranslationSession(AppServices services, OverlayWindow overlay, AppSettings settings, string framesDirectory)
    {
        _services = services;
        _overlay = overlay;
        _settings = settings;
        _frames = PlatformServices.CreateFrameSource(framesDirectory);
    }

    public bool IsAutoWatching => _autoWatch is not null;

    /// <summary>
    /// What the current run has measured, for the Diagnostics tab. Null when auto-watch is off.
    ///
    /// <para>
    /// Surfaced rather than kept internal on purpose. The pacing adapts itself now, and an
    /// adaptation nobody can see is indistinguishable from a bug — if the overlay feels slow, the
    /// first useful question is what rhythm the app thinks it is watching, and this is the only
    /// place that answers it.
    /// </para>
    /// </summary>
    public (TimeSpan? Cadence, int Requests, TimeSpan Elapsed, bool Outrunning)? WatchStats =>
        _watch is { } watch
            ? (watch.Cadence, watch.Requests, watch.Elapsed, watch.OutrunningTheFloor)
            : null;

    /// <summary>
    /// What the content detector has concluded, and what it is running as. Surfaced for the same
    /// reason the cadence is: an automatic mode nobody can inspect is indistinguishable from a
    /// bug, and "why is it waiting so long" has exactly one useful first answer.
    /// </summary>
    public (ContentKind Kind, WatchMode Running)? ContentVerdict =>
        _watch is null ? null : (_rhythm.Kind, _settings.WatchMode == WatchMode.Auto ? _running : _settings.WatchMode);

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

            _ = await ProcessAsync(frame, Trigger.Hotkey, ct).ConfigureAwait(false);
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

        // Otherwise switching auto-watch off and straight back on sits on Unchanged until the
        // player advances the dialogue, which reads as the toggle having done nothing. The repeat
        // guard needs the same treatment for the same reason and one layer up: the gate would let
        // the frame through and the pipeline would then drop it as a line it has already shown.
        // Deliberately NOT ResetContext - the three lines the player was just reading are still the
        // scene they are in, and throwing them away costs a worse translation for nothing.
        _settle.Reset();
        _services.Pipeline.ResetRepeatGuard();
        _rhythm.Reset();
        _consecutiveFailures = 0;
        _consecutiveEmpty = 0;
        _overlay.Notice = null;

        // The mode is read here rather than per tick, so one run has one set of caps and one
        // cadence estimate. Switching mode mid-run would silently reset the clock the caps are
        // measured against, which is the one thing a cap must not allow.
        // Auto resolves to Dialogue until the detector has seen enough, which is the cheap mistake
        // to make while deciding: patience on a film costs a few late lines, impatience on a
        // typewriter reveal costs a request per half-written sentence.
        var pacing = WatchPacing.For(_settings.WatchMode);
        if (_settings.SecondsBetweenTranslations > 0)
            pacing = pacing with { MinimumInterval = TimeSpan.FromSeconds(_settings.SecondsBetweenTranslations) };

        _watch = new WatchSession(pacing) { Unbounded = _settings.WatchWithoutLimit };
        _watch.Start();
        _settle.Retune(_watch.Settle());

        var worker = new Thread(() => AutoWatchLoop(token))
        {
            IsBackground = true,
            Name = "auto-watch",

            // The game's render thread must always win. On weak hardware a capture-and-OCR tick
            // competing at normal priority is visible as dropped frames.
            Priority = ThreadPriority.BelowNormal,
        };
        worker.Start();

        // On the overlay too, not only in Settings. Switching it on is the moment to say what it
        // will cost and when it will stop by itself — afterwards the player is looking at the game.
        var announcement = string.Format(Text.AutoWatchOn,
            Text.WatchModeName(_settings.WatchMode),
            _watch.Unbounded ? Text.NoLimit : pacing.StopAfter.TotalMinutes.ToString("0"));

        Report(announcement);
        _overlay.ShowMessage(announcement);
    }

    private void AutoWatchLoop(CancellationToken ct)
    {
        var watch = _watch!;
        var expiry = TimeSpan.FromSeconds(_settings.AutoWatchExpirySeconds);
        var lastChange = Stopwatch.GetTimestamp();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Re-read per tick rather than once, because Auto can swap the pacing underneath
                // this loop. Two property reads and a divide; the poll it precedes costs a BitBlt.
                Thread.Sleep(_settings.AutoWatchFps > 0
                    ? TimeSpan.FromSeconds(1.0 / _settings.AutoWatchFps)
                    : watch.Pacing.PollInterval);

                if (ct.IsCancellationRequested) break;

                // The session cap, measured from switch-on. The idle expiry below cannot do this
                // job: it resets on any movement, so over a playing video - or a game with
                // animation anywhere in the capture region - it never fires at all. A guard that a
                // moving screen switches off is not a guard.
                switch (watch.Check())
                {
                    case WatchVerdict.Stop:
                        StopAutoWatch(string.Format(Text.AutoWatchReachedLimit,
                            watch.Elapsed.TotalMinutes.ToString("0"), watch.Requests), onOverlay: true);
                        return;

                    case WatchVerdict.Warn:
                        // Sticky, because it has to survive the translations that keep arriving
                        // after it. This is the line the whole cap exists to deliver.
                        _overlay.Notice = string.Format(Text.AutoWatchStillRunning,
                            watch.Elapsed.TotalMinutes.ToString("0"), watch.Requests);
                        Report(_overlay.Notice);
                        break;
                }

                // The AFK expiry stays, unchanged, for what it was always good at: a toggle left on
                // in front of a genuinely static screen.
                if (Stopwatch.GetElapsedTime(lastChange) > expiry)
                {
                    StopAutoWatch(string.Format(Text.AutoWatchExpired, expiry.TotalSeconds.ToString("0")),
                        onOverlay: true);
                    return;
                }

                // The floor, asked before the frame reaches the gate for the same reason the busy
                // check is: calling a frame Ready commits it, so a frame refused afterwards would
                // be remembered as handled and the screen would sit unchanged for good.
                if (!watch.MayTranslate()) continue;

                var region = ResolveRegionAsync(ct).GetAwaiter().GetResult();
                if (region is null) continue;

                var frame = _frames.GetFrameAsync(region.Value, ct).GetAwaiter().GetResult();
                if (frame is null) continue;

                // BEFORE the gate is offered anything, not after. Deciding a frame is Ready is not
                // a question - it commits that frame as the one now on the overlay - so offering a
                // frame we are in no position to translate loses the line outright: the gate would
                // answer Unchanged for every poll afterwards, and the player would sit looking at
                // the previous line's Arabic until they advanced the dialogue again.
                //
                // The old code could afford the wrong order because a typewriter reveal produced
                // four or five candidate frames and losing one cost nothing. The gate exists to
                // make that exactly one, which is precisely what makes dropping it unrecoverable.
                //
                // _busy is also held by the manual hotkey and by Settings' own buttons, none of
                // which are disabled while auto-watch runs - so this is an ordinary collision, not
                // a rare one. It counts as activity either way: something is being translated.
                if (_busy)
                {
                    lastChange = Stopwatch.GetTimestamp();
                    continue;
                }

                // The cheap gate. Most frames during dialogue are identical to the one already
                // translated and must never reach OCR - and a frame that HAS changed is not
                // translated until it stops changing, so a line that types itself out on screen
                // costs one request rather than one per revealed chunk.
                // Retuned every poll from what the content has turned out to be. Cheap - it is a
                // record assignment - and it is the whole of the adaptation: a dialogue box that
                // advances every eight seconds gets a patient deadline, and the same code watching
                // subtitles that change every three gets an impatient one, without anybody saying
                // which is which.
                _settle.Retune(watch.Settle());

                var signature = FrameSignature.Compute(frame);
                var verdict = _settle.Offer(signature);

                if (verdict == FrameVerdict.Unchanged)
                {
                    // The commonest poll there is, and the one that says most: the line on the
                    // overlay is still the line on screen. Somebody is being waited on.
                    Adapt(watch, new RhythmSample(Changed: false));
                    continue;
                }

                // Anything moving counts as activity, including a reveal still in progress -
                // otherwise the AFK expiry could fire in the middle of a sentence appearing.
                lastChange = Stopwatch.GetTimestamp();

                if (verdict == FrameVerdict.Settling)
                {
                    // A frame mid-change that was never read. Genuinely no evidence either way, and
                    // recorded as exactly that rather than allowed to vote with silence.
                    Adapt(watch, new RhythmSample(Changed: true));
                    continue;
                }

                _busy = true;
                Read read;
                try
                {
                    read = ProcessAsync(frame, Trigger.Poll, ct).GetAwaiter().GetResult();
                }
                finally
                {
                    _busy = false;
                }

                Adapt(watch, new RhythmSample(Changed: true, read.HasText, read.TextChanged));
                _consecutiveFailures = 0;
            }
            catch (OperationCanceledException)
            {
                break;   // shutdown, or the toggle was switched off mid-poll
            }
            catch (Exception e)
            {
                // Per POLL, not per run. One frame that OCR chokes on, one transient database
                // hiccup, one provider throwing something unexpected - none of those is a reason to
                // end a session the user is in the middle of. Only a run of them is.
                if (++_consecutiveFailures < FailuresBeforeGivingUp)
                {
                    Report(string.Format(Text.AutoWatchSkippedFrame, e.Message));
                    continue;
                }

                StopAutoWatch(string.Format(Text.AutoWatchStopped, e.Message), onOverlay: true);
                return;
            }
        }
    }

    /// <summary>What one poll learned about the content, for <see cref="ContentRhythm"/>.</summary>
    private readonly record struct Read(bool? HasText, bool? TextChanged);

    /// <summary>
    /// Feeds the detector and, in <see cref="WatchMode.Auto"/>, swaps the pacing when it changes
    /// its mind. A no-op in the two fixed modes — the detector still runs there, because what it
    /// has worked out is shown in Diagnostics either way and an estimate nobody can see is
    /// indistinguishable from a bug.
    /// </summary>
    private void Adapt(WatchSession watch, RhythmSample sample)
    {
        _rhythm.Observe(sample);
        if (_settings.WatchMode != WatchMode.Auto) return;

        var wanted = _rhythm.Resolved;
        if (wanted == _running) return;

        _running = wanted;

        var pacing = WatchPacing.For(wanted);
        if (_settings.SecondsBetweenTranslations > 0)
            pacing = pacing with { MinimumInterval = TimeSpan.FromSeconds(_settings.SecondsBetweenTranslations) };

        watch.Adapt(pacing);

        // Said out loud, once per change. An automatic mode that switches silently is one nobody
        // can trust or debug: the first question when the pacing feels wrong is which of the two
        // it currently thinks it is in.
        Report(string.Format(Text.WatchModeDetected, Text.WatchModeName(wanted)));
    }

    /// <summary>
    /// Which of the two fixed modes Auto is currently running as. Meaningless outside Auto, where
    /// the mode is whatever the user picked.
    /// </summary>
    private WatchMode _running = WatchMode.Dialogue;

    /// <summary>
    /// <paramref name="onOverlay"/> for anything the user did not ask for. Switching it off by
    /// hotkey needs no announcement — they just pressed the key — but stopping by itself, hitting a
    /// cap, or giving up after a run of errors must reach the screen the player is actually
    /// looking at. Every one of these used to go to the Settings status line alone.
    /// </summary>
    private void StopAutoWatch(string message, bool onOverlay = false)
    {
        _autoWatch?.Cancel();
        _autoWatch?.Dispose();
        _autoWatch = null;
        _watch = null;
        _overlay.Notice = null;

        Report(message);
        if (onOverlay) _overlay.ShowMessage(message);
    }

    /// <summary>
    /// What asked for this translation. It decides three things that genuinely differ between them
    /// — whether a repeated line is suppressed, whether the conversation context applies, and what
    /// "nothing here" means — and a single boolean could only carry one of them.
    /// </summary>
    private enum Trigger
    {
        /// <summary>A hotkey press, or the toolbar. A question, and it always gets an answer.</summary>
        Hotkey,

        /// <summary>One tick of auto-watch. One of dozens a minute, and mostly silent.</summary>
        Poll,

        /// <summary>A box the user dragged around something else on the screen.</summary>
        Snip,
    }

    /// <summary>
    /// The one place a captured frame turns into something on the overlay.
    ///
    /// <para>
    /// <paramref name="trigger"/> changes what "nothing here" means, and that difference is a
    /// player report rather than a nicety. A hotkey press is a question and deserves an answer,
    /// even a disappointing one. A poll is not — the gap between two subtitles is an empty region
    /// by definition, so answering it with «لا نصّ في منطقة الالتقاط. هل يظهر صندوق حوار على الشاشة
    /// فعلاً؟» flashed an error, asking about a dialogue box, over a film, between every line. A
    /// snip is a question again: the user just dragged a box around something, and silence there
    /// reads as the feature being broken.
    /// </para>
    /// </summary>
    private async Task<Read> ProcessAsync(Frame frame, Trigger trigger, CancellationToken ct)
    {
        SaveFrameIfRequested(frame);
        _services.Pipeline.Register = _settings.Register;
        _services.Pipeline.Diacritics = _settings.Diacritics;

        // In the pipeline rather than checked here afterwards: it used to be an after-the-fact
        // guard, which meant the "too short to translate" line had already been translated, paid
        // for, and cached by the time it was discarded.
        _services.Pipeline.MinimumBodyCharacters = _settings.MinimumCharactersToTranslate;

        var (options, regionKey) = trigger switch
        {
            Trigger.Poll => (ProcessOptions.Polled, _settings.LastRegionProfile),

            // Its own region key, so the history and the log can tell a snip from the dialogue box
            // it was taken beside. The cache needs no change for this: the key hashes the body and
            // the register only, so a snip and a dialogue line reading the same English share one
            // row - which is right, and is why a snip is often free.
            Trigger.Snip => (ProcessOptions.Isolated, SnipRegionKey),

            _ => (ProcessOptions.Manual, _settings.LastRegionProfile),
        };

        var outcome = await _services.Pipeline
            .ProcessAsync(frame, regionKey, SourceKind.Screen, options, ct)
            .ConfigureAwait(false);

        // Only frames that held text: the confidence of an empty read is 0 by construction and
        // says nothing about the region, and a snip is a different rectangle - folding either in
        // would have the health check diagnosing the wrong thing.
        if (trigger != Trigger.Snip && outcome.Body.Trim().Length > 0)
            LastOcrConfidence = outcome.OcrConfidence;

        // The line already on the overlay, read again with a comma turned into a full stop. Leave
        // everything exactly as it is: no clear, no error, no empty-read count. Reported to the
        // status line only, because that is where somebody diagnosing quota use is looking and it
        // is the one place the saving is visible.
        if (outcome.Repeat)
        {
            Report(Text.SkippedRepeat);

            // The single most informative outcome the detector gets: the picture moved enough to
            // reach OCR and the WORDS came back the same. That is a dialogue box over an animated
            // scene - the case a pixel comparison calls video every time.
            return new Read(HasText: true, TextChanged: false);
        }

        // Null result: nothing was attempted - an empty dialogue box, or a stray glyph or UI
        // border that OCR'd to a character or two, which is not dialogue.
        if (outcome.Result is not { } result)
        {
            var nothingAtAll = outcome.Body.Trim().Length == 0;

            if (trigger != Trigger.Poll)
            {
                Fail(nothingAtAll
                    ? Text.NoTextInRegion
                    : string.Format(Text.TooShortToTranslate, outcome.Body.Trim()));
                return new Read(!nothingAtAll, null);
            }

            // Clear, do not complain. Silence is the correct answer to a poll that found nothing,
            // and leaving the previous line up would caption one subtitle with the one before it.
            _overlay.Clear();

            if (!nothingAtAll)
            {
                _consecutiveEmpty = 0;
                return new Read(HasText: true, TextChanged: null);
            }

            // Unless it has been nothing for a long time. Then the region is the story, and this is
            // the answer to «ممكن ما يشوفش الكلام لازم اعيد تحديد المكان» - said once, naming the
            // hotkey, instead of once per poll or not at all.
            if (++_consecutiveEmpty == EmptyReadsBeforeBlamingTheRegion)
            {
                var advice = string.Format(Text.RegionSeemsWrong,
                    _settings.HotkeyFor(HotkeyAction.PickRegion).ToString());

                Report(advice);
                _overlay.ShowMessage(advice);
            }

            // Nothing in the rectangle at all. Captions live in gaps; a dialogue box holds its
            // text until the player advances, so this is the strong signal for moving text.
            return new Read(HasText: false, TextChanged: null);
        }

        // A snip found nothing about the watched region, in either direction: it must not clear the
        // empty-read count that is diagnosing that region, and it must not enter the cadence the
        // adaptive settle deadline is derived from. It does spend a request, so the session cap has
        // to see it.
        if (trigger == Trigger.Snip)
        {
            _watch?.CountedOutsideTheRhythm();
        }
        else
        {
            _consecutiveEmpty = 0;
            if (trigger == Trigger.Poll) _watch?.Translated();
            else _watch?.CountedOutsideTheRhythm();
        }

        // Captured before it is overwritten: whether this line differs from the last one shown is
        // what tells the detector a line was replaced rather than merely re-read.
        var previous = _lastSourceText;

        _lastSourceText = outcome.Body;
        _lastArabic = result.Text;

        if (result.IsFallbackEnglish)
            _overlay.ShowFallbackEnglish(outcome.Speaker, result.Text);
        else
            _overlay.ShowTranslation(outcome.Speaker, result.Text);

        var source = result.FromCache ? "cache" : $"{result.Provider}/{result.Model}";
        Report($"{source} · {outcome.Total.TotalMilliseconds:F0} ms · OCR confidence {outcome.OcrConfidence:F0}");

        return new Read(HasText: true, TextChanged: !string.Equals(previous, outcome.Body, StringComparison.Ordinal));
    }

    /// <summary>
    /// Filed under its own name in the log and the history. A free string as far as the store is
    /// concerned, so this needed no schema change.
    /// </summary>
    public const string SnipRegionKey = "snip";

    /// <summary>
    /// One translation of one rectangle the user dragged, and then back to whatever was happening.
    ///
    /// <para>
    /// Deliberately does not go near any of the state auto-watch depends on, and the list is longer
    /// than it looks. It never offers its frame to <see cref="FrameSettleGate"/> — calling a frame
    /// Ready is not an opinion, it records that frame as the one now on the overlay, so a snip
    /// offered to the gate would overwrite the watched region's signature and every later poll of
    /// the real dialogue box would answer Unchanged forever. It does not touch the empty-read
    /// counter, which is diagnosing a different rectangle. It does not repoint
    /// <c>LastRegionProfile</c>, so the next hotkey press still reads the dialogue box. It does not
    /// enter the cadence median. And it does not read or write the conversation context.
    /// </para>
    ///
    /// <para>
    /// It also ignores <c>_busy</c> rather than being swallowed by it, which is what used to happen
    /// to anything arriving mid-translation. Dropping a poll costs nothing — another one is half a
    /// second away — but the user dragged this box by hand, and a hand-dragged box that produces
    /// nothing at all is indistinguishable from a broken feature.
    /// </para>
    /// </summary>
    public async Task SnipAsync(CaptureRegion region, CancellationToken ct = default)
    {
        try
        {
            _overlay.ShowLoading();

            var frame = await _frames.GetFrameAsync(region, ct).ConfigureAwait(false);
            if (frame is null)
            {
                Fail(Text.NothingCaptured);
                return;
            }

            _ = await ProcessAsync(frame, Trigger.Snip, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _overlay.Clear();
        }
        catch (Exception e)
        {
            Fail(string.Format(Text.TranslationFailed, e.Message));
        }
        finally
        {
            // The watched region's line has been pushed off the overlay by the snip, and the gate
            // still believes it is displayed - so without this the player would be left looking at
            // a menu tooltip until the dialogue advanced. Forgetting costs one cache lookup: the
            // line was translated in this session, so it is already stored, and a hit is free.
            if (_autoWatch is not null)
            {
                _settle.Reset();
                _services.Pipeline.ResetRepeatGuard();
            }
        }
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
        //
        // A SET rather than one slot, because one slot only suppresses an immediate repeat: a size
        // alternating A, B, A, B misses every time and warns on every poll, which is precisely what
        // a window switching between two states produces. Remembering every layout already
        // mentioned makes "once per layout" mean what it says.
        if (!profile.MatchesLayout(client.Width, client.Height, window.Scaling)
            && _layoutWarnedFor.Add(LayoutKey(profile, client)))
        {
            Report(Text.RegionLayoutChanged);
        }

        var relative = profile.Resolve(client.Width, client.Height);
        var region = relative.Translate(client.X, client.Y);

        // The display layout can change under a stored region - a monitor unplugged, the game moved
        // to a smaller screen. Capturing the overhang would BitBlt undefined pixels into OCR, which
        // surfaces as garbage text and reads as the model getting worse.
        var desktop = PlatformServices.VirtualDesktop();
        if (desktop.IsEmpty || desktop.Contains(region)) return Announce(region);

        var trimmed = region.ClampTo(desktop);
        if (trimmed.IsEmpty)
        {
            Fail(Text.RegionOffScreenTrimmed);
            return null;
        }

        if (_trimmedWarnedFor.Add(LayoutKey(profile, client)))
        {
            Report(Text.RegionOffScreenTrimmed);
        }

        return Announce(trimmed);
    }

    /// <summary>
    /// Publishes the rectangle that is about to be captured, so the visible frame can outline it.
    ///
    /// <para>
    /// Only on a change. Auto-watch resolves a region twice a second and the frame's response is to
    /// move and resize a window, which at that rate would fight the compositor for no benefit —
    /// the same reason <see cref="GameWindowLocated"/> is raised on a change rather than per tick.
    /// </para>
    /// </summary>
    private CaptureRegion Announce(CaptureRegion region)
    {
        if (_lastResolved == region) return region;

        _lastResolved = region;
        RegionResolved?.Invoke(region);
        return region;
    }

    private CaptureRegion? _lastResolved;

    /// <summary>Raised, on whichever thread resolved it, when the captured rectangle changes.</summary>
    public event Action<CaptureRegion>? RegionResolved;

    /// <summary>
    /// Where the app would capture from right now, or null with the reason already reported. Used
    /// by the capture frame, which has to be able to draw itself before anything has been
    /// translated - otherwise switching it on does nothing until the next dialogue line.
    /// </summary>
    public Task<CaptureRegion?> CurrentRegionAsync(CancellationToken ct = default) =>
        ResolveRegionAsync(ct);

    /// <summary>Identifies one region drawn against one window size, for once-only warnings.</summary>
    private string LayoutKey(RegionProfile profile, CaptureRegion client) =>
        $"{_services.Profile.Id}/{profile.Name}/{client.Width}x{client.Height}";

    private readonly HashSet<string> _layoutWarnedFor = [];
    private readonly HashSet<string> _trimmedWarnedFor = [];

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

        // Read live, not captured once: the language can be switched while the app is running and
        // these messages already follow it, so their direction has to as well.
        _overlay.ShowError(message, Text.IsRightToLeft);
    }

    public void Dispose()
    {
        _autoWatch?.Cancel();
        _autoWatch?.Dispose();
        _frames.Dispose();
    }
}
