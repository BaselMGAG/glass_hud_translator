using GlassHudTranslator.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// Schema and data migrations, tested against databases at every version that has shipped.
///
/// <para>
/// This project has already shipped one migration that never ran. It was written, its commit
/// message described it correctly, no test covered it, and it was inert for its entire life —
/// users who had the old folder simply lost their keys and cache and nobody found out. That is the
/// failure mode these tests exist for: a migration that does nothing looks exactly like a
/// migration that worked.
/// </para>
/// </summary>
public class MigrationTests
{
    // ── the ladder ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AFreshDatabaseArrivesAtTheCurrentVersionWithEveryTable()
    {
        await WithTempDatabase(async path =>
        {
            await using (await AppDatabase.OpenAsync(path, CancellationToken.None)) { }

            Assert.Equal(2, await UserVersion(path));

            foreach (var table in new[]
                     { "translations", "translation_log", "quota", "region_profiles", "counters" })
                Assert.True(await TableExists(path, table), $"{table} is missing.");
        });
    }

    [Fact]
    public async Task OpeningAnUpToDateDatabaseChangesNothing()
    {
        // The common case, ~100 times a day across the userbase. It must be a no-op.
        await WithTempDatabase(async path =>
        {
            await using (await AppDatabase.OpenAsync(path, CancellationToken.None)) { }
            await Insert(path, "translations", "('k','s','a','p','m',0,1,0)");

            await using (await AppDatabase.OpenAsync(path, CancellationToken.None)) { }

            Assert.Equal(2, await UserVersion(path));
            Assert.Equal(1L, await Count(path, "translations"));
        });
    }

    /// <summary>
    /// The bug the ladder exists to prevent, expressed as a test.
    ///
    /// <para>
    /// The old shape was <c>if (version >= SchemaVersion) return;</c> followed by every migration in
    /// sequence. Add a third step to that and it never runs for anyone already at 2 — the check
    /// passes and the function returns. Here the version is forced to each shipped value in turn and
    /// the database is required to arrive at the current one.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ADatabaseAtAnyShippedVersionUpgradesToCurrent(int startingVersion)
    {
        await WithTempDatabase(async path =>
        {
            await using (await AppDatabase.OpenAsync(path, CancellationToken.None)) { }
            await Execute(path, $"PRAGMA user_version={startingVersion};");

            await using (await AppDatabase.OpenAsync(path, CancellationToken.None)) { }

            Assert.Equal(2, await UserVersion(path));
        });
    }

    [Fact]
    public async Task RunningTheLadderTwiceIsSafe()
    {
        // Every step has to be idempotent regardless of the per-step version bump, because a
        // process killed mid-upgrade re-runs the step it was on.
        await WithTempDatabase(async path =>
        {
            await using (await AppDatabase.OpenAsync(path, CancellationToken.None)) { }
            await Execute(path, "PRAGMA user_version=0;");
            await using (await AppDatabase.OpenAsync(path, CancellationToken.None)) { }
            await Execute(path, "PRAGMA user_version=0;");
            await using (await AppDatabase.OpenAsync(path, CancellationToken.None)) { }

            Assert.Equal(2, await UserVersion(path));
        });
    }

    [Fact]
    public async Task ADatabaseFromANewerBuildIsLeftAlone()
    {
        // There is deliberately no self-updater, so re-unzipping an older release is a supported
        // recovery from a bad update. An older build must not touch a newer file - which is only
        // safe because migrations are additive: nothing it knew about has moved.
        await WithTempDatabase(async path =>
        {
            await using (await AppDatabase.OpenAsync(path, CancellationToken.None)) { }
            await Execute(path, "PRAGMA user_version=99;");
            await Insert(path, "translations", "('k','s','a','p','m',0,1,0)");

            await using (await AppDatabase.OpenAsync(path, CancellationToken.None)) { }

            Assert.Equal(99, await UserVersion(path));
            Assert.Equal(1L, await Count(path, "translations"));
        });
    }

    // ── the v1 → v2 region migration, with real data in it ────────────────────────────────

