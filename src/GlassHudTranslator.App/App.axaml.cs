using GlassHudTranslator.App.Views;
using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Platform;
using GlassHudTranslator.Core.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace GlassHudTranslator.App;

public partial class App : Application
{
    private AppServices? _services;
    private TranslationSession? _session;
    private OverlayWindow? _overlay;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avalonia defaults to shutting down when the LAST window closes. The overlay is a
            // second top-level window that stays open, so closing Settings left the process alive
            // with an orphaned overlay on screen that could not be dismissed.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;

            desktop.MainWindow = Program.HasFlag("--render-test")
                ? new ArabicRenderTestWindow(
                    Program.Option("--render-test-out"), Program.HasFlag("--exit-after-render"))
                : Program.HasFlag("--overlay-test")
                    ? BuildOverlaySnapshot(desktop)
                    : Program.HasFlag("--toolbar-test")
                        ? BuildToolbarSnapshot(desktop)
                        : Program.HasFlag("--failure-test")
                            ? BuildFailureSnapshot(desktop)
                            : Program.HasFlag("--wizard-test")
                                ? BuildWizardSnapshot(desktop)
                                : BuildMainWindow();

            if (Program.HasFlag("--ui-shots") && desktop.MainWindow is SettingsWindow shotTarget)
                CaptureSettingsShots(shotTarget, desktop);

            desktop.ShutdownRequested += async (_, _) =>
            {
                // Every top-level window, not just the overlay. Avalonia shuts down on last-window
                // close, so a floating window left open is a live process with no way back into it
                // - which is exactly the bug the first Windows run found, when the overlay was the
                // only second window there was and closing Settings orphaned it.
                _overlay?.Close();
                _toolbar?.Close();
                _frame?.Close();

                _session?.Dispose();
                if (_services is not null) await _services.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private Avalonia.Controls.Window BuildMainWindow()
    {
        PlatformServices.InitialiseDpiAwareness();

        var settings = AppSettings.Load();

        // Lets the documentation pass render the same window in both languages without the
        // developer's own saved preference leaking into the screenshots.
        if (Program.Option("--ui-shots-lang") is { } shotLanguage)
        {
            settings.Language = shotLanguage.Equals("ar", StringComparison.OrdinalIgnoreCase)
                ? UiLanguage.Arabic
                : UiLanguage.English;
        }
        var overlay = _overlay = new OverlayWindow
        {
            BodyFontSize = settings.OverlayFontSize,
            PanelOpacity = settings.OverlayOpacity,
        };

        try
        {
            // The double-clicked-inside-the-zip case, named before it becomes a mystery. Explorer
            // opens a zip like a folder and runs the exe by extracting IT ALONE to a temp
            // directory - no tessdata, no profiles, no data - which then fails as a cascade of
            // missing-file errors that all describe symptoms. The base directory sitting under the
            // OS temp path is the one signal that is never true of a real install or a dev run,
            // and the fix is one sentence.
            if (AppContext.BaseDirectory.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The app is running from inside the zip. Extract the WHOLE zip to a normal "
                    + "folder first (right-click the zip, Extract All), then run it from there.\n"
                    + "البرنامج يعمل من داخل الملف المضغوط. فكّ ضغط الملف كاملاً إلى مجلد عادي "
                    + "أولاً (كليك يمين ← Extract All)، ثم شغّله من هناك.");
            }

            AppPaths.Ensure();
            _services = AppServices.CreateAsync(
                    Program.Option("--data") ?? RepoPaths.Data,
                    Program.Option("--profiles") ?? RepoPaths.Profiles,
                    Program.Option("--profile") ?? settings.ProfileId,
                    useStubProvider: Program.HasFlag("--stub"))
                .GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            // On a NORMAL window, never the overlay. The overlay is transparent, unfocusable and
            // has no taskbar entry - an error shown there produced the exact support report this
            // replaces: "nothing opens after Run anyway", from a machine where the app was running
            // with its explanation on screen the whole time. The log gets the same story, so the
            // answer survives the window being closed.
            Core.Diagnostics.StartupLog.Fail(e);
            overlay.Close();
            return new StartupFailureWindow(e);
        }

        // Applied once here as well as per frame, because the pipeline is reachable without going
        // through a frame at all: Settings' "Test translation" button calls ProcessAsync directly.
        // Without this it ran on defaults until the first real capture, so someone who had chosen
        // Egyptian, restarted, and pressed Test got Modern Standard back and no reason for it.
        _overlay.HideFromCapture = settings.HideOverlayFromCapture;

        _services.Pipeline.Register = settings.Register;
        _services.Pipeline.Diacritics = settings.Diacritics;
        _services.Pipeline.MinimumBodyCharacters = settings.MinimumCharactersToTranslate;
        _services.Pipeline.Ignored = new Core.Text.IgnoreList(settings.IgnoredPhrases);
        _services.Pipeline.ReadAgainWhenUnreadable = settings.ReadUnreadableLinesAgain;

        _session = new TranslationSession(_services, overlay, settings, RepoPaths.TestFrames)
        {
            SaveFramesDirectory = Program.Option("--save-frames"),
        };

        var settingsWindow = new SettingsWindow(_services, overlay, settings, _session);
        _session.Status += message => Dispatcher.UIThread.Post(() => settingsWindow.ReportStatus(message));

        // Auto changing its mind is a state change the toolbar has to show, and it happens on the
        // poll thread, so it hops like everything else that arrives from there.
        _session.ContentModeResolved += _ => Dispatcher.UIThread.Post(() => RefreshToolbar(settings));

        BindHotkeys(settings, settingsWindow);
        BuildFloatingWindows(settings, settingsWindow);
        BuildTray(settings, settingsWindow);

        settingsWindow.Opened += (_, _) => PositionOverlay(overlay, _session, settings);

        // The wizard, exactly once ever. Not in safe mode (settings are not being saved, so
        // HasCompletedFirstRun could not stick and the wizard would greet every safe start), and
        // not during the screenshot passes, whose machine has long since completed its first run.
        if (!settings.HasCompletedFirstRun && !AppSettings.SafeMode && !Program.HasFlag("--ui-shots"))
        {
            settingsWindow.Opened += (_, _) => Dispatcher.UIThread.Post(async () =>
            {
                var wizard = new FirstRunWizard(_services!, settings);
                await wizard.ShowDialog(settingsWindow);

                // The wizard may have changed the interface language and the active profile;
                // the window underneath was built before either choice existed.
                settingsWindow.ReloadLanguage();
                RefreshToolbar(settings);

                // Straight into the picker - with its proposals - while the decision is warm.
                if (wizard.DrawRegionRequested)
                    await settingsWindow.PickRegionAsync(settings.LastRegionProfile);
            });
        }

        // The overlay follows the game window wherever it is, including onto a second monitor.
        _session.GameWindowLocated += game => Dispatcher.UIThread.Post(() =>
        {
            PlaceOverlay(overlay, game, settings);

            // A window that turns up somewhere new usually means the game was Alt-Tabbed away and
            // back, and anything topmost that appeared in between now sits above us permanently.
            overlay.ReassertTopmost();
            _toolbar?.ReassertTopmost();
            _frame?.ReassertTopmost();
        });

        // And it moves the instant a slider does, so where it will sit is something the user sees
        // rather than a number they have to start a game to evaluate.
        settingsWindow.OverlayPlacementChanged += () => PositionOverlay(overlay, _session, settings);
        return settingsWindow;
    }

