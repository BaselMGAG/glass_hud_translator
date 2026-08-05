using GlassHudTranslator.App.Views;
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
                    : BuildMainWindow();

            if (Program.HasFlag("--ui-shots") && desktop.MainWindow is SettingsWindow shotTarget)
                CaptureSettingsShots(shotTarget, desktop);

            desktop.ShutdownRequested += async (_, _) =>
            {
                _overlay?.Close();
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

        _session = new TranslationSession(_services, overlay, settings, RepoPaths.TestFrames)
        {
            SaveFramesDirectory = Program.Option("--save-frames"),
        };

        var settingsWindow = new SettingsWindow(_services, overlay, settings, _session);
        _session.Status += message => Dispatcher.UIThread.Post(() => settingsWindow.ReportStatus(message));

        BindHotkeys(settings, settingsWindow);

        settingsWindow.Opened += (_, _) => PositionOverlay(overlay);
        return settingsWindow;
    }

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
                    settingsWindow.ReportStatus(overlay.ToggleHidden()
                        ? "Overlay shown."
                        : "Overlay hidden. Translation carries on in the background.");
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
    private static void CaptureSettingsShots(
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
    /// Bottom-centre, roughly where a dialogue box sits. On Windows the saved region profile takes
    /// over once one exists.
    /// </summary>
    private static void PositionOverlay(OverlayWindow overlay)
    {
        overlay.Show();

        if (overlay.Screens.Primary is not { } screen) return;

        var bounds = screen.WorkingArea;
        overlay.Position = new PixelPoint(
            bounds.X + (bounds.Width - (int)overlay.Width) / 2,
            bounds.Y + (int)(bounds.Height * 0.72));
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
