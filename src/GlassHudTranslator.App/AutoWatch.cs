using System.Diagnostics;
using GlassHudTranslator.Core.Diagnostics;
using GlassHudTranslator.App.Views;
using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Config;

namespace GlassHudTranslator.App;

/// <summary>
/// The poll loop: capture, decide whether it is worth reading, translate, repeat — plus everything
/// that decides when to stop.
///
/// <para>
/// Split out of <see cref="TranslationSession"/> because it is the one genuinely separate job in
/// there. Everything else the session does is "translate this thing now" in four flavours — a
/// hotkey, a snip, a retry, an edited line — and they share the same six lines of plumbing. This is
/// a machine that runs on its own thread for minutes at a time, owns four pieces of state nothing
/// else touches, and answers a question none of the others ask: <i>should I be doing anything at
/// all right now.</i>
/// </para>
///
/// <para>
/// <b>It holds the session rather than an interface, deliberately.</b> There is one implementation
/// and there will only ever be one; an interface here would be a name for a thing that already has
/// a name, and this codebase has enough of those to look after. What the seam buys is not
/// substitutability, it is that the four pieces of state below cannot be reached from the manual
/// paths by accident — which is exactly the class of bug the snip rules exist to prevent.
/// </para>
///
/// <para>
/// The accounting is the one thing that deliberately crosses back. A manual press during a run
/// still costs a request, so it still moves the session cap — but it is not part of the content's
/// rhythm, so it must not move the cadence. <see cref="Translated"/> and
/// <see cref="CountedOutsideTheRhythm"/> are how the session says which happened, and they no-op
/// when nothing is running.
/// </para>
/// </summary>
internal sealed class AutoWatch(TranslationSession session, AppSettings settings, OverlayWindow overlay)
{
    private readonly TranslationSession _session = session;
    private readonly AppSettings _settings = settings;
    private readonly OverlayWindow _overlay = overlay;

    private UiText Text => UiText.For(_settings.Language);

    private CancellationTokenSource? _cancel;

    /// <summary>
    /// Decides which polled frames are worth translating. Owns the "has this changed" question that
    /// used to be a bare signature comparison — which answered yes on every frame of a typewriter
    /// reveal and so translated one sentence four times over.
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
    /// Consecutive polls that threw. Per POLL rather than per run: one frame OCR chokes on is not a
    /// reason to end a session somebody is in the middle of. Only a run of them is.
    /// </summary>
    private int _consecutiveFailures;

    /// <summary>
    /// Polls in a row where the screen grab came back with nothing. Distinct from a poll that
    /// threw: this one is silent, which is what makes it worth counting.
    /// </summary>
    private int _consecutiveEmptyCaptures;

    /// <summary>Ten polls is about five seconds - long enough not to fire on a transient.</summary>
    private const int CaptureFailuresBeforeSayingSo = 10;

    private const int FailuresBeforeGivingUp = 5;

    public bool IsRunning => _cancel is not null;

    /// <summary>What the run has spent and how fast the content moves, for Diagnostics.</summary>
    public (TimeSpan? Cadence, int Requests, TimeSpan Elapsed, bool Outrunning)? Stats =>
        _watch is null ? null : (_watch.Cadence, _watch.Requests, _watch.Elapsed, _watch.OutrunningTheFloor);

    /// <summary>
    /// Raised on the poll thread when <see cref="WatchMode.Auto"/> settles on one of the two fixed
    /// modes. The subscriber is responsible for getting itself onto the UI thread.
    /// </summary>
    public event Action<WatchMode>? ModeResolved;

    /// <summary>What the detector has concluded, and which pacing that resolves to.</summary>
    public (ContentKind Kind, WatchMode Running)? Verdict =>
        _watch is null ? null : (_rhythm.Kind, _settings.WatchMode == WatchMode.Auto ? _running : _settings.WatchMode);

    /// <summary>A translation that IS part of the watched content: moves the cap and the cadence.</summary>
    public void Translated() => _watch?.Translated();

    /// <summary>
    /// A translation that happened during a run but is not the content — a hotkey press, a snip, a
    /// retry. Counted against the cap and against nothing else: folding it into the cadence would
    /// tell the adaptive deadline the dialogue just advanced when it did not, and one such sample
    /// drags the median for the next eight lines.
    /// </summary>
    public void CountedOutsideTheRhythm() => _watch?.CountedOutsideTheRhythm();

