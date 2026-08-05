using GlassHudTranslator.Core.Regions;
using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Translation;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

public class TranslationCacheTests
{
    private static CachedTranslation Entry(string key, string arabic, bool isOverride = false) =>
        new(key, "Come with me.", arabic, "gemini", "gemini-2.5-flash-lite", isOverride,
            DateTimeOffset.UtcNow, 0);

    [Fact]
    public async Task StoresAndRetrievesATranslation()
    {
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var cache = new SqliteTranslationCache(db);

        await cache.PutAsync(Entry("k1", "تعال معي."), CancellationToken.None);
        var found = await cache.TryGetAsync("k1", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("تعال معي.", found.Arabic);
        Assert.Equal("gemini", found.Provider);
    }

    [Fact]
    public async Task MissReturnsNull()
    {
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var cache = new SqliteTranslationCache(db);

        Assert.Null(await cache.TryGetAsync("absent", CancellationToken.None));
    }

    [Fact]
    public async Task ManualOverrideSurvivesALaterAutomaticTranslation()
    {
        // brief 12: a correction must be permanent, or the user fixes the same line every time
        // the cache is rebuilt.
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var cache = new SqliteTranslationCache(db);
        var ct = CancellationToken.None;

        await cache.PutOverrideAsync("k1", "Come with me.", "تعالَ معي.", ct);
        await cache.PutAsync(Entry("k1", "MODEL OUTPUT"), ct);

        var found = await cache.TryGetAsync("k1", ct);
        Assert.NotNull(found);
        Assert.Equal("تعالَ معي.", found.Arabic);
        Assert.True(found.IsOverride);
    }

    [Fact]
    public async Task OverrideCanItselfBeCorrectedAgain()
    {
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var cache = new SqliteTranslationCache(db);
        var ct = CancellationToken.None;

        await cache.PutOverrideAsync("k1", "Come with me.", "first", ct);
        await cache.PutOverrideAsync("k1", "Come with me.", "second", ct);

        var found = await cache.TryGetAsync("k1", ct);
        Assert.Equal("second", found!.Arabic);
    }

    [Fact]
    public async Task HitRateIsTrackedAcrossLookups()
    {
        // The hit rate is the diagnostic that distinguishes a normalisation bug from ordinary
        // first-playthrough content, so it has to be counted persistently.
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var cache = new SqliteTranslationCache(db);
        var ct = CancellationToken.None;

        await cache.PutAsync(Entry("k1", "أ"), ct);
        await cache.TryGetAsync("k1", ct);
        await cache.TryGetAsync("k1", ct);
        await cache.TryGetAsync("missing", ct);

        var stats = await cache.GetStatsAsync(ct);
        Assert.Equal(1, stats.Entries);
        Assert.Equal(3, stats.Lookups);
        Assert.Equal(2, stats.Hits);
        Assert.Equal(2.0 / 3.0, stats.HitRate, 3);
    }

    [Fact]
    public async Task HitCounterOnTheRowIncrements()
    {
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var cache = new SqliteTranslationCache(db);
        var ct = CancellationToken.None;

        await cache.PutAsync(Entry("k1", "أ"), ct);
        await cache.TryGetAsync("k1", ct);
        var second = await cache.TryGetAsync("k1", ct);

        Assert.Equal(2, second!.Hits);
    }
}

public class QuotaLedgerTests
{
    [Fact]
    public async Task CountsRequestsPerProvider()
    {
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var ledger = new QuotaLedger(db);
        var ct = CancellationToken.None;

        await ledger.RecordAsync("gemini", ct);
        await ledger.RecordAsync("gemini", ct);
        await ledger.RecordAsync("groq", ct);

        Assert.Equal(2, await ledger.UsedTodayAsync("gemini", ct));
        Assert.Equal(1, await ledger.UsedTodayAsync("groq", ct));
    }

    [Fact]
    public async Task UnusedProviderReadsZero()
    {
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var ledger = new QuotaLedger(db);

        Assert.Equal(0, await ledger.UsedTodayAsync("groq", CancellationToken.None));
    }

    [Fact]
    public async Task SnapshotReportsUsageAgainstLimits()
    {
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var ledger = new QuotaLedger(db);
        var ct = CancellationToken.None;

        await ledger.RecordAsync("gemini", ct);

        var snapshot = await ledger.SnapshotAsync([("gemini", 1000), ("groq", 14400)], ct);

        Assert.Equal("gemini 1/1000", snapshot[0].ToString());
        Assert.Equal("groq 0/14400", snapshot[1].ToString());
    }

    [Fact]
    public void DayBoundaryIsPacificMidnight_NotUtc()
    {
        // 07:00 UTC on the 5th is still the 4th in Pacific, so an evening session in Frankfurt
        // must not be counted against the next day's budget.
        Assert.Equal("2026-08-04", QuotaLedger.DayOf(new DateTimeOffset(2026, 8, 5, 6, 0, 0, TimeSpan.Zero)));
        Assert.Equal("2026-08-05", QuotaLedger.DayOf(new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.Zero)));
    }
}

