using System.Text.Json;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Translation;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

public class ModelsConfigTests
{
    private static ModelsConfig Parse(string json) =>
        JsonSerializer.Deserialize<ModelsConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        })!;

    [Fact]
    public void AnEntryWithNoKindIsTreatedAsOpenAiCompatible()
    {
        // Every models.json written before Anthropic existed omits the field, and those files are
        // sitting in users' AppData folders. They must keep working untouched.
        var config = Parse("""
            {"providers":[{"name":"groq","baseUrl":"https://x/v1","models":["m"]}]}
            """);

        Assert.Equal(ProviderKind.OpenAiCompatible, config.Providers[0].Kind);
    }

    [Theory]
    [InlineData("anthropic")]
    [InlineData("Anthropic")]
    [InlineData("ANTHROPIC")]
    public void KindIsMatchedWithoutRegardToCase(string kind)
    {
        var config = Parse($$"""
            {"providers":[{"name":"c","kind":"{{kind}}","models":["m"]}]}
            """);

        Assert.Equal(ProviderKind.Anthropic, config.Providers[0].Kind);
    }

    [Fact]
    public void AnUnknownKindDegradesToOpenAiRatherThanFailingToParse()
    {
        // A typo in one lane must not take the whole file - and every other lane - down with it.
        var config = Parse("""
            {"providers":[
              {"name":"typo","kind":"anthropik","baseUrl":"https://x/v1","models":["m"]},
              {"name":"gemini","baseUrl":"https://y/v1","models":["m"]}
            ]}
            """);

        Assert.Equal(ProviderKind.OpenAiCompatible, config.Providers[0].Kind);
        Assert.Equal(2, config.Providers.Count);
        Assert.Contains(config.Problems(), p => p.Contains("anthropik"));
    }

    [Fact]
    public void ProblemsReportsLanesThatCanNeverBeTried()
    {
        var config = Parse("""
            {"providers":[
              {"name":"empty","baseUrl":"https://x/v1","models":[]},
              {"name":"nourl","models":["m"]},
              {"name":"dupe","baseUrl":"https://x/v1","models":["m"]},
              {"name":"dupe","baseUrl":"https://x/v1","models":["m"]}
            ]}
            """);

        var problems = config.Problems();

        Assert.Contains(problems, p => p.Contains("'empty'") && p.Contains("no models"));
        Assert.Contains(problems, p => p.Contains("'nourl'") && p.Contains("baseUrl"));
        Assert.Contains(problems, p => p.Contains("'dupe'") && p.Contains("2 times"));
    }

    [Fact]
    public void MaxOutputTokensDefaultsToTheValueThatUsedToBeHardcoded()
    {
        var config = Parse("""
            {"providers":[{"name":"g","baseUrl":"https://x/v1","models":["m"]}]}
            """);

        Assert.Equal(300, config.Providers[0].MaxOutputTokens);
    }

    [Fact]
    public void LabelFallsBackToTheLaneName()
    {
        var config = Parse("""
            {"providers":[
              {"name":"groq","models":["m"]},
              {"name":"openai","displayName":"OpenAI","models":["m"]}
            ]}
            """);

        Assert.Equal("groq", config.Providers[0].Label);
        Assert.Equal("OpenAI", config.Providers[1].Label);
    }
}

/// <summary>
/// Guards the shipped data/models.json itself. It is edited by hand far more often than the code
/// around it, and a mistake in it is invisible until a translation quietly falls back to English.
/// </summary>
public class ShippedModelsFileTests
{
    private static ModelsConfig Shipped() =>
        ModelsConfig.Load(Path.Combine(TestPaths.RepoRoot, "data", "models.json"));

    [Fact]
    public void ParsesAndReportsNoProblems()
    {
        Assert.Empty(Shipped().Problems());
    }

    [Fact]
    public void ShipsBothPaidLanesAlongsideTheFreeOnes()
    {
        var names = Shipped().Providers.Select(p => p.Name).ToList();

        Assert.Contains("gemini", names);
        Assert.Contains("groq", names);
        Assert.Contains("openai", names);
        Assert.Contains("anthropic", names);
    }

    [Fact]
    public void FreeLanesComeBeforePaidOnes()
    {
        // Lane order is the cost policy: the router walks it top to bottom, so a paid lane placed
        // above a free one would spend money on lines the free tier would have answered.
        var providers = Shipped().Providers.Where(p => p.Secret is not null).ToList();
        var firstPaid = providers.FindIndex(p => p.IsPaid);
        var lastFree = providers.FindLastIndex(p => !p.IsPaid);

        Assert.True(firstPaid > lastFree,
            "A paid provider is ordered above a free one in data/models.json.");
    }

    [Fact]
    public void EveryShippedModelBudgetsForReasoningOrTurnsItDown()
    {
        // The config default of 300 tokens was sized for models that answer immediately. The free
        // catalogues are reasoning models now, and their thinking spends max_tokens before one
        // word of output exists - at 300 every completion came back empty, on every model of both
        // free lanes at once, while the key test passed because "Hello." needs no thought.
        //
        // Read per MODEL, not per lane, and that distinction is the whole point of this test after
        // v0.5.2: the groq lane still declares 4096 while its three models override it with 700,
        // 700 and 1024, so a lane-level check would pass while guarding nothing on the only lane
        // that changed. There are exactly two safe shapes, and a model must be one of them.
        foreach (var lane in Shipped().Providers)
        {
            foreach (var entry in lane.ModelEntries)
            {
                var budget = entry.MaxOutputTokens ?? lane.MaxOutputTokens;
                var thinksLess = string.Equals(entry.ReasoningEffort, "low", StringComparison.OrdinalIgnoreCase);
                var floor = thinksLess ? 512 : 1024;

                Assert.True(budget >= floor,
                    $"'{lane.Name}/{entry.Id}' allows {budget} output tokens" +
                    (thinksLess ? " at low reasoning effort" : " with reasoning left at its default") +
                    $", under the {floor} floor. A reasoning model spends that budget thinking and " +
                    "returns an empty completion on every line. Raise maxOutputTokens, or add " +
                    "\"reasoningEffort\": \"low\" and keep it above 512.");
            }
        }
    }

