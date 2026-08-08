namespace GlassHudTranslator.Core.Storage;

/// <summary>
/// Every (raw OCR -> normalized -> provider -> translation -> latency) tuple, appended.
///
/// <para>
/// This one table is simultaneously the correction dataset, the source of the OCR error
/// dictionary, the evidence for whether Gemini's Arabic is actually good enough, and the cache
/// hit-rate diagnostics (brief 12). Session 3 is driven entirely by reading it, which is why it
/// records outcomes that are not translations - a dropped stale request and a fallback to English
/// are exactly the rows worth counting.
/// </para>
/// </summary>
public sealed class TranslationLog(AppDatabase db)
{
    public Task AppendAsync(TranslationLogEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return db.ExecuteAsync("""
            INSERT INTO translation_log
              (at, raw_ocr, normalized, speaker, provider, model, arabic, latency_ms, from_cache, outcome, game, region)
            VALUES ($at, $raw, $norm, $speaker, $provider, $model, $arabic, $latency, $cache, $outcome, $game, $region);
            """, ct,
            ("$at", entry.At.ToUnixTimeSeconds()),
            ("$raw", entry.RawOcr),
            ("$norm", entry.Normalized),
            ("$speaker", entry.Speaker),
            ("$provider", entry.Provider),
            ("$model", entry.Model),
            ("$arabic", entry.Arabic),
            ("$latency", (long)entry.Latency.TotalMilliseconds),
            ("$cache", entry.FromCache ? 1 : 0),
            ("$outcome", entry.Outcome),
            ("$game", entry.Game),
            ("$region", entry.Region));
    }

    public async Task<long> CountAsync(CancellationToken ct) =>
        Convert.ToInt64(await db.ScalarAsync("SELECT COUNT(*) FROM translation_log;", ct).ConfigureAwait(false));
}

/// <summary>
/// <paramref name="Game"/> and <paramref name="Region"/> are provenance, added for the history
/// view and for per-region diagnostics; rows from before v3 carry null in both, which readers must
/// treat as "unknown", not as a game named nothing.
/// </summary>
public sealed record TranslationLogEntry(
    DateTimeOffset At,
    string RawOcr,
    string Normalized,
    string? Speaker,
    string? Provider,
    string? Model,
    string? Arabic,
    TimeSpan Latency,
    bool FromCache,
    string Outcome,
    string? Game = null,
    string? Region = null);
