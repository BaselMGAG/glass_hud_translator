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

    public List<(string Model, int Attempt)> Calls { get; } = [];

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
        var request = new TranslationRequest("And then?", PreviousLine: "She went to Limsa Lominsa.");

        var (_, user) = PromptBuilder.Build(request);

        Assert.Contains("Previous line:", user);
        Assert.Contains("She went to Limsa Lominsa.", user);
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
