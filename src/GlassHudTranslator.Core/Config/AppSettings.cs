using System.Text.Json;
using System.Text.Json.Serialization;
using GlassHudTranslator.Core.Platform;
using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Translation;

namespace GlassHudTranslator.Core.Config;

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

    /// <summary>
    /// Language of the interface itself, not of the translation. Defaults to English because that
    /// is what the documentation and screenshots show, but the point of switching it is that
    /// someone who needs Arabic subtitles should not have to read English to turn them on.
    /// </summary>
    [JsonPropertyName("language")] public UiLanguage Language { get; set; } = UiLanguage.English;

    [JsonPropertyName("register")] public ArabicRegister Register { get; set; } = ArabicRegister.ModernStandard;

    /// <summary>
    /// Whether the overlay shows tashkeel. Off, and false is also default(bool), so a settings file
    /// written before this existed lands on the right answer without a migration.
    ///
    /// <para>
    /// Off because unrequested short-vowel marks are a change of register rather than a nicety -
    /// fully vowelled text is how scripture, poetry and school primers are set - and because the
    /// models add them unevenly, so the same conversation came back half vowelled and half not
    /// depending on which model in the fallback chain answered which line.
    /// </para>
    /// </summary>
    [JsonPropertyName("diacritics")] public bool Diacritics { get; set; }

    [JsonPropertyName("overlayFontSize")] public double OverlayFontSize { get; set; } = 26;

    [JsonPropertyName("overlayOpacity")] public double OverlayOpacity { get; set; } = 0.82;

    /// <summary>
    /// Where the panel sits inside the game window, each 0 to 1 across the space the panel does
    /// not occupy. See <see cref="Capture.OverlayPlacement"/>; the defaults are centred and low,
    /// which is where a dialogue box usually is and where this used to be nailed down.
    /// </summary>
    [JsonPropertyName("overlayHorizontal")]
    public double OverlayHorizontal { get; set; } = Capture.OverlayPlacement.DefaultHorizontal;

    [JsonPropertyName("overlayVertical")]
    public double OverlayVertical { get; set; } = Capture.OverlayPlacement.DefaultVertical;

    /// <summary>
    /// What auto-watch is looking at. Decides the poll rate, how long it waits for the text to
    /// settle, the shortest gap between two translations, and both session caps — see
    /// <see cref="Capture.WatchPacing"/>, where every one of those numbers differs between the two.
    /// </summary>
    [JsonPropertyName("watchMode")] public Capture.WatchMode WatchMode { get; set; } = Capture.WatchMode.Dialogue;

    /// <summary>
    /// Whether the overlay hides itself from screen recorders. True, and the default has to stay
    /// true: without it our own capture includes the Arabic we just drew, OCR reads it back, and
    /// the app translates its own output. False is for someone who wants the translation in a
    /// recording or a stream, which is a fair thing to want and used to be impossible.
    /// </summary>
    [JsonPropertyName("hideOverlayFromCapture")] public bool HideOverlayFromCapture { get; set; } = true;

    /// <summary>
    /// Lets auto-watch run without a session cap. Off, and deliberately not the default: the cap
    /// exists because the idle timer it replaced could never fire on moving content, so switching
    /// this on is switching off the only guard there is. It still warns.
    /// </summary>
    [JsonPropertyName("watchWithoutLimit")] public bool WatchWithoutLimit { get; set; }

    /// <summary>
    /// The shortest gap between two translations, in seconds. Zero means "whatever the mode says",
    /// which is what almost everyone should leave it at.
    ///
    /// <para>
    /// Asked for directly by a player — «البرنامج محتاج أن المستخدم يتحكم في عدد ثواني الترجمه بدل
    /// انو تلقائي». This is the honest reading of that: the useful knob is not the poll rate, which
    /// only decides how quickly a change is noticed, but how often a translation is allowed to
    /// arrive. Turning it up slows the overlay down and spends less; turning it down does the
    /// reverse, up to the point where the text is arriving faster than it can be read.
    /// </para>
    /// </summary>
    [JsonPropertyName("secondsBetweenTranslations")]
    public double SecondsBetweenTranslations { get; set; }

    /// <summary>
    /// Overrides the mode's poll rate when above zero; zero uses the mode's own. Hand-edit only,
    /// and left that way on purpose: after measuring, the poll rate turned out to be worth about
    /// 300 ms of a 4.6-second delay, so exposing it would invite people to spend CPU on the wrong
    /// thing. The settle cap is what mattered, and that is adaptive now.
    /// </summary>
    [JsonPropertyName("autoWatchFps")] public double AutoWatchFps { get; set; }

    /// <summary>
    /// Auto-watch turns itself off after this long with no change on screen. A toggle left on
    /// during an AFK is the main way to leak API quota, so this is not optional.
    /// </summary>
    [JsonPropertyName("autoWatchExpirySeconds")] public int AutoWatchExpirySeconds { get; set; } = 90;

    /// <summary>Bindings as display strings, e.g. "Ctrl+Shift+T". Missing entries fall back to defaults.</summary>
    [JsonPropertyName("hotkeys")] public Dictionary<string, string> Hotkeys { get; set; } = [];

    /// <summary>
    /// Below this many characters the capture is treated as "no dialogue on screen" and nothing is
    /// sent. Stops an empty text box, or a stray UI border OCR'd as a stray glyph, costing a request.
    /// </summary>
    [JsonPropertyName("minimumCharactersToTranslate")]
    public int MinimumCharactersToTranslate { get; set; } = 3;

    [JsonPropertyName("lastRegionProfile")] public string LastRegionProfile { get; set; } = "dialogue";

    [JsonPropertyName("hasCompletedFirstRun")] public bool HasCompletedFirstRun { get; set; }

    /// <summary>
    /// Whether to ask GitHub, once a day, whether a newer release exists. On by default: the person
    /// this app is for is not going to be watching a repository for tags, and an app they cannot
    /// read the release notes of is one they will simply never update. It is the only request the
    /// app makes that is not a translation, so it is disclosed in both READMEs and switchable here.
    /// Nothing is sent but the request itself - no identifiers, no usage, no key.
    /// </summary>
    [JsonPropertyName("checkForUpdates")] public bool CheckForUpdates { get; set; } = true;

    [JsonPropertyName("lastUpdateCheckUtc")] public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>
    /// The newest release seen by a previous check. Lets the notice appear immediately on launch
    /// rather than only in the one session per day where the check actually runs - and it clears
    /// itself, because once the user has updated it is no longer newer than what is running.
    /// </summary>
    [JsonPropertyName("lastSeenRelease")] public string? LastSeenRelease { get; set; }

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