    private ToolbarWindow? _toolbar;
    private CaptureFrameWindow? _frame;

    /// <summary>
    /// The exit of last resort, and the reason <c>0-force-stop.bat</c> could be retired.
    ///
    /// <para>
    /// Every window this app floats is deliberately hard to reach — the overlay is click-through
    /// with no taskbar entry, the toolbar can be switched off, Settings can sit behind a
    /// fullscreen game. When all of them are out of reach at once, the old answer was a batch
    /// file that ran <c>taskkill</c>: a script beside an unsigned exe, which is exactly the shape
    /// antivirus heuristics dislike, doing violently what the app can do cleanly. The tray is the
    /// one control surface the OS itself keeps reachable, so it carries the way back in and the
    /// way out.
    /// </para>
    ///
    /// <para>
    /// The icon is rendered at runtime from <see cref="Icons"/> geometry rather than shipped as an
    /// asset — the same reasoning as the toolbar, one layer further: no file to quarantine, no
    /// font to substitute, nothing on the machine that can change what it looks like.
    /// </para>
    /// </summary>
    private void BuildTray(AppSettings settings, SettingsWindow settingsWindow)
    {
        if (_session is null || _overlay is null) return;

        var overlay = _overlay;
        var text = UiText.For(settings.Language);

        var open = new NativeMenuItem(text.TrayOpenSettings);
        open.Click += (_, _) =>
        {
            settingsWindow.Show();
            settingsWindow.Activate();
        };

        var toggle = new NativeMenuItem(text.TrayToggleOverlay);
        toggle.Click += (_, _) => settingsWindow.ReportStatus(
            overlay.ToggleHidden() ? text.OverlayShown : text.OverlayHidden);

        var exit = new NativeMenuItem(text.TrayExit);
        exit.Click += (_, _) =>
        {
            // The clean path, not taskkill: closes every floating window and disposes the
            // services, exactly as closing Settings does.
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        };

        var tray = new TrayIcon
        {
            ToolTipText = "Glass HUD Translator",
            Icon = RenderTrayIcon(),
            Menu = [open, toggle, new NativeMenuItemSeparator(), exit],
        };

        tray.Clicked += (_, _) =>
        {
            settingsWindow.Show();
            settingsWindow.Activate();
        };

        TrayIcon.SetIcons(this, [tray]);
    }

    /// <summary>The speech-bubble mark on a dark rounded tile, drawn from path geometry.</summary>
    private static WindowIcon RenderTrayIcon()
    {
        var tile = new Avalonia.Controls.Border
        {
            Width = 32,
            Height = 32,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#12141a")),
            CornerRadius = new Avalonia.CornerRadius(7),
            Child = Icons.Draw(Icons.TranslateNow, 24,
                new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#e8eaed"))),
        };

        tile.Measure(new Avalonia.Size(32, 32));
        tile.Arrange(new Rect(0, 0, 32, 32));

        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize(32, 32), new Vector(96, 96));
        bitmap.Render(tile);

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;

        return new WindowIcon(stream);
    }

