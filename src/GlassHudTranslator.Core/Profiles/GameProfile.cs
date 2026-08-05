using System.Text.Json;
using System.Text.Json.Serialization;
using GlassHudTranslator.Core.Glossary;
using GlassHudTranslator.Core.Regions;
using GlassHudTranslator.Core.Text;

namespace GlassHudTranslator.Core.Profiles;

/// <summary>
/// Everything that is specific to one game, in one folder.
///
/// <para>
/// Nothing about the capture-OCR-translate pipeline is tied to a particular title. What differs
/// between games is only: where the text sits on screen, what the proper nouns are, how the writing
/// is meant to sound, and which characters the OCR tends to get wrong. Putting all four in a
/// profile folder means supporting a new game is a data task, not a code change - anyone can add
/// one without opening the solution.
/// </para>
///
/// <para>
/// Final Fantasy XIV is the profile everything was designed and tested against, so it is the most
/// complete one and doubles as the worked example.
/// </para>
/// </summary>
public sealed record GameProfile
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }

    /// <summary>Substrings used to find the game's window. Case-insensitive.</summary>
    [JsonPropertyName("windowTitles")] public string[] WindowTitles { get; init; } = [];

    /// <summary>Tesseract language code for the game's on-screen text.</summary>
    [JsonPropertyName("sourceLanguage")] public string SourceLanguage { get; init; } = "eng";

    /// <summary>
    /// One or two sentences describing how the writing should sound, dropped into the system
    /// prompt. This is what stops a solemn epic being translated in the register of a shop sign.
    /// </summary>
    [JsonPropertyName("styleHint")] public string? StyleHint { get; init; }

    /// <summary>Does the game put a speaker name on its own line above the text?</summary>
    [JsonPropertyName("hasSpeakerNames")] public bool HasSpeakerNames { get; init; } = true;

    [JsonPropertyName("notes")] public string? Notes { get; init; }

    /// <summary>Starting rectangles, as fractions of the client area, before the user drags their own.</summary>
    [JsonPropertyName("regions")] public Dictionary<string, ProfileRegion> Regions { get; init; } = [];

    [JsonIgnore] public GlossaryStore Glossary { get; init; } = GlossaryStore.Empty;
    [JsonIgnore] public OcrCorrections Corrections { get; init; } = OcrCorrections.Empty;

    public RegionProfile RegionOrDefault(string name) =>
        Regions.TryGetValue(name, out var r)
            ? new RegionProfile(name, "unknown", 1.0, r.X, r.Y, r.Width, r.Height)
            : RegionProfile.Default(name);
}

public sealed record ProfileRegion
{
    [JsonPropertyName("x")] public double X { get; init; }
    [JsonPropertyName("y")] public double Y { get; init; }
    [JsonPropertyName("w")] public double Width { get; init; }
    [JsonPropertyName("h")] public double Height { get; init; }
}

/// <summary>Discovers and loads profile folders. A profile is a directory with a profile.json.</summary>
public static class GameProfileStore
{
    public const string ProfileFileName = "profile.json";
    public const string GlossaryFileName = "glossary.json";
    public const string CorrectionsFileName = "ocr-corrections.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Profile ids found in <paramref name="profilesDirectory"/>, excluding "_"-prefixed ones.</summary>
    public static IReadOnlyList<string> Discover(string profilesDirectory)
    {
        if (!Directory.Exists(profilesDirectory)) return [];

        return Directory.GetDirectories(profilesDirectory)
            .Where(d => !Path.GetFileName(d).StartsWith('_'))
            .Where(d => File.Exists(Path.Combine(d, ProfileFileName)))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    public static GameProfile Load(string profilesDirectory, string id)
    {
        var directory = Path.Combine(profilesDirectory, id);
        var path = Path.Combine(directory, ProfileFileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No profile.json for game profile '{id}' in {directory}", path);

        var profile = JsonSerializer.Deserialize<GameProfile>(File.ReadAllText(path), Options)
                      ?? throw new InvalidDataException($"Could not read {path}");

        return profile with
        {
            Glossary = GlossaryStore.Load(Path.Combine(directory, GlossaryFileName)),
            Corrections = OcrCorrections.Load(Path.Combine(directory, CorrectionsFileName)),
        };
    }

    /// <summary>
    /// Loads the requested profile, or the only one available, or the first alphabetically. Missing
    /// profiles are a configuration problem rather than a crash - the app still runs with an empty
    /// glossary, it just translates less consistently.
    /// </summary>
    public static GameProfile LoadOrFallback(string profilesDirectory, string? preferredId)
    {
        var available = Discover(profilesDirectory);
        if (available.Count == 0)
        {
            return new GameProfile { Id = "generic", DisplayName = "Unknown game" };
        }

        var id = preferredId is not null && available.Contains(preferredId) ? preferredId : available[0];
        return Load(profilesDirectory, id);
    }
}
