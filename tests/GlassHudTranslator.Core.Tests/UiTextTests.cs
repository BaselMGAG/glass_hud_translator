using System.Reflection;
using System.Text.RegularExpressions;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Platform;
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
