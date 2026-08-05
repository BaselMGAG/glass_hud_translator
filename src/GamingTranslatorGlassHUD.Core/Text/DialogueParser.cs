namespace GamingTranslatorGlassHUD.Core.Text;

/// <summary>
/// Splits a normalised capture into speaker and body. FFXIV puts the NPC name on its own first
/// line in the dialogue box; the cutscene subtitle bar and the quest-accept window have no name
/// line at all, so the speaker is optional.
///
/// <para>
/// The speaker is worth isolating because it goes into the prompt as context rather than as text
/// to translate - the model should not render "Y'shtola" as a sentence.
/// </para>
/// </summary>
public static class DialogueParser
{
    /// <summary>
    /// Longest plausible name line. Real FFXIV speakers run to about "The Crystal Exarch" or
    /// "Ardbert" - anything longer is a sentence that happened to lack terminal punctuation.
    /// </summary>
    private const int MaxSpeakerLength = 40;

    public static (string? Speaker, string Body) Parse(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return (null, string.Empty);

        var lines = normalized.Split('\n');
        if (lines.Length < 2) return (null, normalized.Trim());

        var candidate = lines[0].Trim();
        var rest = string.Join(' ', lines.Skip(1)).Trim();

        return LooksLikeSpeaker(candidate) && rest.Length > 0
            ? (candidate, rest)
            : (null, string.Join(' ', lines).Trim());
    }

    private static bool LooksLikeSpeaker(string line)
    {
        if (line.Length is 0 or > MaxSpeakerLength) return false;

        // A name line does not end a sentence. This is the single most reliable discriminator,
        // because dialogue that wraps onto a second line practically always ends the first with
        // punctuation or mid-clause words.
        if (line.EndsWith('.') || line.EndsWith('!') || line.EndsWith('?') ||
            line.EndsWith(',') || line.EndsWith(':') || line.EndsWith(';')) return false;

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 4) return false;

        // Names are capitalised. Allow lowercase particles ("of", "the") in titles such as
        // "The Crystal Exarch" or "Y'shtola of the Scions".
        return words.Any(w => char.IsUpper(w[0]))
            && words.All(w => char.IsUpper(w[0]) || IsParticle(w));
    }

    private static bool IsParticle(string word) =>
        word is "of" or "the" or "de" or "van" or "von";
}
