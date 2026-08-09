namespace GlassHudTranslator.Core.Text;

/// <summary>
/// "Is this the same line I just showed, misread slightly differently?"
///
/// <para>
/// The frame-level gate answers a cheaper question and cannot answer this one. It compares
/// binarised thumbnails, so a subtitle burnt into moving footage differs from the previous frame in
/// whatever the picture is doing behind it — the text can be pixel-identical while the signature
/// says the screen changed, and every one of those changes used to be a fresh OCR, a fresh cache
/// key and a fresh request. OCR itself adds the rest: the same glyphs at a slightly different
/// moment come back with a comma turned into a full stop, an <c>l</c> read as an <c>I</c>, a
/// trailing space gained. Each variant hashes differently, so the cache cannot save us either.
/// </para>
///
/// <para>
/// So this runs after OCR and before the cache lookup, where it costs one string comparison and
/// saves the metered half. It is the second of two nets and it catches what the first cannot: the
/// first spends free polls to avoid requests, this one spends a finished OCR to avoid one.
/// </para>
/// </summary>
public static class TextSimilarity
{
    /// <summary>
    /// The largest edit distance that still counts as the same line. Three characters is about what
    /// one poll of OCR jitter produces on a sentence; a fourth is usually a real word changing.
    /// </summary>
    public const int DefaultRepeatDistance = 3;

    /// <summary>
    /// Levenshtein distance, computed only as far as <paramref name="max"/> and abandoned the
    /// moment it is certain to exceed it. Returns null for "further apart than that".
    ///
    /// <para>
    /// Banded rather than full: only the cells within <paramref name="max"/> of the diagonal can
    /// contribute to a path that stays inside the budget, so the work is O(n × max) instead of
    /// O(n × m). At the budget this is called with, that is seven cells a row. It matters because
    /// this runs on every poll of a session that may last an hour.
    /// </para>
    ///
    /// <para>
    /// The length check first is not an optimisation so much as the common case: two strings whose
    /// lengths differ by more than the budget cannot possibly be within it, and a subtitle changing
    /// to a longer subtitle is most of what this sees.
    /// </para>
    /// </summary>
    public static int? DistanceAtMost(string a, string b, int max, bool ignoreCase = false)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (max < 0) return null;

        var n = a.Length;
        var m = b.Length;

        if (Math.Abs(n - m) > max) return null;
        if (n == 0) return m <= max ? m : null;
        if (m == 0) return n <= max ? n : null;

        var previous = new int[m + 1];
        var current = new int[m + 1];
        for (var j = 0; j <= m; j++) previous[j] = j;

        var unreachable = max + 1;

        for (var i = 1; i <= n; i++)
        {
            var from = Math.Max(1, i - max);
            var to = Math.Min(m, i + max);

            current[0] = i;
            if (from > 1) current[from - 1] = unreachable;

            var bestInRow = unreachable;

            for (var j = from; j <= to; j++)
            {
                var same = ignoreCase
                    ? char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1])
                    : a[i - 1] == b[j - 1];

                var value = Math.Min(
                    Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (same ? 0 : 1));

                current[j] = value;
                if (value < bestInRow) bestInRow = value;
            }

            // Everything the next row could build on already costs more than the budget, and edit
            // distance never decreases as the rows advance. Nothing below can recover.
            if (bestInRow > max) return null;

            if (to < m) current[to + 1] = unreachable;

            (previous, current) = (current, previous);
        }

        var distance = previous[m];
        return distance <= max ? distance : null;
    }

    /// <summary>
    /// Whether <paramref name="current"/> is the same line as <paramref name="previous"/>, allowing
    /// for OCR jitter.
    ///
    /// <para>
    /// Two conditions, and the second is the one that is easy to leave out. An absolute budget of
    /// three characters is right for a sentence and absurd for a word: "yes" and "no" are three
    /// edits apart and are not the same line, and neither are "Open" and "Exit". So the budget is
    /// also capped at a quarter of the shorter body, which makes it 0 for anything under four
    /// characters, 1 by four, and the full three only once the line is twelve characters or more.
    /// Short strings therefore have to match exactly, which is the correct answer for a menu label.
    /// </para>
    ///
    /// <para>
    /// Case is ignored. "Limsa" read as "limsa" is one misread letter, not a new line — and the
    /// cache key lowercases before hashing anyway, so treating them as different here would
    /// contradict what the row they end up in already says.
    /// </para>
    /// </summary>
    public static bool LooksLikeARepeat(
        string? current, string? previous, int maxDistance = DefaultRepeatDistance)
    {
        if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(previous)) return false;
        if (string.Equals(current, previous, StringComparison.OrdinalIgnoreCase)) return true;

        var shorter = Math.Min(current.Length, previous.Length);
        var budget = Math.Min(maxDistance, shorter / 4);

        return budget > 0 && DistanceAtMost(current, previous, budget, ignoreCase: true) is not null;
    }
}
