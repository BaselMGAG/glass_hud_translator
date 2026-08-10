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

using System.Runtime.CompilerServices;
using Avalonia;
using GlassHudTranslator.Core.Diagnostics;
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

        // The black box. "Nothing opens" reached support twice with zero evidence either time:
        // the app's own error reporting draws on the overlay, which is transparent, unfocusable
        // and absent from the taskbar - so a startup failure and a successful, invisible start
        // look identical from outside. From here on, the log answers which one happened, and the
        // absence of the log answers the third possibility: the process never ran at all.
        StartupLog.Begin(UpdateCheck.RunningVersion?.ToString() ?? "0.0.0-dev");

        // Log-only hooks for the threads no try/catch below can see. Auto-watch has its own
        // per-poll handling; these catch what nothing else does.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            StartupLog.Note($"unhandled ({(e.IsTerminating ? "fatal" : "non-fatal")}): {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            StartupLog.Note($"unobserved task: {e.Exception}");
            e.SetObserved();
        };

        try
        {
            var code = RunAvalonia(args);
            StartupLog.Note($"exited normally ({code})");
            return code;
        }
        catch (Exception e)
        {
            // Avalonia itself failed - graphics initialisation, or a dependency the antivirus
            // quarantined. There is no toolkit left to draw an error with, so the report is a
            // native message box and the log. Bilingual, because we cannot know which language
            // the person reading it needs, and this is precisely the moment settings may be
            // unreadable too.
            StartupLog.Fail(e);

            PlatformServices.ShowFatalError(
                "Glass HUD Translator",
                "The app could not start.\n"
                + "تعذّر تشغيل البرنامج.\n\n"
                + $"{e.GetType().Name}: {e.Message}\n\n"
                + $"Details / التفاصيل:\n{StartupLog.Path ?? "(log could not be written)"}");

            return 1;
        }
    }

    /// <summary>
    /// Kept out of <see cref="Main"/> and never inlined, deliberately. Assemblies load when the
    /// method that references them is first JIT-compiled — so if an Avalonia DLL is missing or
    /// quarantined, the throw happens at the CALL to this method, inside Main's try, rather than
    /// while Main itself is being compiled, outside every handler in the program.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunAvalonia(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

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
