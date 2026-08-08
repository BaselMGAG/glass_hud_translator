using GlassHudTranslator.Core.Glossary;
using GlassHudTranslator.Core.Translation;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>Scriptable provider: each call pops the next behaviour from a queue.</summary>
internal sealed class FakeProvider(string name, params string[] models) : ITranslationProvider
{
    private readonly Queue<Func<string, string>> _script = new();

    public string Name { get; } = name;

    public IReadOnlyList<string> Models { get; } = models.Length > 0 ? models : ["m1"];

    public bool IsConfigured { get; set; } = true;

    public List<(string Model, int Attempt)> Calls { get; } = [];

    /// <summary>Every request as received, so tests can assert on context, glossary and speaker.</summary>
    public List<TranslationRequest> Requests { get; } = [];

    public FakeProvider Returns(string text)
    {
        _script.Enqueue(_ => text);
        return this;
    }

    public FakeProvider Fails(ProviderFailure failure, int times = 1)
    {
        for (var i = 0; i < times; i++)
            _script.Enqueue(model => throw new ProviderException(Name, model, failure, failure.ToString()));
        return this;
    }

    public Task<string> TranslateAsync(TranslationRequest request, string model, CancellationToken ct)
    {
        Calls.Add((model, Calls.Count));
        Requests.Add(request);
        if (_script.Count == 0) throw new ProviderException(Name, model, ProviderFailure.Fatal, "script exhausted");
        return Task.FromResult(_script.Dequeue()(model));
    }
}

public class ProviderRouterTests
{
    private static readonly RouterOptions FastRetries = new()
    {
        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
    };

    private static TranslationRequest Request(DateTimeOffset? requestedAt = null) =>
        new("Come, the aether stirs.", "Y'shtola", RequestedAt: requestedAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public async Task UsesTheFirstProviderWhenItWorks()
    {
        var gemini = new FakeProvider("gemini").Returns("تعال");
        var groq = new FakeProvider("groq").Returns("NOT THIS");
        var router = new ProviderRouter([(gemini, 13), (groq, 28)], FastRetries);

        var result = await router.TranslateAsync(Request(), CancellationToken.None);

        Assert.Equal("تعال", result.Text);
        Assert.Equal("gemini", result.Provider);
        Assert.Equal(TranslationLogOutcomes.Ok, result.Outcome);
        Assert.Empty(groq.Calls);
    }

    [Fact]
    public async Task RateLimitedProviderFailsOverToTheNextLaneImmediately()
    {
        // Not only on daily exhaustion - a 429 mid-cutscene must move to Groq at once, or the
        // densest dialogue outruns Gemini's ~15 RPM on its own.
        var gemini = new FakeProvider("gemini").Fails(ProviderFailure.RateLimited);
        var groq = new FakeProvider("groq").Returns("من groq");
        var router = new ProviderRouter([(gemini, 13), (groq, 28)], FastRetries);

        var result = await router.TranslateAsync(Request(), CancellationToken.None);

        Assert.Equal("groq", result.Provider);
        Assert.Equal("من groq", result.Text);
    }

    [Fact]
    public async Task RateLimitedProviderStaysInCooldownForSubsequentRequests()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gemini = new FakeProvider("gemini").Fails(ProviderFailure.RateLimited).Returns("recovered");
        var groq = new FakeProvider("groq").Returns("first").Returns("second");
        var router = new ProviderRouter([(gemini, 13), (groq, 28)], FastRetries, clock);

        await router.TranslateAsync(Request(clock.GetUtcNow()), CancellationToken.None);
        var second = await router.TranslateAsync(Request(clock.GetUtcNow()), CancellationToken.None);

        Assert.Equal("groq", second.Provider);
        Assert.Single(gemini.Calls);   // not retried during cooldown
    }

