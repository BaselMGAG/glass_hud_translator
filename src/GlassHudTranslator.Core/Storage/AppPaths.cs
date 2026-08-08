namespace GlassHudTranslator.Core.Storage;

/// <summary>
/// Where the app keeps its state — the API keys, the database, the settings and the user's own game
/// profiles. Resolves under the OS application-data folder (<c>%APPDATA%\GlassHudTranslator</c> on
/// Windows), so the same code runs everywhere without a platform check leaking out of here.
/// </summary>
public static class AppPaths
{
    public const string FolderName = "GlassHudTranslator";

    /// <summary>
    /// The folder used before the v0.2.0 rename. Moved from on first run.
    ///
    /// <para>
    /// This shipped broken: both constants were written as the new name, so the guard below
    /// (<c>!Exists(current) &amp;&amp; Exists(legacy)</c>) compared a path to itself and could never
    /// be true. The migration never ran once, and the commit that introduced it says in its own
    /// message that without it "everyone's API keys, capture regions and cache would silently
    /// vanish" — which is exactly what happened to anyone who had used the app before the rename.
    /// </para>
    ///
    /// <para>
    /// Fixed rather than deleted. Deleting it would make that data loss permanent for anyone who
    /// still has the old folder, and the move is safe: it only fires when there is nothing at the
    /// new location to overwrite. There is a test.
    /// </para>
    /// </summary>
    private const string LegacyFolderName = "GamingTranslatorGlassHUD";

    public static string DataDirectory { get; } = Resolve();

    private static string Resolve() => ResolveUnder(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create));

    /// <summary>
    /// The data directory under a given application-data root, migrating the pre-rename folder if
    /// one is there. Takes the root as a parameter purely so this can be tested — the one time this
    /// logic shipped untested, it was inert for its entire life.
    /// </summary>
    internal static string ResolveUnder(string root)
    {
        var current = Path.Combine(root, FolderName);
        var legacy = Path.Combine(root, LegacyFolderName);

        // Renaming the app must not silently orphan someone's API keys, capture regions and
        // translation cache. Only moves when there is nothing at the new location to overwrite.
        if (!Directory.Exists(current) && Directory.Exists(legacy))
        {
            try
            {
                Directory.Move(legacy, current);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Fall back to the old location rather than starting empty and losing their setup.
                return legacy;
            }
        }

        return current;
    }

    public static string Database => Path.Combine(DataDirectory, "glasshud.db");

    public static string Settings => Path.Combine(DataDirectory, "config.json");

    /// <summary>
    /// Game profiles the user created or edited.
    ///
    /// <para>
    /// Deliberately here and not in the app's own <c>profiles/</c> folder. That folder ships with
    /// the app and is replaced wholesale by an update - the release notes say as much - so a
    /// profile written there would be deleted the first time the user updated, taking the regions,
    /// glossary and setup with it. Here it sits beside their keys and database, which already
    /// survive.
    /// </para>
    /// </summary>
    public static string UserProfiles => Path.Combine(DataDirectory, "profiles");

    /// <summary>Dev-only secret file. Never written on Windows - see DpapiSecretStore.</summary>
    public static string DevSecrets => Path.Combine(DataDirectory, "secrets.dev.json");

    public static string Ensure()
    {
        Directory.CreateDirectory(DataDirectory);
        return DataDirectory;
    }
}
