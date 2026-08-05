using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GamingTranslatorGlassHUD.Core.Config;

namespace GamingTranslatorGlassHUD.Core.Translation;

/// <summary>
/// One HTTP client for every provider.
///
/// <para>
/// Gemini, Groq and Ollama all expose the OpenAI chat-completions shape, so the only things that
/// differ are the base URL, the key, and the model name - which means there is no reason for
/// GeminiProvider and GroqProvider to exist as separate classes (brief 4.1).
/// </para>
/// </summary>
public sealed class OpenAiCompatibleProvider(
    ProviderConfig config,
    HttpClient http,
    Func<string?> apiKey) : ITranslationProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => config.Name;

    public IReadOnlyList<string> Models => config.Models;

    public async Task<string> TranslateAsync(TranslationRequest request, string model, CancellationToken ct)
    {
        var (system, user) = PromptBuilder.Build(request);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{config.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(new ChatRequest(
                model,
                [new ChatMessage("system", system), new ChatMessage("user", user)],
                Temperature: 0.3,
                MaxTokens: 300), options: Json),
        };

        var key = apiKey();
        if (config.Secret is not null)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ProviderException(Name, model, ProviderFailure.Fatal,
                    $"No API key for '{Name}'. Enter it in Settings.");

            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The 4-second cap fired. Past that the dialogue has advanced and the answer is
            // worthless, so this is a transient failure to be moved past, not waited on.
            throw new ProviderException(Name, model, ProviderFailure.Transient, "Request timed out.");
        }
        catch (HttpRequestException e)
        {
            throw new ProviderException(Name, model, ProviderFailure.Transient, e.Message, e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw await FailureFor(response, model, ct).ConfigureAwait(false);

            var body = await response.Content.ReadFromJsonAsync<ChatResponse>(Json, ct).ConfigureAwait(false);
            var text = body?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

            if (string.IsNullOrWhiteSpace(text))
                throw new ProviderException(Name, model, ProviderFailure.Transient, "Empty completion.");

            return text;
        }
    }

    private async Task<ProviderException> FailureFor(HttpResponseMessage response, string model, CancellationToken ct)
    {
        var detail = await SafeReadAsync(response, ct).ConfigureAwait(false);

        var failure = response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => ProviderFailure.RateLimited,
            HttpStatusCode.NotFound => ProviderFailure.ModelNotFound,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderFailure.Fatal,

            // Several providers answer a retired model with 400 rather than 404, so the body has
            // to be inspected. Getting this wrong would burn the whole lane on a dead model name.
            HttpStatusCode.BadRequest when MentionsModel(detail) => ProviderFailure.ModelNotFound,

            HttpStatusCode.BadRequest => ProviderFailure.Fatal,
            _ when (int)response.StatusCode >= 500 => ProviderFailure.Transient,
            _ => ProviderFailure.Transient,
        };

        return new ProviderException(Name, model, failure,
            $"{(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(detail, 300)}");
    }

    private static bool MentionsModel(string body) =>
        body.Contains("model", StringComparison.OrdinalIgnoreCase) &&
        (body.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("decommissioned", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("invalid", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            return "<unreadable body>";
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "...";

    private sealed record ChatRequest(
        string Model,
        ChatMessage[] Messages,
        double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatResponse(Choice[]? Choices);

    private sealed record Choice(ChatMessage? Message);
}

/// <summary>
/// Fixed Arabic after a realistic delay. The most-used provider during development by a wide
/// margin: hotkeys, capture, OCR, cache, overlay layout and the stability loop have nothing to do
/// with translation quality, and this catches RTL layout bugs just as well as a real model while
/// costing nothing and working offline (brief 9).
/// </summary>
public sealed class StubProvider(TimeSpan? delay = null) : ITranslationProvider
{
    private readonly TimeSpan _delay = delay ?? TimeSpan.FromMilliseconds(400);

    public string Name => ProviderNames.Stub;

    public IReadOnlyList<string> Models => ["stub"];

    public async Task<string> TranslateAsync(TranslationRequest request, string model, CancellationToken ct)
    {
        await Task.Delay(_delay, ct).ConfigureAwait(false);

        // Long enough to exercise wrapping, and carrying the speaker so the overlay can be checked
        // against a line whose length varies with the input.
        var speaker = string.IsNullOrWhiteSpace(request.Speaker) ? "" : $"[{request.Speaker}] ";
        return $"{speaker}هذا نص تجريبي للترجمة العربية، طوله يتناسب مع طول السطر الأصلي "
             + $"({request.Body.Length} حرفاً).";
    }
}
