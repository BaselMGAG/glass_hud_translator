using System.Net;
using System.Text;
using System.Text.Json;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Translation;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// Per-model overrides in models.json, and what reaches the wire because of them.
///
/// <para>
/// The reason these exist: Groq admits a request against <c>prompt_tokens + max_tokens</c> against
/// a per-minute token ceiling, so the lane-wide 4096 was a cap on how many LINES a minute could be
/// translated - one - dressed up as a cap on how long an answer could be. Everything here is about
/// keeping the two numbers that fix it, the budget and the reasoning effort, attached to the model
/// they apply to.
/// </para>
/// </summary>
public class ModelEntryTests
{
    private static ModelsConfig Parse(string json) =>
        JsonSerializer.Deserialize<ModelsConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        })!;

    [Fact]
    public void APlainStringIsStillAModel()
    {
        // Every models.json already on a user's machine is written this way. It has to keep working
        // untouched, which is why the object form was added beside the string rather than instead.
        var lane = Parse("""
            {"providers":[{"name":"g","baseUrl":"https://x/v1","models":["a","b"]}]}
            """).Providers[0];

        Assert.Equal(["a", "b"], lane.Models);
        Assert.Null(lane.ModelFor("a")!.MaxOutputTokens);
        Assert.Null(lane.ModelFor("a")!.ReasoningEffort);
    }

    [Fact]
    public void AnObjectEntryCarriesItsOverrides()
    {
        var lane = Parse("""
            {"providers":[{"name":"groq","baseUrl":"https://x/v1","maxOutputTokens":4096,"models":[
              {"id":"openai/gpt-oss-120b","maxOutputTokens":700,"reasoningEffort":"low"},
              "llama-3.3-70b-versatile"
            ]}]}
            """).Providers[0];

        // The slash matters: ModelFor matches ordinally, and every Groq gpt-oss id contains one.
        var oss = lane.ModelFor("openai/gpt-oss-120b");
        Assert.NotNull(oss);
        Assert.Equal(700, oss.MaxOutputTokens);
        Assert.Equal("low", oss.ReasoningEffort);

        // Mixed forms in one list, because that is what the shipped file looks like.
        var llama = lane.ModelFor("llama-3.3-70b-versatile");
        Assert.NotNull(llama);
        Assert.Null(llama.MaxOutputTokens);
        Assert.Null(llama.ReasoningEffort);

        Assert.Equal(["openai/gpt-oss-120b", "llama-3.3-70b-versatile"], lane.Models);
    }

    [Fact]
    public void AModelNotInTheLaneHasNoOverrides()
    {
        var lane = Parse("""
            {"providers":[{"name":"g","baseUrl":"https://x/v1","models":["a"]}]}
            """).Providers[0];

        Assert.Null(lane.ModelFor("something-else"));
    }

    [Fact]
    public void AnUnknownKeyInsideAnEntryIsIgnoredRatherThanFatal()
    {
        // Same contract as an unknown 'kind': a file written by a future version, or by someone
        // guessing at a field name, must not take every other lane down with it.
        var lane = Parse("""
            {"providers":[{"name":"g","baseUrl":"https://x/v1","models":[
              {"id":"a","somethingNew":{"nested":true},"maxOutputTokens":800}
            ]}]}
            """).Providers[0];

        Assert.Equal(["a"], lane.Models);
        Assert.Equal(800, lane.ModelFor("a")!.MaxOutputTokens);
    }

    [Fact]
    public void AMalformedEntryDegradesToAReportedProblemRatherThanRefusingToLoad()
    {
        // models.json is explicitly meant to be hand-edited - that is the whole reason model names
        // are not in code. Refusing to start over a stray number would punish the edit the file
        // exists to invite, and leave a user with an app that will not open and no way to know why.
        var config = Parse("""
            {"providers":[
              {"name":"g","baseUrl":"https://x/v1","models":[123,"good"]},
              {"name":"h","baseUrl":"https://y/v1","models":[{"model":"typo-in-the-key-name"}]}
            ]}
            """);

        Assert.Equal(2, config.Providers.Count);
        Assert.Contains("good", config.Providers[0].Models);

        var problems = config.Problems();
        Assert.Contains(problems, p => p.Contains("'g'") && p.Contains("no id"));
        Assert.Contains(problems, p => p.Contains("'h'") && p.Contains("no id"));
    }

    [Theory]
    // A quoted number, next to the quoted "low" that belongs beside it. The likeliest hand-edit
    // there is, on the one knob this file's own instructions tell people to tune.
    [InlineData("{\"id\":\"a\",\"maxOutputTokens\":\"700\"}")]
    [InlineData("{\"id\":\"a\",\"maxOutputTokens\":null}")]
    [InlineData("{\"id\":\"a\",\"maxOutputTokens\":700.5}")]
    [InlineData("{\"id\":\"a\",\"reasoningEffort\":true}")]
    [InlineData("{\"id\":\"a\",\"reasoningEffort\":3}")]
    public void AWrongTypedValueLeavesTheOverrideUnsetRatherThanKillingTheApp(string entry)
    {
        // Unguarded, GetInt32 on a string token threw straight out of ModelsConfig.Load, which
        // catches nothing - so the app came up as a bare overlay reading "Startup failed", with no
        // Settings window and therefore no way to reach the Problems() list that names the field.
        // An unstartable app is a poor answer to a stray quote mark.
        var lane = Parse($"{{\"providers\":[{{\"name\":\"g\",\"baseUrl\":\"https://x/v1\",\"models\":[{entry}]}}]}}")
            .Providers[0];

        Assert.Equal(["a"], lane.Models);

        // The override the user meant is lost, which is the honest outcome - it was not written in
        // a form anything could read. The lane's own value applies and the app starts.
        var entryRead = lane.ModelFor("a");
        Assert.NotNull(entryRead);
        Assert.Null(entryRead.MaxOutputTokens);
        Assert.Null(entryRead.ReasoningEffort);
    }

    [Fact]
    public void AWrongTypedIdIsStillAnEntryWithNoId()
    {
        var config = Parse("{\"providers\":[{\"name\":\"g\",\"baseUrl\":\"https://x/v1\","
            + "\"models\":[{\"id\":7},{\"id\":[\"a\"]}]}]}");

        Assert.Equal(2, config.Providers[0].ModelEntries.Length);
        Assert.All(config.Problems(), p => Assert.Contains("no id", p));
    }

    [Fact]
    public void ANullInTheModelListIsNotANullReferenceAtStartup()
    {
        // Commenting a model out by replacing it with null is an ordinary hand-edit, and before
        // models[] could hold objects it was harmless - a null string in a string[]. System.Text.
        // Json short-circuits a null token before a converter runs unless HandleNull says
        // otherwise, so without that the entry came back as a C# null and Models, ModelFor and
        // Problems() ALL threw NullReferenceException out of ModelsConfig.Load. Which is to say:
        // the mechanism that exists to report a broken file was what the broken file broke.
        var config = Parse("{\"providers\":[{\"name\":\"g\",\"baseUrl\":\"https://x/v1\","
            + "\"models\":[null,\"gemini-3.5-flash\"]}]}");

        Assert.Equal(["", "gemini-3.5-flash"], config.Providers[0].Models);
        Assert.Null(config.Providers[0].ModelFor("nope"));
        Assert.Contains(config.Problems(), p => p.Contains("no id"));
    }

    [Fact]
    public void AProviderNamedLikeAKeySlotIsReported()
    {
        // '#' is how a key slot is spelled. A provider literally called "gemini#2" would share a
        // quota-ledger row with gemini's second key, merging two accounts' daily usage into one
        // number - and LimitsFor would show it gemini's allowance rather than its own.
        var config = Parse("{\"providers\":[{\"name\":\"gemini#2\",\"baseUrl\":\"https://x/v1\","
            + "\"models\":[\"m\"]}]}");

        Assert.Contains(config.Problems(), p => p.Contains("gemini#2") && p.Contains("key slots"));
    }

    [Fact]
    public void ProblemsReportsABudgetTooSmallToAnswerIn()
    {
        var config = Parse("""
            {"providers":[{"name":"g","baseUrl":"https://x/v1","models":[
              {"id":"a","maxOutputTokens":16}
            ]}]}
            """);

        Assert.Contains(config.Problems(), p => p.Contains("'g/a'") && p.Contains("16"));
    }

    [Theory]
    [InlineData("lowest")]
    [InlineData("minimal")]
    [InlineData("none")]
    public void ProblemsReportsAReasoningEffortNoProviderAccepts(string effort)
    {
        // A typo here is a 400 on every line of that model, which in the router log reads exactly
        // like the model having been deleted.
        var config = Parse($$"""
            {"providers":[{"name":"g","baseUrl":"https://x/v1","models":[
              {"id":"a","reasoningEffort":"{{effort}}"}
            ]}]}
            """);

        Assert.Contains(config.Problems(), p => p.Contains("'g/a'") && p.Contains(effort));
    }

    [Theory]
    [InlineData("low")]
    [InlineData("Medium")]
    [InlineData("HIGH")]
    public void TheThreeAcceptedEffortsAreNotReported(string effort)
    {
        var config = Parse($$"""
            {"providers":[{"name":"g","baseUrl":"https://x/v1","maxOutputTokens":1024,"models":[
              {"id":"a","reasoningEffort":"{{effort}}"}
            ]}]}
            """);

        Assert.Empty(config.Problems());
    }
}

