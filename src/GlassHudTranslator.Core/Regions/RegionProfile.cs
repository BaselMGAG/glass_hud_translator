using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Storage;
using Microsoft.Data.Sqlite;

namespace GlassHudTranslator.Core.Regions;

/// <summary>
/// A capture rectangle stored as fractions of the FFXIV client rect, keyed to the resolution and
/// UI scale it was drawn at (brief 8).
///
/// <para>
/// Fractions rather than desktop coordinates so the profile survives the window being moved, and
/// keyed to (resolution, UI scale) because the dialogue box does not sit at the same relative
/// position when either changes.
/// </para>
/// </summary>
public sealed record RegionProfile(
    string Name,
    string Resolution,
    double UiScale,
    double RelX,
    double RelY,
    double RelWidth,
    double RelHeight)
{
    /// <summary>FFXIV renders narrative text in at least three places (brief 8).</summary>
    public static class Names
    {
        public const string Dialogue = "dialogue";
        public const string Subtitle = "subtitle";
        public const string Quest = "quest";

        public static readonly string[] All = [Dialogue, Subtitle, Quest];
    }

    public CaptureRegion Resolve(int clientWidth, int clientHeight) => new(
        (int)Math.Round(RelX * clientWidth),
        (int)Math.Round(RelY * clientHeight),
        (int)Math.Round(RelWidth * clientWidth),
        (int)Math.Round(RelHeight * clientHeight));

    public static RegionProfile FromPixels(
        string name, CaptureRegion region, int clientWidth, int clientHeight, double uiScale) => new(
        name,
        $"{clientWidth}x{clientHeight}",
        uiScale,
        (double)region.X / clientWidth,
        (double)region.Y / clientHeight,
        (double)region.Width / clientWidth,
        (double)region.Height / clientHeight);

    /// <summary>Where FFXIV's NPC dialogue box sits by default, as a starting point for the picker.</summary>
    public static RegionProfile Default(string name) => name switch
    {
        Names.Subtitle => new RegionProfile(name, "unknown", 1.0, 0.20, 0.78, 0.60, 0.12),
        Names.Quest => new RegionProfile(name, "unknown", 1.0, 0.30, 0.25, 0.40, 0.40),
        _ => new RegionProfile(name, "unknown", 1.0, 0.22, 0.70, 0.56, 0.20),
    };
}

public sealed class RegionProfileStore(AppDatabase db)
{
    public Task SaveAsync(string gameProfileId, RegionProfile profile, CancellationToken ct) =>
        db.ExecuteAsync("""
            INSERT INTO region_profiles (profile, name, resolution, ui_scale, rel_x, rel_y, rel_w, rel_h)
            VALUES ($profile, $name, $resolution, $scale, $x, $y, $w, $h)
            ON CONFLICT(profile, name) DO UPDATE SET
              resolution = excluded.resolution, ui_scale = excluded.ui_scale,
              rel_x = excluded.rel_x, rel_y = excluded.rel_y,
              rel_w = excluded.rel_w, rel_h = excluded.rel_h;
            """, ct,
            ("$profile", gameProfileId),
            ("$name", profile.Name), ("$resolution", profile.Resolution), ("$scale", profile.UiScale),
            ("$x", profile.RelX), ("$y", profile.RelY), ("$w", profile.RelWidth), ("$h", profile.RelHeight));

    public Task<RegionProfile?> LoadAsync(string gameProfileId, string name, CancellationToken ct) =>
        db.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT name, resolution, ui_scale, rel_x, rel_y, rel_w, rel_h
                FROM region_profiles WHERE profile = $profile AND name = $name;
                """;
            command.Parameters.AddWithValue("$profile", gameProfileId);
            command.Parameters.AddWithValue("$name", name);

            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;

            return new RegionProfile(reader.GetString(0), reader.GetString(1), reader.GetDouble(2),
                reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6));
        }, ct);

    public async Task<RegionProfile> LoadOrDefaultAsync(string gameProfileId, string name, CancellationToken ct) =>
        await LoadAsync(gameProfileId, name, ct).ConfigureAwait(false) ?? RegionProfile.Default(name);

    /// <summary>True once the user has picked this region themselves rather than inheriting a default.</summary>
    public async Task<bool> HasAsync(string gameProfileId, string name, CancellationToken ct) =>
        await LoadAsync(gameProfileId, name, ct).ConfigureAwait(false) is not null;

    /// <summary>
    /// Forgets every rectangle belonging to one game profile. Called when a profile is deleted:
    /// without it the rows outlive the profile, and a later profile that happened to slug to the
    /// same id would silently inherit rectangles dragged for a different game.
    /// </summary>
    public Task DeleteAllAsync(string gameProfileId, CancellationToken ct) =>
        db.ExecuteAsync("DELETE FROM region_profiles WHERE profile = $profile;", ct,
            ("$profile", gameProfileId));
}
