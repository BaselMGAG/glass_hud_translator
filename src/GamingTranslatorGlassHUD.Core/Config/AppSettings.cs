using System.Text.Json;
using System.Text.Json.Serialization;
using GamingTranslatorGlassHUD.Core.Platform;
using GamingTranslatorGlassHUD.Core.Storage;
using GamingTranslatorGlassHUD.Core.Translation;

namespace GamingTranslatorGlassHUD.Core.Config;

/// <summary>
/// User settings, stored as JSON next to the database. Deliberately plain and hand-editable - if
/// the UI ever gets a binding wrong, the file is the escape hatch.
///
/// <para>
/// API keys are NOT here. They go through <see cref="ISecretStore"/>, which is DPAPI on Windows.
/// </para>
/// </summary>
public sealed record AppSettings
{
    [JsonPropertyName("profile")] public string? ProfileId { get; set; }

    [JsonPropertyName("register")] public ArabicRegister Register { get; set; } = ArabicRegister.ModernStandard;

    [JsonPropertyName("overlayFontSize")] public double OverlayFontSize { get; set; } = 26;

    [JsonPropertyName("overlayOpacity")] public double OverlayOpacity { get; set; } = 0.82;

    /// <summary>Auto-watch poll rate. 2 fps rather than 3 keeps headroom on weak hardware.</summary>
    [JsonPropertyName("autoWatchFps")] public double AutoWatchFps { get; set; } = 2;

    /// <summary>
    /// Auto-watch turns itself off after this long with no change on screen. A toggle left on
    /// during an AFK is the main way to leak API quota, so this is not optional.
    /// </summary>
    [JsonPropertyName("autoWatchExpirySeconds")] public int AutoWatchExpirySeconds { get; set; } = 90;

    /// <summary>Bindings as display strings, e.g. "Ctrl+Shift+T". Missing entries fall back to defaults.</summary>
    [JsonPropertyName("hotkeys")] public Dictionary<string, string> Hotkeys { get; set; } = [];

    [JsonPropertyName("lastRegionProfile")] public string LastRegionProfile { get; set; } = "dialogue";

    [JsonPropertyName("hasCompletedFirstRun")] public bool HasCompletedFirstRun { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public Hotkey HotkeyFor(HotkeyAction action) =>
        (Hotkeys.TryGetValue(action.ToString(), out var text) ? Hotkey.TryParse(text) : null)
        ?? DefaultHotkeys.All[action];

    public void SetHotkey(HotkeyAction action, Hotkey hotkey) =>
        Hotkeys[action.ToString()] = hotkey.ToString();

    public IReadOnlyDictionary<HotkeyAction, Hotkey> ResolvedHotkeys() =>
        Enum.GetValues<HotkeyAction>().ToDictionary(a => a, HotkeyFor);

    /// <summary>
    /// Two actions on one combination means one of them silently never fires, which is miserable to
    /// diagnose from the outside. Reported so the UI can say so plainly.
    /// </summary>
    public IReadOnlyList<HotkeyAction> FindConflicts()
    {
        var resolved = ResolvedHotkeys();
        return resolved
            .GroupBy(kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(kv => kv.Key))
            .ToList();
    }

    public static AppSettings Load(string? path = null)
    {
        path ??= AppPaths.Settings;
        if (!File.Exists(path)) return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // A corrupt settings file must not stop the app starting - defaults are always usable.
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.Settings;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }
}
