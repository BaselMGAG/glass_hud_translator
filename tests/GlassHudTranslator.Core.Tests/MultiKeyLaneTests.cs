using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Translation;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// One provider, up to three keys, three lanes.
///
/// <para>
/// The feature exists because Gemini's free tier is per ACCOUNT and the answer to running out of it
/// is a second account, not a second provider. Expanding into lanes rather than teaching the router
/// about keys is what makes "all the Geminis first, then Groq" fall out of the ordering the router
/// already had.
/// </para>
/// </summary>
public class MultiKeyLaneTests
{
    private static ProviderConfig Lane(string name = "gemini", string? secret = "GeminiApiKey") => new()
    {
        Name = name,
        BaseUrl = "https://example.test/v1",
        Secret = secret,
        ModelEntries = [ModelEntry.Named("m")],
    };

    [Fact]
    public void TheFirstSlotKeepsTheNameEveryExistingInstallationAlreadyUses()
    {
        // Load-bearing. Every machine running v0.5.1 has a key filed under "GeminiApiKey"; a
        // suffix on slot 1 would silently log all of them out of their own provider, and the
        // symptom would be the English fallback with no explanation.
        var lane = Lane();

        Assert.Equal("GeminiApiKey", lane.SecretSlot(1));
        Assert.Equal("GeminiApiKey#2", lane.SecretSlot(2));
        Assert.Equal("GeminiApiKey#3", lane.SecretSlot(3));

        Assert.Equal("gemini", lane.LaneName(1));
        Assert.Equal("gemini#2", lane.LaneName(2));
    }

    [Theory]
    [InlineData("gemini", "gemini")]
    [InlineData("gemini#2", "gemini")]
    [InlineData("gemini#3", "gemini")]
    [InlineData("openai", "openai")]
    public void ALaneNameResolvesBackToItsProvider(string lane, string provider)
    {
        Assert.Equal(provider, ProviderConfig.ProviderNameOf(lane));
    }

    [Fact]
    public void EveryKeySlotBecomesALaneWhetherOrNotItHasAKeyYet()
    {
        // Building only the slots that held keys at STARTUP would be cheaper and wrong: a second
        // key pasted into Settings would do nothing until the app was restarted, while the Test
        // button beside it said it worked. That exact shape - a positive confirmation next to a
        // control that had not taken effect - is the defect v0.5.0 shipped.
        using var http = new HttpClient();
        var secrets = new InMemorySecretStore();

        var lanes = ProviderFactory.CreateLanes(Lane(), http, secrets).ToList();

        Assert.Equal(ProviderConfig.MaxKeys, lanes.Count);
        Assert.Equal(["gemini", "gemini#2", "gemini#3"], lanes.Select(l => l.Provider.Name));
        Assert.All(lanes, l => Assert.False(l.Provider.IsConfigured));

        // Pasted after the lanes were built, exactly as it happens in Settings.
        secrets.Set("GeminiApiKey#2", "second-account-key");

        Assert.False(lanes[0].Provider.IsConfigured);
        Assert.True(lanes[1].Provider.IsConfigured);
        Assert.False(lanes[2].Provider.IsConfigured);
    }

    [Fact]
    public void OnlyTheFirstSlotComplainsAboutHavingNoKey()
    {
        // The router's "No API key for: ..." line is there to rescue someone who has entered
        // nothing at all. Listing gemini#2, gemini#3, groq#2, groq#3 alongside would bury the two
        // names that matter under four they never asked for.
        using var http = new HttpClient();
        var lanes = ProviderFactory.CreateLanes(Lane(), http, new InMemorySecretStore()).ToList();

        Assert.True(lanes[0].Provider.AnnouncesMissingKey);
        Assert.False(lanes[1].Provider.AnnouncesMissingKey);
        Assert.False(lanes[2].Provider.AnnouncesMissingKey);
    }

    [Fact]
    public void ALaneThatTakesNoKeyStaysASingleLane()
    {
        // Ollama. Three copies of a local endpoint would be three identical lanes racing one
        // process, and the second and third would never be reached anyway.
        using var http = new HttpClient();

        var lanes = ProviderFactory
            .CreateLanes(Lane("ollama", secret: null), http, new InMemorySecretStore())
            .ToList();

        var single = Assert.Single(lanes);
        Assert.Equal("ollama", single.Provider.Name);
        Assert.True(single.Provider.IsConfigured);
    }

    [Fact]
    public void EveryLaneOfAProviderCarriesThatProvidersRateLimit()
    {
        using var http = new HttpClient();

        var lanes = ProviderFactory
            .CreateLanes(Lane() with { Rpm = 14 }, http, new InMemorySecretStore())
            .ToList();

        Assert.All(lanes, l => Assert.Equal(14, l.Rpm));
    }

    [Fact]
    public void EachSlotReadsItsOwnKey()
    {
        using var http = new HttpClient();
        var secrets = new InMemorySecretStore();
        secrets.Set("GeminiApiKey", "first");
        secrets.Set("GeminiApiKey#3", "third");

        var lanes = ProviderFactory.CreateLanes(Lane(), http, secrets).ToList();

        Assert.True(lanes[0].Provider.IsConfigured);
        Assert.False(lanes[1].Provider.IsConfigured);
        Assert.True(lanes[2].Provider.IsConfigured);
    }

    [Fact]
    public void TheQuotaReadoutGivesEachKeyItsOwnFullAllowance()
    {
        // A second key is a second account, so it brings its own daily budget rather than a share
        // of the first one's. Reading the ledger back per provider instead of per lane would hide
        // everything the extra keys spent, which is the one question adding them raises.
        var models = new ModelsConfig
        {
            Providers = [new ProviderConfig { Name = "gemini", Rpd = 540 }],
        };

        var limits = models.LimitsFor(["gemini", "gemini#2", "gemini#3"]);

        Assert.Equal(3, limits.Count);
        Assert.All(limits, l => Assert.Equal(540, l.Rpd));
        Assert.Equal(["gemini", "gemini#2", "gemini#3"], limits.Select(l => l.Lane));
    }

    [Fact]
    public void ExpandingIntoKeySlotsKeepsEveryFreeLaneAheadOfEveryPaidOne()
    {
        // Lane order is the cost policy, and the test that guards it reads the FILE. Once one
        // provider becomes three lanes, the file's order and the router's walk are no longer the
        // same list - so the property has to be re-asserted on the list the router actually walks.
        using var http = new HttpClient();
        var secrets = new InMemorySecretStore();

        var models = ModelsConfig.Load(Path.Combine(TestPaths.RepoRoot, "data", "models.json"));
        var lanes = models.Enabled(includeDevOnly: false)
            .SelectMany(config => ProviderFactory.CreateLanes(config, http, secrets)
                .Select(lane => (lane.Provider.Name, config.IsPaid)))
            .ToList();

        var firstPaid = lanes.FindIndex(l => l.IsPaid);
        var lastFree = lanes.FindLastIndex(l => !l.IsPaid);

        Assert.True(firstPaid > lastFree,
            "A paid lane is walked before a free one: " +
            string.Join(" -> ", lanes.Select(l => l.Name)));
    }
}
