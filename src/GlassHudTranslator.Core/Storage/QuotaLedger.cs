namespace GlassHudTranslator.Core.Storage;

public readonly record struct QuotaSnapshot(string Provider, int Used, int Limit)
{
    public double Fraction => Limit <= 0 ? 0 : (double)Used / Limit;

    public override string ToString() => $"{Provider} {Used}/{Limit}";
}

/// <summary>
/// Per-provider request counts, bucketed by Pacific day.
///
/// <para>
/// Counts are persisted rather than held in memory so they survive the app restarting mid-session.
/// The day boundary is Pacific midnight because that is when Google's daily quota resets - 09:00 in
/// Frankfurt, so an evening session always starts on a full budget (brief 4.3).
/// </para>
///
/// <para>
/// This ledger is also how the two genuinely open questions get answered: nobody publishes a
/// guaranteed free-tier table any more, so the real limits are discovered by playing for an
/// evening and reading the numbers back (brief 15, questions 2 and 4).
/// </para>
/// </summary>
public sealed class QuotaLedger(AppDatabase db, TimeProvider? clock = null)
{
    private static readonly TimeZoneInfo Pacific = ResolvePacific();

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public string CurrentDay => DayOf(_clock.GetUtcNow());

    public static string DayOf(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Pacific).ToString("yyyy-MM-dd");

    public Task RecordAsync(string provider, CancellationToken ct) =>
        db.ExecuteAsync("""
            INSERT INTO quota (provider, day_pacific, used) VALUES ($provider, $day, 1)
            ON CONFLICT(provider, day_pacific) DO UPDATE SET used = used + 1;
            """, ct, ("$provider", provider), ("$day", CurrentDay));

    public async Task<int> UsedTodayAsync(string provider, CancellationToken ct)
    {
        var value = await db.ScalarAsync(
            "SELECT used FROM quota WHERE provider = $provider AND day_pacific = $day;", ct,
            ("$provider", provider), ("$day", CurrentDay)).ConfigureAwait(false);

        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    /// <summary>Readout for the settings window: "Gemini 412/1000 · Groq 0/14400".</summary>
    public async Task<IReadOnlyList<QuotaSnapshot>> SnapshotAsync(
        IReadOnlyList<(string Provider, int Limit)> providers, CancellationToken ct)
    {
        var snapshots = new List<QuotaSnapshot>(providers.Count);
        foreach (var (provider, limit) in providers)
            snapshots.Add(new QuotaSnapshot(provider, await UsedTodayAsync(provider, ct).ConfigureAwait(false), limit));

        return snapshots;
    }

    private static TimeZoneInfo ResolvePacific()
    {
        // .NET resolves IANA ids on Windows too, but fall back rather than crash the app over a
        // quota display if the platform's time zone data is unusual.
        foreach (var id in new[] { "America/Los_Angeles", "Pacific Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Try the next spelling.
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("LedgerPacific", TimeSpan.FromHours(-8), "Pacific", "Pacific");
    }
}
