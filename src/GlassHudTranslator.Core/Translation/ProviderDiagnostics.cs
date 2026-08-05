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

    public static string Truncate(string? text, int max) =>
        text is null ? "" : text.Length <= max ? text : text[..max] + "...";
}
