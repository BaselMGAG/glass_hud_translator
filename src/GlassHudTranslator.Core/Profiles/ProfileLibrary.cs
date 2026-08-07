using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GlassHudTranslator.Core.Glossary;

namespace GlassHudTranslator.Core.Profiles;

/// <summary>Where a profile's files actually live, which decides what may be done to it.</summary>
public enum ProfileOrigin
{
    /// <summary>Shipped in the app folder. Replaced wholesale by an update.</summary>
    Bundled,

    /// <summary>Created by the user, under their own data directory.</summary>
    User,

    /// <summary>A bundled profile the user has edited. Their copy shadows the shipped one.</summary>
    Override,
}

/// <summary>What the user typed into the profile editor, before it becomes files on disk.</summary>
public sealed record GameProfileDraft
{
    /// <summary>Null when creating. Set when editing, so the id and its saved regions survive.</summary>
    public string? ExistingId { get; init; }

    public required string DisplayName { get; init; }
    public string[] WindowTitles { get; init; } = [];
    public string[] ProcessNames { get; init; } = [];
    public string SourceLanguage { get; init; } = "eng";
    public string? StyleHint { get; init; }
    public bool HasSpeakerNames { get; init; } = true;
    public IReadOnlyList<GlossaryTerm> Terms { get; init; } = [];
}

/// <summary>
/// The profiles a user can choose between, across both places they can live.
///
/// <para>
/// Profiles ship in the app folder, and the app folder is replaced wholesale by an update - the
/// release notes say so in both languages. So anything the user creates or edits has to be written
/// somewhere else, next to their keys and database, or the first update after they set up a game
/// would silently delete it. This class merges the two roots and keeps the rule in one place:
/// <b>the user's copy always wins</b>, and the shipped one is left untouched underneath it so it
/// can keep improving with each release.
/// </para>
///
/// <para>
/// Deleting a bundled profile is therefore not a file deletion - the next update would restore it.
/// It is recorded as a tombstone in the user's directory instead, which is what makes "delete
/// Final Fantasy XIV because I don't play it" stay deleted.
/// </para>
/// </summary>
public sealed class ProfileLibrary(string bundledRoot, string userRoot)
{
    /// <summary>
    /// The screen-relative profile. Read-only and undeletable on purpose: it is the fallback that
    /// works on anything, it is what the app falls back to when a game profile is removed, and it
    /// carries no game-specific content anyone would want to change.
    /// </summary>
    public const string GeneralProfileId = "general";

    private const string TombstoneFileName = "_removed.json";

    public string UserRoot { get; } = userRoot;

