using System.Text.RegularExpressions;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The platform seam, enforced mechanically rather than by discipline.
///
/// <para>
/// The whole codebase is arranged so that everything except live screen capture, global hotkeys and
/// a real game runs on macOS — which is what makes the fast loop possible: build, test and replay
/// recorded frames without a Windows machine in the room. That arrangement rests on one rule,
/// <c>PlatformServices.cs</c> being the only file in the App with <c>#if WINDOWS</c> in it, and on
/// Core never referencing the Windows projects at all.
/// </para>
///
/// <para>
/// The rule survived while a Windows machine was scarce, because breaking it was inconvenient. It
/// is now easy to break and the cost of breaking it is delayed and invisible: someone reaches for a
/// Win32 call in a view because it is quick to test today, and six months later the macOS build is
/// dead and every change needs the slow loop. A rule that depends on remembering is not a rule, so
/// these tests are the rule.
/// </para>
/// </summary>
public class PlatformSeamTests
{
    private const string AppProject = "src/GlassHudTranslator.App";
    private const string CoreProject = "src/GlassHudTranslator.Core";
    private const string SeamFile = "PlatformServices.cs";

    /// <summary>Matches any preprocessor directive mentioning WINDOWS, however it is spelled.</summary>
    private static readonly Regex WindowsDirective = new(
        @"^\s*#\s*(if|elif)\b[^\r\n]*\bWINDOWS\b", RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public void PlatformServicesIsTheOnlyFileInTheAppWithAWindowsDirective()
    {
        var offenders = SourceFiles(AppProject)
            .Where(f => Path.GetFileName(f) != SeamFile)
            .Where(f => WindowsDirective.IsMatch(File.ReadAllText(f)))
            .Select(Relative)
            .ToList();

        Assert.True(offenders.Count == 0,
            "The platform seam has leaked. Only PlatformServices.cs may contain #if WINDOWS, or the "
            + "macOS build stops being a faithful rehearsal of the Windows one. Offending files:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheSeamFileStillExistsAndStillHoldsTheSwitch()
    {
        // Guards against the test above passing for the wrong reason - if PlatformServices.cs were
        // renamed or emptied, "no other file has a directive" becomes trivially true.
        var seam = SourceFiles(AppProject).SingleOrDefault(f => Path.GetFileName(f) == SeamFile);

        Assert.NotNull(seam);
        Assert.Matches(WindowsDirective, File.ReadAllText(seam));
    }

    [Fact]
    public void CoreNeverReferencesTheWindowsProjects()
    {
        // Core is the part that must build and run anywhere - all the logic and all the tests. A
        // using of the Interop or Windows assembly there would make the whole project Windows-only
        // and would not fail on a Windows developer's machine.
        var offenders = SourceFiles(CoreProject)
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains("GlassHudTranslator.Interop", StringComparison.Ordinal)
                       || text.Contains("GlassHudTranslator.Windows", StringComparison.Ordinal);
            })
            .Select(Relative)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Core referenced a Windows-only project:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void CoreHasNoConditionalCompilationAtAll()
    {
        // Not a style preference. A platform branch inside Core means the tests exercise one arm and
        // users get the other, which is the one situation this architecture is built to avoid.
        var offenders = SourceFiles(CoreProject)
            .Where(f => WindowsDirective.IsMatch(File.ReadAllText(f)))
            .Select(Relative)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Core must be platform-neutral:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheAppStillMultiTargetsSoThePlatformAnalyserKeepsWorking()
    {
        // net10.0 alongside net10.0-windows is what makes the compiler tell the truth about which
        // code is Windows-only. Dropping the neutral TFM would silence [SupportedOSPlatform] and
        // the seam would erode with nothing complaining.
        var csproj = File.ReadAllText(Path.Combine(
            TestPaths.RepoRoot, AppProject, "GlassHudTranslator.App.csproj"));

        Assert.Contains("net10.0;net10.0-windows", csproj, StringComparison.Ordinal);
    }

    private static IEnumerable<string> SourceFiles(string project) =>
        Directory.EnumerateFiles(Path.Combine(TestPaths.RepoRoot, project), "*.cs",
                SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal));

    private static string Relative(string path) =>
        Path.GetRelativePath(TestPaths.RepoRoot, path);
}
