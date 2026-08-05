using System.Text;

namespace GlassHudTranslator.Core.Text;

/// <summary>
/// Turns raw OCR into the canonical form used everywhere downstream.
///
/// <para>
/// Case is preserved. PROJECT_PLAN.md 1.5: the brief's normalisation order lowercases before
/// hashing, which is right for the cache key and wrong for the model input - Tesseract's casing is
/// real signal, and "limsa lominsa" translates worse than "Limsa Lominsa". So normalisation
/// produces one case-preserved body, and <see cref="CacheKey"/> lowercases separately on its way
/// to the hash.
/// </para>
/// </summary>
public static class TextNormalizer
{
    /// <summary>
    /// The FFXIV "advance" cursor, stripped so a line does not hash differently purely because the
    /// cursor happened to be drawn in that frame.
    ///
    /// <para>
    /// Deliberately narrow. The obvious temptation is to also strip the ASCII shapes OCR might
    /// misread the arrow as - quotes, dashes, carets - but FFXIV ends lines with a real em dash
    /// constantly for interrupted speech ("But I thought-"), and eating that changes the meaning
    /// of the line being translated. What OCR actually produces for the cursor is unknown until
    /// there are real frames to look at, so Session 3 extends this from the log rather than
    /// guessing now.
    /// </para>
    /// </summary>
    private const string TrailingNoise = "▼▽►▶◆";

    public static string Normalize(string raw, OcrCorrections? corrections = null)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var text = UnifyPunctuation(raw);
        text = CollapseWhitespacePerLine(text);
        text = RepairStandaloneI(text);

        // Corrections run after whitespace and punctuation are unified, so a rule can be written
        // as the phrase a human would type rather than having to anticipate OCR's spacing.
        text = (corrections ?? OcrCorrections.Empty).Apply(text);

        return StripTrailingNoise(text);
    }

    /// <summary>
    /// Folds the quote and dash variants OCR produces onto one spelling each. This is a direct
    /// quota guard: FFXIV names are apostrophe-heavy (Y'shtola, G'raha Tia), and a line that
    /// hashes differently because OCR chose a curly apostrophe is a translation paid for twice
    /// (brief 5).
    /// </summary>
    private static string UnifyPunctuation(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '‘' or '’' or 'ʼ' or 'ʹ' or '´' or '`' => '\'',
                '“' or '”' => '"',
                '–' or '—' or '−' => '-',
                '\u00A0' or '\u2007' or '\u2009' or '\u202F' => ' ',   // nbsp / figure / thin / narrow-nbsp
                _ => c,
            });
        }

        return builder.ToString();
    }

    /// <summary>
    /// Collapses runs of spaces within each line but keeps the line breaks, because
    /// <see cref="DialogueParser"/> still needs them to find the speaker name. Blank lines go.
    /// </summary>
    private static string CollapseWhitespacePerLine(string text)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(line => line.Length > 0);

        return string.Join('\n', lines);
    }

    /// <summary>
    /// A lone "|" is the pronoun "I". Tesseract mistakes the two constantly - it happened twice in
    /// twelve frames on the first end-to-end run ("| have seen enough", "But | thought-").
    ///
    /// <para>
    /// Handled here as a structural rule rather than as entries in ocr-corrections.json, because a
    /// phrase-keyed dictionary would need one rule per surrounding context and would still miss the
    /// next one. A bare pipe never legitimately appears in FFXIV dialogue, so the rule is safe as
    /// long as it only fires on a whole token.
    /// </para>
    /// </summary>
    private static string RepairStandaloneI(string text)
    {
        if (!text.Contains('|')) return text;

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var isolated = text[i] == '|'
                && (i == 0 || text[i - 1] is ' ' or '\n')
                && (i == text.Length - 1 || text[i + 1] is ' ' or '\n');

            builder.Append(isolated ? 'I' : text[i]);
        }

        return builder.ToString();
    }

    private static string StripTrailingNoise(string text)
    {
        var end = text.Length;
        while (end > 0 && (char.IsWhiteSpace(text[end - 1]) || TrailingNoise.Contains(text[end - 1])))
            end--;

        return text[..end];
    }
}
