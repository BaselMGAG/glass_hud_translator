namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// Locates the repository from wherever the test binary happens to be running.
///
/// <para>
/// Several tests assert against files that ship with the repo rather than fixtures they build -
/// profiles/, data/models.json - because those files are edited by hand and a mistake in one of
/// them is invisible until a translation quietly falls back to English at play time.
/// </para>
/// </summary>
internal static class TestPaths
{
    public static string RepoRoot { get; } = Find();

    public static string Profiles => Path.Combine(RepoRoot, "profiles");

    public static string Data => Path.Combine(RepoRoot, "data");

    private static string Find()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "GlassHudTranslator.slnx")))
            dir = Path.GetDirectoryName(dir);

        return dir ?? Directory.GetCurrentDirectory();
    }
}
