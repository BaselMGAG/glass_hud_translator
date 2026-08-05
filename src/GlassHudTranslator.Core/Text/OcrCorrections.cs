using System.Text;
using System.Text.Json;

namespace GlassHudTranslator.Core.Text;

/// <summary>
/// Known OCR failure modes, applied before both cache hashing and translation (brief 6).
///
/// Populated from real logs in Session 3, not from imagination - the seed file only carries the
/// handful of cases the brief already names. Longest patterns are applied first so that a phrase
/// correction wins over a word correction that would otherwise consume part of it.
/// </summary>
public sealed class OcrCorrections
{
    private readonly (string From, string To)[] _rules;

    public OcrCorrections(IReadOnlyDictionary<string, string> rules)
    {
        _rules = rules
            .Select(kv => (kv.Key, kv.Value))
            .OrderByDescending(r => r.Key.Length)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .ToArray();
    }

    public static OcrCorrections Empty { get; } = new(new Dictionary<string, string>());

    public int Count => _rules.Length;

    public static OcrCorrections Load(string path)
    {
        if (!File.Exists(path)) return Empty;

        var rules = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                    ?? new Dictionary<string, string>();

        // JSON has no comments, so the data file documents itself with "_"-prefixed keys. Without
        // this filter each one would become a live correction rule.
        return new OcrCorrections(rules
            .Where(kv => !kv.Key.StartsWith('_'))
            .ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    /// <summary>
    /// One left-to-right pass, taking the longest rule that matches at each position and skipping
    /// past whatever it emitted.
    ///
    /// <para>
    /// Not a sequence of <see cref="string.Replace(string, string)"/> calls. Running rules one
    /// after another over the whole string lets a later, shorter rule chew into the output an
    /// earlier one just produced: with rules <c>"Limsa Lominsa" -> "Limsa Lominsa"</c> and
    /// <c>"Limsa" -> X</c>, the long rule matches first, changes nothing visible, and the short
    /// rule then corrupts its result. Emitting and skipping makes a replacement final.
    /// </para>
    /// </summary>
    public string Apply(string text)
    {
        if (_rules.Length == 0 || string.IsNullOrEmpty(text)) return text;

        var builder = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var matched = MatchAt(text, i);
            if (matched is null)
            {
                builder.Append(text[i]);
                i++;
                continue;
            }

            builder.Append(matched.Value.To);
            i += matched.Value.From.Length;
        }

        return builder.ToString();
    }

    private (string From, string To)? MatchAt(string text, int index)
    {
        foreach (var rule in _rules)
        {
            if (index + rule.From.Length > text.Length) continue;
            if (string.Compare(text, index, rule.From, 0, rule.From.Length,
                    StringComparison.OrdinalIgnoreCase) == 0)
                return rule;
        }

        return null;
    }
}
