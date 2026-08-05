using System.Security.Cryptography;
using System.Text;

namespace GamingTranslatorGlassHUD.Core.Text;

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
    public static string For(string normalizedBody)
    {
        ArgumentNullException.ThrowIfNull(normalizedBody);

        var canonical = normalizedBody.ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
    }
}
