using System.Net;
using System.Text;
using System.Text.Json;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Ocr;
using GlassHudTranslator.Core.Translation;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>Captures the request that would have gone out, and answers with a script.</summary>
internal sealed class RecordingHandler(params HttpResponseMessage[] replies) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _replies = new(replies);

    public List<string> Bodies { get; } = [];

    public List<string> Urls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // A real handler observes the token; without this the test would exercise nothing.
        ct.ThrowIfCancellationRequested();

        Urls.Add(request.RequestUri!.ToString());
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));

        return _replies.Count > 0
            ? _replies.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("out of replies") };
    }

    public static HttpResponseMessage Says(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }),
            Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage Fails(HttpStatusCode code, string body = "{}") =>
        new(code) { Content = new StringContent(body) };
}

/// <summary>
/// The transport. Everything interesting about this feature is decided elsewhere and tested there;
/// what these hold is the wire format, which is the one part that cannot be checked by reading and
/// whose failure mode against a real endpoint is a 400 with no useful message.
/// </summary>
public class VisionOcrReaderTests
{
    private static ProviderConfig Lane(params string[] visionModels) => new()
    {
        Name = "gemini",
        BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
        Secret = "GeminiApiKey",
        VisionModels = visionModels,
    };

    private static VisionRequest Ask(string local = "Vou musL nnt heed") =>
        new(new VisionImage([0x89, 0x50, 0x4E, 0x47, 1, 2, 3], 800, 200, 1.0), local, []);

    [Fact]
    public async Task TheImageGoesUpAsABase64DataUriInAnImageUrlPart()
    {
        // The exact shape the OpenAI-compatible endpoints accept, and the one thing here that a
        // reading of the code cannot confirm: get it wrong and every request is a 400.
        var handler = new RecordingHandler(RecordingHandler.Says("You must not heed"));
        var reader = new VisionOcrReader(Lane("gemini-3.1-flash-lite"), new HttpClient(handler), () => "key");

        await reader.ReadAsync(Ask(), default);

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        var content = body.RootElement.GetProperty("messages")[1].GetProperty("content");

        Assert.Equal(JsonValueKind.Array, content.ValueKind);
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("image_url", content[1].GetProperty("type").GetString());
        Assert.StartsWith("data:image/png;base64,",
            content[1].GetProperty("image_url").GetProperty("url").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItPostsToTheChatCompletionsEndpointOfTheLaneItWasGiven()
    {
        var handler = new RecordingHandler(RecordingHandler.Says("x"));
        var reader = new VisionOcrReader(Lane("m"), new HttpClient(handler), () => "key");

        await reader.ReadAsync(Ask(), default);

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", handler.Urls[0]);
    }

    [Fact]
    public async Task ReadingIsAtTemperatureZeroBecauseThereIsOnlyOneRightAnswer()
    {
        // Not the 0.3 the translation lanes use. There is one thing written on the screen, and any
        // creativity here is by definition invention.
        var handler = new RecordingHandler(RecordingHandler.Says("x"));
        var reader = new VisionOcrReader(Lane("m"), new HttpClient(handler), () => "key");

        await reader.ReadAsync(Ask(), default);

        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(0, body.RootElement.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task TheLocalReadingIsSentSoTheModelIsCorrectingRatherThanGuessing()
    {
        var handler = new RecordingHandler(RecordingHandler.Says("x"));
        var reader = new VisionOcrReader(Lane("m"), new HttpClient(handler), () => "key");

        await reader.ReadAsync(Ask("Vou musL nnt heed"), default);

        Assert.Contains("Vou musL nnt heed", handler.Bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWithdrawnModelCostsTheNextModelRatherThanTheLine()
    {
        // Free catalogues churn, and this project has watched both of its free providers delete
        // every shipped model inside one week.
        var handler = new RecordingHandler(
            RecordingHandler.Fails(HttpStatusCode.NotFound),
            RecordingHandler.Says("You must not heed"));

        var reader = new VisionOcrReader(Lane("gone", "still-here"), new HttpClient(handler), () => "key");

        var answer = await reader.ReadAsync(Ask(), default);

        Assert.Equal("You must not heed", answer.Text);
        Assert.Equal(2, handler.Bodies.Count);
    }

    [Fact]
    public async Task EverythingFailingIsAnEmptyAnswerRatherThanAThrow()
    {
        // The contract that matters most: this is an optional second opinion bolted onto a pipeline
        // whose promise is that something always appears.
        var handler = new RecordingHandler(
            RecordingHandler.Fails(HttpStatusCode.TooManyRequests),
            RecordingHandler.Fails(HttpStatusCode.InternalServerError));

        var reader = new VisionOcrReader(Lane("a", "b"), new HttpClient(handler), () => "key");

        var answer = await reader.ReadAsync(Ask(), default);

        Assert.Equal("", answer.Text);
    }

    [Fact]
    public async Task ALaneWithNoVisionModelsIsSkippedInSilence()
    {
        // Which is every lane in an existing installation, since the list did not exist until now.
        var handler = new RecordingHandler();
        var reader = new VisionOcrReader(Lane(), new HttpClient(handler), () => "key");

        Assert.False(reader.IsConfigured);
        Assert.Equal("", (await reader.ReadAsync(Ask(), default)).Text);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task NoKeyMeansNoRequest()
    {
        var handler = new RecordingHandler();
        var reader = new VisionOcrReader(Lane("m"), new HttpClient(handler), () => null);

        Assert.False(reader.IsConfigured);
        await reader.ReadAsync(Ask(), default);

        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task TheAnswerIsParsedThroughTheSamePathAsEverythingElse()
    {
        var handler = new RecordingHandler(RecordingHandler.Says("```\nYou must not heed\n```"));
        var reader = new VisionOcrReader(Lane("m"), new HttpClient(handler), () => "key");

        Assert.Equal("You must not heed", (await reader.ReadAsync(Ask(), default)).Text);
    }

    [Fact]
    public async Task CancellationIsNotSwallowedAsAFailedLane()
    {
        // A cancelled poll is the app shutting down or the toggle going off - not a model refusing,
        // and walking the remaining models would be pure waste at the worst moment.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var handler = new RecordingHandler();
        var reader = new VisionOcrReader(Lane("a", "b"), new HttpClient(handler), () => "key");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(Ask(), cancelled.Token));
    }
}
