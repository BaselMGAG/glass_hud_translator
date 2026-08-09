using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Storage;

namespace GlassHudTranslator.Core.Translation;

/// <summary>
/// Turns one models.json entry into one lane per key the user has entered for it.
///
/// <para>
/// Shared by the app and by tools/Replay so that the headless harness exercises the same provider
/// wiring the overlay does - the point of the harness is that what is debugged on the Mac is what
/// runs on Windows, and that stops being true the moment the two build their lanes differently.
/// </para>
/// </summary>
public static class ProviderFactory
{
    /// <summary>
    /// One lane, using the provider's first key slot. What every caller wanted before a provider
    /// could hold more than one key, and still what the key-test button wants: it is asking about
    /// one key in one box.
    /// </summary>
    public static ITranslationProvider Create(ProviderConfig config, HttpClient http, ISecretStore secrets) =>
        Create(config, http, secrets, slot: 1);

    /// <summary>
    /// One lane for one key slot. Slot 1 reads the plain secret name and is called by the
    /// provider's own name, so nothing about a single-key installation changes.
    /// </summary>
    public static ITranslationProvider Create(
        ProviderConfig config, HttpClient http, ISecretStore secrets, int slot)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(secrets);

        var secretName = config.SecretSlot(slot);

        // Read per call, not captured: a key pasted into Settings has to take effect without a
        // restart, and on the paid lanes it is also what switches the lane on at all.
        string? Key() => config.Secret is null ? null : secrets.Get(secretName);

        return config.Kind switch
        {
            ProviderKind.Anthropic => new AnthropicProvider(config, Key, keySlot: slot),
            _ => new OpenAiCompatibleProvider(config, http, Key, slot),
        };
    }

    /// <summary>
    /// Every lane one provider contributes, in key order: its first key, then its second, then its
    /// third, each paired with the provider's per-minute allowance.
    ///
    /// <para>
    /// Expanding here rather than inside the router is deliberate. The router already walks an
    /// ordered list of lanes and tries each in turn, so three Gemini keys ahead of Groq is exactly
    /// the behaviour asked for - all the Geminis, then Groq - with no new concept anywhere in the
    /// routing logic. Each key also gets its own token bucket and its own rate-limit cooldown,
    /// which is right: the limits being worked around are per account.
    /// </para>
    ///
    /// <para>
    /// Every slot is built whether or not it holds a key, and an empty one costs nothing: the
    /// router skips an unconfigured lane in silence, and
    /// <see cref="ITranslationProvider.AnnouncesMissingKey"/> keeps the extra slots out of the
    /// "no API key" line. Building only the slots that had keys AT STARTUP would have been
    /// cheaper and wrong - it would mean a second key pasted into Settings did nothing until the
    /// app was restarted, while a Test button beside it said «يعمل». That exact shape of promise -
    /// a positive confirmation next to a control that had not actually taken effect - is the
    /// v0.5.0 defect this project already paid for once.
    /// </para>
    /// </summary>
    public static IEnumerable<(ITranslationProvider Provider, int Rpm)> CreateLanes(
        ProviderConfig config, HttpClient http, ISecretStore secrets)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(secrets);

        foreach (var slot in config.KeySlots())
            yield return (Create(config, http, secrets, slot), config.Rpm);
    }
}
