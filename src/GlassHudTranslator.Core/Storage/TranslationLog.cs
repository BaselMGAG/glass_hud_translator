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

    /// <summary>How many rows the history view asks for at once. See <see cref="RecentAsync"/>.</summary>
    public const int DefaultPageSize = 200;

    /// <summary>
    /// The most recent lines, newest first, optionally filtered.
    ///
    /// <para>
    /// <b>Always limited, and the limit is not negotiable from the UI.</b> This table is append-only
    /// and never pruned — it is the correction dataset and the evidence for whether the Arabic is
    /// any good, so nothing deletes from it — which means after a few long sessions it holds tens of
    /// thousands of rows. A history view that loaded them all would freeze the window on the machine
    /// of the person who has used the app most.
    /// </para>
    ///
    /// <para>
    /// <b>The search is a LIKE over three columns, deliberately.</b> Somebody looking for a line
    /// remembers it in one of three ways: the English they saw on screen, the Arabic they were
    /// shown, or the speaker who said it. Matching only one of those makes the box feel broken in a
    /// way the user cannot diagnose. It is not FTS: adding a virtual table would be a schema
    /// migration on a file this project promises to keep readable by older builds, and LIKE over a
    /// few tens of thousands of short rows is instant.
    /// </para>
    /// </summary>
    public Task<IReadOnlyList<HistoryRow>> RecentAsync(
        string? search = null, int limit = DefaultPageSize, CancellationToken ct = default)
    {
        var term = string.IsNullOrWhiteSpace(search) ? null : $"%{Escape(search.Trim())}%";

        return db.WithConnectionAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();

            // ESCAPE is what stops a user typing % or _ into the search box from matching
            // everything - which looks exactly like the filter being ignored.
            command.CommandText = term is null
                ? """
                  SELECT id, at, normalized, speaker, arabic, provider, model, outcome, game, region
                  FROM translation_log ORDER BY id DESC LIMIT $limit;
                  """
                : """
                  SELECT id, at, normalized, speaker, arabic, provider, model, outcome, game, region
                  FROM translation_log
                  WHERE normalized LIKE $q ESCAPE '\'
                     OR arabic     LIKE $q ESCAPE '\'
                     OR speaker    LIKE $q ESCAPE '\'
                  ORDER BY id DESC LIMIT $limit;
                  """;

            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));
            if (term is not null) command.Parameters.AddWithValue("$q", term);

            var rows = new List<HistoryRow>();
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);

            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(new HistoryRow(
                    reader.GetInt64(0),
                    DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1)),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9)));
            }

            return (IReadOnlyList<HistoryRow>)rows;
        }, ct);
    }

    /// <summary>
    /// Neutralises the LIKE wildcards. A search for "100%" must find "100%", not everything.
    /// </summary>
    private static string Escape(string term) => term
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}

/// <summary>
/// One line as the history view shows it. Deliberately not <see cref="TranslationLogEntry"/>: that
/// one is what gets written and carries the raw OCR and the latency, neither of which belongs in a
/// list somebody is reading. This carries the id, which the write side has no use for and the read
/// side cannot work without.
/// </summary>
public sealed record HistoryRow(
    long Id,
    DateTimeOffset At,
    string Source,
    string? Speaker,
    string? Arabic,
    string? Provider,
    string? Model,
    string Outcome,
    string? Game,
    string? Region);

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
