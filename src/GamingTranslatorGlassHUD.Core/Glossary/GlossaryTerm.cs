using System.Text.Json.Serialization;

namespace GamingTranslatorGlassHUD.Core.Glossary;

/// <summary>
/// One pinned proper noun. The glossary is the highest-value quality lever in the whole app
/// (brief 6): without it the model invents a different transliteration of "Y'shtola" every third
/// line, and inconsistent names are more disorienting to a reader than a slightly clumsy sentence.
/// </summary>
public sealed record GlossaryTerm(
    [property: JsonPropertyName("en")] string En,
    [property: JsonPropertyName("ar")] string Ar,
    [property: JsonPropertyName("type")] string Type = "term",
    [property: JsonPropertyName("aliases")] string[]? Aliases = null)
{
    /// <summary>Every spelling that should match this term, longest first.</summary>
    public IEnumerable<string> Surfaces =>
        new[] { En }.Concat(Aliases ?? []).Where(s => !string.IsNullOrWhiteSpace(s));

    public string ToPromptLine() => $"{En} = {Ar}";
}