    [Fact]
    public async Task DeletedModelFallsThroughToTheNextModelInTheSameProvider()
    {
        // The failure that silently takes a shipped build offline weeks later.
        var gemini = new FakeProvider("gemini", "gemini-2.5-flash-lite", "gemini-2.5-flash")
            .Fails(ProviderFailure.ModelNotFound)
            .Returns("من النموذج الثاني");
        var logged = new List<string>();
        var router = new ProviderRouter([(gemini, 13)], FastRetries, log: logged.Add);

        var result = await router.TranslateAsync(Request(), CancellationToken.None);

        Assert.Equal("من النموذج الثاني", result.Text);
        Assert.Equal("gemini-2.5-flash", result.Model);
        Assert.Contains(logged, l => l.Contains("MODEL GONE"));
    }

    [Fact]
    public async Task TransientFailuresAreRetriedThenTheLaneIsAbandoned()
    {
        var gemini = new FakeProvider("gemini").Fails(ProviderFailure.Transient, times: 3);
        var groq = new FakeProvider("groq").Returns("من groq");
        var router = new ProviderRouter([(gemini, 13), (groq, 28)], FastRetries);

        var result = await router.TranslateAsync(Request(), CancellationToken.None);

        Assert.Equal(3, gemini.Calls.Count);   // initial attempt + 2 retries
        Assert.Equal("groq", result.Provider);
    }

    [Fact]
    public async Task TransientFailureThatRecoversOnRetryIsServed()
    {
        var gemini = new FakeProvider("gemini").Fails(ProviderFailure.Transient).Returns("نجح");
        var router = new ProviderRouter([(gemini, 13)], FastRetries);

        var result = await router.TranslateAsync(Request(), CancellationToken.None);

        Assert.Equal("نجح", result.Text);
    }

    [Fact]
    public async Task StaleRequestIsDroppedRatherThanSent()
    {
        // Serving a backlog produces translations for dialogue that is already gone.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gemini = new FakeProvider("gemini").Returns("too late");
        var router = new ProviderRouter([(gemini, 13)], FastRetries, clock);

        var requestedAt = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromSeconds(7));

        var result = await router.TranslateAsync(Request(requestedAt), CancellationToken.None);