public class TranslationLogTests
{
    [Fact]
    public async Task AppendsRowsIncludingNonTranslationOutcomes()
    {
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var log = new TranslationLog(db);
        var ct = CancellationToken.None;

        await log.AppendAsync(new TranslationLogEntry(
            DateTimeOffset.UtcNow, "Come wlth me.", "Come with me.", "Y'shtola",
            "gemini", "gemini-2.5-flash-lite", "تعال معي.", TimeSpan.FromMilliseconds(640),
            false, TranslationLogOutcomes.Ok), ct);

        await log.AppendAsync(new TranslationLogEntry(
            DateTimeOffset.UtcNow, "raw", "normalized", null, null, null, null,
            TimeSpan.Zero, false, TranslationLogOutcomes.Stale), ct);

        Assert.Equal(2, await log.CountAsync(ct));
    }
}

public class SecretStoreTests
{
    [Fact]
    public void RoundTripsAndDeletes()
    {
        var store = new InMemorySecretStore();

        Assert.False(store.Has(SecretNames.GeminiApiKey));
        store.Set(SecretNames.GeminiApiKey, "abc123");
        Assert.True(store.Has(SecretNames.GeminiApiKey));
        Assert.Equal("abc123", store.Get(SecretNames.GeminiApiKey));

        store.Delete(SecretNames.GeminiApiKey);
        Assert.Null(store.Get(SecretNames.GeminiApiKey));
    }

    [Fact]
    public void DevFileStorePersistsAcrossInstances()
    {
        var path = Path.Combine(Path.GetTempPath(), $"glasshud-secrets-{Guid.NewGuid():N}.json");
        try
        {
            new DevPlainFileSecretStore(path).Set(SecretNames.GroqApiKey, "gsk_test");

            Assert.Equal("gsk_test", new DevPlainFileSecretStore(path).Get(SecretNames.GroqApiKey));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class RegionProfileStoreTests
{
    [Fact]
    public async Task RegionsAreKeptSeparatePerGameProfile()
    {
        // Switching between a game and the desktop must not clobber either rectangle. Keyed by name
        // alone, picking a region for one profile silently overwrote the other's.
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var store = new RegionProfileStore(db);
        var ct = CancellationToken.None;

        await store.SaveAsync("ffxiv", new RegionProfile("dialogue", "1920x1080", 1.0, 0.2, 0.7, 0.6, 0.2), ct);
        await store.SaveAsync("general", new RegionProfile("dialogue", "1920x1080", 1.0, 0.1, 0.1, 0.3, 0.3), ct);

        var game = await store.LoadAsync("ffxiv", "dialogue", ct);
        var desktop = await store.LoadAsync("general", "dialogue", ct);

        Assert.Equal(0.7, game!.RelY, 3);
        Assert.Equal(0.1, desktop!.RelY, 3);
    }

    [Fact]
    public async Task SwitchingBackFindsTheOriginalRegionWaiting()
    {
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var store = new RegionProfileStore(db);
        var ct = CancellationToken.None;

        await store.SaveAsync("ffxiv", new RegionProfile("dialogue", "1920x1080", 1.0, 0.2, 0.7, 0.6, 0.2), ct);
        await store.SaveAsync("general", new RegionProfile("dialogue", "1920x1080", 1.0, 0.1, 0.1, 0.3, 0.3), ct);

        Assert.True(await store.HasAsync("ffxiv", "dialogue", ct));
        Assert.Equal(0.6, (await store.LoadOrDefaultAsync("ffxiv", "dialogue", ct)).RelWidth, 3);
    }

    [Fact]
    public async Task AnUnpickedProfileFallsBackToDefaultsRatherThanAnotherProfilesRegion()
    {
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var store = new RegionProfileStore(db);
        var ct = CancellationToken.None;

        await store.SaveAsync("ffxiv", new RegionProfile("dialogue", "1920x1080", 1.0, 0.2, 0.7, 0.6, 0.2), ct);

        Assert.False(await store.HasAsync("general", "dialogue", ct));
        Assert.Equal(RegionProfile.Default("dialogue").RelY,
            (await store.LoadOrDefaultAsync("general", "dialogue", ct)).RelY, 3);
    }

    [Fact]
    public async Task EachProfileKeepsItsOwnDialogueSubtitleAndQuestRegions()
    {
        await using var db = await AppDatabase.OpenInMemoryAsync();
        var store = new RegionProfileStore(db);
        var ct = CancellationToken.None;

        foreach (var name in RegionProfile.Names.All)
            await store.SaveAsync("ffxiv", new RegionProfile(name, "1920x1080", 1.0, 0.1, 0.2, 0.3, 0.4), ct);

        foreach (var name in RegionProfile.Names.All)
            Assert.True(await store.HasAsync("ffxiv", name, ct), name);
    }
}