    /// <summary>
    /// Creates the toolbar and the capture frame and connects them to everything they drive.
    ///
    /// <para>
    /// Both are built whether or not they are switched on, for the same reason every provider lane
    /// is: a setting that needs a restart to take effect is a setting people conclude does not
    /// work. They are simply not shown.
    /// </para>
    /// </summary>
    private void BuildFloatingWindows(AppSettings settings, SettingsWindow settingsWindow)
    {
        if (_session is null || _overlay is null) return;

        var session = _session;
        var overlay = _overlay;

        var frame = _frame = new CaptureFrameWindow();
        frame.Adjusted += region => Dispatcher.UIThread.Post(
            () => _ = settingsWindow.FrameAdjustedAsync(region));

        // The frame outlines whatever is about to be captured, so it follows the region rather than
        // being told where to go. Marshalled: auto-watch resolves regions on its own thread.
        session.RegionResolved += region => Dispatcher.UIThread.Post(() => frame.Track(region));

        // And it follows a re-pick or a drag straight away, rather than outlining the old rectangle
        // until the next translation happens to resolve one.
        settingsWindow.RegionChanged += () => _ = RetrackFrameAsync();

        var toolbar = _toolbar = new ToolbarWindow(UiText.For(settings.Language), settings,
            new ToolbarActions(
                TranslateNow: () => _ = session.TranslateNowAsync(),
                ToggleAutoWatch: () =>
                {
                    session.ToggleAutoWatch();
                    RefreshToolbar(settings);
                },
                Snip: () => _ = settingsWindow.SnipAsync(),
                Retry: () => _ = _session!.RetryAsync(),
                PickRegion: () => _ = settingsWindow.PickRegionAsync(settings.LastRegionProfile),
                ToggleMoveMode: () => _ = ToggleMoveModeAsync(settings, settingsWindow),
                ToggleCaptureFrame: () => _ = CycleCaptureFrameAsync(settings),
                ToggleOverlay: () =>
                {
                    var text = UiText.For(settings.Language);
                    settingsWindow.ReportStatus(overlay.ToggleHidden() ? text.OverlayShown : text.OverlayHidden);
                    RefreshToolbar(settings);
                },
                OpenSettings: () =>
                {
                    settingsWindow.Show();
                    settingsWindow.Activate();
                },
                ToggleWatchMode: () =>
                {
                    // All THREE, in the same order Settings lists them. It used to flip between two,
                    // so Auto was reachable only from Settings - and the toolbar drew an Auto icon
                    // it could never actually show, which is worse than not offering it: the button
                    // advertised a mode and then refused to reach it. The list lives in Core so the
                    // two surfaces cannot drift apart again.
                    settings.WatchMode = WatchModes.After(settings.WatchMode);
                    settings.Save();

                    // Both surfaces do the same three things, because a mode chosen from the
                    // toolbar mid-game is exactly when applying it immediately matters most.
                    _session?.WatchModeChanged();

                    // <b>On the overlay, not only in Settings.</b> This button exists so that
                    // somebody inside a fullscreen game can change mode without alt-tabbing, and it
                    // was answering them on the one screen they cannot see - so pressing it gave no
                    // feedback at all beyond a shape changing, and three shapes cycling silently do
                    // not tell you which one you have landed on. Reported as "switching between
                    // modes on the toolbar still does not tell you which mode is detected".
                    var text = UiText.For(settings.Language);
                    var chosen = string.Format(text.WatchModeSetTo, text.WatchModeName(settings.WatchMode));

                    settingsWindow.ReportStatus(chosen);
                    _overlay?.ShowMessage(chosen);
                    RefreshToolbar(settings);
                },
                ToggleDiacritics: () =>
                {
                    settings.Diacritics = !settings.Diacritics;
                    settings.Save();
                    _services!.Pipeline.Diacritics = settings.Diacritics;
                    var text = UiText.For(settings.Language);
                    settingsWindow.ReportStatus(settings.Diacritics ? text.DiacriticsShown : text.DiacriticsHidden);
                    RefreshToolbar(settings);
                },
                ToggleDialect: () =>
                {
                    settings.Register = settings.Register == Core.Translation.ArabicRegister.Egyptian
                        ? Core.Translation.ArabicRegister.ModernStandard
                        : Core.Translation.ArabicRegister.Egyptian;
                    settings.Save();
                    _services!.Pipeline.Register = settings.Register;

                    var text = UiText.For(settings.Language);
                    settingsWindow.ReportStatus(string.Format(text.RegisterSetTo,
                        settings.Register == Core.Translation.ArabicRegister.Egyptian
                            ? text.RegisterEgyptian
                            : text.RegisterMsa));
                    RefreshToolbar(settings);
                },
                ToggleRecording: () =>
                {
                    settings.HideOverlayFromCapture = !settings.HideOverlayFromCapture;
                    settings.Save();
                    overlay.HideFromCapture = settings.HideOverlayFromCapture;
                    RefreshToolbar(settings);
                },
                ToggleReadAgain: () =>
                {
                    // Through Settings rather than straight onto the pipeline, so the checkbox and
                    // the button cannot end up disagreeing about a switch that spends quota.
                    settingsWindow.ApplyReadAgain(!settings.ReadUnreadableLinesAgain);
                    RefreshToolbar(settings);
                },
                PinCorrection: () => _ = settingsWindow.CorrectCurrentAsync(),
                Quit: () => settingsWindow.Close()));

        settingsWindow.FloatingWindowsChanged += () => _ = ApplyFloatingWindowsAsync(settings);

        // The mode has two controls now - the Translating tab and the toolbar - and either has to
        // repaint the other, or the toolbar shows dialogue while the app is watching a film.
        settingsWindow.WatchModeChanged += () =>
        {
            // The running loop as well as the button. Pacing is read once when a run starts, so
            // without this a mode chosen mid-run did nothing at all until auto-watch was toggled
            // off and on - which reads as the switch being broken.
            _session?.WatchModeChanged();
            RefreshToolbar(settings);
        };

        // The Settings copy of the same button. One owner, two surfaces - neither can drift.
        settingsWindow.MoveModeToggled += () => _ = ToggleMoveModeAsync(settings, settingsWindow);

        // A drag ends in physical screen pixels; the stored form is two fractions of the game's
        // free space, so the panel keeps its place when the window is resized or moved to another
        // monitor. Converting here means dragging and the two sliders are the same setting.
        overlay.Moved += landed => Dispatcher.UIThread.Post(() =>
        {
            var area = _session?.OverlayAnchor() ?? WholeScreenFallback(overlay);
            var (horizontal, vertical) = OverlayPlacement.FractionsWithin(
                area, overlay.Width, overlay.PanelHeight, ScalingOf(overlay, area),
                landed.X, landed.Y);

            settings.OverlayHorizontal = horizontal;
            settings.OverlayVertical = vertical;
            settings.Save();

            // Rebuild the sliders so they show where it actually is, rather than where it was
            // before the drag - two controls for one setting must never disagree.
            settingsWindow.ReloadOverlayPlacement();
        });

        settingsWindow.Opened += (_, _) => _ = ApplyFloatingWindowsAsync(settings);
        _ = toolbar;
    }

