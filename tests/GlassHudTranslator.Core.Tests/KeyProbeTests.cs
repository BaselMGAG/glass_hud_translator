using GlassHudTranslator.Core.Translation;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The key probe turns a failure that only shows up mid-game into a two-second answer. Its whole
/// value rests on one distinction: "this key is wrong" and "I could not check" must never be
/// confused, because telling someone their key is bad when their connection is down sends them to
/// regenerate a key that was never the problem.
/// </summary>
public class KeyProbeTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    private sealed class FakeProvider(
        IReadOnlyList<string> models,
        Func<string, string> respond,
        bool configured = true) : ITranslationProvider
    {
        public string Name => "fake";
        public IReadOnlyList<string> Models => models;
        public bool IsConfigured => configured;
        public int Calls { get; private set; }

        public Task<string> TranslateAsync(TranslationRequest request, string model, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(respond(model));
        }
    }

    private static ProviderException Fail(ProviderFailure kind, string model = "m") =>
        new("fake", model, kind, kind.ToString());

    [Fact]
    public async Task AKeyThatTranslatesIsWorking()
    {
        var provider = new FakeProvider(["m1"], _ => "مرحباً");

        var result = await KeyProbe.TestAsync(provider, Budget, default);

        Assert.Equal(KeyStatus.Working, result.Status);
        Assert.Equal("m1", result.Detail);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ARejectedKeyIsReportedAsRejected()
    {
        // Fatal is what both providers map 401/403 onto. It is the one verdict worth stating
        // plainly, because it is the only one the user can act on.
        var provider = new FakeProvider(["m1", "m2"],
            _ => throw Fail(ProviderFailure.Fatal));

        var result = await KeyProbe.TestAsync(provider, Budget, default);

        Assert.Equal(KeyStatus.Rejected, result.Status);

        // And it stops immediately - trying the second model with a key the provider has already
        // refused spends a request to learn nothing.
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ARetiredFirstModelFallsThroughRatherThanBlamingTheKey()
    {
        // This is a live situation, not a hypothetical: gemini-2.5-flash-lite started 404ing for
        // new users mid-project. Reporting that as a bad key would send someone to regenerate a
        // perfectly good one.
        var provider = new FakeProvider(["gone", "alive"],
            model => model == "gone" ? throw Fail(ProviderFailure.ModelNotFound, "gone") : "مرحباً");

        var result = await KeyProbe.TestAsync(provider, Budget, default);

        Assert.Equal(KeyStatus.Working, result.Status);
        Assert.Equal("alive", result.Detail);
        Assert.Equal(2, provider.Calls);
    }

    [Theory]
    [InlineData(ProviderFailure.RateLimited)]
    [InlineData(ProviderFailure.Transient)]
    [InlineData(ProviderFailure.ModelNotFound)]
    public async Task AnythingThatIsNotARefusalLeavesTheVerdictUnknown(ProviderFailure failure)
    {
        var provider = new FakeProvider(["m1"], _ => throw Fail(failure));

        Assert.Equal(KeyStatus.Unknown, (await KeyProbe.TestAsync(provider, Budget, default)).Status);
    }

    [Fact]
    public async Task NoNetworkIsUnknownRatherThanRejected()
    {
        var provider = new FakeProvider(["m1"], _ => throw new HttpRequestException("DNS"));

        Assert.Equal(KeyStatus.Unknown, (await KeyProbe.TestAsync(provider, Budget, default)).Status);
    }

    [Fact]
    public async Task AProviderWithNoKeyIsNotAsked()
    {
        var provider = new FakeProvider(["m1"], _ => "مرحباً", configured: false);

        Assert.Equal(KeyStatus.NotSet, (await KeyProbe.TestAsync(provider, Budget, default)).Status);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task AnEmptyReplyIsNotSuccess()
    {
        // A lane that answers 200 with nothing has not proved the key works, and calling it Working
        // would send the user into a game with an overlay that never fills in.
        var provider = new FakeProvider(["m1"], _ => "   ");

        Assert.Equal(KeyStatus.Unknown, (await KeyProbe.TestAsync(provider, Budget, default)).Status);
    }

    [Fact]
    public async Task AProviderWithNoModelsIsUnknownRatherThanThrowing()
    {
        var provider = new FakeProvider([], _ => "مرحباً");

        Assert.Equal(KeyStatus.Unknown, (await KeyProbe.TestAsync(provider, Budget, default)).Status);
    }

    [Fact]
    public async Task TheProbeNeverThrows()
    {
        // A diagnostic that fails loudly while reporting on something else is worse than useless.
        var provider = new FakeProvider(["m1"], _ => throw new InvalidOperationException("boom"));

        var result = await KeyProbe.TestAsync(provider, Budget, default);

        Assert.Equal(KeyStatus.Unknown, result.Status);
        Assert.Equal("boom", result.Detail);
    }
}
