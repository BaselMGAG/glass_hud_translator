using Avalonia;
using GlassHudTranslator.Core.Update;

namespace GlassHudTranslator.App;

internal static class Program
{
    /// <summary>Raw command line, read by <see cref="App"/> when it picks a startup window.</summary>
    internal static string[] Args { get; private set; } = [];

    [STAThread]
    public static int Main(string[] args)
    {
        Args = args;

        // Answers "which version are you running?" without asking someone to open a window and
        // read a label in a language they may not use. Also how the release build's version stamp
        // gets verified: from source this prints 0.0.0, and CI stamps the tag over it.
        if (HasFlag("--version"))
        {
            Console.WriteLine(UpdateCheck.RunningVersion?.ToString() ?? "unknown");
            return 0;
        }

        // Runs the update check on the console and prints what it found. Support can ask for this
        // instead of guessing whether a machine is behind a proxy, rate limited, or simply current.
        if (HasFlag("--check-updates"))
        {
            using var http = new HttpClient();
            var result = UpdateCheck
                .FetchAsync(http, UpdateCheck.RunningVersion, CancellationToken.None)
                .GetAwaiter().GetResult();

            Console.WriteLine($"running   {UpdateCheck.RunningVersion}");
            Console.WriteLine($"outcome   {result.Outcome}");
            if (result.Update is { } update)
                Console.WriteLine($"available {update.Tag}  {update.AssetName}  {update.ReleaseUrl}");

            return 0;
        }

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
