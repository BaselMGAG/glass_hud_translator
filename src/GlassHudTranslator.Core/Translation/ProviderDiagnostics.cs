namespace GlassHudTranslator.Core.Translation;

/// <summary>
/// Failure-classification helpers shared by every provider client.
///
/// <para>
/// Telling "this model is gone" apart from "your request was wrong" is the single most important
/// distinction the router makes: the first must fall through to the next model in models.json and
/// say so loudly, the second must not, and getting it backwards either burns a working lane on a
/// dead model name or silently walks the whole fallback list on a bug in our own request.
/// </para>
/// </summary>
public static class ProviderDiagnostics
{
    private static readonly string[] MissingModelPhrases =
    [
        "not found",
        "does not exist",
        "decommissioned",
        "deprecated",
        "no longer available",
        "unsupported",
        "invalid",
    ];

    /// <summary>
    /// True when an error body looks like a retired or misspelled model name rather than a bad
    /// request. Several providers answer a dead model with 400 rather than 404, so the status code
    /// alone is not enough to decide.
    /// </summary>
    public static bool MentionsMissingModel(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        if (!body.Contains("model", StringComparison.OrdinalIgnoreCase)) return false;

        return MissingModelPhrases.Any(phrase => body.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] BadKeyPhrases =
    [
        // Gemini's actual wording, read off the live endpoint: {"code":400,"message":"Invalid Auth
        // key.","status":"INVALID_ARGUMENT"}. Guessing at this list is how it was wrong the first
        // time - every phrase below should come from a real response, not from imagination.
        "invalid auth",
        "api key not valid",
        "api key expired",
        "invalid api key",
        "invalid authentication",
        "invalid_api_key",
        "api_key_invalid",
        "please pass a valid api key",
        "incorrect api key",
        "unauthorized",
        "permission denied",
    ];

    /// <summary>
    /// True when an error body is about the KEY rather than the request or the model.
    ///
    /// <para>
    /// Needed because Gemini answers a bad key with HTTP 400, not 401 — verified against the live
    /// endpoint. Status code alone would therefore file it as "this model refused this request",
    /// which sends the router on to try every remaining model with a key that cannot work, and
    /// leaves the Settings key test reporting «تعذّر التحقّق» — "could not check" — for a key the
    /// provider explicitly rejected. Telling someone their key might be fine when it certainly is
    /// not is the exact failure the three-way verdict was built to prevent.
    /// </para>
    /// </summary>
    public static bool MentionsBadKey(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;

        return BadKeyPhrases.Any(phrase => body.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    public static string Truncate(string? text, int max) =>
        text is null ? "" : text.Length <= max ? text : text[..max] + "...";
}
