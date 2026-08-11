namespace GlassHudTranslator.Core.Text;

/// <summary>
/// Lines the user never wants translated: «Press E to continue», a hotbar label that keeps drifting
/// into the capture region, a speaker name shown on its own.
///
/// <para>
/// <b>Whole line, not substring, and that is a usability decision before it is a safety one.</b> A
/// substring rule lets one careless entry silence everything — a phrase of "the" would swallow most
/// dialogue in English — and it cannot be explained in one sentence to somebody adding entries from
/// a settings box. Whole-line can: <i>this exact line is never translated</i>. It is also what makes
/// the History tab's "never translate this" button honest, because the button hands over the exact
/// text that was read, so there is nothing to guess at.
/// </para>
///
/// <para>
/// <b>Matched with the same jitter tolerance as the repeat guard</b>, and for the same reason. OCR
/// is not repeatable: the line that produced «Press E to continue» once produces «Press E to
/// continue.» or «Press E ta continue» on the next frame, and an exact-match ignore rule would let
/// every one of those through — which is worse than useless, because the user believes they have
/// silenced it and is then charged for it. <see cref="TextSimilarity.DistanceAtMost"/> is already
/// written, already tested and already mutation-checked, and its budget is proportional to the
/// shorter string, so a three-character phrase still needs an exact match.
/// </para>
/// </summary>
public sealed class IgnoreList
{
    /// <summary>Nothing ignored. The default, and what a profile with no list resolves to.</summary>
    public static readonly IgnoreList Empty = new([]);

    private readonly string[] _phrases;

    public IgnoreList(IEnumerable<string> phrases)
    {
        ArgumentNullException.ThrowIfNull(phrases);

        // Blank entries would match a blank body and are what an empty row in a text box produces,
        // so they are dropped on the way in rather than guarded against on every line.
        _phrases = phrases
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>The phrases, trimmed and de-duplicated, in the order they were given.</summary>
    public IReadOnlyList<string> Phrases => _phrases;

    public int Count => _phrases.Length;

    /// <summary>
    /// Parses the settings-box form: one phrase per line. Anything blank is skipped, so a trailing
    /// newline — which every text box produces — does not become an entry that matches empty reads.
    /// </summary>
    public static IgnoreList Parse(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Empty
            : new IgnoreList(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The settings-box form again, for round-tripping into the editor.</summary>
    public override string ToString() => string.Join('\n', _phrases);

    /// <summary>
    /// True when this line is one the user has said never to translate.
    ///
    /// <para>
    /// Asked of the parsed BODY rather than the raw OCR, so a phrase entered once matches whether
    /// or not the game happened to prefix it with a speaker name that frame.
    /// </para>
    /// </summary>
    public bool ShouldSkip(string? body)
    {
        if (_phrases.Length == 0 || string.IsNullOrWhiteSpace(body)) return false;

        var line = body.Trim();

        return _phrases.Any(phrase =>
            TextSimilarity.LooksLikeARepeat(line, phrase));
    }
}
