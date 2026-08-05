namespace GlassHudTranslator.Core.Glossary;

/// <summary>
/// Finds which glossary terms actually occur in a line.
///
/// <para>
/// Only the matches are injected into the prompt, never the whole glossary (brief 6). With ~200
/// pinned terms, shipping all of them would put roughly 2,000 tokens into every request to serve
/// a line that needs two of them - and since this workload is request-limited rather than
/// token-limited, the cost of that is latency and context dilution rather than quota.
/// </para>
/// </summary>
public sealed class GlossaryMatcher
{
    /// <summary>Matches per request. Above this the prompt is being padded, not informed.</summary>
    public const int DefaultMaxMatches = 12;

    private readonly (string Surface, GlossaryTerm Term)[] _surfaces;

    public GlossaryMatcher(GlossaryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        // Longest first so "Scions of the Seventh Dawn" wins over a bare "Scions" at the same
        // position, and the shorter surface never gets a chance to claim part of the longer one.
        _surfaces = store.Terms
            .SelectMany(term => term.Surfaces.Select(surface => (Surface: surface, Term: term)))
            .OrderByDescending(x => x.Surface.Length)
            .ThenBy(x => x.Surface, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<GlossaryTerm> Match(string text, int max = DefaultMaxMatches)
    {
        if (string.IsNullOrWhiteSpace(text) || _surfaces.Length == 0 || max <= 0) return [];

        var found = new List<GlossaryTerm>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var i = 0;
        while (i < text.Length && found.Count < max)
        {
            var match = MatchAt(text, i);
            if (match is null)
            {
                i++;
                continue;
            }

            var (surface, term) = match.Value;
            if (seen.Add(term.En)) found.Add(term);
            i += surface.Length;
        }

        return found;
    }

    private (string Surface, GlossaryTerm Term)? MatchAt(string text, int index)
    {
        if (!IsBoundary(text, index - 1)) return null;

        foreach (var candidate in _surfaces)
        {
            var end = index + candidate.Surface.Length;
            if (end > text.Length) continue;
            if (!IsBoundary(text, end)) continue;
            if (string.Compare(text, index, candidate.Surface, 0, candidate.Surface.Length,
                    StringComparison.OrdinalIgnoreCase) == 0)
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// True when the character at <paramref name="index"/> cannot be part of a word, so a match
    /// starting or ending here is a whole word. Keeps "aether" out of "aetheryte".
    /// Apostrophes count as boundaries, which is what makes "Y'shtola" match as a unit.
    /// </summary>
    private static bool IsBoundary(string text, int index) =>
        index < 0 || index >= text.Length || !char.IsLetterOrDigit(text[index]);
}
