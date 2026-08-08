using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using GlassHudTranslator.Core.Config;

namespace GlassHudTranslator.Core.Translation;

/// <summary>
/// Claude, through the official Anthropic SDK.
///
/// <para>
/// The other four lanes share one HTTP client because they all speak OpenAI chat-completions.
/// Anthropic does not: different auth header, a required version header, the system prompt as a
/// top-level parameter rather than a message, and content returned as typed blocks. Bending the
/// OpenAI client into that shape would put provider-specific branches inside the class whose entire
/// justification is that it has none, so this is a separate lane using the vendor's own SDK.
/// </para>
///
/// <para>
/// This is the paid option. It exists because most people who would use this app already pay for
/// exactly one AI subscription, and being told to go and register for a second provider is where
/// they stop. The free lanes stay first in models.json; nothing here changes that.
/// </para>
/// </summary>
public sealed class AnthropicProvider(
    ProviderConfig config,
    Func<string?> apiKey,
    TimeSpan? timeout = null) : ITranslationProvider
{
    private readonly Lock _gate = new();
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);

    private AnthropicClient? _client;
    private string? _clientKey;

    public string Name => config.Name;

    public IReadOnlyList<string> Models => config.Models;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey());

    public async Task<string> TranslateAsync(TranslationRequest request, string model, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = apiKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new ProviderException(Name, model, ProviderFailure.Fatal,
                $"No API key for '{Name}'. Enter it in Settings.");

        var (system, user) = PromptBuilder.Build(request);

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = config.MaxOutputTokens,
            System = system,

            // Thinking is on by default on current Claude models, and deliberately left on. The
            // documented failure mode of turning it off is that internal <thinking> markup leaks
            // into the visible answer - which here means XML rendered onto the overlay, over the
            // game. Low effort is what keeps a one-line subtitle inside the router's four-second
            // budget; it is the latency dial, and thinking is not.
            OutputConfig = new OutputConfig { Effort = Effort.Low },

            Messages = [new() { Role = Role.User, Content = user }],
        };

        Message message;
        try
        {
            message = await ClientFor(key).Messages.Create(parameters, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Same contract as OpenAiCompatibleProvider: a timeout moves to the next model rather
            // than being retried on this one.
            throw new ProviderException(Name, model, ProviderFailure.Timeout, "Request timed out.");
        }
        catch (AnthropicNotFoundException e)
        {
            throw new ProviderException(Name, model, ProviderFailure.ModelNotFound, e.Message, e);
        }
        catch (AnthropicRateLimitException e)
        {
            throw new ProviderException(Name, model, ProviderFailure.RateLimited, e.Message, e);
        }
        catch (AnthropicBadRequestException e)
        {
            // A retired model can arrive as a 400 rather than a 404, exactly as on the OpenAI lanes.
            var failure = ProviderDiagnostics.MentionsMissingModel(e.Message)
                ? ProviderFailure.ModelNotFound
                : ProviderFailure.Fatal;

            throw new ProviderException(Name, model, failure, e.Message, e);
        }
        catch (Exception e) when (e is AnthropicUnauthorizedException or AnthropicForbiddenException)
        {
            throw new ProviderException(Name, model, ProviderFailure.Fatal,
                $"Key rejected by {Name}: {e.Message}", e);
        }
        catch (Exception e) when (e is Anthropic5xxException or AnthropicIOException)
        {
            throw new ProviderException(Name, model, ProviderFailure.Transient, e.Message, e);
        }
        catch (AnthropicApiException e)
        {
            throw new ProviderException(Name, model, ProviderFailure.Transient, e.Message, e);
        }

        // A safety decline is a successful HTTP 200 with no usable content, so it has to be checked
        // before the body is read or it surfaces as a confusing "empty completion". Game dialogue
        // tripping this should be vanishingly rare, but the honest handling is to give up on this
        // lane and let the router try a different provider, which is what Fatal does.
        if (message.StopReason == StopReason.Refusal)
        {
            var category = message.StopDetails?.Category is { } c ? $" ({c})" : "";
            throw new ProviderException(Name, model, ProviderFailure.Fatal,
                $"Declined this line{category}. Moving to the next provider.");
        }

        var text = string.Concat(message.Content
                .Select(block => block.Value)
                .OfType<TextBlock>()
                .Select(block => block.Text))
            .Trim();

        if (string.IsNullOrWhiteSpace(text))
            throw new ProviderException(Name, model, ProviderFailure.Transient, "Empty completion.");

        return text;
    }

    /// <summary>
    /// One client per key value, rebuilt when the user saves a different key in Settings rather
    /// than requiring a restart.
    /// </summary>
    private AnthropicClient ClientFor(string key)
    {
        lock (_gate)
        {
            if (_client is not null && _clientKey == key) return _client;

            _clientKey = key;

            // The router owns retries and the four-second budget. A second retry loop inside the
            // SDK would spend that budget silently and hand back a translation for dialogue that
            // has already left the screen.
            //
            // baseUrl is set only when models.json names one, so that pointing the lane at a
            // gateway or proxy is possible without this code having to know, or hardcode, what
            // the SDK's own default endpoint is.
            return _client = string.IsNullOrWhiteSpace(config.BaseUrl)
                ? new AnthropicClient { ApiKey = key, Timeout = _timeout, MaxRetries = 0 }
                : new AnthropicClient
                {
                    ApiKey = key, Timeout = _timeout, MaxRetries = 0, BaseUrl = config.BaseUrl,
                };
        }
    }
}