    /// <summary>
    /// Forgets the watched region after a snip, so the next poll re-translates the main line.
    /// Looks like waste and is not: that line was translated this session, so it is already cached
    /// and a hit is free. Without it the player sits looking at a menu tooltip until the dialogue
    /// advances.
    /// </summary>
    public void ForgetWhatIsOnScreen() => _settle.Reset();

    public void Toggle()
    {
        if (_cancel is not null)
        {
            Stop(Text.AutoWatchOff);
            return;
        }

        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;

        // Otherwise switching auto-watch off and straight back on sits on Unchanged until the
        // player advances the dialogue, which reads as the toggle having done nothing. The repeat
        // guard needs the same treatment for the same reason and one layer up: the gate would let
        // the frame through and the pipeline would then drop it as a line it has already shown.
        // Deliberately NOT ResetContext - the three lines the player was just reading are still the
        // scene they are in, and throwing them away costs a worse translation for nothing.
        _settle.Reset();
        _session.ResetRepeatGuard();
        _rhythm.Reset();
        _consecutiveFailures = 0;
        _consecutiveEmptyCaptures = 0;
        _session.ResetEmptyRun();
        PollTrace.Clear();
        _overlay.Notice = null;

        // The mode is read here rather than per tick, so one run has one set of caps and one
        // cadence estimate. Switching mode mid-run would silently reset the clock the caps are
        // measured against, which is the one thing a cap must not allow.
        // Auto resolves to Dialogue until the detector has seen enough, which is the cheap mistake
        // to make while deciding: patience on a film costs a few late lines, impatience on a
        // typewriter reveal costs a request per half-written sentence.
        var pacing = PacingFor(_settings.WatchMode);

        // Every number the run will use, said once. The pacing is what the two reported symptoms
        // were really about - a mode that "does nothing" and a mode that is "too slow" are both
        // claims about these four values, and until now the trace showed only two of them.
        PollTrace.Write($"START mode={_settings.WatchMode} "
            + $"poll={_settings.PollIntervalFor(pacing).TotalMilliseconds:F0}ms "
            + $"settle={pacing.SettleCap.TotalMilliseconds:F0}ms "
            + $"floor={pacing.MinimumInterval.TotalMilliseconds:F0}ms "
            + $"reads={pacing.ReadsBeforeGivingUp}");

        _watch = new WatchSession(pacing) { Unbounded = _settings.WatchWithoutLimit };
        _watch.Start();
        _settle.Retune(_watch.Settle());

        var worker = new Thread(() => Loop(token))
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

        _session.Report(announcement);
        _overlay.ShowMessage(announcement);
    }

    private void Loop(CancellationToken ct)
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
                Thread.Sleep(_settings.PollIntervalFor(watch.Pacing));

                if (ct.IsCancellationRequested) break;

                // The session cap, measured from switch-on. The idle expiry below cannot do this
                // job: it resets on any movement, so over a playing video - or a game with
                // animation anywhere in the capture region - it never fires at all. A guard that a
                // moving screen switches off is not a guard.
                switch (watch.Check())
                {
                    case WatchVerdict.Stop:
                        Stop(string.Format(Text.AutoWatchReachedLimit,
                            watch.Elapsed.TotalMinutes.ToString("0"), watch.Requests), onOverlay: true);
                        return;

                    case WatchVerdict.Warn:
                        // Sticky, because it has to survive the translations that keep arriving
                        // after it. This is the line the whole cap exists to deliver.
                        _overlay.Notice = string.Format(Text.AutoWatchStillRunning,
                            watch.Elapsed.TotalMinutes.ToString("0"), watch.Requests);
                        _session.Report(_overlay.Notice);
                        break;
                }

                // The AFK expiry stays, unchanged, for what it was always good at: a toggle left on
                // in front of a genuinely static screen.
                if (Stopwatch.GetElapsedTime(lastChange) > expiry)
                {
                    Stop(string.Format(Text.AutoWatchExpired, expiry.TotalSeconds.ToString("0")),
                        onOverlay: true);
                    return;
                }

