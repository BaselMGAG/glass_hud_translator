using System.Text;

namespace GamingTranslatorGlassHUD.Core.Translation;

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
        var dialect = request.Register == ArabicRegister.Egyptian
            ? "Egyptian Arabic, as spoken naturally in Cairo."
            : "Modern Standard Arabic.";

        // The game and its voice come from the active profile rather than being baked in. Register
        // is the difference between a solemn epic and a shop sign, and no single instruction suits
        // every game - so each profile states its own in profile.json.
        var voice = string.IsNullOrWhiteSpace(request.StyleHint)
            ? "Match the tone of the original rather than flattening it into neutral prose."
            : request.StyleHint.Trim();

        return $"""
            You translate on-screen text from the video game {request.GameName} into {dialect}

            Rules:
            - Voice: {voice}
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

        // One line of context, not a transcript: it disambiguates pronouns and gender agreement
        // without pushing the request size up on every call.
        if (!string.IsNullOrWhiteSpace(request.PreviousLine))
        {
            builder.AppendLine("Previous line:");
            builder.AppendLine(request.PreviousLine);
            builder.AppendLine();
        }

        // The speaker is context, not text to translate.
        if (!string.IsNullOrWhiteSpace(request.Speaker))
            builder.AppendLine($"Speaker: {request.Speaker}");

        builder.Append("Line: ").Append(request.Body);
        return builder.ToString();
    }
}