        Assert.Equal(TranslationLogOutcomes.Stale, result.Outcome);
        Assert.Empty(gemini.Calls);
    }

    [Fact]
    public async Task EverythingFailingYieldsEnglishRatherThanNothing()
    {
        // Never blank, never crash.
        var gemini = new FakeProvider("gemini").Fails(ProviderFailure.Fatal);
        var groq = new FakeProvider("groq").Fails(ProviderFailure.Fatal);
        var router = new ProviderRouter([(gemini, 13), (groq, 28)], FastRetries);

        var result = await router.TranslateAsync(Request(), CancellationToken.None);

        Assert.Equal("Come, the aether stirs.", result.Text);
        Assert.True(result.IsFallbackEnglish);
        Assert.Equal(TranslationLogOutcomes.FallbackEnglish, result.Outcome);
    }

    [Fact]
    public async Task ExhaustedRateBucketSkipsTheLaneWithoutCallingIt()
    {
        var gemini = new FakeProvider("gemini").Returns("a").Returns("b");
        var groq = new FakeProvider("groq").Returns("from groq");
        var router = new ProviderRouter([(gemini, 1), (groq, 28)], FastRetries);

        await router.TranslateAsync(Request(), CancellationToken.None);
        var second = await router.TranslateAsync(Request(), CancellationToken.None);

        Assert.Single(gemini.Calls);
        Assert.Equal("groq", second.Provider);
    }

    [Fact]
    public async Task SuccessfulCallsRaiseProviderUsedSoQuotaCanBeCounted()
    {
        var used = new List<string>();
        var router = new ProviderRouter([(new FakeProvider("gemini").Returns("x"), 13)], FastRetries);
        router.ProviderUsed += (name, _) => { used.Add(name); return Task.CompletedTask; };

        await router.TranslateAsync(Request(), CancellationToken.None);

        Assert.Equal(["gemini"], used);
    }

    [Fact]
    public async Task AnUnconfiguredLaneIsSkippedWithoutBeingCalled()
    {
        // What makes the paid lanes safe to ship switched on. Without a key the lane must cost
        // nothing and say nothing, not fail once per line into the log the user reads to diagnose
        // real problems.
        var paid = new FakeProvider("anthropic").Returns("NOT THIS");
        paid.IsConfigured = false;
        var free = new FakeProvider("gemini").Returns("من gemini");

        var router = new ProviderRouter([(paid, 40), (free, 13)], FastRetries);
        var result = await router.TranslateAsync(Request(), CancellationToken.None);

        Assert.Equal("gemini", result.Provider);
        Assert.Empty(paid.Calls);
    }

    [Fact]
    public async Task WhenEveryLaneIsUnconfiguredTheLogSaysSoRatherThanJustGivingUp()
    {
        // The first-run case: nothing is set up yet, so every line falls back to English. Staying
        // silent per lane is right during play, but here it left the user with "all providers
        // exhausted" and no hint that the only problem was a missing key.
        var log = new List<string>();
        var gemini = new FakeProvider("gemini") { IsConfigured = false };
        var anthropic = new FakeProvider("anthropic") { IsConfigured = false };

        var router = new ProviderRouter([(gemini, 13), (anthropic, 40)], FastRetries, log: log.Add);
        var result = await router.TranslateAsync(Request(), CancellationToken.None);

        Assert.Equal(ProviderNames.Fallback, result.Provider);

        var exhausted = Assert.Single(log, line => line.Contains("exhausted"));
        Assert.Contains("No API key for: gemini, anthropic", exhausted);
        Assert.Contains("Settings", exhausted);
    }

    [Fact]
    public async Task AConfiguredLaneFailingDoesNotClaimAMissingKey()
    {
        var log = new List<string>();
        var gemini = new FakeProvider("gemini").Fails(ProviderFailure.Fatal);

        var router = new ProviderRouter([(gemini, 13)], FastRetries, log: log.Add);
        await router.TranslateAsync(Request(), CancellationToken.None);

        var exhausted = Assert.Single(log, line => line.Contains("exhausted"));
        Assert.DoesNotContain("No API key", exhausted);
    }

    [Fact]
    public async Task APerAttemptTimeoutFallsThroughInsteadOfThrowing()
    {
        // A provider that simply awaits the token it was handed raises a bare
        // OperationCanceledException when the per-attempt cap fires. That used to escape the
        // router entirely - out of a class whose contract, and whose callers, depend on it never
        // throwing - and surfaced as a crash rather than as English on the overlay.
        var hanging = new HangingProvider("slow");
        var quick = new FakeProvider("groq").Returns("من groq");

        var router = new ProviderRouter([(hanging, 60), (quick, 60)], new RouterOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(50),
            MaxTransientRetries = 0,
        });

        var result = await router.TranslateAsync(Request(), CancellationToken.None);

        Assert.Equal("groq", result.Provider);
        Assert.Equal(TranslationLogOutcomes.Ok, result.Outcome);
    }

    [Fact]
    public async Task ATimeoutOnEveryLaneStillDegradesToEnglish()
    {
        var hanging = new HangingProvider("slow");

        var router = new ProviderRouter([(hanging, 60)], new RouterOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(50),
            MaxTransientRetries = 0,
        });

        var result = await router.TranslateAsync(Request(), CancellationToken.None);

        Assert.Equal(ProviderNames.Fallback, result.Provider);
        Assert.Equal(TranslationLogOutcomes.FallbackEnglish, result.Outcome);
        Assert.Equal("Come, the aether stirs.", result.Text);
    }
}