                // The floor, asked before the frame reaches the gate for the same reason the busy
                // check is: calling a frame Ready commits it, so a frame refused afterwards would
                // be remembered as handled and the screen would sit unchanged for good.
                if (!watch.MayTranslate())
                {
                    PollTrace.Write("floor      too soon since the last translation");
                    continue;
                }

                var region = _session.ResolveRegionAsync(ct).GetAwaiter().GetResult();
                if (region is null)
                {
                    PollTrace.Write("no region  resolve returned nothing - see the status line");
                    continue;
                }

                var frame = _session.CaptureAsync(region.Value, ct).GetAwaiter().GetResult();
                if (frame is null)
                {
                    PollTrace.Write($"no frame   capture of {region.Value} returned nothing"
                        + (_session.LastCaptureFailure is { } why ? $" - {why}" : ""));

                    // Said OUT LOUD after a run of them, on the overlay, because a capture that
                    // returns nothing produces no error, no exception and no log line - the app
                    // simply goes quiet, which from the outside is indistinguishable from it having
                    // decided there was nothing to translate. That is the exact shape of failure
                    // this project has now shipped twice, and the fix is the same both times: if
                    // the app knows something is wrong, it has to say so somewhere the player is
                    // actually looking.
                    if (++_consecutiveEmptyCaptures == CaptureFailuresBeforeSayingSo)
                        _overlay.Notice = Text.CaptureFailing;

                    continue;
                }

