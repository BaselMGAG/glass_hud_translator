using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GlassHudTranslator.Core.Config;

namespace GlassHudTranslator.Core.Translation;

/// <summary>
/// One HTTP client for every provider that speaks OpenAI chat-completions.
///
/// <para>
/// Gemini, Groq, OpenAI itself and Ollama all expose that shape, so the only things that differ
/// are the base URL, the key, and the model name - which means there is no reason for
/// GeminiProvider and GroqProvider to exist as separate classes (brief 4.1). Anthropic is the one
/// provider that does not fit; it has its own lane rather than a branch in here.
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

    public bool IsConfigured => config.Secret is null || !string.IsNullOrWhiteSpace(apiKey());

    public async Task<string> TranslateAsync(TranslationRequest request, string model, CancellationToken ct)
    {
        var (system, user) = PromptBuilder.Build(request);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{config.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(new ChatRequest(
                model,
                [new ChatMessage("system", system), new ChatMessage("user", user)],
                Temperature: 0.3,
                MaxTokens: config.MaxOutputTokens), options: Json),
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
            // The per-attempt cap fired. Timeout, not Transient: the router retries transients on
            // the same model, and a model that spent the whole window thinking will do it again -
            // the useful next step is the next model, immediately.
            throw new ProviderException(Name, model, ProviderFailure.Timeout, "Request timed out.");
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
            var text = StripInlineReasoning(body?.Choices?.FirstOrDefault()?.Message?.Content)?.Trim();

            if (string.IsNullOrWhiteSpace(text))
                throw new ProviderException(Name, model, ProviderFailure.Transient, "Empty completion.");

            return text;
        }
    }

    /// <summary>
    /// Removes a leading <c>&lt;think&gt;...&lt;/think&gt;</c> block. Qwen-family models on
    /// OpenAI-compatible endpoints put their reasoning inline in the content, ahead of the answer
    /// — left alone, the overlay would show paragraphs of English deliberation with the Arabic at
    /// the bottom, and the cache would store all of it forever. An unterminated block means the
    /// model ran out of tokens mid-thought and never answered at all; returning null lets the
    /// empty-completion check below say so, rather than shipping half a chain of thought as a
    /// "translation".
    /// </summary>
    internal static string? StripInlineReasoning(string? content)
    {
        if (content is null) return null;

        var trimmed = content.TrimStart();
        if (!trimmed.StartsWith("<think>", StringComparison.OrdinalIgnoreCase)) return content;

        var end = trimmed.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        return end < 0 ? null : trimmed[(end + "</think>".Length)..];
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
            HttpStatusCode.BadRequest when ProviderDiagnostics.MentionsMissingModel(detail)
                => ProviderFailure.ModelNotFound,

            // Checked BEFORE the generic 400: Gemini reports a bad key as 400 rather than 401, and
            // without this the key test would answer "could not check" for a key the provider had
            // flatly refused, while the router walked every remaining model with it.
            HttpStatusCode.BadRequest when ProviderDiagnostics.MentionsBadKey(detail)
                => ProviderFailure.Fatal,

            // Not Fatal: a 400 is about this request and this model - a token ceiling lower than
            // we asked for, a parameter this one does not take - and the next model on the same
            // key may well accept it. Only an auth failure condemns the whole lane.
            HttpStatusCode.BadRequest => ProviderFailure.ModelRejected,
            _ when (int)response.StatusCode >= 500 => ProviderFailure.Transient,
            _ => ProviderFailure.Transient,
        };

        return new ProviderException(Name, model, failure,
            $"{(int)response.StatusCode} {response.ReasonPhrase}: {ProviderDiagnostics.Truncate(detail, 300)}");
    }

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
