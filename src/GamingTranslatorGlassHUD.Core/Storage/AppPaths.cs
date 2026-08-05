namespace GamingTranslatorGlassHUD.Core.Storage;

/// <summary>
/// Where the app keeps its state. Resolves to %APPDATA%\GamingTranslatorGlassHUD on Windows and
/// ~/.config/GamingTranslatorGlassHUD on macOS, so the same code runs in both places without a platform
/// check leaking out of here.
/// </summary>
public static class AppPaths
{
    public const string FolderName = "GamingTranslatorGlassHUD";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create),
        FolderName);

    public static string Database => Path.Combine(DataDirectory, "glasshud.db");

    public static string Settings => Path.Combine(DataDirectory, "config.json");

    /// <summary>Dev-only secret file. Never written on Windows - see DpapiSecretStore.</summary>
    public static string DevSecrets => Path.Combine(DataDirectory, "secrets.dev.json");

    public static string Ensure()
    {
        Directory.CreateDirectory(DataDirectory);
        return DataDirectory;
    }
}
