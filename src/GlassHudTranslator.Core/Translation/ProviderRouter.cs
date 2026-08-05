using System.Diagnostics;

namespace GlassHudTranslator.Core.Translation;

public sealed record RouterOptions
{
    /// <summary>
    /// Past this age a request is dropped rather than sent. If a line has waited six seconds the
    /// NPC has moved on, and serving a backlog produces translations for dialogue that is already
    /// gone - which is worse than a gap (brief 5).
    /// </summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>Hard cap per attempt. Same reasoning as <see cref="StaleAfter"/>.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>How long a 429 sidelines a provider before it is tried again.</summary>
    public TimeSpan RateLimitCooldown { get; init; } = TimeSpan.FromSeconds(60);

    public int MaxTransientRetries { get; init; } = 2;

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(250);
}

/// <summary>
/// Walks the provider chain and always returns something.
///
/// <para>
/// Once there is no local model the network is a hard dependency, which makes graceful degradation
/// mandatory rather than optional: on total failure the user sees the OCR'd English with a warning
/// marker. Never blank, never crash (brief 2.7). This class therefore does not throw.
/// </para>
/// </summary>
public sealed class ProviderRouter(
    IReadOnlyList<(ITranslationProvider Provider, int Rpm)> lanes,
    RouterOptions? options = null,
    TimeProvider? clock = null,
    Action<string>? log = null)
{
    private readonly Lane[] _lanes = lanes
        .Select(l => new Lane(l.Provider, new TokenBucket(l.Rpm, clock)))
        .ToArray();

    private readonly RouterOptions _options = options ?? new RouterOptions();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Action<string> _log = log ?? (_ => { });

    /// <summary>Raised for every successful call, so the quota ledger can count it.</summary>
    public event Func<string, CancellationToken, Task>? ProviderUsed;

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var started = Stopwatch.GetTimestamp();
        var age = _clock.GetUtcNow() - request.Requested;
        if (age > _options.StaleAfter)
        {
            _log($"router: dropping stale request ({age.TotalSeconds:F1}s old)");
            return new TranslationResult(string.Empty, ProviderNames.Fallback, "-", false,
                Stopwatch.GetElapsedTime(started), TranslationLogOutcomes.Stale);
        }

        // Collected rather than logged per lane. An unconfigured lane is switched off, not broken,
        // and saying so once per line would drown the log that reports the failures that do
        // matter - but staying silent when EVERY lane is unconfigured left a first-run user with
        // "all providers exhausted" and no hint that the cause was simply a missing key.
        List<string>? unconfigured = null;

        foreach (var lane in _lanes)
        {
            if (ct.IsCancellationRequested) break;

            if (!lane.Provider.IsConfigured)
            {
                (unconfigured ??= []).Add(lane.Provider.Name);
                continue;
            }

            if (lane.CooldownUntil > _clock.GetUtcNow())
            {
                _log($"router: {lane.Provider.Name} in cooldown, skipping");
                continue;
            }

            // Fail over on the bucket, not only on daily exhaustion - that is what makes the
            // second provider a parallel lane rather than a reserve tank.
            if (!lane.Bucket.TryTake())
            {
                _log($"router: {lane.Provider.Name} rate bucket empty, next lane");
                continue;
            }

            var text = await TryLaneAsync(lane, request, ct).ConfigureAwait(false);
            if (text is null) continue;

            if (ProviderUsed is not null)
                await ProviderUsed(lane.Provider.Name, ct).ConfigureAwait(false);

            return new TranslationResult(text.Value.Text, lane.Provider.Name, text.Value.Model,
                false, Stopwatch.GetElapsedTime(started), TranslationLogOutcomes.Ok);
        }

        _log(unconfigured is null
            ? "router: all providers exhausted, falling back to English"
            : "router: all providers exhausted, falling back to English. No API key for: " +
              $"{string.Join(", ", unconfigured)} - enter one in Settings, Providers tab.");
        return new TranslationResult(request.Body, ProviderNames.Fallback, "-", false,
            Stopwatch.GetElapsedTime(started), TranslationLogOutcomes.FallbackEnglish);
    }

    /// <summary>Returns null when the whole lane is unusable and the caller should move on.</summary>
    private async Task<(string Text, string Model)?> TryLaneAsync(
        Lane lane, TranslationRequest request, CancellationToken ct)
    {
        foreach (var model in lane.Provider.Models)
        {
            for (var attempt = 0; attempt <= _options.MaxTransientRetries; attempt++)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(_options.RequestTimeout);

                    var text = await lane.Provider.TranslateAsync(request, model, timeout.Token)
                        .ConfigureAwait(false);
                    return (text, model);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // The caller gave up - the window closed, or a newer line arrived. Abandon
                    // everything rather than moving down the chain for a line nobody wants.
                    return null;
                }
                catch (Exception raised) when (raised is ProviderException or OperationCanceledException)
                {
                    // A provider that lets the per-attempt cap surface as a raw cancellation would
                    // otherwise escape this method entirely and throw out of a router that
                    // documents, and is depended on for, never throwing. Treat it as what it is:
                    // no answer in time, worth one more try or the next lane.
                    var e = raised as ProviderException;
                    var failure = e?.Failure ?? ProviderFailure.Transient;
                    var detail = e?.Message
                                 ?? $"no answer within {_options.RequestTimeout.TotalSeconds:F0}s";

                    switch (failure)
                    {
                        case ProviderFailure.ModelNotFound:
                            // Loudly, because this is the failure that silently takes a working
                            // build offline weeks after it shipped.
                            _log($"router: MODEL GONE - {lane.Provider.Name}/{model} no longer exists " +
                                 $"({detail}). Falling through to the next model in models.json. " +
                                 "Update that file.");
                            goto nextModel;

                        case ProviderFailure.RateLimited:
                            lane.CooldownUntil = _clock.GetUtcNow() + _options.RateLimitCooldown;
                            _log($"router: {lane.Provider.Name} rate limited, cooling down " +
                                 $"{_options.RateLimitCooldown.TotalSeconds:F0}s");
                            return null;

                        case ProviderFailure.Fatal:
                            _log($"router: {lane.Provider.Name} fatal - {detail}");
                            return null;

                        case ProviderFailure.Transient:
                        default:
                            if (attempt == _options.MaxTransientRetries)
                            {
                                _log($"router: {lane.Provider.Name}/{model} failed after " +
                                     $"{attempt + 1} attempts - {detail}");
                                return null;
                            }

                            await BackoffAsync(attempt, ct).ConfigureAwait(false);
                            break;
                    }
                }
            }

            nextModel: ;
        }

        return null;
    }

    /// <summary>Exponential backoff with jitter, so two lanes recovering do not resynchronise.</summary>
    private Task BackoffAsync(int attempt, CancellationToken ct)
    {
        var baseDelay = _options.RetryBaseDelay * Math.Pow(2, attempt);
        var jitter = Random.Shared.NextDouble() * 0.5 + 0.75;
        return Task.Delay(baseDelay * jitter, _clock, ct);
    }

    private sealed class Lane(ITranslationProvider provider, TokenBucket bucket)
    {
        public ITranslationProvider Provider { get; } = provider;
        public TokenBucket Bucket { get; } = bucket;
        public DateTimeOffset CooldownUntil { get; set; } = DateTimeOffset.MinValue;
    }
}

/// <summary>
/// Outcome strings, shared by the router and the translation log. Session 3 groups the log by this
/// column, so the values that are not "ok" are the interesting ones.
/// </summary>
public static class TranslationLogOutcomes
{
    public const string Ok = "ok";
    public const string Cached = "cached";
    public const string Stale = "stale";
    public const string FallbackEnglish = "fallback_english";

    public static string Error(string kind) => $"error:{kind}";
}
