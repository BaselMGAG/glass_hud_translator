using Microsoft.Data.Sqlite;

namespace GlassHudTranslator.Core.Storage;

public sealed record CachedTranslation(
    string Key,
    string Source,
    string Arabic,
    string Provider,
    string Model,
    bool IsOverride,
    DateTimeOffset CreatedAt,
    long Hits);

public readonly record struct CacheStats(long Entries, long Overrides, long Hits, long Lookups)
{
    public double HitRate => Lookups == 0 ? 0 : (double)Hits / Lookups;
}

public interface ITranslationCache
{
    Task<CachedTranslation?> TryGetAsync(string key, CancellationToken ct);
    Task PutAsync(CachedTranslation entry, CancellationToken ct);
    Task PutOverrideAsync(string key, string source, string arabic, CancellationToken ct);
    Task<CacheStats> GetStatsAsync(CancellationToken ct);
}

/// <summary>
/// The cache is load-bearing rather than an optimisation: the network is a hard dependency once
/// there is no local model, so every hit is a line that survives an outage (brief 2.7). It is also
/// the main quota lever - hit rate below 10% after a few sessions means a normalisation bug, not a
/// content problem, which is why lookups and hits are counted persistently rather than in memory.
/// </summary>
public sealed class SqliteTranslationCache(AppDatabase db) : ITranslationCache
{
    private const string LookupCounter = "cache_lookups";
    private const string HitCounter = "cache_hits";

    public async Task<CachedTranslation?> TryGetAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var entry = await db.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT key, source, arabic, provider, model, is_override, created_at, hits
                FROM translations WHERE key = $key;
                """;
            command.Parameters.AddWithValue("$key", key);

            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;

            return new CachedTranslation(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetInt64(5) != 0,
                DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(6)), reader.GetInt64(7));
        }, ct).ConfigureAwait(false);

        await BumpAsync(LookupCounter, ct).ConfigureAwait(false);
        if (entry is null) return null;

        await BumpAsync(HitCounter, ct).ConfigureAwait(false);
        await db.ExecuteAsync("UPDATE translations SET hits = hits + 1 WHERE key = $key;", ct,
            ("$key", key)).ConfigureAwait(false);

        return entry with { Hits = entry.Hits + 1 };
    }

    public Task PutAsync(CachedTranslation entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // A manual correction always wins, so a later automatic translation of the same line must
        // not overwrite it (brief 12).
        return db.ExecuteAsync("""
            INSERT INTO translations (key, source, arabic, provider, model, is_override, created_at, hits)
            VALUES ($key, $source, $arabic, $provider, $model, $override, $at, 0)
            ON CONFLICT(key) DO UPDATE SET
              source = excluded.source, arabic = excluded.arabic,
              provider = excluded.provider, model = excluded.model, created_at = excluded.created_at
            WHERE translations.is_override = 0;
            """, ct,
            ("$key", entry.Key), ("$source", entry.Source), ("$arabic", entry.Arabic),
            ("$provider", entry.Provider), ("$model", entry.Model),
            ("$override", entry.IsOverride ? 1 : 0),
            ("$at", entry.CreatedAt.ToUnixTimeSeconds()));
    }

    public Task PutOverrideAsync(string key, string source, string arabic, CancellationToken ct) =>
        db.ExecuteAsync("""
            INSERT INTO translations (key, source, arabic, provider, model, is_override, created_at, hits)
            VALUES ($key, $source, $arabic, 'manual', 'manual', 1, $at, 0)
            ON CONFLICT(key) DO UPDATE SET
              arabic = excluded.arabic, source = excluded.source,
              provider = 'manual', model = 'manual', is_override = 1, created_at = excluded.created_at;
            """, ct,
            ("$key", key), ("$source", source), ("$arabic", arabic),
            ("$at", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    public async Task<CacheStats> GetStatsAsync(CancellationToken ct) => new(
        Entries: Convert.ToInt64(await db.ScalarAsync("SELECT COUNT(*) FROM translations;", ct).ConfigureAwait(false)),
        Overrides: Convert.ToInt64(await db.ScalarAsync("SELECT COUNT(*) FROM translations WHERE is_override = 1;", ct).ConfigureAwait(false)),
        Hits: await ReadCounterAsync(HitCounter, ct).ConfigureAwait(false),
        Lookups: await ReadCounterAsync(LookupCounter, ct).ConfigureAwait(false));

    private Task BumpAsync(string name, CancellationToken ct) =>
        db.ExecuteAsync("""
            INSERT INTO counters (name, value) VALUES ($name, 1)
            ON CONFLICT(name) DO UPDATE SET value = value + 1;
            """, ct, ("$name", name));

    private async Task<long> ReadCounterAsync(string name, CancellationToken ct)
    {
        var value = await db.ScalarAsync("SELECT value FROM counters WHERE name = $name;", ct,
            ("$name", name)).ConfigureAwait(false);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }
}