    [Fact]
    public async Task V1RegionsAreCarriedOntoTheFfxivProfileRatherThanDropped()
    {
        // v1 keyed regions by name alone. The rows are a user's hand-dragged rectangles; losing
        // them means re-picking the capture region, which is the fiddliest step in setup.
        await WithTempDatabase(async path =>
        {
            await Execute(path, """
                CREATE TABLE region_profiles (
                  name        TEXT PRIMARY KEY,
                  resolution  TEXT NOT NULL,
                  ui_scale    REAL NOT NULL,
                  rel_x REAL NOT NULL, rel_y REAL NOT NULL, rel_w REAL NOT NULL, rel_h REAL NOT NULL
                );
                INSERT INTO region_profiles VALUES ('dialogue','2560x1440',1.25,0.22,0.70,0.56,0.20);
                INSERT INTO region_profiles VALUES ('subtitle','2560x1440',1.25,0.20,0.78,0.60,0.12);
                PRAGMA user_version=1;
                """);

            await using (await AppDatabase.OpenAsync(path, CancellationToken.None)) { }

            Assert.Equal(2, await UserVersion(path));
            Assert.Equal(2L, await Count(path, "region_profiles"));
            Assert.Equal(2L, await Scalar(path,
                "SELECT COUNT(*) FROM region_profiles WHERE profile='ffxiv';"));

            // The rectangle itself has to survive intact, not merely the row.
            Assert.Equal(0.22, Convert.ToDouble(await Scalar(path,
                "SELECT rel_x FROM region_profiles WHERE name='dialogue';")), 3);
        });
    }

    [Fact]
    public async Task AV1DatabaseGainsTablesAddedAfterV1()
    {
        // The ladder must not skip the base schema for an old file. A v1 database predates the
        // counters table; upgrading has to produce it, or the cache statistics throw on first read.
        await WithTempDatabase(async path =>
        {
            await Execute(path, """
                CREATE TABLE region_profiles (
                  name        TEXT PRIMARY KEY,
                  resolution  TEXT NOT NULL,
                  ui_scale    REAL NOT NULL,
                  rel_x REAL NOT NULL, rel_y REAL NOT NULL, rel_w REAL NOT NULL, rel_h REAL NOT NULL
                );
                PRAGMA user_version=1;
                """);

            await using (await AppDatabase.OpenAsync(path, CancellationToken.None)) { }

            Assert.True(await TableExists(path, "counters"));
            Assert.True(await TableExists(path, "translations"));
        });
    }

    // ── the data folder rename ────────────────────────────────────────────────────────────

    [Fact]
    public void ThePreRenameDataFolderIsMovedAcross()
    {
        // This is the migration that shipped inert. Both constants were written as the new name,
        // so its guard compared a path to itself and could never fire.
        var root = TempRoot();
        try
        {
            var legacy = Path.Combine(root, "GamingTranslatorGlassHUD");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "config.json"), "{}");

            var resolved = AppPaths.ResolveUnder(root);

            Assert.Equal(Path.Combine(root, AppPaths.FolderName), resolved);
            Assert.True(File.Exists(Path.Combine(resolved, "config.json")),
                "The user's settings did not come across.");
            Assert.False(Directory.Exists(legacy));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AnExistingDataFolderIsNeverOverwrittenByTheLegacyOne()
    {
        // Someone who has run both builds has both folders. The current one wins; the old one is
        // left where it is rather than clobbering live keys.
        var root = TempRoot();
        try
        {
            var legacy = Path.Combine(root, "GamingTranslatorGlassHUD");
            var current = Path.Combine(root, AppPaths.FolderName);
            Directory.CreateDirectory(legacy);
            Directory.CreateDirectory(current);
            File.WriteAllText(Path.Combine(legacy, "config.json"), "old");
            File.WriteAllText(Path.Combine(current, "config.json"), "current");

            var resolved = AppPaths.ResolveUnder(root);

            Assert.Equal(current, resolved);
            Assert.Equal("current", File.ReadAllText(Path.Combine(current, "config.json")));
            Assert.True(Directory.Exists(legacy));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AFirstRunWithNeitherFolderJustUsesTheCurrentName()
    {
        var root = TempRoot();
        try
        {
            Assert.Equal(Path.Combine(root, AppPaths.FolderName), AppPaths.ResolveUnder(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ghmig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task WithTempDatabase(Func<string, Task> body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ghmig-{Guid.NewGuid():N}.db");
        try
        {
            await body(path);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            {
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                }
                catch (IOException)
                {
                    // A leftover temp file is not worth failing a test over.
                }
            }
        }
    }

    private static async Task Execute(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static Task Insert(string path, string table, string values) =>
        Execute(path, $"INSERT INTO {table} VALUES {values};");

    private static async Task<object?> Scalar(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task<int> UserVersion(string path) =>
        Convert.ToInt32(await Scalar(path, "PRAGMA user_version;"));

    private static async Task<long> Count(string path, string table) =>
        Convert.ToInt64(await Scalar(path, $"SELECT COUNT(*) FROM {table};"));

    private static async Task<bool> TableExists(string path, string table) =>
        Convert.ToInt64(await Scalar(path,
            $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';")) == 1;
}