/// <summary>
/// What the per-model overrides actually put on the wire. Asserted against a captured request
/// rather than inferred, because the failure they guard against - a parameter sent to a model that
/// does not know it - is a 400 on every single line and is invisible from inside the config.
/// </summary>
public class OpenAiRequestShapeTests
{
    private static ProviderConfig Lane(params ModelEntry[] models) => new()
    {
        Name = "groq",
        BaseUrl = "https://api.example.test/v1",
        Secret = "GroqApiKey",
        MaxOutputTokens = 4096,
        ModelEntries = models,
    };

    private static async Task<JsonDocument> Sent(ProviderConfig lane, string model)
    {
        var capture = new CapturingHandler("""
            {"choices":[{"message":{"role":"assistant","content":"مرحبا"},"finish_reason":"stop"}]}
            """);

        using var http = new HttpClient(capture);
        var provider = new OpenAiCompatibleProvider(lane, http, () => "key");

        await provider.TranslateAsync(new TranslationRequest("Hello."), model, CancellationToken.None);
        return JsonDocument.Parse(capture.Body!);
    }

    [Fact]
    public async Task APerModelBudgetBeatsTheLaneBudget()
    {
        using var sent = await Sent(
            Lane(ModelEntry.Named("m") with { MaxOutputTokens = 700 }), "m");

        Assert.Equal(700, sent.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task WithoutAnOverrideTheLaneBudgetIsUsed()
    {
        using var sent = await Sent(Lane(ModelEntry.Named("m")), "m");

        Assert.Equal(4096, sent.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task ReasoningEffortIsSentOnlyWhenTheModelDeclaresIt()
    {
        using var withEffort = await Sent(
            Lane(ModelEntry.Named("m") with { ReasoningEffort = "low" }), "m");

        Assert.Equal("low", withEffort.RootElement.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task ReasoningEffortIsAbsentFromTheJsonWhenTheModelDoesNotDeclareIt()
    {
        // Not null, ABSENT. llama-3.3-70b-versatile answers 400 "`reasoning_effort` is not
        // supported with this model" to the parameter, and a JSON null is still the parameter.
        using var without = await Sent(Lane(ModelEntry.Named("m")), "m");

        Assert.False(without.RootElement.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task AnEmptyAnswerThatRanOutOfRoomNamesTheFileToEdit()
    {
        // The v0.5.0 failure, given a voice. A reasoning model whose whole budget went on thinking
        // returns finish_reason "length" and no content - deterministically, on every line - and it
        // used to be reported as a generic transient error and retried twice on the same model.
        // ModelRejected moves to the next model at once, and the message says what to change.
        var capture = new CapturingHandler("""
            {"choices":[{"message":{"role":"assistant","content":""},"finish_reason":"length"}]}
            """);

        using var http = new HttpClient(capture);
        var provider = new OpenAiCompatibleProvider(Lane(ModelEntry.Named("m")), http, () => "key");

        var failure = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.TranslateAsync(new TranslationRequest("Hello."), "m", CancellationToken.None));

        Assert.Equal(ProviderFailure.ModelRejected, failure.Failure);
        Assert.Contains("models.json", failure.Message);
        Assert.Contains("reasoningEffort", failure.Message);
    }

    [Fact]
    public async Task HalfATranslationIsRefusedRatherThanCachedForever()
    {
        // finish_reason "length" WITH content. The check used to sit inside the empty-answer
        // branch, so a sentence cut off mid-word was returned as a success, the router reported
        // Ok, and the pipeline wrote it to the cache - where every later capture of that English
        // line replayed the fragment permanently, with no retry and no marker on the overlay.
        //
        // Unreachable while the lane reserved 4096 tokens. Cutting the reservation is what makes
        // it live, so the two changes had to land together.
        var capture = new CapturingHandler(
            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"تعال، فالأثير هنا يغدو\"},"
            + "\"finish_reason\":\"length\"}]}");

        using var http = new HttpClient(capture);
        var provider = new OpenAiCompatibleProvider(Lane(ModelEntry.Named("m")), http, () => "key");

        var failure = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.TranslateAsync(new TranslationRequest("Come."), "m", CancellationToken.None));

        Assert.Equal(ProviderFailure.ModelRejected, failure.Failure);
        Assert.Contains("cut off", failure.Message);
    }

    [Fact]
    public async Task AWholeAnswerThatStoppedNormallyIsReturned()
    {
        var capture = new CapturingHandler(
            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"تعال.\"},"
            + "\"finish_reason\":\"stop\"}]}");

        using var http = new HttpClient(capture);
        var provider = new OpenAiCompatibleProvider(Lane(ModelEntry.Named("m")), http, () => "key");

        Assert.Equal("تعال.",
            await provider.TranslateAsync(new TranslationRequest("Come."), "m", CancellationToken.None));
    }

    [Fact]
    public async Task AnEmptyAnswerThatSimplyStoppedIsStillWorthOneMoreTry()
    {
        var capture = new CapturingHandler("""
            {"choices":[{"message":{"role":"assistant","content":"  "},"finish_reason":"stop"}]}
            """);

        using var http = new HttpClient(capture);
        var provider = new OpenAiCompatibleProvider(Lane(ModelEntry.Named("m")), http, () => "key");

        var failure = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.TranslateAsync(new TranslationRequest("Hello."), "m", CancellationToken.None));

        Assert.Equal(ProviderFailure.Transient, failure.Failure);
    }

    [Fact]
    public async Task A429CarriesTheProvidersOwnRetryAfterBack()
    {
        // Groq answers a per-minute token refusal with a few seconds and a daily one with far more.
        // The router shortens its cooldown to match, so a brief overrun costs seconds rather than
        // sidelining the lane for a fixed minute.
        var capture = new CapturingHandler("{}", HttpStatusCode.TooManyRequests, retryAfterSeconds: 4);

        using var http = new HttpClient(capture);
        var provider = new OpenAiCompatibleProvider(Lane(ModelEntry.Named("m")), http, () => "key");

        var failure = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.TranslateAsync(new TranslationRequest("Hello."), "m", CancellationToken.None));

        Assert.Equal(ProviderFailure.RateLimited, failure.Failure);
        Assert.Equal(TimeSpan.FromSeconds(4), failure.RetryAfter);
    }

    private sealed class CapturingHandler(
        string response,
        HttpStatusCode status = HttpStatusCode.OK,
        int? retryAfterSeconds = null) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(ct);

            var message = new HttpResponseMessage(status)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };

            if (retryAfterSeconds is { } seconds)
                message.Headers.Add("Retry-After", seconds.ToString());

            return message;
        }
    }
}
