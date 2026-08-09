namespace GlassHudTranslator.Core.Text;

/// <summary>
/// Arabic-side text handling, as opposed to <see cref="TextNormalizer"/>, which cleans up what OCR
/// read off the screen.
/// </summary>
public static class ArabicText
{
    /// <summary>
    /// Removes tashkeel - the short-vowel marks a model sometimes adds and sometimes does not.
    ///
    /// <para>
    /// Off by default because unrequested diacritics are a change in register, not a nicety: fully
    /// vowelled text is how the Qur'an, poetry and children's primers are set, and a subtitle
    /// wearing it reads as either scripture or a school book. It is also inconsistent, which is
    /// worse than either - the same conversation comes back half vowelled and half not, depending
    /// on which model in the fallback chain answered which line.
    /// </para>
    ///
    /// <para>
    /// Only the marks that sit ON a letter are removed. The combining hamza and maddah at
    /// U+0653-U+0655 are deliberately left alone: they are how أ, إ and آ are spelled when a text
    /// does not use the precomposed forms, so stripping them would change letters rather than
    /// decoration. Tatweel is left too - it is a stretch, not a vowel, and removing it would
    /// re-space a line the model chose to set that way.
    /// </para>
    /// </summary>
    public static string WithoutDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Overwhelmingly the common case - most models return none at all - and worth one scan to
        // hand back the very same string rather than a copy of it.
        var hasAny = false;
        foreach (var c in text)
        {
            if (!IsDiacritic(c)) continue;
            hasAny = true;
            break;
        }

        if (!hasAny) return text;

        var kept = new char[text.Length];
        var written = 0;
        foreach (var c in text)
            if (!IsDiacritic(c))
                kept[written++] = c;

        return new string(kept, 0, written);
    }

    /// <summary>
    /// True for a mark that decorates a letter rather than forming one.
    ///
    /// <list type="bullet">
    /// <item>U+064B-U+0652 — the harakat proper: the tanween, fatha, damma, kasra, shadda, sukun.</item>
    /// <item>U+0670 — superscript alef, as in هَٰذَا.</item>
    /// <item>U+06D6-U+06ED — Qur'anic annotation. Vanishingly unlikely in game dialogue, but a model
    /// asked for classical register has produced them, and they are unambiguously decoration.</item>
    /// </list>
    /// </summary>
    ///
    /// <para>
    /// Written as escapes rather than as the characters themselves. These are zero-width combining
    /// marks: pasted literally into source they attach to the quote in front of them, and the range
    /// stops being readable — or reviewable.
    /// </para>
    private static bool IsDiacritic(char c) =>
        c is (>= '\u064B' and <= '\u0652') or '\u0670' or (>= '\u06D6' and <= '\u06ED');
}
