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

    /// <summary>
    /// Consecutive polls that read nothing at all. A region that has stopped matching the game —
    /// the window resized, the HUD layout changed, the wrong rectangle was saved — looks exactly
    /// like a quiet moment, and the app used to say "no text in the capture region" once per poll
    /// and leave the user to guess. After enough of them in a row it says the useful thing instead.
    /// </summary>
    private int _consecutiveEmpty;

    /// <summary>How many empty reads in a row before the region itself becomes the suspect.</summary>
    private const int EmptyReadsBeforeBlamingTheRegion = 12;

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

        // Built here rather than injected: it is not a policy anyone chooses between, it is the
        // other half of this object.
        _auto = new AutoWatch(this, settings, overlay);
    }

    /// <summary>The poll loop, and everything that decides when it should stop.</summary>
    private readonly AutoWatch _auto;

    public bool IsAutoWatching => _auto.IsRunning;

    /// <summary>Switches the poll loop on or off. The loop itself lives in <see cref="AutoWatch"/>.</summary>
    public void ToggleAutoWatch() => _auto.Toggle();

    /// <summary>The watch mode was changed while a run may be in progress.</summary>
    public void WatchModeChanged() => _auto.ModeChanged();

    /// <summary>
    /// Auto has settled on one of the two fixed modes. Raised on the poll thread — subscribers hop
    /// to the UI thread themselves, the same as <see cref="Status"/>.
    /// </summary>
    public event Action<WatchMode>? ContentModeResolved
    {
        add => _auto.ModeResolved += value;
        remove => _auto.ModeResolved -= value;
    }

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
    public (TimeSpan? Cadence, int Requests, TimeSpan Elapsed, bool Outrunning)? WatchStats => _auto.Stats;

    public (ContentKind Kind, WatchMode Running)? ContentVerdict => _auto.Verdict;

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
                FailedToCapture();
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
    /// <summary>What one poll learned about the content, for <see cref="ContentRhythm"/>.</summary>
    internal readonly record struct Read(bool? HasText, bool? TextChanged);

    /// <summary>
    /// The surface <see cref="AutoWatch"/> drives this session through. Deliberately small, and
    /// deliberately not an interface: there is one caller and one implementation, so an interface
    /// would name a thing that already has a name. What it buys is that the loop cannot reach the
    /// session's own state except through these — which is the class of accident the snip rules
    /// exist to prevent.
    /// </summary>
    internal bool Busy => _busy;

    internal void Report(string message) => Status?.Invoke(message);

    internal void ResetRepeatGuard() => _services.Pipeline.ResetRepeatGuard();

    /// <summary>
    /// Forgets the run of empty reads. A new run starts the region-is-wrong diagnosis over, which
    /// is right: the last run's silence says nothing about this one.
    /// </summary>
    internal void ResetEmptyRun() => _consecutiveEmpty = 0;

    /// <summary>
    /// The answer to «ممكن ما يشوفش الكلام لازم اعيد تحديد المكان» — said once, naming the hotkey,
    /// instead of once per poll or not at all.
    ///
    /// <para>
    /// Called from both empty-read paths, which is the whole reason it is a method. A frame the
    /// settle gate is still deciding about and a frame that finished as nothing are different facts
    /// everywhere else, and the same fact here: the rectangle held no text either way, and a
    /// rectangle pointed at the wrong part of the screen only ever produces the first kind.
    /// </para>
    /// </summary>
    private void BlameTheRegionIfThisKeepsHappening()
    {
        if (++_consecutiveEmpty != EmptyReadsBeforeBlamingTheRegion) return;

        var advice = string.Format(Text.RegionSeemsWrong,
            _settings.HotkeyFor(HotkeyAction.PickRegion).ToString());

        Report(advice);
        _overlay.ShowMessage(advice);
    }

    /// <summary>
    /// A still of every monitor, for the region picker and the snip, taken with the session's own
    /// frame source — because there is exactly one, and the two callers that used to build their
    /// own broke translation for the rest of the session every time they ran.
    /// </summary>
    internal Frame? CaptureWholeDesktop() => PlatformServices.CaptureFullScreen(_frames);

    /// <summary>Why the last capture came back empty, for anything that has to explain itself.</summary>
    internal string? LastCaptureFailure => _frames.LastFailure;

    /// <summary>
    /// The watched rectangle has no text in it at all, so nothing that was on it is on it any more.
    ///
    /// <para>
    /// <b>Both halves of the forgetting, and the second one is the half that was missing.</b>
    /// Clearing the overlay is obvious — leaving the previous Arabic up captions one line with the
    /// one before it. But the repeat guard still held that line as the reference, so a dialogue box
    /// that closed and reopened on the SAME sentence came back to a cleared overlay and was then
    /// dropped as a repeat of itself: the words plainly on screen, and nothing shown. Reported as
    /// "small problems when a dialogue disappears and comes back".
    /// </para>
    ///
    /// <para>
    /// The gate forgets its side for the same reason and in the same breath (see
    /// <c>FrameSettleGate.Discard</c>). Re-translating a line that has already been seen this
    /// session costs a cache hit, which is free — and being free is what makes correctness the
    /// cheaper option here.
    /// </para>
    /// </summary>
    private void TheRegionWentEmpty()
    {
        _overlay.Clear();
        _services.Pipeline.ResetRepeatGuard();
    }

    /// <summary>
    /// The capture came back with nothing, and this says which nothing.
    ///
    /// <para>
    /// «لم يُلتقط شيء. هل اللعبة شغّالة بوضع النافذة بلا إطار؟» on its own is a GUESS, and it was the
    /// wrong guess in the report that produced this method: the diagnostic on the same screen had
    /// already confirmed the game window was found, borderless and capturable, so the one message
    /// the app could produce was contradicting its own evidence and sending the user to change a
    /// setting that was already correct. Borderless is still the commonest cause and still leads,
    /// but the Win32 reason follows it whenever there is one.
    /// </para>
    ///
    /// <para>
    /// The reason is left in English on purpose, for the same reason every other platform message
    /// is: it names Win32 calls, and translating "GetDIBits read no scan lines" helps nobody.
    /// </para>
    /// </summary>
    private void FailedToCapture() =>
        Fail(_frames.LastFailure is { Length: > 0 } why
            ? $"{Text.NothingCaptured}\n{why}"
            : Text.NothingCaptured);

    internal Task<Frame?> CaptureAsync(CaptureRegion region, CancellationToken ct) =>
        _frames.GetFrameAsync(region, ct);

    /// <summary>
    /// One polled frame, translated and shown. Holds <see cref="Busy"/> for the duration, because
    /// the manual hotkey and Settings' own buttons share it and neither is disabled during a run.
    /// </summary>
    internal async Task<Read> PollAsync(
        Frame frame, CancellationToken ct, BodyConfirmation? confirm = null)
    {
        _busy = true;
        try
        {
            return await ProcessAsync(frame, Trigger.Poll, ct, confirm).ConfigureAwait(false);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<Read> ProcessAsync(
        Frame frame, Trigger trigger, CancellationToken ct, BodyConfirmation? confirm = null)
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

        // Only a poll can carry one, and only the poll loop knows what to do with the answer.
        if (confirm is not null) options = options with { Confirm = confirm };

        var outcome = await _services.Pipeline
            .ProcessAsync(frame, regionKey, SourceKind.Screen, options, ct)
            .ConfigureAwait(false);

        // The gate is still making its mind up about this frame, so nothing here has happened yet:
        // no translation, no overlay change, no empty-read count. Leaving the previous line up is
        // deliberate and is the only honest thing to show - it is still the best available claim
        // about what is on the screen, and clearing it would flash the overlay blank once per
        // reading. The detector does get told, because a frame that reached OCR at all is evidence.
        if (outcome.Unconfirmed)
        {
            var sawWords = outcome.Body.Trim().Length > 0
                || outcome.RejectedWordCount >= Core.Ocr.EscalationPolicy.RejectedWordsThatMeanText;

            if (sawWords)
            {
                _consecutiveEmpty = 0;
                return new Read(HasText: true, TextChanged: null);
            }

            // Nothing in the rectangle at all, and it still has to count. A wrong capture region
            // produces NOTHING BUT unconfirmed empty readings, so letting this path skip the tally
            // would silence «ممكن ما يشوفش الكلام لازم اعيد تحديد المكان» permanently - the advice
            // would be unreachable in precisely the situation it was added for.
            //
            // Clearing is the same reasoning one branch down: the region has gone blank, and
            // leaving the previous Arabic up captions one line with the one before it.
            TheRegionWentEmpty();
            BlameTheRegionIfThisKeepsHappening();

            return new Read(HasText: false, TextChanged: null);
        }

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
            if (nothingAtAll) TheRegionWentEmpty();
            else _overlay.Clear();

            if (!nothingAtAll)
            {
                _consecutiveEmpty = 0;
                return new Read(HasText: true, TextChanged: null);
            }

            // Unless it has been nothing for a long time. Then the region is the story.
            BlameTheRegionIfThisKeepsHappening();

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
            _auto.CountedOutsideTheRhythm();
        }
        else
        {
            _consecutiveEmpty = 0;
            if (trigger == Trigger.Poll) _auto.Translated();
            else _auto.CountedOutsideTheRhythm();
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
    /// Translates the line already on the overlay again, refusing the saved answer.
    ///
    /// <para>
    /// The first thing anyone wants when a translation reads badly, and it was impossible until
    /// now: the line is cached the moment it succeeds, so every later encounter — including any
    /// attempt to ask again — replayed the same words. It costs a request, deliberately, and says
    /// so in the note beside the button.
    /// </para>
    ///
    /// <para>
    /// Uses the BODY of the last line rather than re-capturing. Re-capturing would translate
    /// whatever is on screen NOW, which after a second of play is a different line — so the button
    /// would appear to work while quietly answering a question nobody asked.
    /// </para>
    /// </summary>
    public async Task RetryAsync(CancellationToken ct = default)
    {
        if (_lastSourceText is not { Length: > 0 } body)
        {
            Report(Text.NothingToRetry);
            return;
        }

        await RetranslateAsync(body, fresh: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Translates text the user has typed or corrected, rather than anything on screen.
    ///
    /// <para>
    /// Shared by the retry button and by "fix what was read". Deliberately outside every piece of
    /// state auto-watch depends on, for the reasons the snip documents: it must not become the
    /// repeat reference (the poll that follows is still looking at the original line), must not
    /// touch the settle gate, and must not enter the cadence the adaptive deadline is taken from.
    /// It IS counted against the session cap, because it costs a request like any other.
    /// </para>
    /// </summary>
    public async Task RetranslateAsync(string text, bool fresh = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Report(Text.NothingToEdit);
            return;
        }

        Report(Text.Retrying);

        try
        {
            var outcome = await _services.Pipeline
                .TranslateTextAsync(text, fresh: fresh, regionKey: _settings.LastRegionProfile, ct: ct)
                .ConfigureAwait(false);

            _auto.CountedOutsideTheRhythm();

            if (outcome.Result is not { } result)
            {
                Report(Text.NothingToRetry);
                return;
            }

            // The overlay is updated but the repeat reference deliberately is not: the next poll is
            // reading the same pixels as before and is still a repeat OF the original line.
            _lastArabic = result.Text;

            if (result.IsFallbackEnglish)
                _overlay.ShowFallbackEnglish(null, result.Text);
            else
                _overlay.ShowTranslation(null, result.Text);

            var source = result.FromCache ? "cache" : $"{result.Provider}/{result.Model}";
            Report($"{source} · {outcome.Total.TotalMilliseconds:F0} ms");
        }
        catch (OperationCanceledException)
        {
            // Ordinary: the window closed, or the user pressed it twice.
        }
        catch (Exception e)
        {
            Fail(string.Format(Text.TranslationFailed, e.Message));
        }
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
                FailedToCapture();
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
            if (_auto.IsRunning)
            {
                _auto.ForgetWhatIsOnScreen();
                _services.Pipeline.ResetRepeatGuard();
            }
        }
    }

    /// <summary>
    /// Turns the stored fractional profile into screen pixels against the game's current client
    /// area, which is what makes a saved region survive the window being moved.
    /// </summary>
    internal async Task<CaptureRegion?> ResolveRegionAsync(CancellationToken ct)
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

        // Everything from here is arithmetic over facts, so it lives in Core where it can be
        // tested. What stays here is the gathering above - which window is the game, how big the
        // desktop is - and the deciding below of how loud each complaint should be, which is a
        // question about a person rather than about a rectangle.
        var outcome = RegionResolver.Resolve(profile, client, window.Scaling, PlatformServices.VirtualDesktop());

        foreach (var warning in outcome.Warnings) Mention(warning, profile, client);

        if (outcome.Region is not { } region)
        {
            Fail(Describe(outcome.Failure ?? RegionProblem.EntirelyOffScreen));
            return null;
        }

        return Announce(region);
    }

    /// <summary>
    /// Says a thing once per layout rather than once per frame. Auto-watch resolves a region twice
    /// a second, so a warning repeated 120 times a minute is noise the user learns to ignore.
    ///
    /// <para>
    /// A SET rather than one slot, because one slot only suppresses an IMMEDIATE repeat: a value
    /// alternating A, B, A, B misses on every comparison and warns on every poll, which is exactly
    /// what a window switching between two states produces - and something always alternates.
    /// </para>
    /// </summary>
    private void Mention(RegionProblem warning, RegionProfile profile, CaptureRegion client)
    {
        var said = warning switch
        {
            RegionProblem.LayoutChanged => _layoutWarnedFor,
            _ => _trimmedWarnedFor,
        };

        if (said.Add($"{warning}/{LayoutKey(profile, client)}")) Report(Describe(warning));
    }

    private string Describe(RegionProblem problem) => problem switch
    {
        RegionProblem.LayoutChanged => Text.RegionLayoutChanged,
        _ => Text.RegionOffScreenTrimmed,
    };

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
        _auto.Dispose();
        _frames.Dispose();
    }
}