    /// <summary>
    /// Shows or hides the toolbar and the frame to match the settings, and re-applies the window
    /// styles both depend on.
    /// </summary>
    private async Task ApplyFloatingWindowsAsync(AppSettings settings)
    {
        if (_toolbar is { } toolbar)
        {
            if (settings.ShowToolbar)
            {
                toolbar.Show();

                // Re-applied rather than set once: the focus escape hatch is a checkbox, and it has
                // to take effect on the window that is already open.
                toolbar.ApplyPlatformStyles();
                toolbar.PlaceNear(_session?.OverlayAnchor()
                                  ?? WholeScreenFallback(toolbar));
                RefreshToolbar(settings);
            }
            else
            {
                toolbar.Hide();
            }
        }

        if (_frame is { } frame)
        {
            // The rectangle FIRST, then the mode. Showing a frame that has never been told where to
            // go is a zero-by-zero window somewhere near the origin, which looks like the feature
            // being broken rather than like there being no region to outline yet.
            if (settings.ShowCaptureFrame) await RetrackFrameAsync();

            var wanted = settings.ShowCaptureFrame ? FrameMode.Shown : FrameMode.Hidden;

            // Never demote an in-progress adjustment: the checkbox says whether the frame is drawn,
            // and someone dragging it is already past that question.
            if (frame.Mode != FrameMode.Adjustable || wanted == FrameMode.Hidden) frame.Mode = wanted;
        }
    }

    /// <summary>
    /// The frame button cycles hidden → outlined → grabbable → hidden.
    ///
    /// <para>
    /// Three states on one button because they are three points on one axis — how much of itself
    /// the frame is offering to the mouse — and splitting them across two controls would put the
    /// rarely-used one somewhere it has to be hunted for. The icon changes with the state, so which
    /// one you are in is visible rather than remembered.
    /// </para>
    /// </summary>
    private async Task CycleCaptureFrameAsync(AppSettings settings)
    {
        if (_frame is not { } frame) return;

        var next = frame.Mode switch
        {
            FrameMode.Hidden => FrameMode.Shown,
            FrameMode.Shown => FrameMode.Adjustable,
            _ => FrameMode.Hidden,
        };

        settings.ShowCaptureFrame = next != FrameMode.Hidden;
        settings.Save();

        if (next != FrameMode.Hidden) await RetrackFrameAsync();

        frame.Mode = next;
        RefreshToolbar(settings);
    }

