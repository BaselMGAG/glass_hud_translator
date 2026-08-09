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

    /// <summary>
    /// Hard cap per attempt. Ten seconds, up from four, and the four was not wrong when it was
    /// written: the free models of early 2026 answered a one-line translation in about a second,
    /// so four seconds was generous. Their replacements are reasoning models - they think before
    /// they answer, the thinking takes seconds, and it happens on the provider's clock. Under the
    /// old cap every attempt on such a model timed out by construction: the key test passed (its
    /// budget is 20 s) while every real translation died, which reads as "the API works but the
    /// app is broken". The stale gate below still drops requests that queued too long; this cap
    /// governs how long one attempt may run once it has started, and those are different budgets.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The LONGEST a provider is sidelined once EVERY model it offers has been rate limited. Not
    /// one model — every one, because the limits are per model and a lane holds several. When the
    /// provider says how long to wait, that answer is used instead, clamped into
    /// [<see cref="MinimumCooldown"/>, this].
    /// </summary>
    public TimeSpan RateLimitCooldown { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The shortest a rate-limit cooldown may be, whatever the provider asks for. A lane that
    /// answers "retry after 0" would otherwise be re-tried on every line, which is a spin rather
    /// than a fallback.
    /// </summary>
    public TimeSpan MinimumCooldown { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ceiling on one whole call, across every lane and model. Needed precisely because a failure
    /// now walks on to the next model instead of ending its lane: with seven models between the
    /// two free providers, a run of timeouts could otherwise spend over a minute before the
    /// overlay said anything at all. Most failures are instant — a 404 or a 429 costs
    /// milliseconds — so this only ever bites on a genuinely slow provider.
    /// </summary>
    public TimeSpan TotalBudget { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The shortest an attempt may be cut to by the budget. Every lane is guaranteed one try even
    /// when the budget is spent — a fallback provider that is never asked is the failure this
    /// whole release is about — and a guaranteed attempt with no time left is not an attempt.
    /// </summary>
    public TimeSpan MinimumAttempt { get; init; } = TimeSpan.FromSeconds(3);

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

        var deadline = _clock.GetUtcNow() + _options.TotalBudget;

        // Collected rather than logged per lane. An unconfigured lane is switched off, not broken,
        // and saying so once per line would drown the log that reports the failures that do
        // matter - but staying silent when EVERY lane is unconfigured left a first-run user with
        // "all providers exhausted" and no hint that the cause was simply a missing key.
        List<string>? unconfigured = null;

        // Which PROVIDERS have been asked, as opposed to which lanes. Once the budget is spent,
        // the guaranteed attempt is granted per provider rather than per lane, and that
        // distinction is what keeps the ceiling a ceiling now that one provider can be three
        // lanes. The guarantee exists so a slow Gemini cannot starve Groq — a different endpoint
        // that might well answer. A second Gemini KEY is not a different endpoint: if the first
        // one has just spent ten seconds not answering, the second will spend ten more.
        //
        // Measured before this: six configured lanes against a stalled provider took 35 seconds
        // for one line, against a documented ceiling of twenty, with the overlay saying
        // "translating" throughout and every hotkey press in that window silently dropped.
        var askedProviders = new HashSet<string>(StringComparer.Ordinal);

        foreach (var lane in _lanes)
        {
            if (ct.IsCancellationRequested) break;

            if (!lane.Provider.IsConfigured)
            {
                // The optional extra key slots are not reported. They exist unconditionally so a
                // key pasted into Settings works without a restart, and naming every empty one
                // would turn the one line a first-run user needs into a list of six.
                if (lane.Provider.AnnouncesMissingKey) (unconfigured ??= []).Add(lane.Provider.Name);
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

            var provider = Config.ProviderConfig.ProviderNameOf(lane.Provider.Name);
            if (_clock.GetUtcNow() >= deadline && !askedProviders.Add(provider))
            {
                _log($"router: out of time, skipping {lane.Provider.Name} - {provider} has already " +
                     "had its attempt");
                continue;
            }

            askedProviders.Add(provider);

            var text = await TryLaneAsync(lane, request, deadline, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Returns null when the whole lane is unusable and the caller should move on.
    ///
    /// <para>
    /// Every failure except a bad key now moves to the NEXT MODEL rather than ending the lane. The
    /// old shape abandoned the provider on the first 429, which is wrong because free providers
    /// meter per model: one Gemini model allows 20 requests a day and another 500, and Groq gives
    /// each of three models its own thousand. A lane was being written off with almost all of its
    /// budget untouched, and the user was told every provider was exhausted while two of Groq's
    /// three models had never been called once.
    /// </para>
    /// </summary>
    private async Task<(string Text, string Model)?> TryLaneAsync(
        Lane lane, TranslationRequest request, DateTimeOffset deadline, CancellationToken ct)
    {
        var rateLimited = 0;
        var tried = 0;

        // The soonest any refused model said it would take us back. Null until one of them says.
        TimeSpan? retryAfter = null;

        foreach (var model in lane.Provider.Models)
        {
            // The first model of a lane is always worth one attempt, even out of time. A lane that
            // is never asked at all is precisely the bug this release exists to fix, and letting a
            // slow first provider starve a healthy second one would reintroduce it wearing a
            // different hat: the user would see English while Groq sat idle.
            if (tried > 0 && _clock.GetUtcNow() >= deadline)
            {
                _log($"router: out of time before {lane.Provider.Name}/{model}");
                break;
            }

            tried++;

            for (var attempt = 0; attempt <= _options.MaxTransientRetries; attempt++)
            {
                if (attempt > 0 && _clock.GetUtcNow() >= deadline)
                {
                    _log($"router: out of time retrying {lane.Provider.Name}/{model}");
                    goto nextModel;
                }

                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);

                    // Clamped to what is left of the budget, or the cap is not a cap: three
                    // attempts at ten seconds each is thirty, on one model, against a twenty
                    // second ceiling. The floor keeps the guaranteed first attempt of a late lane
                    // from being cancelled before it can travel.
                    timeout.CancelAfter(AttemptTimeout(deadline));

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
                    // documents, and is depended on for, never throwing.
                    //
                    // Timeout, not Transient, and that distinction was dead code until now: the
                    // provider's own timeout catch is guarded on the token it was handed, which IS
                    // the cancelled one when the cap fires, so the guard is false and a raw
                    // cancellation arrives here instead. Filing it as Transient meant every
                    // timeout was retried twice more on the same model - thirty seconds spent
                    // proving a slow model is still slow - and the Timeout branch below never ran
                    // for a real HTTP provider at all. The outer-cancellation case is already
                    // handled above, so a bare cancellation reaching here can only be the cap.
                    var e = raised as ProviderException;
                    var failure = e?.Failure ?? ProviderFailure.Timeout;
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
                            // Next model, and the lane is only sidelined if every one of them
                            // says this. The daily allowances are per model and wildly uneven.
                            rateLimited++;

                            // Keep the SOONEST of them. One model out of tokens for the day and
                            // another out for the next four seconds is the normal mixture, and the
                            // lane is usable again as soon as the first one is.
                            if (e?.RetryAfter is { } wait && (retryAfter is null || wait < retryAfter))
                                retryAfter = wait;

                            _log($"router: {lane.Provider.Name}/{model} rate limited, next model" +
                                 $" — {detail}");
                            goto nextModel;

                        case ProviderFailure.ModelRejected:
                            _log($"router: {lane.Provider.Name}/{model} refused the request " +
                                 $"({detail}). Trying the next model.");
                            goto nextModel;

                        case ProviderFailure.Timeout:
                            // Next model, no retry. Retrying a timeout re-runs the exact thing
                            // that just spent the whole window, and with reasoning models in the
                            // free lanes a "retry" is ten more seconds of overlay silence for a
                            // near-certain identical result.
                            _log($"router: {lane.Provider.Name}/{model} timed out, next model");
                            goto nextModel;

                        case ProviderFailure.Fatal:
                            // The one failure that really is about the provider rather than the
                            // model: the key is bad, so no sibling model will do better.
                            _log($"router: {lane.Provider.Name} fatal - {detail}");
                            return null;

                        case ProviderFailure.Transient:
                        default:
                            if (attempt == _options.MaxTransientRetries)
                            {
                                _log($"router: {lane.Provider.Name}/{model} failed after " +
                                     $"{attempt + 1} attempts - {detail}. Trying the next model.");
                                goto nextModel;
                            }

                            // Guarded, because this await sits INSIDE a catch block and an
                            // exception thrown there is not caught by that try's own clauses. A
                            // cancelled token during the backoff delay therefore escaped the
                            // router entirely - out of the one class in this codebase whose
                            // contract is that it never throws. Pre-existing, and made three
                            // times more reachable by walking on to the next model instead of
                            // ending the lane.
                            if (!await BackoffAsync(attempt, ct).ConfigureAwait(false)) return null;
                            break;
                    }
                }
            }

            nextModel: ;
        }

        // Sidelined only when rate limiting is the whole story, and only when the whole story was
        // actually heard. Two conditions, and the second one is easy to lose:
        //
        //   * every model that WAS tried said "too many requests" - if even one failed for another
        //     reason the provider may be healthy, and a blackout would cost us the models that
        //     were never the problem;
        //   * and every model was tried at all. A walk cut short by the budget leaves the rest
        //     UNKNOWN, not refused, and treating one 429 as a verdict on two models nobody asked
        //     is the v0.5.1 defect exactly - abandoning a lane with most of its allowance intact.
        //     It bites hardest with several keys: one slow lane eats the budget, and every lane
        //     behind it gets written off on the first model's answer.
        if (tried > 0 && rateLimited == tried && tried == lane.Provider.Models.Count)
        {
            var cooldown = CooldownFor(retryAfter);
            lane.CooldownUntil = _clock.GetUtcNow() + cooldown;
            _log($"router: {lane.Provider.Name} rate limited on all {tried} models, cooling down " +
                 $"{cooldown.TotalSeconds:F0}s");
        }

        return null;
    }

    /// <summary>
    /// How long to sideline a lane every one of whose models refused.
    ///
    /// <para>
    /// The provider's own <c>retry-after</c> wins when there is one, because the fixed minute was
    /// wrong in both directions. Groq refuses on tokens PER MINUTE and asks for about four seconds
    /// back: a burst that briefly outran one minute's allowance was costing the whole lane a full
    /// sixty, during which the router reported it as unavailable and fell through to nothing. The
    /// floor stops a provider that answers "0" from turning the walk into a spin.
    /// </para>
    /// </summary>
    private TimeSpan CooldownFor(TimeSpan? retryAfter) =>
        retryAfter is not { } wait
            ? _options.RateLimitCooldown
            : wait < _options.MinimumCooldown ? _options.MinimumCooldown
            : wait > _options.RateLimitCooldown ? _options.RateLimitCooldown
            : wait;

    /// <summary>
    /// How long this attempt may run: the per-attempt cap, or whatever is left of the whole
    /// call's budget if that is less. Floored at <see cref="RouterOptions.MinimumAttempt"/> so the
    /// one attempt every lane is guaranteed is a real attempt rather than an instant cancellation.
    /// </summary>
    private TimeSpan AttemptTimeout(DateTimeOffset deadline)
    {
        var remaining = deadline - _clock.GetUtcNow();

        if (remaining < _options.MinimumAttempt) return _options.MinimumAttempt;
        return remaining < _options.RequestTimeout ? remaining : _options.RequestTimeout;
    }

    /// <summary>
    /// Exponential backoff with jitter, so two lanes recovering do not resynchronise. Returns
    /// false when the caller cancelled during the delay, rather than throwing: this is awaited
    /// from inside a catch block, where a thrown exception bypasses the enclosing try's own
    /// handlers and leaves the router.
    /// </summary>
    private async Task<bool> BackoffAsync(int attempt, CancellationToken ct)
    {
        var baseDelay = _options.RetryBaseDelay * Math.Pow(2, attempt);
        var jitter = Random.Shared.NextDouble() * 0.5 + 0.75;

        try
        {
            await Task.Delay(baseDelay * jitter, _clock, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
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
