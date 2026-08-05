using System.Diagnostics;
using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Glossary;
using GlassHudTranslator.Core.Ocr;
using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Text;
using GlassHudTranslator.Core.Translation;

namespace GlassHudTranslator.Core.Pipeline;

public sealed record PipelineOutcome(
    string RawOcr,
    string Normalized,
    string? Speaker,
    string Body,
    IReadOnlyList<GlossaryTerm> GlossaryHits,
    TranslationResult Result,
    float OcrConfidence,
    TimeSpan Total)
{
    public bool ProducedText => !string.IsNullOrWhiteSpace(Result.Text);
}

/// <summary>
/// One captured frame in, one translated line out. Shared by the overlay and by tools/Replay so
/// that what the harness exercises on the Mac is the same code that runs against the game.
/// </summary>
public sealed class TranslationPipeline(
    IOcrEngine ocr,
    ITranslationCache cache,
    GlossaryMatcher glossary,
    ProviderRouter router,
    OcrCorrections? corrections = null,
    TranslationLog? log = null)
{
    private string? _previousLine;
    private GlossaryMatcher _glossary = glossary;
    private OcrCorrections _corrections = corrections ?? OcrCorrections.Empty;

    public ArabicRegister Register { get; set; } = ArabicRegister.ModernStandard;

    /// <summary>From the active game profile. Both go into the system prompt.</summary>
    public string GameName { get; set; } = "a video game";

    public string? StyleHint { get; set; }

    /// <summary>
    /// Swaps everything a game profile owns, without rebuilding the pipeline. Switching between a
    /// game and the desktop is a thing people do several times an hour, and making that a restart
    /// would be enough friction that they simply would not bother.
    /// </summary>
    public void UseProfile(string gameName, string? styleHint, GlossaryMatcher matcher, OcrCorrections ocrCorrections)
    {
        GameName = gameName;
        StyleHint = styleHint;
        _glossary = matcher;
        _corrections = ocrCorrections;
        ResetContext();
    }

    /// <summary>Cleared when the player leaves a conversation, so context does not bleed scenes.</summary>
    public void ResetContext() => _previousLine = null;

    public async Task<PipelineOutcome> ProcessAsync(Frame frame, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var started = Stopwatch.GetTimestamp();
        var requestedAt = DateTimeOffset.UtcNow;

        var recognised = await ocr.RecognizeAsync(frame, ct).ConfigureAwait(false);
        var normalized = TextNormalizer.Normalize(recognised.RawText, _corrections);
        var (speaker, body) = DialogueParser.Parse(normalized);

        if (string.IsNullOrWhiteSpace(body))
        {
            return new PipelineOutcome(recognised.RawText, normalized, speaker, body, [],
                new TranslationResult(string.Empty, ProviderNames.Fallback, "-", false, TimeSpan.Zero,
                    TranslationLogOutcomes.Stale),
                recognised.Confidence, Stopwatch.GetElapsedTime(started));
        }

        var key = CacheKey.For(body, Register);
        var cached = await cache.TryGetAsync(key, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            var hit = new TranslationResult(cached.Arabic, ProviderNames.Cache, cached.Model, true,
                Stopwatch.GetElapsedTime(started), TranslationLogOutcomes.Cached);
            _previousLine = body;
            await LogAsync(recognised, normalized, speaker, hit, ct).ConfigureAwait(false);

            return new PipelineOutcome(recognised.RawText, normalized, speaker, body, [], hit,
                recognised.Confidence, Stopwatch.GetElapsedTime(started));
        }

        var hits = _glossary.Match(body);
        var result = await router.TranslateAsync(
            new TranslationRequest(body, speaker, hits, _previousLine, Register, requestedAt,
                GameName, StyleHint), ct)
            .ConfigureAwait(false);

        // Only cache a genuine translation. Caching the English fallback would poison the entry
        // permanently - the next lookup would hit and never retry the provider.
        if (result.Outcome == TranslationLogOutcomes.Ok)
        {
            await cache.PutAsync(new CachedTranslation(key, body, result.Text, result.Provider,
                result.Model, false, DateTimeOffset.UtcNow, 0), ct).ConfigureAwait(false);
            _previousLine = body;
        }

        await LogAsync(recognised, normalized, speaker, result, ct).ConfigureAwait(false);

        return new PipelineOutcome(recognised.RawText, normalized, speaker, body, hits, result,
            recognised.Confidence, Stopwatch.GetElapsedTime(started));
    }

    private Task LogAsync(OcrResult recognised, string normalized, string? speaker,
        TranslationResult result, CancellationToken ct) =>
        log?.AppendAsync(new TranslationLogEntry(
            DateTimeOffset.UtcNow, recognised.RawText, normalized, speaker,
            result.Provider, result.Model, result.Text, result.Latency,
            result.FromCache, result.Outcome), ct) ?? Task.CompletedTask;
}