    /// <summary>Every profile the user may choose, bundled and their own, minus anything removed.</summary>
    public IReadOnlyList<string> Discover()
    {
        var removed = Tombstones();

        return GameProfileStore.Discover(bundledRoot)
            .Concat(GameProfileStore.Discover(userRoot))
            .Where(id => !removed.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    public ProfileOrigin OriginOf(string id)
    {
        var inUser = File.Exists(Path.Combine(userRoot, id, GameProfileStore.ProfileFileName));
        var inBundled = File.Exists(Path.Combine(bundledRoot, id, GameProfileStore.ProfileFileName));

        return (inUser, inBundled) switch
        {
            (true, true) => ProfileOrigin.Override,
            (true, false) => ProfileOrigin.User,
            _ => ProfileOrigin.Bundled,
        };
    }

    /// <summary>Only the general profile. Everything else, including the shipped game, is editable.</summary>
    public static bool IsReadOnly(string id) =>
        id.Equals(GeneralProfileId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The general profile can never be removed. It is the escape hatch that reads anything on
    /// screen, and deleting the last profile would leave the app with nothing to translate against.
    /// </summary>
    public static bool CanDelete(string id) => !IsReadOnly(id);

    public GameProfile Load(string id) =>
        OriginOf(id) is ProfileOrigin.Bundled
            ? GameProfileStore.Load(bundledRoot, id)
            : GameProfileStore.Load(userRoot, id);

    public GameProfile LoadOrFallback(string? preferredId)
    {
        var available = Discover();
        if (available.Count == 0) return new GameProfile { Id = "generic", DisplayName = "Unknown game" };

        var id = preferredId is not null &&
                 available.Contains(preferredId, StringComparer.OrdinalIgnoreCase)
            ? preferredId
            : available[0];

        try
        {
            return Load(id);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException)
        {
            // A profile the user hand-edited into invalid JSON must not stop the app starting.
            return available.Count > 1 && id != available[0]
                ? Load(available[0])
                : new GameProfile { Id = "generic", DisplayName = "Unknown game" };
        }
    }

    /// <summary>
    /// Writes a draft as a profile folder and returns its id. Always writes to the user root, even
    /// when editing a bundled profile - that is what makes the edit survive the next update.
    /// </summary>
    public string Save(GameProfileDraft draft)
    {
        var id = draft.ExistingId ?? UniqueId(SlugFor(draft.DisplayName));
        var directory = Path.Combine(userRoot, id);
        Directory.CreateDirectory(directory);

        var profile = new GameProfile
        {
            Id = id,
            DisplayName = draft.DisplayName.Trim(),
            WindowTitles = Clean(draft.WindowTitles),
            ProcessNames = Clean(draft.ProcessNames),
            SourceLanguage = string.IsNullOrWhiteSpace(draft.SourceLanguage) ? "eng" : draft.SourceLanguage,
            StyleHint = string.IsNullOrWhiteSpace(draft.StyleHint) ? null : draft.StyleHint.Trim(),
            HasSpeakerNames = draft.HasSpeakerNames,

            // Deliberately not written: capture regions live in the database, keyed by profile id,
            // and are dragged rather than typed. The starting rectangles in a bundled profile.json
            // exist only so a shipped profile is usable before anyone drags anything.
            Regions = PreservedRegions(id),
        };

        WriteJson(Path.Combine(directory, GameProfileStore.ProfileFileName), profile);
        WriteGlossary(Path.Combine(directory, GameProfileStore.GlossaryFileName), draft.Terms);

        // Created empty rather than left absent, so the folder is a complete, shareable profile and
        // whoever opens it can see where OCR fixes are meant to go.
        var corrections = Path.Combine(directory, GameProfileStore.CorrectionsFileName);
        if (!File.Exists(corrections)) File.WriteAllText(corrections, "{}\n", Encoding.UTF8);

        // An edit un-deletes: the user is plainly not trying to remove it.
        RemoveTombstone(id);
        return id;
    }

    /// <summary>
    /// Removes a profile. For one the user made, that is the folder. For a shipped one it is a
    /// tombstone, because deleting the files would only work until the next update restored them.
    /// </summary>
    public void Delete(string id)
    {
        if (!CanDelete(id))
            throw new InvalidOperationException($"'{id}' cannot be deleted.");

        var directory = Path.Combine(userRoot, id);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);

        if (File.Exists(Path.Combine(bundledRoot, id, GameProfileStore.ProfileFileName)))
            AddTombstone(id);
    }

    /// <summary>
    /// Reverts a bundled profile to the shipped version by dropping the user's copy. Nothing calls
    /// this for a user-created profile - there is no shipped version to fall back to.
    /// </summary>
    public bool Reset(string id)
    {
        if (OriginOf(id) is not ProfileOrigin.Override) return false;

        Directory.Delete(Path.Combine(userRoot, id), recursive: true);
        return true;
    }

    // ── ids ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A folder name from a display name. Restrictive on purpose: this string becomes a path, and
    /// it comes from a text box, so anything that could climb out of the profiles directory or
    /// collide with a Windows reserved name has to be gone before it reaches the filesystem.
    /// </summary>
    public static string SlugFor(string displayName)
    {
        var slug = new StringBuilder();
        foreach (var c in displayName.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }

        var result = slug.ToString().Trim('-');

        // Non-Latin names - Arabic, Japanese - slug to nothing. They still need a folder.
        if (result.Length == 0) result = "game";

        // Leading underscore is how the loader marks a folder as not-a-profile (_template).
        if (result.StartsWith('_')) result = result.TrimStart('_');

        return result.Length > 40 ? result[..40].TrimEnd('-') : result;
    }

    private string UniqueId(string baseId)
    {
        var taken = Discover()
            .Concat(GameProfileStore.Discover(userRoot))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(baseId)) return baseId;

        for (var n = 2; n < 1000; n++)
            if (!taken.Contains($"{baseId}-{n}"))
                return $"{baseId}-{n}";

        return $"{baseId}-{Guid.NewGuid():N}";
    }

    // ── tombstones ────────────────────────────────────────────────────────────────────────

    private HashSet<string> Tombstones()
    {
        var path = Path.Combine(userRoot, TombstoneFileName);
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var read = JsonSerializer.Deserialize<TombstoneFile>(File.ReadAllText(path));
            return (read?.Removed ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // A corrupt tombstone file must not hide every profile. Better to show one the user
            // deleted than to start with an empty list they cannot explain.
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void AddTombstone(string id)
    {
        var removed = Tombstones();
        removed.Add(id);
        WriteTombstones(removed);
    }

    private void RemoveTombstone(string id)
    {
        var removed = Tombstones();
        if (removed.Remove(id)) WriteTombstones(removed);
    }

    private void WriteTombstones(HashSet<string> removed)
    {
        Directory.CreateDirectory(userRoot);
        WriteJson(Path.Combine(userRoot, TombstoneFileName),
            new TombstoneFile { Removed = removed.OrderBy(r => r, StringComparer.Ordinal).ToArray() });
    }

    private sealed record TombstoneFile
    {
        [JsonPropertyName("removed")] public string[] Removed { get; init; } = [];

        [JsonPropertyName("_comment")]
        public string Comment { get; init; } =
            "Bundled profiles the user removed. Recorded here rather than deleted, because the app "
            + "folder is replaced by an update and the files would come back.";
    }

    // ── writing ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Keeps whatever starting rectangles the profile already had. Editing the name or the voice of
    /// a game must not throw away the rectangles that make a shared profile useful to the next
    /// person who imports it.
    /// </summary>
    private Dictionary<string, ProfileRegion> PreservedRegions(string id)
    {
        try
        {
            return OriginOf(id) is ProfileOrigin.Bundled && !Directory.Exists(Path.Combine(userRoot, id))
                ? new Dictionary<string, ProfileRegion>(GameProfileStore.Load(bundledRoot, id).Regions)
                : new Dictionary<string, ProfileRegion>(Load(id).Regions);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException
                                      or FileNotFoundException)
        {
            return [];
        }
    }

    private static string[] Clean(string[] values) => values
        .Select(v => v.Trim())
        .Where(v => v.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,

        // Profile files are meant to be read, hand-edited and shared as plain text. The default
        // encoder escapes every non-ASCII character, which would turn an Arabic glossary into
        // pages of أ and make the file useless to the people most likely to fix it.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, WriteOptions) + "\n", Encoding.UTF8);

    private static void WriteGlossary(string path, IReadOnlyList<GlossaryTerm> terms) =>
        WriteJson(path, terms
            .Where(t => !string.IsNullOrWhiteSpace(t.En) && !string.IsNullOrWhiteSpace(t.Ar))
            .Select(t => new GlossaryTerm(t.En.Trim(), t.Ar.Trim(),
                string.IsNullOrWhiteSpace(t.Type) ? "term" : t.Type, t.Aliases ?? []))
            .ToArray());
}
