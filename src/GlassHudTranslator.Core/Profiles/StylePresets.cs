namespace GlassHudTranslator.Core.Profiles;

/// <summary>
/// Ready-made answers to "how should this sound?", so setting up a game does not require writing a
/// prompt.
///
/// <para>
/// <c>styleHint</c> goes straight into the system prompt and is what stops a solemn epic being
/// translated in the register of a shop sign - it is the single highest-value field in a profile
/// and the one least likely to be filled in by someone who has never seen a system prompt. So the
/// editor offers these and writes the sentence for them. The hint text stays English because the
/// prompt is English; only the labels the user reads are translated.
/// </para>
/// </summary>
public sealed record StylePreset(string Id, string Hint)
{
    public const string CustomId = "custom";

    /// <summary>The default for a new profile: accurate, and wrong for no genre in particular.</summary>
    public const string PlainId = "plain";

    public static readonly StylePreset Plain = new(PlainId,
        "Ordinary contemporary prose. Translate plainly and accurately; do not add formality the "
        + "original does not have.");

    public static readonly StylePreset Epic = new("epic",
        "Serious high fantasy. Formal and weighty, with an archaic flavour. Keep that weight; do "
        + "not modernise it into casual speech.");

    public static readonly StylePreset Modern = new("modern",
        "Modern and conversational. Contemporary speech, contractions, the rhythm of people "
        + "actually talking. Do not make it stiff or literary.");

    public static readonly StylePreset Comic = new("comic",
        "Light and comic. Keep the jokes working as jokes in Arabic rather than translating them "
        + "word for word; a pun that dies in translation should be replaced, not preserved.");

    public static readonly StylePreset Technical = new("technical",
        "Factual and precise. Menus, items, statistics and instructions. Prefer the plainest "
        + "wording; never embellish, and keep numbers and units exactly as they appear.");

    /// <summary>In the order the editor shows them, plainest first.</summary>
    public static IReadOnlyList<StylePreset> All { get; } = [Plain, Epic, Modern, Comic, Technical];

    /// <summary>
    /// Which preset a stored hint came from, or null when it was hand-written. Matched on the text
    /// rather than on a stored id so that an imported or hand-edited profile still lights up the
    /// right tile instead of silently falling back to Custom.
    /// </summary>
    public static StylePreset? Match(string? hint) =>
        string.IsNullOrWhiteSpace(hint)
            ? Plain
            : All.FirstOrDefault(p => string.Equals(
                Squash(p.Hint), Squash(hint), StringComparison.OrdinalIgnoreCase));

    private static string Squash(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