                _consecutiveEmptyCaptures = 0;

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
                if (_session.Busy)
                {
                    PollTrace.Write("busy       something else is mid-translation, poll dropped");
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

                PollTrace.Write($"gate       {verdict} scene-moves={_settle.SceneMovement} "
                    + $"{_rhythm.Explain()}");

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

                // Two ways to arrive here and they cost very different things.
                //
                // Ready is the free path: the scene is quiet, two polls were identical, and the
                // frame is the line. Translate it, exactly as before.
                //
                // Read is the gate saying the pixels cannot decide - this scene never holds still
                // enough for a 1536-cell thumbnail to prove anything, or the deadline has expired.
                // The frame is OCR'd and the WORDS decide, inside the pipeline and before the first
                // metered call. Most of those readings cost one local Tesseract pass and stop
                // there, which is the difference between spending 100 ms on a frame that turned out
                // to be scenery and spending several seconds - during all of which this thread is
                // blocked and nothing is watching the screen.
                var confirmed = true;

                var read = verdict == FrameVerdict.Read
                    ? _session.PollAsync(frame, ct, (body, illegible) =>
                    {
                        var answer = _settle.Confirm(body, illegible);
                        confirmed = answer == ReadVerdict.Translate;

                        PollTrace.Write($"  confirm  {answer} body='{Short(body)}' illegible={illegible}");
                        return confirmed;
                    }).GetAwaiter().GetResult()
                    : _session.PollAsync(frame, ct).GetAwaiter().GetResult();

                PollTrace.Write($"read       text={read.HasText} changed={read.TextChanged} "
                    + $"confirmed={confirmed}");

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
                PollTrace.Write($"THREW      {e.GetType().Name}: {e.Message}");

                if (++_consecutiveFailures < FailuresBeforeGivingUp)
                {
                    _session.Report(string.Format(Text.AutoWatchSkippedFrame, e.Message));
                    continue;
                }

                Stop(string.Format(Text.AutoWatchStopped, e.Message), onOverlay: true);
                return;
            }
        }
    }

    /// <summary>
    /// Feeds the detector and, in <see cref="WatchMode.Auto"/>, swaps the pacing when it changes
    /// its mind. A no-op in the two fixed modes — the detector still runs there, because what it
    /// has worked out is shown in Diagnostics either way and an estimate nobody can see is
    /// indistinguishable from a bug.
    /// </summary>
    /// <summary>The mode's timings, with the user's floor applied if they have set one.</summary>
    private WatchPacing PacingFor(WatchMode mode)
    {
        var pacing = WatchPacing.For(mode);

        return _settings.SecondsBetweenTranslations > 0
            ? pacing with { MinimumInterval = TimeSpan.FromSeconds(_settings.SecondsBetweenTranslations) }
            : pacing;
    }

    /// <summary>
    /// Applies a mode chosen while a run is in progress.
    ///
    /// <para>
    /// <b>Adapts rather than restarts, and that distinction is the whole method.</b> The obvious
    /// implementation — stop and start — would reset the clock and the request count the session
    /// caps are measured against, so a user flipping between modes would hold the app open past
    /// every limit it has, which is the one thing a cap must not allow. <c>WatchSession.Adapt</c>
    /// exists precisely because Auto needs to swap timings without touching that accounting, and a
    /// mode chosen by hand is the same operation chosen by a person.
    /// </para>
    ///
    /// <para>
    /// Without this the change silently did nothing until auto-watch was toggled off and on, since
    /// the pacing is read once at the start of a run — which is indistinguishable, from outside,
    /// from the mode switch being broken.
    /// </para>
    /// </summary>
    public void ModeChanged()
    {
        if (_watch is null) return;

        _running = _settings.WatchMode == WatchMode.Auto ? _rhythm.Resolved : _settings.WatchMode;

        _watch.Adapt(PacingFor(_running));
        _settle.Retune(_watch.Settle());

        // <b>Forget what is on screen, exactly as switching auto-watch off and on again does.</b>
        //
        // Adapting the timings was necessary and was not sufficient, and the difference is what the
        // report "switching still forces me to turn auto translate off and on" was actually about.
        // The new pacing only takes effect on the next CHANGE, and the line the player is looking at
        // while they reach for the mode button is not a change: the gate still holds it as the frame
        // on the overlay, so it answers Unchanged, and the switch appears to do nothing until the
        // content moves on by itself. Toggling the feature off and on was the only way to say "look
        // again", which is the workaround being described.
        //
        // Everything the session CAP is measured against — elapsed time, request count — is
        // deliberately untouched. That is the whole reason this is not simply a stop and a start:
        // restarting resets the clock, so flipping modes would hold the app open past every limit it
        // has, which is the one thing a cap must not allow.
        _settle.Reset();
        _session.ResetRepeatGuard();
        _session.ResetEmptyRun();
        _consecutiveEmptyCaptures = 0;

        var now = PacingFor(_running);
        PollTrace.Write($"mode       changed to {_settings.WatchMode} mid-run, now running {_running} - "
            + $"poll={_settings.PollIntervalFor(now).TotalMilliseconds:F0}ms "
            + $"settle={_watch.Settle().Cap.TotalMilliseconds:F0}ms "
            + $"floor={now.MinimumInterval.TotalMilliseconds:F0}ms");
    }

    /// <summary>Enough of a line to recognise it in a trace, and never enough to fill one.</summary>
    private static string Short(string? text) =>
        text is null ? "" : text.Length <= 48 ? text : text[..48] + "…";

    private void Adapt(WatchSession watch, RhythmSample sample)
    {
        _rhythm.Observe(sample);
        if (_settings.WatchMode != WatchMode.Auto) return;

        var wanted = _rhythm.Resolved;
        if (wanted == _running) return;

        _running = wanted;

        watch.Adapt(PacingFor(wanted));

        // Said out loud, once per change, and ON THE OVERLAY as well as in Settings.
        //
        // It went only to the Settings status line, which is the mistake this project has now made
        // four separate times and written down twice: anything auto-watch decides on its own has to
        // reach the screen the player is actually looking at, and somebody inside a fullscreen game
        // never sees Settings. Reported as "the auto mode does not tell you which mode is on" -
        // which was exactly true, and an automatic mode nobody can see the state of is one nobody
        // can trust.
        var announcement = string.Format(Text.WatchModeDetected, Text.WatchModeName(wanted));

        _session.Report(announcement);
        _overlay.ShowMessage(announcement);

        // And the toolbar button, so the state is legible after the message has gone rather than
        // only at the instant it changes.
        ModeResolved?.Invoke(wanted);
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
    public void Stop(string message, bool onOverlay = false)
    {
        _cancel?.Cancel();
        _cancel?.Dispose();
        _cancel = null;
        _watch = null;
        _overlay.Notice = null;

        _session.Report(message);
        if (onOverlay) _overlay.ShowMessage(message);
    }

    /// <summary>
    /// Cancels a run in progress without announcing anything: the app is closing, and a message on
    /// an overlay that is about to disappear reaches nobody.
    /// </summary>
    public void Dispose()
    {
        _cancel?.Cancel();
        _cancel?.Dispose();
        _cancel = null;
    }
}
