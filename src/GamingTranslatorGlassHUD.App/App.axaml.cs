using GamingTranslatorGlassHUD.App.Views;
using GamingTranslatorGlassHUD.Core.Config;
using GamingTranslatorGlassHUD.Core.Platform;
using GamingTranslatorGlassHUD.Core.Storage;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace GamingTranslatorGlassHUD.App;

public partial class App : Application
{
    private AppServices? _services;
    private TranslationSession? _session;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Program.HasFlag("--render-test")
                ? new ArabicRenderTestWindow(
                    Program.Option("--render-test-out"), Program.HasFlag("--exit-after-render"))
                : Program.HasFlag("--overlay-test")
                    ? BuildOverlaySnapshot(desktop)
                    : BuildMainWindow();

            desktop.ShutdownRequested += async (_, _) =>
            {
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
        var overlay = new OverlayWindow
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

        _session = new TranslationSession(_services, overlay, settings, RepoPaths.TestFrames);

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
        if (_services is null || _session is null) return;

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
            }
        });

        settingsWindow.ReportHotkeyRegistrations(_services.Hotkeys.Register(settings.ResolvedHotkeys()));
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
        while (dir is not null && !File.Exists(Path.Combine(dir, "GamingTranslatorGlassHUD.slnx")))
            dir = Path.GetDirectoryName(dir);

        return dir is null
            ? Path.Combine(AppContext.BaseDirectory, folder)
            : Path.Combine(dir, folder);
    }
}