    /// <summary>
    /// Unlocks both floating surfaces at once, and locks them together again.
    ///
    /// <para>
    /// One mode rather than two, because "let me move the thing that is in my way" is a single
    /// intention and the user should not have to know that the outline and the panel are separate
    /// windows. It is also the only state in which either eats a click, which is why it is a
    /// deliberate toggle with a visible outline on both, and why turning it off restores
    /// click-through on both — a mode you can leave the app in by accident would be a mode that
    /// quietly steals every click aimed at the game.
    /// </para>
    ///
    /// <para>
    /// The outline is forced visible while unlocked. Being asked to drag something you cannot see
    /// is not an interaction; if it was hidden it comes back hidden, so the mode leaves no trace.
    /// </para>
    /// </summary>
    private async Task ToggleMoveModeAsync(AppSettings settings, SettingsWindow settingsWindow)
    {
        if (_overlay is not { } overlay) return;

        _moveMode = !_moveMode;
        overlay.Movable = _moveMode;

        if (_frame is { } frame)
        {
            if (_moveMode)
            {
                await RetrackFrameAsync();
                frame.Mode = FrameMode.Adjustable;
            }
            else
            {
                frame.Mode = settings.ShowCaptureFrame ? FrameMode.Shown : FrameMode.Hidden;
            }
        }

        var text = UiText.For(settings.Language);
        settingsWindow.ReportStatus(_moveMode ? text.MoveModeOn : text.MoveModeOff);
        overlay.ShowMessage(_moveMode ? text.MoveModeOn : text.MoveModeOff);

        RefreshToolbar(settings);
    }

    private bool _moveMode;

    private async Task RetrackFrameAsync()
    {
        if (_frame is not { } frame || _session is null) return;
        if (await _session.CurrentRegionAsync() is { } region)
            frame.Track(region);
    }

    private void RefreshToolbar(AppSettings settings)
    {
        if (_toolbar is not { } toolbar || _overlay is null || _session is null) return;

        toolbar.UseLanguage(UiText.For(settings.Language));
        toolbar.ShowState(_session.IsAutoWatching, _overlay.HiddenByUser,
            _frame?.Mode is FrameMode.Shown or FrameMode.Adjustable,
            settings.Diacritics, settings.WatchMode,
            moveMode: _moveMode,
            egyptian: settings.Register == Core.Translation.ArabicRegister.Egyptian,
            recordable: !settings.HideOverlayFromCapture,
            readAgain: settings.ReadUnreadableLinesAgain,

            // What Auto has settled on, so the button answers the question instead of only saying
            // that a question is being asked on the user's behalf.
            running: _session.ContentVerdict?.Running);
    }

    private static CaptureRegion WholeScreenFallback(Avalonia.Controls.Window window) =>
        window.Screens.Primary is { } screen
            ? new CaptureRegion(screen.WorkingArea.X, screen.WorkingArea.Y,
                screen.WorkingArea.Width, screen.WorkingArea.Height)
            : new CaptureRegion(0, 0, 1920, 1080);

    /// <summary>
    /// Hotkeys arrive on a dedicated Win32 message thread, so every handler hops back to the UI
    /// thread before touching a window.
    /// </summary>
    private void BindHotkeys(AppSettings settings, SettingsWindow settingsWindow)
    {
        if (_services is null || _session is null || _overlay is null) return;

        var overlay = _overlay;

        _services.Hotkeys.Pressed += action => Dispatcher.UIThread.Post(() =>
        {
            switch (action)
            {
                case HotkeyAction.TranslateNow:
                    _ = _session.TranslateNowAsync();
                    break;
                case HotkeyAction.ToggleAutoWatch:
                    _session.ToggleAutoWatch();
                    break;
                case HotkeyAction.PickRegion:
                    _ = settingsWindow.PickRegionAsync(settings.LastRegionProfile);
                    break;
                case HotkeyAction.FlagTranslation:
                    _ = settingsWindow.CorrectCurrentAsync();
                    break;
                case HotkeyAction.ToggleOverlay:
                    var text = UiText.For(settings.Language);
                    settingsWindow.ReportStatus(overlay.ToggleHidden()
                        ? text.OverlayShown
                        : text.OverlayHidden);
                    break;
                case HotkeyAction.SnipTranslate:
                    _ = settingsWindow.SnipAsync();
                    break;
                case HotkeyAction.RetryTranslation:
                    _ = _session!.RetryAsync();
                    break;
                case HotkeyAction.OpenSettings:
                    // Settings has no taskbar button of its own, so once it is behind a fullscreen
                    // game there is no way back to it without leaving the game and hunting. Every
                    // failure this app can have sends the user here.
                    settingsWindow.Show();
                    settingsWindow.Activate();
                    break;
            }
        });

        settingsWindow.ReportHotkeyRegistrations(_services.Hotkeys.Register(settings.ResolvedHotkeys()));
    }

