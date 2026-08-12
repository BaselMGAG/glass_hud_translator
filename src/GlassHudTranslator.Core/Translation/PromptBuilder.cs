using System.Text;

namespace GlassHudTranslator.Core.Translation;

/// <summary>
/// Builds the two messages sent to every provider. All three speak the OpenAI chat shape, so this
/// is written once (brief 4.1).
/// </summary>
public static class PromptBuilder
{
    /// <summary>
    /// What the model says instead of guessing, when the line it was given is not readable text.
    ///
    /// <para>
    /// This exists because of a measured failure, not a hypothetical one. A frame captured while
    /// the dialogue box was still animating produced the reading
    /// <c>'an gp - ESS BF OE Ri, SI iat ee SES mia kyo ee 1'</c> — and the model returned a
    /// perfectly fluent Arabic sentence, because it had three coherent previous lines sitting in
    /// the prompt as context and reached for one of those instead. The user saw every translation
    /// arrive one sentence late, which is a far worse failure than seeing nothing: nothing is
    /// obviously nothing, while a confident translation of the wrong line is indistinguishable
    /// from a correct one to somebody who cannot read the English.
    /// </para>
    /// </summary>
    public const string Unreadable = "<UNREADABLE>";

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

        // Asked for explicitly, because models add tashkeel unevenly - the same conversation comes
        // back half vowelled and half not, depending which model in the fallback chain answered
        // which line. The overlay strips them when they are unwanted regardless; this stops us
        // spending output tokens on them first, which is the scarce resource on the Groq lane.
        var diacritics = request.Diacritics
            ? "Vowel the text fully (تشكيل كامل)."
            : "Do NOT add تشكيل - no fatha, kasra, damma, shadda or sukun. Plain unvowelled Arabic, "
              + "the way a subtitle is normally written.";

        return $"""
            You translate on-screen text from the video game {request.GameName} into Arabic.

            Rules:
            - DIALECT, and this is not negotiable: {dialect}
            - Diacritics: {diacritics}
            - Tone: {voice}
              Express that tone *within* the dialect above. If the tone and the dialect pull in
              different directions, the dialect wins - an archaic source still gets translated into
              the requested dialect, not into a more formal one.
            - Proper nouns: use the glossary spellings exactly. For a name that is not in the
              glossary, transliterate it into Arabic and keep that spelling consistent.
            - Translate ONLY the text after "Line:". Anything under "Previous lines" is there so you
              can resolve pronouns, gender and names - it is background, it has already been
              translated, and translating it again is always wrong. If the Line and the previous
              lines disagree, the Line is what is on screen and the previous lines are the past.
            - The Line is read off the screen by text recognition, so it is sometimes captured
              mid-change and arrives as nonsense - stray letters, fragments, no sentence. When that
              happens, reply with exactly {Unreadable} and nothing else. Do NOT reach for a previous
              line, and do NOT invent a plausible sentence: the app can try again a moment later,
              but it cannot un-show a confident translation of something that was never on screen.
            - Do not continue the scene, explain, or add notes.
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
