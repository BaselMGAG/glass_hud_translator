using System.Text.Json;

namespace GamingTranslatorGlassHUD.Core.Glossary;

public sealed class GlossaryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public GlossaryStore(IReadOnlyList<GlossaryTerm> terms) => Terms = terms;

    public static GlossaryStore Empty { get; } = new([]);

    public IReadOnlyList<GlossaryTerm> Terms { get; }

    public int Count => Terms.Count;

    public static GlossaryStore Load(string path)
    {
        if (!File.Exists(path)) return Empty;

        var terms = JsonSerializer.Deserialize<List<GlossaryTerm>>(File.ReadAllText(path), Options) ?? [];

        // JSON has no comment syntax, so the data file carries its own notes as entries typed
        // "_comment". They must not become matchable terms.
        return new GlossaryStore(terms
            .Where(t => !string.IsNullOrWhiteSpace(t.En) && t.Type != "_comment")
            .ToList());
    }
}
