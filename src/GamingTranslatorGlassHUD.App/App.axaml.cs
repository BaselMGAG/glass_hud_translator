using GamingTranslatorGlassHUD.App.Views;
using GamingTranslatorGlassHUD.Core.Storage;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace GamingTranslatorGlassHUD.App;

public partial class App : Application
{
    private AppServices? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Program.HasFlag("--render-test")
                ? new ArabicRenderTestWindow(
                    saveTo: Program.Option("--render-test-out"),
                    exitAfterSave: Program.HasFlag("--exit-after-render"))
                : Program.HasFlag("--overlay-test")
                    ? BuildOverlaySnapshot(desktop)
                    : BuildMainWindow();

            desktop.ShutdownRequested += async (_, _) =>
            {
                if (_services is not null) await _services.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Renders the production overlay in each of its three states and writes them out, so the
    /// shaping rules in OverlayWindow can be verified without a game or a running session.
    /// </summary>
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

    private Avalonia.Controls.Window BuildMainWindow()
    {
        var overlay = new OverlayWindow();

        try
        {
            AppPaths.Ensure();
            _services = AppServices.CreateAsync(
                    Program.Option("--data") ?? RepoPaths.Data,
                    Program.Option("--profiles") ?? RepoPaths.Profiles,
                    Program.Option("--profile"),
                    useStubProvider: Program.HasFlag("--stub"))
                .GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            // Starting with no window at all would leave the user with a process and no
            // explanation, so surface the failure in the overlay itself.
            overlay.ShowMessage($"Startup failed: {e.Message}");
            overlay.Show();
            return overlay;
        }

        var settings = new SettingsWindow(_services, overlay);
        settings.Opened += (_, _) => PositionOverlay(overlay);
        return settings;
    }

    /// <summary>
    /// Bottom-centre, roughly where FFXIV's dialogue box sits. On Windows the region profile
    /// governs this instead (Session 2).
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
/// Finds data/ and profiles/ whether the app is running from a build output during development or
/// from the published folder next to the exe.
/// </summary>
internal static class RepoPaths
{
    public static string Data => Resolve("data");

    public static string Profiles => Resolve("profiles");

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