    [Fact]
    public void TheGroqLaneKeepsItsPerMinuteTokenBudgetSmall()
    {
        // Groq admits a request against prompt_tokens + max_tokens - what is RESERVED, not what the
        // answer costs - against a ceiling of 8,000 tokens a minute on the gpt-oss models. At the
        // lane's own 4096 that was one request a minute; the second was refused, all three models
        // refused in turn, and the router put the whole lane in a sixty-second cooldown while
        // reporting Groq as exhausted. It never was: measured 9 August 2026 it answers in under a
        // second and had 1,000 requests a day untouched.
        //
        // 2000 leaves room for four lines inside any one minute, which is faster than anyone reads.
        var groq = Shipped().Providers.Single(p => p.Name == "groq");

        foreach (var entry in groq.ModelEntries)
        {
            var budget = entry.MaxOutputTokens ?? groq.MaxOutputTokens;
            Assert.True(budget <= 2000,
                $"'groq/{entry.Id}' reserves {budget} output tokens. Groq withholds that from an " +
                "8,000-a-minute allowance whether the answer uses it or not, so this is a cap on " +
                "how many lines a minute can be translated, not on how long an answer may be.");
        }
    }

    [Fact]
    public void OnlyModelsThatAcceptReasoningEffortAreSentIt()
    {
        // llama-3.3-70b-versatile answers 400 "`reasoning_effort` is not supported with this model"
        // - verified against a live key on 9 August 2026 - while the two gpt-oss models beside it
        // in the same lane need it. One lane, three models, two request shapes: that is why the
        // parameter is per model, and this is the assertion that keeps it that way.
        var groq = Shipped().Providers.Single(p => p.Name == "groq");
        var llama = groq.ModelFor("llama-3.3-70b-versatile");

        Assert.NotNull(llama);
        Assert.Null(llama.ReasoningEffort);
    }

    [Fact]
    public void EveryPaidLaneNamesWhereToGetItsKey()
    {
        foreach (var paid in Shipped().Providers.Where(p => p.IsPaid))
        {
            Assert.False(string.IsNullOrWhiteSpace(paid.KeyUrl), $"{paid.Name} has no keyUrl.");
            Assert.False(string.IsNullOrWhiteSpace(paid.Secret), $"{paid.Name} has no secret name.");
        }
    }

    [Fact]
    public void TheAnthropicLaneUsesTheAnthropicClient()
    {
        var anthropic = Shipped().Providers.Single(p => p.Name == "anthropic");

        Assert.Equal(ProviderKind.Anthropic, anthropic.Kind);
        Assert.Equal(SecretNames.AnthropicApiKey, anthropic.Secret);
    }

    [Fact]
    public void EverySecretNamedInTheFileHasAConstant()
    {
        // The UI generates its key fields from this file, but tests and platform code still name
        // individual keys. The two must not drift apart.
        var declared = typeof(SecretNames)
            .GetFields()
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        foreach (var secret in Shipped().Providers.Select(p => p.Secret).Where(s => s is not null))
            Assert.Contains(secret!, declared);
    }
}

public class ProviderFactoryTests
{
    private static ProviderConfig Config(string name, string? kind = null) => new()
    {
        Name = name,
        KindName = kind,
        BaseUrl = "https://example.test/v1",
        Secret = "SomeKey",
        ModelEntries = [ModelEntry.Named("m1")],
    };

    [Fact]
    public void BuildsTheAnthropicClientOnlyForAnthropicLanes()
    {
        using var http = new HttpClient();
        var secrets = new InMemorySecretStore();

        Assert.IsType<AnthropicProvider>(ProviderFactory.Create(Config("anthropic", "anthropic"), http, secrets));
        Assert.IsType<OpenAiCompatibleProvider>(ProviderFactory.Create(Config("openai"), http, secrets));
    }

    [Fact]
    public void ALaneWithNoKeyReportsItselfUnconfigured()
    {
        using var http = new HttpClient();
        var secrets = new InMemorySecretStore();

        var openai = ProviderFactory.Create(Config("openai"), http, secrets);
        var anthropic = ProviderFactory.Create(Config("anthropic", "anthropic"), http, secrets);

        Assert.False(openai.IsConfigured);
        Assert.False(anthropic.IsConfigured);

        // Read per call, so pasting a key into Settings switches the lane on without a restart.
        secrets.Set("SomeKey", "sk-test");

        Assert.True(openai.IsConfigured);
        Assert.True(anthropic.IsConfigured);
    }

    [Fact]
    public void ALaneThatNeedsNoKeyIsAlwaysConfigured()
    {
        using var http = new HttpClient();

        var ollama = ProviderFactory.Create(
            Config("ollama") with { Secret = null }, http, new InMemorySecretStore());

        Assert.True(ollama.IsConfigured);
    }
}