public class TokenBucketTests
{
    [Fact]
    public void AllowsUpToCapacityThenRefuses()
    {
        var bucket = new TokenBucket(3, new FakeTimeProvider());

        Assert.True(bucket.TryTake());
        Assert.True(bucket.TryTake());
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public void RefillsOverTime()
    {
        var clock = new FakeTimeProvider();
        var bucket = new TokenBucket(60, clock);   // one per second

        for (var i = 0; i < 60; i++) Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());

        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(bucket.TryTake());
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public void NeverRefillsAboveCapacity()
    {
        var clock = new FakeTimeProvider();
        var bucket = new TokenBucket(5, clock);

        clock.Advance(TimeSpan.FromHours(1));

        for (var i = 0; i < 5; i++) Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }
}

public class PromptBuilderTests
{
    [Fact]
    public void IncludesOnlyTheMatchedGlossaryTerms()
    {
        var request = new TranslationRequest("Come, the aether stirs.", "Y'shtola",
            [new GlossaryTerm("aether", "الأثير")]);

        var (_, user) = PromptBuilder.Build(request);

        Assert.Contains("aether = الأثير", user);
        Assert.Contains("Speaker: Y'shtola", user);
        Assert.Contains("Line: Come, the aether stirs.", user);
    }

    [Fact]
    public void OmitsEmptySections()
    {
        var (_, user) = PromptBuilder.Build(new TranslationRequest("Hello."));

        Assert.DoesNotContain("Glossary:", user);
        Assert.DoesNotContain("Speaker:", user);
        Assert.DoesNotContain("Previous line:", user);
        Assert.Equal("Line: Hello.", user);
    }

    [Fact]
    public void CarriesOneLineOfContextForPronounAgreement()
    {
        var request = new TranslationRequest("And then?", PreviousLines: ["She went to Limsa Lominsa."]);

        var (_, user) = PromptBuilder.Build(request);

        Assert.Contains("Previous line:", user);
        Assert.Contains("She went to Limsa Lominsa.", user);
    }

    [Fact]
    public void RendersAContextWindowOldestFirst()
    {
        var request = new TranslationRequest("And then?", PreviousLines:
            ["First this was said.", "Then this.", "And finally this."]);

        var (_, user) = PromptBuilder.Build(request);

        // The header says which end is which, because the model has no other way to know - and a
        // window read newest-first inverts who "she" was two lines ago.
        Assert.Contains("Previous lines, oldest first:", user);
        Assert.True(
            user.IndexOf("First this was said.", StringComparison.Ordinal)
                < user.IndexOf("Then this.", StringComparison.Ordinal)
            && user.IndexOf("Then this.", StringComparison.Ordinal)
                < user.IndexOf("And finally this.", StringComparison.Ordinal),
            "Context lines rendered out of order.");
    }

    [Theory]
    [InlineData(ArabicRegister.ModernStandard, "Modern Standard Arabic")]
    [InlineData(ArabicRegister.Egyptian, "Egyptian Arabic")]
    public void RegisterIsOneLineOfTheSystemPrompt(ArabicRegister register, string expected)
    {
        var (system, _) = PromptBuilder.Build(new TranslationRequest("Hello.", Register: register));

        Assert.Contains(expected, system);
    }

    [Fact]
    public void SystemPromptForbidsCommentary()
    {
        var (system, _) = PromptBuilder.Build(new TranslationRequest("Hello."));

        Assert.Contains("Output ONLY the Arabic translation", system);
    }
}

/// <summary>Waits on whatever token it is given, which is how a real client behaves on a stall.</summary>
internal sealed class HangingProvider(string name) : ITranslationProvider
{
    public string Name { get; } = name;

    public IReadOnlyList<string> Models { get; } = ["m1"];

    public async Task<string> TranslateAsync(TranslationRequest request, string model, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
        return "unreachable";
    }
}

public class ProviderTimeoutTests
{
    private static readonly RouterOptions FastRetries = new()
    {
        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
    };

