using System.Reflection;
using System.Text.RegularExpressions;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Platform;
using GlassHudTranslator.Core.Regions;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

public class UiTextTests
{
    private static IEnumerable<PropertyInfo> StringProperties => typeof(UiText)
        .GetProperties()
        .Where(p => p.PropertyType == typeof(string));

    [Fact]
    public void EveryStringIsTranslatedInBothLanguages()
    {
        // required init properties already force both instances to set every field, so this is
        // really guarding against someone "filling one in" with an empty string to shut the
        // compiler up.
        foreach (var property in StringProperties)
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(UiText.En)),
                $"English {property.Name} is blank.");
            Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(UiText.Ar)),
                $"Arabic {property.Name} is blank.");
        }
    }

    [Fact]
    public void FormatPlaceholdersMatchBetweenLanguages()
    {
        // The failure this prevents is nasty and one-sided: a translation carrying {1} where the
        // English carries only {0} throws FormatException at runtime, and only ever for the users
        // reading that language - which here is precisely the audience the app exists for.
        foreach (var property in StringProperties)
        {
            var english = Placeholders((string)property.GetValue(UiText.En)!);
            var arabic = Placeholders((string)property.GetValue(UiText.Ar)!);

            Assert.True(english.SetEquals(arabic),
                $"{property.Name} uses {{{string.Join(",", english.Order())}}} in English but " +
                $"{{{string.Join(",", arabic.Order())}}} in Arabic.");
        }

        static HashSet<int> Placeholders(string text) => Regex
            .Matches(text, @"\{(\d+)\}")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToHashSet();
    }

    [Fact]
    public void EveryHotkeyActionHasADescriptionInBothLanguages()
    {
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            foreach (var text in new[] { UiText.En, UiText.Ar })
            {
                var described = text.HotkeyDescription(action);

                Assert.False(string.IsNullOrWhiteSpace(described));

                // The switch falls back to action.ToString() for anything unhandled, which would
                // put a C# enum name in front of the user.
                Assert.NotEqual(action.ToString(), described);
            }
        }
    }

    [Fact]
    public void EveryCaptureRegionHasADisplayNameInBothLanguages()
    {
        // The bug this locks down: region names are stored English keys, and the buttons used to be
        // built by gluing one onto a translated verb - "حدد dialogue". Half a translated interface
        // reads as an unfinished build, which is exactly what a first-time user should not see.
        foreach (var region in RegionProfile.Names.All)
        {
            Assert.NotEqual(region, UiText.Ar.RegionName(region));
            Assert.Equal(region, UiText.En.RegionName(region));
        }
    }

    [Fact]
    public void AnUnknownRegionFallsThroughRatherThanVanishing()
    {
        // A region name added to RegionProfile.Names without a translation should show as itself.
        // Wrong-looking is recoverable; a blank button is not.
        Assert.Equal("minimap", UiText.Ar.RegionName("minimap"));
    }

    [Fact]
    public void NoArabicLabelLeavesAnEnglishWordInTheMiddleOfIt()
    {
        // Latin is legitimate in a handful of strings - hotkey syntax, provider names, file names -
        // so this checks only the short labels a user reads as a unit: buttons, rows and tabs.
        string[] shortLabels =
        [
            nameof(UiText.TabProviders), nameof(UiText.TabTranslating), nameof(UiText.TabOverlay),
            nameof(UiText.TabHotkeys), nameof(UiText.TabDiagnostics), nameof(UiText.SaveKeys),
            nameof(UiText.ActiveLanes), nameof(UiText.Profile), nameof(UiText.Arabic),
            nameof(UiText.Register), nameof(UiText.CaptureRegions), nameof(UiText.PickRegion),
            nameof(UiText.RegionDialogue), nameof(UiText.RegionSubtitle), nameof(UiText.RegionQuest),
            nameof(UiText.Corrections), nameof(UiText.PinCorrection), nameof(UiText.FontSize),
            nameof(UiText.PanelOpacity), nameof(UiText.PreviewOverlay), nameof(UiText.PasteKeyHere),
            nameof(UiText.ApplyHotkeys), nameof(UiText.ResetToDefaults), nameof(UiText.TranslateNow),
            nameof(UiText.RouterLog), nameof(UiText.TestTranslation), nameof(UiText.Refresh),
        ];

        foreach (var name in shortLabels)
        {
            var value = (string)typeof(UiText).GetProperty(name)!.GetValue(UiText.Ar)!;

            Assert.DoesNotContain(value, c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        }
    }

    [Fact]
    public void ArabicIsRightToLeftAndEnglishIsNot()
    {
        Assert.True(UiText.Ar.IsRightToLeft);
        Assert.False(UiText.En.IsRightToLeft);
    }

    [Fact]
    public void ArabicStringsAreActuallyArabic()
    {
        // Catches a translation left as the English text after a copy-paste. Skips the entries
        // that are deliberately identical or Latin - a product name, and the language names in
        // the picker, which are each written in their own language on purpose.
        string[] deliberatelyLatin = [nameof(UiText.WindowTitle)];

        foreach (var property in StringProperties.Where(p => !deliberatelyLatin.Contains(p.Name)))
        {
            var arabic = (string)property.GetValue(UiText.Ar)!;

            Assert.True(arabic.Any(c => c is >= '؀' and <= 'ۿ'),
                $"Arabic {property.Name} contains no Arabic letters: \"{arabic}\"");
        }
    }

    [Fact]
    public void BothLanguagesAreOfferedInThePicker()
    {
        Assert.Equal(2, UiText.Choices.Count);
        Assert.Contains(UiText.Choices, c => c.Language == UiLanguage.English);
        Assert.Contains(UiText.Choices, c => c.Language == UiLanguage.Arabic);

        // Each language names itself in its own script, so the switch is findable by someone who
        // cannot read the language the app is currently showing.
        Assert.Equal("العربية", UiText.Choices.Single(c => c.Language == UiLanguage.Arabic).Name);
    }

    [Fact]
    public void ForReturnsTheMatchingInstance()
    {
        Assert.Same(UiText.En, UiText.For(UiLanguage.English));
        Assert.Same(UiText.Ar, UiText.For(UiLanguage.Arabic));
    }

    [Fact]
    public void SettingsDefaultToEnglish()
    {
        // English stays the default because it is what the screenshots and documentation show.
        Assert.Equal(UiLanguage.English, new AppSettings().Language);
    }
}

file static class OrderExtensions
{
    public static IEnumerable<int> Order(this HashSet<int> values) => values.OrderBy(v => v);
}
