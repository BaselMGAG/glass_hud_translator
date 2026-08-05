using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Storage;

namespace GlassHudTranslator.Core.Translation;

/// <summary>
/// Turns one models.json entry into a lane.
///
/// <para>
/// Shared by the app and by tools/Replay so that the headless harness exercises the same provider
/// wiring the overlay does - the point of the harness is that what is debugged on the Mac is what
/// runs on Windows, and that stops being true the moment the two build their lanes differently.
/// </para>
/// </summary>
public static class ProviderFactory
{
    public static ITranslationProvider Create(ProviderConfig config, HttpClient http, ISecretStore secrets)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(secrets);

        // Read per call, not captured: a key pasted into Settings has to take effect without a
        // restart, and on the paid lanes it is also what switches the lane on at all.
        string? Key() => config.Secret is null ? null : secrets.Get(config.Secret);

        return config.Kind switch
        {
            ProviderKind.Anthropic => new AnthropicProvider(config, Key),
            _ => new OpenAiCompatibleProvider(config, http, Key),
        };
    }
}
