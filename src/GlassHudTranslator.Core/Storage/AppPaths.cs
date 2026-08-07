namespace GlassHudTranslator.Core.Storage;

/// <summary>
/// Where the app keeps its state. Resolves to %APPDATA%\GlassHudTranslator on Windows and
/// ~/.config/GlassHudTranslator on macOS, so the same code runs in both places without a platform
/// check leaking out of here.
/// </summary>
public static class AppPaths
{
    public const string FolderName = "GlassHudTranslator";

    /// <summary>The name used before the app was renamed. Migrated from on first run.</summary>
    private const string LegacyFolderName = "GlassHudTranslator";

    public static string DataDirectory { get; } = Resolve();

    private static string Resolve()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);

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
