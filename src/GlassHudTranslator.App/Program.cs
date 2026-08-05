using Avalonia;

namespace GlassHudTranslator.App;

internal static class Program
{
    /// <summary>Raw command line, read by <see cref="App"/> when it picks a startup window.</summary>
    internal static string[] Args { get; private set; } = [];

    [STAThread]
    public static int Main(string[] args)
    {
        Args = args;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Referenced by name by the Avalonia designer tooling - do not rename.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    internal static bool HasFlag(string name) => Args.Contains(name, StringComparer.Ordinal);

    internal static string? Option(string name)
    {
        var i = Array.IndexOf(Args, name);
        return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null;
    }
}
