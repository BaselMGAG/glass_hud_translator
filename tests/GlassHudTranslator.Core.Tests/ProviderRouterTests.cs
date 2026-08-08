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
