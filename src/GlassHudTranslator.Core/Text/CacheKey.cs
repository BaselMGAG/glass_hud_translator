using System.Security.Cryptography;
using GlassHudTranslator.Core.Translation;
using System.Text;

namespace GlassHudTranslator.Core.Text;

/// <summary>
/// The cache key for a line of dialogue.
///
/// <para>
/// Lowercasing happens here and only here - see <see cref="TextNormalizer"/> for why it must not
/// happen earlier. Everything that reduces spurious key variation directly reduces API spend:
/// the realistic way to exhaust a daily quota is not long play sessions, it is the same line
/// hashing two different ways (brief 5).
/// </para>
///
/// <para>
/// The key deliberately does not include the provider or model. Including them would fragment the
/// cache every time the router falls over to Groq, which is exactly when requests are scarcest.
/// Provider and model are stored as columns on the row instead.
/// </para>
/// </summary>
public static class CacheKey
{
    /// <summary>
    /// The register is part of the key. Without it, switching from Modern Standard to Egyptian
    /// returned the Modern Standard translation straight from cache, so the setting appeared to do
    /// nothing at all - the request never reached a model. They are genuinely different
    /// translations of the same line and deserve separate entries.
    /// </summary>
    public static string For(string normalizedBody, string register = "msa")
    {
        ArgumentNullException.ThrowIfNull(normalizedBody);

        var canonical = $"{register.ToLowerInvariant()}\n{normalizedBody.ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
    }

    public static string For(string normalizedBody, ArabicRegister register) =>
        For(normalizedBody, register == ArabicRegister.Egyptian ? "eg" : "msa");
}
