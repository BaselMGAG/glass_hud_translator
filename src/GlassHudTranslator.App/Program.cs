// Glass HUD Translator — Arabic subtitles for games that never shipped with Arabic support.
// Copyright 2026 Basel
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU Affero General Public License as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
// implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero
// General Public License in the LICENSE file, or at <https://www.gnu.org/licenses/>.
//
// Releases up to and including v0.5.3 were published under the Apache License 2.0 and remain
// available under those terms.

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

        // AGPL section 5(d): an interactive program that normally prints a notice must keep doing
        // so. This one has no console to print to, so the notice lives on the LICENSE file shipped
        // beside the exe, on the Diagnostics tab, and here for anyone who asks.
        if (HasFlag("--licence") || HasFlag("--license"))
        {
            Console.WriteLine("Glass HUD Translator — Copyright 2026 Basel");
            Console.WriteLine("GNU Affero General Public License v3 or later. See the LICENSE file");
            Console.WriteLine("beside this program, or https://www.gnu.org/licenses/agpl-3.0.html");
            Console.WriteLine();
            Console.WriteLine("Source: https://github.com/BaselMGAG/glass_hud_translator");
            Console.WriteLine("Releases up to v0.5.3 were Apache-2.0 and remain so.");
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
