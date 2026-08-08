using System.Text;

namespace GlassHudTranslator.Core.Translation;

/// <summary>
/// Builds the two messages sent to every provider. All three speak the OpenAI chat shape, so this
/// is written once (brief 4.1).
/// </summary>
public static class PromptBuilder
{
    public static (string System, string User) Build(TranslationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (SystemPrompt(request), UserPrompt(request));
    }

    private static string SystemPrompt(TranslationRequest request)
    {
        // Spelled out concretely because models default hard to Modern Standard: asked merely for
        // "Egyptian Arabic" they produce fus'ha with an Egyptian word or two dropped in.
        var dialect = request.Register == ArabicRegister.Egyptian
            ? "Egyptian Arabic (العامية المصرية) as actually spoken in Cairo. Use Egyptian "
              + "vocabulary and sentence structure - إزاي، عايز، دلوقتي، بتاع، مش، كده - not "
              + "Modern Standard forms. Do NOT write in فصحى."
            : "Modern Standard Arabic (الفصحى).";

        // The game and its voice come from the active profile rather than being baked in. Register
        // is the difference between a solemn epic and a shop sign, and no single instruction suits
        // every game - so each profile states its own in profile.json.
        var voice = string.IsNullOrWhiteSpace(request.StyleHint)
            ? "Match the tone of the original rather than flattening it into neutral prose."
            : request.StyleHint.Trim();

        return $"""
            You translate on-screen text from the video game {request.GameName} into Arabic.

            Rules:
            - DIALECT, and this is not negotiable: {dialect}
            - Tone: {voice}
              Express that tone *within* the dialect above. If the tone and the dialect pull in
              different directions, the dialect wins - an archaic source still gets translated into
              the requested dialect, not into a more formal one.
            - Proper nouns: use the glossary spellings exactly. For a name that is not in the
              glossary, transliterate it into Arabic and keep that spelling consistent.
            - Translate only the line given. Do not continue the scene, explain, or add notes.
            - Preserve the speaker's tone, including interruptions and trailing dashes.
            - Output ONLY the Arabic translation. No romanisation, no quotes around the whole
              line, no commentary, no alternatives.
            """;
    }

    private static string UserPrompt(TranslationRequest request)
    {
        var builder = new StringBuilder();

        // Only the terms that actually occur in this line - see GlossaryMatcher for why.
        if (request.GlossaryTerms.Count > 0)
        {
            builder.AppendLine("Glossary:");
            foreach (var term in request.GlossaryTerms)
                builder.AppendLine(term.ToPromptLine());
            builder.AppendLine();
        }

        // A few lines of context, not a transcript: they disambiguate pronouns and gender
        // agreement without pushing the request size up on every call. The pipeline caps the
        // window at three - anything wider buys little and widens the gap between a live
        // translation and a cache hit, which carries no context at all.
        if (request.ContextLines.Count > 0)
        {
            builder.AppendLine(request.ContextLines.Count == 1
                ? "Previous line:"
                : "Previous lines, oldest first:");
            foreach (var line in request.ContextLines)
                builder.AppendLine(line);
            builder.AppendLine();
        }

        // The speaker is context, not text to translate.
        if (!string.IsNullOrWhiteSpace(request.Speaker))
            builder.AppendLine($"Speaker: {request.Speaker}");

        builder.Append("Line: ").Append(request.Body);
        return builder.ToString();
    }
}
