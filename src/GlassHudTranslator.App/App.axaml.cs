using GlassHudTranslator.App.Views;
using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Platform;
using GlassHudTranslator.Core.Storage;
using Avalonia;
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
            // Starting with no window at all would leave a process running with no explanation,
            // so the failure goes on the overlay itself.
            overlay.ShowMessage($"Startup failed: {e.Message}");
            overlay.Show();
            return overlay;
        }

        // Applied once here as well as per frame, because the pipeline is reachable without going
        // through a frame at all: Settings' "Test translation" button calls ProcessAsync directly.
        // Without this it ran on defaults until the first real capture, so someone who had chosen
        // Egyptian, restarted, and pressed Test got Modern Standard back and no reason for it.
        _overlay.HideFromCapture = settings.HideOverlayFromCapture;

        _services.Pipeline.Register = settings.Register;
        _services.Pipeline.Diacritics = settings.Diacritics;
        _services.Pipeline.MinimumBodyCharacters = settings.MinimumCharactersToTranslate;

        _session = new TranslationSession(_services, overlay, settings, RepoPaths.TestFrames)
        {
            SaveFramesDirectory = Program.Option("--save-frames"),
        };

        var settingsWindow = new SettingsWindow(_services, overlay, settings, _session);
        _session.Status += message => Dispatcher.UIThread.Post(() => settingsWindow.ReportStatus(message));

        BindHotkeys(settings, settingsWindow);
        BuildFloatingWindows(settings, settingsWindow);

        settingsWindow.Opened += (_, _) => PositionOverlay(overlay, _session, settings);

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
                PickRegion: () => _ = settingsWindow.PickRegionAsync(settings.LastRegionProfile),
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
                    settings.WatchMode = settings.WatchMode == WatchMode.Video
                        ? WatchMode.Dialogue
                        : WatchMode.Video;
                    settings.Save();
                    settingsWindow.ReportStatus(string.Format(UiText.For(settings.Language).WatchModeSetTo,
                        UiText.For(settings.Language).WatchModeName(settings.WatchMode)));
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
                PinCorrection: () => _ = settingsWindow.CorrectCurrentAsync(),
                Quit: () => settingsWindow.Close()));

        settingsWindow.FloatingWindowsChanged += () => _ = ApplyFloatingWindowsAsync(settings);

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
            settings.Diacritics, settings.WatchMode);
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

        var slugs = new[] { "providers", "translating", "overlay", "hotkeys", "diagnostics" };

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
        var actions = new ToolbarActions(nothing, nothing, nothing, nothing, nothing, nothing,
            nothing, nothing, nothing, nothing, nothing);

        var toolbar = new ToolbarWindow(
            UiText.For(Program.Option("--toolbar-test-lang") == "ar" ? UiLanguage.Arabic : UiLanguage.English),
            settings, actions);

        toolbar.Opened += async (_, _) =>
        {
            foreach (var (file, expanded) in new[] { ("toolbar-simple.png", false), ("toolbar-advanced.png", true) })
            {
                // Set through the window rather than through the expander button, because that
                // writes to the settings file and this run must not touch it. Then a delay, so the
                // layout pass actually runs before anything is measured - a snapshot taken in the
                // same tick as the visibility change captures the old geometry.
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    toolbar.ShowAdvanced(expanded);
                    toolbar.ShowState(autoWatch: expanded, overlayHidden: false,
                        captureFrame: expanded, diacritics: false, mode: WatchMode.Dialogue);
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

            desktop.Shutdown();
        };

        return toolbar;
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