    [Fact]
    public async Task ATimeoutMovesToTheNextModelInsteadOfRetryingTheSameOne()
    {
        // The free lanes run reasoning models now, and a model that spent the whole window
        // thinking will spend the next window the same way. Retrying used to triple the wait:
        // three ten-second attempts on a model that was never going to answer, while the overlay
        // showed nothing. One try, next model.
        var provider = new FakeProvider("gemini", "slow", "fast");
        provider.Fails(ProviderFailure.Timeout).Returns("ترجمة");
        var router = new ProviderRouter([(provider, 600)], FastRetries);

        var result = await router.TranslateAsync(
            new TranslationRequest("Come with me.", RequestedAt: DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal("ترجمة", result.Text);
        Assert.Equal(2, provider.Calls.Count);
        Assert.Equal("slow", provider.Calls[0].Model);
        Assert.Equal("fast", provider.Calls[1].Model);
    }

    [Fact]
    public async Task EveryModelTimingOutStillEndsInEnglishFallbackNotAnException()
    {
        var provider = new FakeProvider("gemini", "m1", "m2");
        provider.Fails(ProviderFailure.Timeout, times: 2);
        var router = new ProviderRouter([(provider, 600)], FastRetries);

        var result = await router.TranslateAsync(
            new TranslationRequest("Come with me.", RequestedAt: DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.True(result.IsFallbackEnglish);
        Assert.Equal(2, provider.Calls.Count);   // one attempt per model, no retries
    }
}

public class RateLimitFallthroughTests
{
    private static readonly RouterOptions FastRetries = new()
    {
        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
    };

    private static TranslationRequest Line() =>
        new("Come with me.", RequestedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task ARateLimitedModelFallsThroughToItsSiblingInTheSameLane()
    {
        // The bug, exactly as it was reported. Free providers meter PER MODEL - one Gemini model
        // allows 20 requests a day and another 500 - so a 429 on the first says nothing about the
        // rest. Ending the lane there left 498 requests unspent and told the user every provider
        // was exhausted.
        var gemini = new FakeProvider("gemini", "small-quota", "big-quota");
        gemini.Fails(ProviderFailure.RateLimited).Returns("تعال معي.");
        var router = new ProviderRouter([(gemini, 600)], FastRetries);

        var result = await router.TranslateAsync(Line(), CancellationToken.None);

        Assert.Equal("تعال معي.", result.Text);
        Assert.Equal("big-quota", result.Model);
        Assert.Equal(2, gemini.Calls.Count);
    }

    [Fact]
    public async Task AnExhaustedGeminiHandsOverToGroqInsteadOfGivingUp()
    {
        // Precisely the evening this was reported: Gemini's daily allowance gone, Groq untouched,
        // and the overlay saying "translation failed" the whole time.
        var gemini = new FakeProvider("gemini", "m1", "m2", "m3");
        gemini.Fails(ProviderFailure.RateLimited, times: 3);

        var groq = new FakeProvider("groq", "llama", "gpt-oss");
        groq.Returns("هلمّ معي.");

        var router = new ProviderRouter([(gemini, 600), (groq, 600)], FastRetries);

        var result = await router.TranslateAsync(Line(), CancellationToken.None);

        Assert.Equal("هلمّ معي.", result.Text);
        Assert.Equal("groq", result.Provider);
        Assert.False(result.IsFallbackEnglish);

        // Every Gemini model was actually tried before moving on - not one and out.
        Assert.Equal(3, gemini.Calls.Count);
    }

    [Fact]
    public async Task ALaneCoolsDownOnlyWhenEveryModelIsRateLimited()
    {
        var clock = new FakeTimeProvider();
        var gemini = new FakeProvider("gemini", "m1", "m2");
        gemini.Fails(ProviderFailure.RateLimited, times: 2);
        var groq = new FakeProvider("groq").Returns("أ").Returns("ب");

        var router = new ProviderRouter([(gemini, 600), (groq, 600)], FastRetries, clock);

        await router.TranslateAsync(Line(), CancellationToken.None);
        Assert.Equal(2, gemini.Calls.Count);

        // Sidelined, so the second line does not pay for two more instant 429s.
        await router.TranslateAsync(
            new TranslationRequest("And then?", RequestedAt: clock.GetUtcNow()),
            CancellationToken.None);

        Assert.Equal(2, gemini.Calls.Count);
    }

    [Fact]
    public async Task OneRateLimitAmongOtherFailuresDoesNotSidelineTheLane()
    {
        // A lane where one model is throttled and another is merely broken is not a throttled
        // lane. Blacking it out for a minute would keep us from the models that were fine.
        //
        // Timeout rather than Transient for the second model, deliberately: a transient failure
        // backs off through the injected clock, and a FakeTimeProvider that nobody advances makes
        // that delay wait forever. A timeout goes straight to the next model, which is the
        // behaviour under test here anyway.
        var clock = new FakeTimeProvider();
        var gemini = new FakeProvider("gemini", "limited", "broken");
        gemini.Fails(ProviderFailure.RateLimited).Fails(ProviderFailure.Timeout);
        var groq = new FakeProvider("groq").Returns("أ").Returns("ب");

        var router = new ProviderRouter([(gemini, 600), (groq, 600)], FastRetries, clock);

        await router.TranslateAsync(Line(), CancellationToken.None);
        var afterFirst = gemini.Calls.Count;

        await router.TranslateAsync(
            new TranslationRequest("And then?", RequestedAt: clock.GetUtcNow()),
            CancellationToken.None);

        Assert.True(gemini.Calls.Count > afterFirst,
            "The lane was sidelined even though not every model was rate limited.");
    }

    [Fact]
    public async Task AModelThatRefusesTheRequestDoesNotCondemnTheProvider()
    {
        // A 400 is about this model and this request - a token ceiling below what we asked for,
        // a parameter a sibling accepts. Only a bad key ends the lane.
        var gemini = new FakeProvider("gemini", "fussy", "fine");
        gemini.Fails(ProviderFailure.ModelRejected).Returns("تعال معي.");
        var router = new ProviderRouter([(gemini, 600)], FastRetries);

        var result = await router.TranslateAsync(Line(), CancellationToken.None);

        Assert.Equal("تعال معي.", result.Text);
        Assert.Equal(2, gemini.Calls.Count);
    }

    [Fact]
    public async Task ABadKeyStillEndsItsLaneImmediately()
    {
        // The one failure that really is about the provider. Walking the rest of the models with a
        // key the provider has already refused spends requests to learn nothing.
        var gemini = new FakeProvider("gemini", "m1", "m2", "m3");
        gemini.Fails(ProviderFailure.Fatal, times: 3);
        var groq = new FakeProvider("groq").Returns("هلمّ معي.");

        var router = new ProviderRouter([(gemini, 600), (groq, 600)], FastRetries);

        var result = await router.TranslateAsync(Line(), CancellationToken.None);

        Assert.Equal("groq", result.Provider);
        Assert.Equal(1, gemini.Calls.Count);
    }

    [Fact]
    public async Task AnExhaustedBudgetStopsTheWalkButStillTriesOneModelPerLane()
    {
        // Two rules meeting. The budget stops a run of slow models spending a minute before the
        // overlay says anything - but it must never mean a lane goes unasked, because that is the
        // original bug in another form. So: the first model of each lane always gets a turn, and
        // the rest of that lane's list is what the budget cuts.
        var clock = new FakeTimeProvider();
        var gemini = new FakeProvider("gemini", "m1", "m2", "m3", "m4");
        for (var i = 0; i < 4; i++) gemini.Fails(ProviderFailure.Timeout);
        var groq = new FakeProvider("groq", "g1", "g2").Returns("هلمّ معي.");

        var router = new ProviderRouter(
            [(gemini, 600), (groq, 600)],
            new RouterOptions
            {
                RetryBaseDelay = TimeSpan.FromMilliseconds(1),
                TotalBudget = TimeSpan.Zero,
            },
            clock);

        var result = await router.TranslateAsync(Line(), CancellationToken.None);

        // Gemini's walk was cut to its first model, and Groq - out of time from the start - was
        // still asked, and answered.
        Assert.Single(gemini.Calls);
        Assert.Equal("هلمّ معي.", result.Text);
        Assert.False(result.IsFallbackEnglish);
    }
}

/// <summary>
/// Everything an adversarial review confirmed before v0.5.1 shipped. Each of these is a way the
/// router could still leave the user looking at English while a provider was willing to answer.
/// </summary>
public class RouterBudgetTests
{
    private static TranslationRequest Line() =>
        new("Come with me.", RequestedAt: DateTimeOffset.UtcNow);

    /// <summary>Blocks until released, so a test can hold an attempt open past a deadline.</summary>
    private sealed class HangingUntilCancelled(string name, params string[] models) : ITranslationProvider
    {
        public string Name { get; } = name;

        public IReadOnlyList<string> Models { get; } = models.Length > 0 ? models : ["m1"];

        public int Calls { get; private set; }

        public async Task<string> TranslateAsync(TranslationRequest r, string model, CancellationToken ct)
        {
            Calls++;
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return "unreachable";
        }
    }

    [Fact]
    public async Task ASlowFirstLaneStillLeavesTheSecondLaneATurn()
    {
        // The regression the budget itself introduced, and the worst one: a provider that hangs
        // could spend the whole allowance and the healthy lane behind it was never asked. That is
        // the original bug wearing a different hat - English on screen while Groq sits idle.
        var gemini = new HangingUntilCancelled("gemini", "m1", "m2", "m3");
        var groq = new FakeProvider("groq").Returns("هلمّ معي.");

        var router = new ProviderRouter(
            [(gemini, 600), (groq, 600)],
            new RouterOptions
            {
                RequestTimeout = TimeSpan.FromMilliseconds(60),
                TotalBudget = TimeSpan.FromMilliseconds(80),
                MinimumAttempt = TimeSpan.FromMilliseconds(60),
                MaxTransientRetries = 0,
                RetryBaseDelay = TimeSpan.FromMilliseconds(1),
            });

        var result = await router.TranslateAsync(Line(), CancellationToken.None);

        Assert.Equal("هلمّ معي.", result.Text);
        Assert.False(result.IsFallbackEnglish);
    }

    [Fact]
    public async Task OneModelCannotSpendTheWholeBudgetThreeTimesOver()
    {
        // The per-attempt cap was not clamped to the budget, so three retries at ten seconds ran
        // for thirty against a twenty second ceiling - on a single model, before any other lane
        // was reached.
        var slow = new HangingUntilCancelled("gemini", "m1", "m2", "m3");

        var router = new ProviderRouter(
            [(slow, 600)],
            new RouterOptions
            {
                RequestTimeout = TimeSpan.FromMilliseconds(200),
                TotalBudget = TimeSpan.FromMilliseconds(150),
                MinimumAttempt = TimeSpan.FromMilliseconds(20),
                RetryBaseDelay = TimeSpan.FromMilliseconds(1),
            });

        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await router.TranslateAsync(Line(), CancellationToken.None);
        started.Stop();

        Assert.True(result.IsFallbackEnglish);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2),
            $"One lane ran for {started.Elapsed.TotalSeconds:F1}s against a 0.15s budget.");
    }

    [Fact]
    public async Task ATimeoutIsNotRetriedOnTheSameModel()
    {
        // A raw cancellation from the per-attempt cap used to be filed as Transient, because the
        // provider's own timeout catch is guarded on the very token that was cancelled. Every
        // timeout was therefore retried twice more on a model that had just proved it was slow,
        // and the Timeout branch never ran for a real HTTP provider at all.
        var slow = new HangingUntilCancelled("gemini", "m1", "m2");

        var router = new ProviderRouter(
            [(slow, 600)],
            new RouterOptions
            {
                RequestTimeout = TimeSpan.FromMilliseconds(40),
                TotalBudget = TimeSpan.FromSeconds(5),
                MinimumAttempt = TimeSpan.FromMilliseconds(40),
                MaxTransientRetries = 2,
                RetryBaseDelay = TimeSpan.FromMilliseconds(1),
            });

        await router.TranslateAsync(Line(), CancellationToken.None);

        // Two models, one attempt each. Six would mean timeouts are being retried.
        Assert.Equal(2, slow.Calls);
    }

    [Fact]
    public async Task CancellingDuringTheRetryBackoffDoesNotThrowOutOfTheRouter()
    {
        // The await sat inside a catch block, where a throw bypasses that try's own handlers and
        // leaves the class whose entire contract is that it never throws.
        var provider = new FakeProvider("gemini", "m1", "m2");
        provider.Fails(ProviderFailure.Transient, times: 6);

        var router = new ProviderRouter(
            [(provider, 600)],
            new RouterOptions { RetryBaseDelay = TimeSpan.FromMilliseconds(400) });

        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        var result = await router.TranslateAsync(Line(), cancelled.Token);

        Assert.NotNull(result);
        Assert.True(result.IsFallbackEnglish);
    }
}

public class BadKeyClassificationTests
{
    [Theory]
    // Verbatim from the live Gemini endpoint with a corrupted key. The first guess at this list
    // did not contain it, and a probe against the real provider is the only reason it does now.
    [InlineData("[{\"error\":{\"code\":400,\"message\":\"Invalid Auth key.\",\"status\":\"INVALID_ARGUMENT\"}}]")]
    [InlineData("{\"error\":{\"code\":400,\"message\":\"API key not valid. Please pass a valid API key.\"}}")]
    [InlineData("{\"error\":{\"message\":\"Incorrect API key provided\"}}")]
    [InlineData("{\"error\":{\"message\":\"invalid_api_key\"}}")]
    public void ABadKeyIsRecognisedEvenWhenTheProviderCallsIt400(string body)
    {
        // Gemini answers a rejected key with 400, not 401 - verified against the live endpoint.
        // Without this the key test says «تعذّر التحقّق», "could not check", about a key the
        // provider explicitly refused, which is the one confusion the three-way verdict exists to
        // prevent.
        Assert.True(ProviderDiagnostics.MentionsBadKey(body));
    }

    [Theory]
    [InlineData("{\"error\":{\"message\":\"max_tokens is too large for this model\"}}")]
    [InlineData("{\"error\":{\"message\":\"temperature must be between 0 and 2\"}}")]
    [InlineData("")]
    [InlineData(null)]
    public void AnOrdinaryBadRequestIsNotMistakenForABadKey(string? body)
    {
        // These must stay ModelRejected so the router tries the next model rather than writing
        // off a working key.
        Assert.False(ProviderDiagnostics.MentionsBadKey(body));
    }
}

public class InlineReasoningTests
{
    [Fact]
    public void ReasoningAheadOfTheAnswerIsStripped()
    {
        // Qwen-family models on these endpoints put their chain of thought inline, before the
        // answer. Unstripped, the overlay shows paragraphs of English deliberation with the Arabic
        // at the bottom - and the cache stores all of it forever under that line's key.
        var content = "<think>The user wants Arabic. Let me consider tone...</think>\nتعال معي.";

        Assert.Equal("\nتعال معي.", OpenAiCompatibleProvider.StripInlineReasoning(content));
    }

    [Fact]
    public void AnUnterminatedThinkBlockIsNoAnswerAtAll()
    {
        // The model ran out of tokens mid-thought. Half a chain of thought must not ship as a
        // translation; null routes it into the empty-completion failure, which is the truth.
        Assert.Null(OpenAiCompatibleProvider.StripInlineReasoning(
            "<think>First, the aetheryte. The word aether comes from"));
    }

    [Fact]
    public void OrdinaryAnswersPassThroughUntouched()
    {
        Assert.Equal("تعال معي.", OpenAiCompatibleProvider.StripInlineReasoning("تعال معي."));
        Assert.Null(OpenAiCompatibleProvider.StripInlineReasoning(null));

        // A think tag mid-answer is content, not a wrapper - only a LEADING block is reasoning.
        var mentions = "الكلمة <think> تعني التفكير.";
        Assert.Equal(mentions, OpenAiCompatibleProvider.StripInlineReasoning(mentions));
    }
}

public class StubProviderTests
{
    [Fact]
    public async Task ReturnsArabicWithoutANetworkCall()
    {
        var stub = new StubProvider(TimeSpan.Zero);

        var text = await stub.TranslateAsync(new TranslationRequest("Come.", "Y'shtola"), "stub",
            CancellationToken.None);

        Assert.Contains("Y'shtola", text);
        Assert.Contains("تجريبي", text);
    }
}
