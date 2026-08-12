using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Ocr;

namespace GlassHudTranslator.Core.Translation;

/// <summary>
/// Sends a crop to a multimodal model and returns what it read.
///
/// <para>
/// The transport only. Everything that decides <i>whether</i> to ask, what to say, and whether to
/// believe the answer lives in <see cref="EscalationPolicy"/>, <see cref="VisionPrompt"/> and
/// <see cref="ReadingJudge"/> — all of which are testable without a key, which is why they are not
/// in here.
/// </para>
///
/// <para>
/// <b>It speaks the same OpenAI chat-completions shape the text lanes do</b>, because the endpoint
/// this is aimed at is one this app already calls: Gemini's OpenAI-compatible layer takes the
/// standard <c>image_url</c> content part with a base64 data URI, at the base URL already in
/// <c>models.json</c>. So a lane becomes able to read pictures by gaining a <c>visionModels</c>
/// list in that file and nothing else — which is the same promise the text lanes keep, that adding
/// a provider is a config edit rather than a code change.
/// </para>
///
/// <para>
/// <b>It never throws.</b> Same contract as the router, and for a stronger reason: this is an
/// optional second opinion bolted onto a pipeline whose whole promise is that something always
/// appears. Every failure — no key, a model that cannot see, an image too large, a refusal, a
/// timeout — degrades to an empty answer, which the judge then treats as "declined" and the
/// pipeline treats as "keep what the local engine said".
/// </para>
/// </summary>
public sealed class VisionOcrReader(
    ProviderConfig config,
    HttpClient http,
    Func<string?> apiKey,
    Action<string>? log = null) : IVisionReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// What one reading may spend. Small on purpose: the answer is one line of text, and this is
    /// the number that on at least one provider is withheld from a per-minute allowance whether it
    /// is used or not — which this project has already lost an evening to.
    /// </summary>
    private const int MaxAnswerTokens = 700;

    public string Name => config.Name;

    public bool IsConfigured =>
        config.CanSee && (config.Secret is null || !string.IsNullOrWhiteSpace(apiKey()));

    public async Task<VisionAnswer> ReadAsync(VisionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsConfigured) return Nothing;

        // Walked in order, like the text lanes: a free catalogue changes without warning, so a
        // model that has been withdrawn must cost the next model rather than the line.
        foreach (var model in config.VisionModels)
        {
            try
            {
                return await AskAsync(request, model, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                // Including the per-attempt timeout, which surfaces as a cancellation on a token
                // that is not the caller's - the exact shape that once escaped the text router.
                log?.Invoke($"vision: {config.Name}/{model} - {e.Message}");
            }
        }

        return Nothing;
    }

    private static VisionAnswer Nothing => new("");

    private async Task<VisionAnswer> AskAsync(VisionRequest request, string model, CancellationToken ct)
    {
        var image = $"data:image/png;base64,{Convert.ToBase64String(request.Image.Png)}";

        var content = new object[]
        {
            new TextPart(VisionPrompt.User(request)),
            new ImagePart(new ImageUrl(image)),
        };

        using var message = new HttpRequestMessage(
            HttpMethod.Post, $"{config.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(new VisionChatRequest(
                model,
                [
                    new VisionMessage("system", VisionPrompt.System(request)),
                    new VisionMessage("user", content),
                ],

                // Zero, not the 0.3 the translation lanes use. Reading is not a task with a range
                // of good answers: there is one thing written on the screen, and any creativity
                // here is by definition invention.
                Temperature: 0,
                MaxTokens: MaxAnswerTokens), options: Json),
        };

        if (config.Secret is not null)
        {
            message.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey());
        }

        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"{(int)response.StatusCode} {Trim(body)}");
        }

        var answer = await response.Content.ReadFromJsonAsync<VisionChatResponse>(Json, ct)
            .ConfigureAwait(false);

        return VisionPrompt.Parse(answer?.Choices?.FirstOrDefault()?.Message?.Content);
    }

    /// <summary>Provider error bodies run to kilobytes of JSON; the log wants the first line of it.</summary>
    private static string Trim(string body) =>
        body.Length <= 200 ? body : body[..200] + "…";

    // ── the wire ─────────────────────────────────────────────────────────────────────────────
    //
    // Separate records from the text lane's, rather than widening ChatMessage.Content to object.
    // That type is used to DESERIALISE responses as well as to send them, where the content is
    // always a plain string - so making it polymorphic to serve one caller would put a cast in the
    // path of every translation this app performs.

    private sealed record VisionChatRequest(
        string Model,
        VisionMessage[] Messages,
        double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record VisionMessage(string Role, object Content);

    private sealed record TextPart(string Text)
    {
        [JsonPropertyName("type")] public string Type => "text";
    }

    private sealed record ImagePart([property: JsonPropertyName("image_url")] ImageUrl ImageUrl)
    {
        [JsonPropertyName("type")] public string Type => "image_url";
    }

    private sealed record ImageUrl(string Url);

    private sealed record VisionChatResponse(VisionChoice[]? Choices);

    private sealed record VisionChoice(VisionReply? Message);

    private sealed record VisionReply(string? Content);
}
