using Avalonia.Media;

namespace GamingTranslatorGlassHUD.App;

/// <summary>
/// Bundled fonts. Never rely on the target machine having an Arabic font installed - the whole
/// point of shipping self-contained is that the user installs nothing (brief 6, 10).
/// </summary>
public static class Fonts
{
    private const string Base = "avares://GamingTranslatorGlassHUD/Assets/Fonts";

    /// <summary>Noto Sans Arabic, bundled. The '#' suffix must match the font's internal family name.</summary>
    public static readonly FontFamily Arabic =
        new($"{Base}/NotoSansArabic-Regular.ttf#Noto Sans Arabic");
}
