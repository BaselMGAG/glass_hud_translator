using Microsoft.Data.Sqlite;

namespace GamingTranslatorGlassHUD.Core.Storage;

/// <summary>
/// Owns the single SQLite connection and the schema. One file, WAL, migrate on open.
///
/// <para>
/// A single connection guarded by a semaphore rather than a pool: throughput here is a handful of
/// statements per second at most, and serialising removes any question about SQLite write locking
/// while the overlay and the capture loop touch the database from different threads.
/// </para>
/// </summary>
public sealed class AppDatabase : IAsyncDisposable
{
    private const int SchemaVersion = 1;

    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AppDatabase(SqliteConnection connection) => _connection = connection;

    public static Task<AppDatabase> OpenAsync(string path, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        return OpenCoreAsync(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString(), ct);
    }

    /// <summary>For tests. The connection must stay open or a shared-cache memory database vanishes.</summary>
    public static Task<AppDatabase> OpenInMemoryAsync(CancellationToken ct = default) =>
        OpenCoreAsync(new SqliteConnectionStringBuilder
        {
            DataSource = $"glasshud-test-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString(), ct);

    private static async Task<AppDatabase> OpenCoreAsync(string connectionString, CancellationToken ct)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var db = new AppDatabase(connection);
        await db.InitialiseAsync(ct).ConfigureAwait(false);
        return db;
    }

    private async Task InitialiseAsync(CancellationToken ct)
    {
        // WAL keeps a read during capture from blocking a write from the translation task.
        // It is a no-op for :memory:, which is fine.
        await ExecuteAsync("PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);
        await ExecuteAsync("PRAGMA synchronous=NORMAL;", ct).ConfigureAwait(false);

        var version = Convert.ToInt32(await ScalarAsync("PRAGMA user_version;", ct).ConfigureAwait(false));
        if (version >= SchemaVersion) return;

        await ExecuteAsync(Schema, ct).ConfigureAwait(false);
        await ExecuteAsync($"PRAGMA user_version={SchemaVersion};", ct).ConfigureAwait(false);
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS translations (
          key         TEXT PRIMARY KEY,
          source      TEXT NOT NULL,
          arabic      TEXT NOT NULL,
          provider    TEXT NOT NULL,
          model       TEXT NOT NULL,
          is_override INTEGER NOT NULL DEFAULT 0,
          created_at  INTEGER NOT NULL,
          hits        INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS translation_log (
          id          INTEGER PRIMARY KEY AUTOINCREMENT,
          at          INTEGER NOT NULL,
          raw_ocr     TEXT NOT NULL,
          normalized  TEXT NOT NULL,
          speaker     TEXT,
          provider    TEXT,
          model       TEXT,
          arabic      TEXT,
          latency_ms  INTEGER,
          from_cache  INTEGER NOT NULL,
          outcome     TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_log_at ON translation_log(at);
        CREATE INDEX IF NOT EXISTS ix_log_outcome ON translation_log(outcome);

        CREATE TABLE IF NOT EXISTS quota (
          provider    TEXT NOT NULL,
          day_pacific TEXT NOT NULL,
          used        INTEGER NOT NULL,
          PRIMARY KEY (provider, day_pacific)
        );

        CREATE TABLE IF NOT EXISTS region_profiles (
          name        TEXT PRIMARY KEY,
          resolution  TEXT NOT NULL,
          ui_scale    REAL NOT NULL,
          rel_x REAL NOT NULL, rel_y REAL NOT NULL, rel_w REAL NOT NULL, rel_h REAL NOT NULL
        );

        CREATE TABLE IF NOT EXISTS counters (
          name  TEXT PRIMARY KEY,
          value INTEGER NOT NULL
        );
        """;

    internal async Task<T> WithConnectionAsync<T>(
        Func<SqliteConnection, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await work(_connection, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal Task ExecuteAsync(string sql, CancellationToken ct, params (string Name, object? Value)[] parameters) =>
        WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);

    internal Task<object?> ScalarAsync(string sql, CancellationToken ct, params (string Name, object? Value)[] parameters) =>
        WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        }, ct);

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
