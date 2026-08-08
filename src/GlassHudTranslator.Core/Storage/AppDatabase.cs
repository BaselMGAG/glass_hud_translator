using Microsoft.Data.Sqlite;

namespace GlassHudTranslator.Core.Storage;

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
    private const int SchemaVersion = 3;

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

    /// <summary>
    /// Schema steps, applied in order from whatever version the file is already at.
    ///
    /// <para>
    /// A ladder rather than one conditional block, and the difference is not stylistic. The previous
    /// shape was <c>if (version >= SchemaVersion) return;</c> followed by every migration in
    /// sequence — which works exactly once. The second migration written that way would be skipped
    /// entirely for every user already at the current version, which is all of them: the check
    /// passes, the function returns, and the new step never runs. The failure is silent and it
    /// lands on the people who have been using the app longest.
    /// </para>
    ///
    /// <para>
    /// Rules for adding a step. Append, never renumber — the index is persisted in
    /// <c>user_version</c> on real machines. Make each step idempotent anyway, because a process
    /// killed between the work and the version bump will re-run it. And **migrations are additive
    /// forever**: never rename a column, never drop one. There is deliberately no self-updater, so
    /// re-unzipping an older release is a supported recovery, and an older build opening a newer
    /// database proceeds without complaint — it will simply ignore what it does not know about,
    /// which is only safe if nothing it *did* know about has moved.
    /// </para>
    /// </summary>
    private IReadOnlyList<Func<CancellationToken, Task>> Migrations =>
    [
        // 0 → 1: nothing beyond the base schema, which has already been applied by the time any
        // step runs. Kept as an explicit no-op so the list index and the version number line up.
        _ => Task.CompletedTask,

        // 1 → 2: capture regions gain a game profile column.
        MigrateRegionsToV2Async,

        // 2 → 3: the log gains provenance - which game and which region produced each row.
        MigrateLogToV3Async,
    ];

    private async Task InitialiseAsync(CancellationToken ct)
    {
        // WAL keeps a read during capture from blocking a write from the translation task.
        // It is a no-op for :memory:, which is fine.
        await ExecuteAsync("PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);
        await ExecuteAsync("PRAGMA synchronous=NORMAL;", ct).ConfigureAwait(false);

        var version = Convert.ToInt32(await ScalarAsync("PRAGMA user_version;", ct).ConfigureAwait(false));

        // A database written by a NEWER build. Nothing to do and nothing to complain about:
        // migrations are additive, so everything this build knows about is still where it left it.
        // This matters because there is no self-updater - re-unzipping an older release is a
        // supported way out of a bad update, and it must not corrupt anything on the way.
        if (version >= SchemaVersion) return;

        // Applied before any step, on every upgrade path. Every statement is CREATE IF NOT EXISTS,
        // so it is idempotent and cheap - and it means a database from ANY older version arrives at
        // the per-version steps with the full current table set. Without this, a v1 file upgrading
        // today would skip straight to step 1 and never gain a table added since.
        await ExecuteAsync(Schema, ct).ConfigureAwait(false);

        for (var step = version; step < SchemaVersion; step++)
        {
            await Migrations[step](ct).ConfigureAwait(false);

            // Bumped per step, so an interrupted upgrade resumes where it stopped rather than
            // restarting. Every step is idempotent as well, but that is a belt this does not want
            // to depend on: a step that INSERTs against a UNIQUE constraint would throw on the
            // re-run, inside OpenAsync, and the app would fail to start for someone who cannot
            // read the error.
            await ExecuteAsync($"PRAGMA user_version={step + 1};", ct).ConfigureAwait(false);
        }
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
          outcome     TEXT NOT NULL,
          game        TEXT,
          region      TEXT
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
          profile     TEXT NOT NULL,
          name        TEXT NOT NULL,
          resolution  TEXT NOT NULL,
          ui_scale    REAL NOT NULL,
          rel_x REAL NOT NULL, rel_y REAL NOT NULL, rel_w REAL NOT NULL, rel_h REAL NOT NULL,
          PRIMARY KEY (profile, name)
        );

        CREATE TABLE IF NOT EXISTS counters (
          name  TEXT PRIMARY KEY,
          value INTEGER NOT NULL
        );
        """;

    /// <summary>
    /// v1 keyed capture regions by name alone, so switching game profile silently overwrote the
    /// previous profile's rectangle. Existing rows are attributed to "ffxiv", the only profile that
    /// existed while v1 was in use. Safe to run repeatedly - it checks for the column first.
    /// </summary>
    private async Task MigrateRegionsToV2Async(CancellationToken ct)
    {
        var hasProfileColumn = await WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(region_profiles);";
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);

            while (await reader.ReadAsync(token).ConfigureAwait(false))
                if (reader.GetString(1) == "profile") return true;

            return false;
        }, ct).ConfigureAwait(false);

        if (hasProfileColumn) return;

        await ExecuteAsync("""
            ALTER TABLE region_profiles RENAME TO region_profiles_v1;

            CREATE TABLE region_profiles (
              profile     TEXT NOT NULL,
              name        TEXT NOT NULL,
              resolution  TEXT NOT NULL,
              ui_scale    REAL NOT NULL,
              rel_x REAL NOT NULL, rel_y REAL NOT NULL, rel_w REAL NOT NULL, rel_h REAL NOT NULL,
              PRIMARY KEY (profile, name)
            );

            INSERT INTO region_profiles (profile, name, resolution, ui_scale, rel_x, rel_y, rel_w, rel_h)
            SELECT 'ffxiv', name, resolution, ui_scale, rel_x, rel_y, rel_w, rel_h FROM region_profiles_v1;

            DROP TABLE region_profiles_v1;
            """, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds nullable provenance columns to translation_log. Existing rows keep NULL - there is no
    /// way to know which game produced them, and inventing one would poison the statistics the
    /// column exists for. Guarded per column, because on a fresh database the base schema has
    /// already created the table in its final shape by the time this step runs.
    ///
    /// <para>
    /// Per column, and not one guard for both: SQLite has no multi-statement DDL transaction here,
    /// so a process killed between the two ALTERs leaves <c>game</c> present and <c>region</c>
    /// absent with <c>user_version</c> still at 2. A single guard reading <c>game</c> would then
    /// see the column, return satisfied, and let the ladder bump to 3 - and <c>region</c> would
    /// never exist. Every log INSERT after that throws "no such column", inside ProcessAsync,
    /// which means every translation fails, permanently, with no way back. Idempotence has to be
    /// per statement or it is not idempotence.
    /// </para>
    /// </summary>
    private async Task MigrateLogToV3Async(CancellationToken ct)
    {
        await AddColumnIfMissingAsync("translation_log", "game", "TEXT", ct).ConfigureAwait(false);
        await AddColumnIfMissingAsync("translation_log", "region", "TEXT", ct).ConfigureAwait(false);
    }

    private async Task AddColumnIfMissingAsync(string table, string column, string type, CancellationToken ct)
    {
        if (await HasColumnAsync(table, column, ct).ConfigureAwait(false)) return;
        await ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {type};", ct).ConfigureAwait(false);
    }

    private Task<bool> HasColumnAsync(string table, string column, CancellationToken ct) =>
        WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);

            while (await reader.ReadAsync(token).ConfigureAwait(false))
                if (reader.GetString(1) == column) return true;

            return false;
        }, ct);

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