    /// <summary>
    /// Writes one PNG per settings tab and exits. Documentation screenshots are generated from the
    /// running UI rather than taken by hand, because a hand-taken one is stale the next time the
    /// layout moves - which is exactly what happened to the old settings screenshot.
    ///
    /// <para>Run with --stub so it needs no API key and makes no network call.</para>
    /// </summary>
    private void CaptureSettingsShots(
        SettingsWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var directory = Program.Option("--ui-shots-out") ?? Path.GetTempPath();
        Directory.CreateDirectory(directory);

        // Tab headers are themselves translated, so the Arabic run would otherwise write files
        // named after Arabic words. The suffix keeps both sets side by side with stable names.
        var suffix = Program.Option("--ui-shots-lang") is { } lang &&
                     lang.Equals("ar", StringComparison.OrdinalIgnoreCase)
            ? "-ar"
            : "";

        var slugs = new[] { "providers", "translating", "overlay", "hotkeys", "history", "diagnostics" };

        window.Opened += async (_, _) =>
        {
            for (var i = 0; i < window.TabNames.Count; i++)
            {
                var name = (i < slugs.Length ? slugs[i] : $"tab{i}") + suffix;
                window.SelectTab(i);

                // Let layout and the first render pass settle before capturing.
                await Task.Delay(400);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var path = Path.Combine(directory, $"settings-{name}.png");
                    try
                    {
                        window.SaveSnapshot(path);
                        Console.WriteLine($"ui-shots: wrote {path}");
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"ui-shots: FAILED {name} - {e.Message}");
                    }
                });
            }

            // The profile editor is the answer to "my game isn't in the list", so the documentation
            // has to show it. Rendered from the same code path as the real window rather than
            // photographed, for the same reason as every other shot here.
            if (_services is not null)
            {
                var editor = new ProfileEditorWindow(
                    UiText.For(AppSettings.Load().Language switch
                    {
                        _ when suffix == "-ar" => UiLanguage.Arabic,
                        _ => UiLanguage.English,
                    }),
                    _services.Profiles);

                editor.Show();
                await Task.Delay(400);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var path = Path.Combine(directory, $"add-game{suffix}.png");
                    try
                    {
                        editor.SaveSnapshot(path);
                        Console.WriteLine($"ui-shots: wrote {path}");
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"ui-shots: FAILED add-game - {e.Message}");
                    }

                    editor.Close();
                });
            }

            desktop.Shutdown();
        };
    }

    private static Avalonia.Controls.Window BuildOverlaySnapshot(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var overlay = new OverlayWindow();
        var directory = Program.Option("--overlay-test-out") ?? Path.GetTempPath();

        overlay.Opened += async (_, _) =>
        {
            var states = new (string File, Action Apply)[]
            {
                ("overlay-loading.png", () => overlay.ShowLoading("Y'shtola")),
                ("overlay-translation.png", () => overlay.ShowTranslation("Y'shtola",
                    "تعال، فالأثير هنا يزداد اضطراباً. علينا أن نبلغ Limsa Lominsa قبل حلول الليل.")),
                ("overlay-fallback.png", () => overlay.ShowFallbackEnglish("Estinien",
                    "Come, the aether here grows unstable.")),
            };

            foreach (var (file, apply) in states)
            {
                apply();
                await Task.Delay(250);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var path = Path.Combine(directory, file);
                    overlay.SavePanelSnapshot(path);
                    Console.WriteLine($"overlay-test: wrote {path}");
                });
            }

            desktop.Shutdown();
        };

        return overlay;
    }

    /// <summary>
    /// Walks all four wizard steps for the camera and exits. The wizard is the first thing every
    /// new user ever sees and the last thing anyone here sees naturally — a dev machine completed
    /// its first run long ago — so without this flag the most important screen in the app would
    /// only ever render on a stranger's machine. Runs with the stub services: no key, no network.
    /// </summary>
    private Avalonia.Controls.Window BuildWizardSnapshot(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var directory = Program.Option("--wizard-test-out") ?? Path.GetTempPath();
        Directory.CreateDirectory(directory);

        var settings = new AppSettings();
        if (Program.Option("--wizard-test-lang") is { } lang &&
            lang.Equals("ar", StringComparison.OrdinalIgnoreCase))
            settings.Language = UiLanguage.Arabic;

        _services = AppServices.CreateAsync(
                Program.Option("--data") ?? RepoPaths.Data,
                Program.Option("--profiles") ?? RepoPaths.Profiles,
                preferredProfileId: null, useStubProvider: true)
            .GetAwaiter().GetResult();

        var wizard = new FirstRunWizard(_services, settings);

        wizard.Opened += async (_, _) =>
        {
            var suffix = settings.Language == UiLanguage.Arabic ? "-ar" : "";

            for (var step = 0; step <= 3; step++)
            {
                await Dispatcher.UIThread.InvokeAsync(() => wizard.ShowStep(step));
                await Task.Delay(300);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var path = Path.Combine(directory, $"wizard-step{step}{suffix}.png");
                    try
                    {
                        wizard.SaveSnapshot(path);
                        Console.WriteLine($"wizard-test: wrote {path}");
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"wizard-test: FAILED step {step} - {e.Message}");
                    }
                });
            }

            desktop.Shutdown();
        };

        return wizard;
    }

    /// <summary>
    /// Renders the startup-failure window with a staged exception and exits. Same rationale as the
    /// toolbar test: this window only ever appears on a stranger's machine at the worst possible
    /// moment, so the one place it can be rehearsed is here — if the window that reports startup
    /// failures cannot itself be built, that is the most valuable crash this flag can produce.
    /// </summary>
    private static Avalonia.Controls.Window BuildFailureSnapshot(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = new StartupFailureWindow(new InvalidOperationException(
            "Rehearsal: tessdata/eng.traineddata is missing. This is what a real failure looks like."));

        if (Program.Option("--failure-test-out") is { } directory)
        {
            window.Opened += async (_, _) =>
            {
                await Task.Delay(300);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var path = Path.Combine(directory, "startup-failure.png");
                    try
                    {
                        Directory.CreateDirectory(directory);
                        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
                            new PixelSize((int)window.Width, (int)Math.Ceiling(window.Bounds.Height)),
                            new Vector(96, 96));
                        bitmap.Render(window);
                        bitmap.Save(path);
                        Console.WriteLine($"failure-test: wrote {path}");
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"failure-test: FAILED - {e.Message}");
                    }

                    desktop.Shutdown();
                });
            };
        }

        return window;
    }

    /// <summary>
    /// Renders the toolbar, collapsed and expanded, and exits.
    ///
    /// <para>
    /// The icons are path geometry parsed from strings, and a typo in one of them is an exception
    /// thrown while the window is being built — on the user's machine, at startup, on a Windows box
    /// nobody here can reach. There is no compiler to catch it and no unit test that can, because
    /// the parser lives in Avalonia and the test project does not reference it. Rendering the real
    /// control on the development machine is the check: if every icon draws here, the geometry
    /// parses everywhere.
    /// </para>
    /// </summary>
    private static Avalonia.Controls.Window BuildToolbarSnapshot(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var directory = Program.Option("--toolbar-test-out") ?? Path.GetTempPath();
        Directory.CreateDirectory(directory);

        var settings = new AppSettings { ToolbarExpanded = false };
        var nothing = new Action(() => { });
        // Counted rather than listed, because this is the second construction site of a record that
        // grows every session and a miscount here breaks the one rehearsal that proves the icon
        // paths parse. Both sites have now needed hand-fixing twice.
        var actions = new ToolbarActions(
            TranslateNow: nothing, ToggleAutoWatch: nothing, Snip: nothing, Retry: nothing,
            PickRegion: nothing, ToggleMoveMode: nothing, ToggleCaptureFrame: nothing,
            ToggleOverlay: nothing, OpenSettings: nothing, ToggleWatchMode: nothing,
            ToggleDiacritics: nothing, ToggleDialect: nothing, ToggleRecording: nothing,
            ToggleReadAgain: nothing,
            PinCorrection: nothing, Quit: nothing);

        var toolbar = new ToolbarWindow(
            UiText.For(Program.Option("--toolbar-test-lang") == "ar" ? UiLanguage.Arabic : UiLanguage.English),
            settings, actions);

        toolbar.Opened += async (_, _) =>
        {
            // One snapshot per watch mode as well as per width, because the mode button is the only
            // control whose ICON changes rather than just lighting up - three geometries on one
            // button. Rendering only the default meant Icons.WatchAuto had never once been drawn by
            // the rehearsal whose entire job is proving a path string parses, and it is reached by
            // the least-used branch of the least-visited control.
            // Five geometries on one button now, because Auto draws the same dial with the needle
            // in three positions - undecided, and settled on each of the two. Every one of them has
            // to be drawn here or it is a path string that no compiler, no unit test and no
            // developer ever sees fail, on the least-visited control in the app.
            var shots = new[]
            {
                ("toolbar-simple.png", false, WatchMode.Dialogue, (WatchMode?)null),
                ("toolbar-advanced.png", true, WatchMode.Dialogue, null),
                ("toolbar-mode-video.png", true, WatchMode.Video, null),
                ("toolbar-mode-auto.png", true, WatchMode.Auto, null),
                ("toolbar-mode-auto-dialogue.png", true, WatchMode.Auto, WatchMode.Dialogue),
                ("toolbar-mode-auto-video.png", true, WatchMode.Auto, WatchMode.Video),
            };

            foreach (var (file, expanded, mode, running) in shots)
            {
                // Set through the window rather than through the expander button, because that
                // writes to the settings file and this run must not touch it. Then a delay, so the
                // layout pass actually runs before anything is measured - a snapshot taken in the
                // same tick as the visibility change captures the old geometry.
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    toolbar.ShowAdvanced(expanded);
                    toolbar.ShowState(autoWatch: expanded, overlayHidden: false,
                        captureFrame: expanded, diacritics: false, mode: mode, running: running);
                });

                await Task.Delay(250);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var path = Path.Combine(directory, file);
                    try
                    {
                        toolbar.SaveSnapshot(path);
                        Console.WriteLine($"toolbar-test: wrote {path}");
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine($"toolbar-test: FAILED {file} - {e.Message}");
                    }
                });
            }

            SaveTooltipSample(Path.Combine(directory, "toolbar-tooltip.png"));
            desktop.Shutdown();
        };

        return toolbar;
    }

    /// <summary>
    /// Draws the hover label as both interface languages produce it, side by side.
    ///
    /// <para>
    /// The bilingual tooltip is the toolbar's whole argument and it is the one part a screenshot of
    /// the strip cannot show, because a tooltip is a popup. So it is rendered here from the real
    /// <see cref="BilingualTip"/> control rather than mocked up: what the documentation shows is
    /// what the code produces, which is the same rule the settings screenshots follow.
    /// </para>
    /// </summary>
    private static void SaveTooltipSample(string path)
    {
        var row = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 18,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0d0f13")),
        };

        foreach (var language in new[] { UiLanguage.English, UiLanguage.Arabic })
        {
            var text = UiText.For(language);
            row.Children.Add(new Avalonia.Controls.Border
            {
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1c1f26")),
                BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#343943")),
                BorderThickness = new Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Thickness(12, 9),
                Margin = new Thickness(12),
                Child = BilingualTip.For(text, UiText.En.ToolbarSnip, UiText.Ar.ToolbarSnip, "Ctrl+Shift+X"),
            });
        }

        try
        {
            row.Measure(Avalonia.Size.Infinity);
            row.Arrange(new Rect(row.DesiredSize));

            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
                new PixelSize((int)Math.Ceiling(row.Bounds.Width), (int)Math.Ceiling(row.Bounds.Height)),
                new Vector(96, 96));

            bitmap.Render(row);
            bitmap.Save(path);
            Console.WriteLine($"toolbar-test: wrote {path}");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"toolbar-test: FAILED tooltip - {e.Message}");
        }
    }

    /// <summary>
    /// Bottom-centre, roughly where a dialogue box sits. On Windows the saved region profile takes
    /// over once one exists.
    /// </summary>
    /// <summary>
    /// Puts the overlay near the bottom of the game's window, falling back to the primary monitor
    /// when there is no game to find.
    ///
    /// <para>
    /// It was pinned to the primary monitor for the lifetime of the process. That was survivable
    /// only while screen capture was also primary-only — both halves were wrong together, so a
    /// player on a second display got nothing and knew it. Now that capture follows the game, an
    /// overlay left behind is the worse failure: the translation happens, the quota is spent, and
    /// the Arabic appears on a screen nobody is looking at.
    /// </para>
    /// </summary>
    private static void PositionOverlay(
        OverlayWindow overlay, TranslationSession? session, AppSettings settings)
    {
        overlay.Show();

        if (session?.OverlayAnchor() is { } game)
        {
            PlaceOverlay(overlay, game, settings);
            return;
        }

        // No game found yet, so the screen stands in for it. The same fractions apply, which is
        // what makes the preview in Settings mean anything before a game is running.
        if (overlay.Screens.Primary is not { } screen) return;

        var bounds = screen.WorkingArea;
        PlaceOverlay(overlay,
            new CaptureRegion(bounds.X, bounds.Y, bounds.Width, bounds.Height), settings);
    }

    /// <summary>
    /// Places the panel inside <paramref name="area"/>, converting the window's size out of
    /// device-independent pixels first.
    ///
    /// <para>
    /// Three quantities meet here and two of them used to be in the wrong unit.
    /// <c>overlay.Width</c> and <see cref="OverlayWindow.PanelHeight"/> are DIPs — 900 and whatever
    /// the text wrapped to — while <paramref name="area"/> and <see cref="Window.Position"/> are
    /// physical screen pixels. At 100% scaling those are the same number and nothing was visibly
    /// wrong. At 125% the panel is 1125 physical pixels wide while the placement arithmetic
    /// believed 900, so "flush with the right edge" hung 225 pixels off the screen. Invisible until
    /// something has to line up exactly with the rectangle being captured — which is precisely what
    /// the capture frame does.
    /// </para>
    ///
    /// <para>
    /// The scaling comes from the monitor the panel is being sent TO, not from
    /// <c>overlay.RenderScaling</c>, which is the monitor it currently happens to be on. Using the
    /// latter makes the move to a differently-scaled second screen a two-step affair: land in the
    /// wrong place at the old scale, then correct on the next placement.
    /// </para>
    /// </summary>
    private static void PlaceOverlay(OverlayWindow overlay, CaptureRegion area, AppSettings settings)
    {
        var (x, y) = OverlayPlacement.Within(area, overlay.Width, overlay.PanelHeight,
            ScalingOf(overlay, area), settings.OverlayHorizontal, settings.OverlayVertical);

        overlay.Position = new PixelPoint(x, y);
    }

    /// <summary>
    /// The DPI scale of the monitor <paramref name="area"/> sits on, falling back to the window's
    /// own and then to 1. A zero or NaN here would multiply a size into a coordinate far off any
    /// screen, which looks exactly like the overlay having vanished.
    /// </summary>
    private static double ScalingOf(Avalonia.Controls.Window window, CaptureRegion area)
    {
        var centre = new PixelPoint(area.X + area.Width / 2, area.Y + area.Height / 2);
        var scaling = window.Screens.ScreenFromPoint(centre)?.Scaling ?? window.RenderScaling;

        return double.IsNaN(scaling) || scaling <= 0 ? 1.0 : scaling;
    }
}

/// <summary>
/// Finds data/, profiles/ and test-frames/ whether running from a build output during development
/// or from the published folder next to the exe.
/// </summary>
internal static class RepoPaths
{
    public static string Data => Resolve("data");

    public static string Profiles => Resolve("profiles");

    public static string TestFrames => Resolve("test-frames");

    private static string Resolve(string folder)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "GlassHudTranslator.slnx")))
            dir = Path.GetDirectoryName(dir);

        return dir is null
            ? Path.Combine(AppContext.BaseDirectory, folder)
            : Path.Combine(dir, folder);
    }
}
