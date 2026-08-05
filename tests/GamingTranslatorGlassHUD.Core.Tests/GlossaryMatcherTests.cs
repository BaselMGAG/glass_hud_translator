using GamingTranslatorGlassHUD.Core.Glossary;
using Xunit;

namespace GamingTranslatorGlassHUD.Core.Tests;

public class GlossaryMatcherTests
{
    private static readonly GlossaryStore Store = new(
    [
        new GlossaryTerm("Y'shtola", "يشتولا", "person", ["Y'shtola Rhul"]),
        new GlossaryTerm("aether", "الأثير"),
        new GlossaryTerm("aetheryte", "الأثيرايت"),
        new GlossaryTerm("Scions of the Seventh Dawn", "أبناء الفجر السابع", "organisation", ["Scions"]),
        new GlossaryTerm("Limsa Lominsa", "ليمسا لومينسا", "place"),
        new GlossaryTerm("Warrior of Light", "محارب النور", "title"),
    ]);

    private static readonly GlossaryMatcher Matcher = new(Store);

    [Fact]
    public void FindsTermsPresentInTheLine()
    {
        var matches = Matcher.Match("Come, the aether here grows unstable.");

        Assert.Equal(["aether"], matches.Select(m => m.En));
    }

    [Fact]
    public void IgnoresTermsAbsentFromTheLine()
    {
        Assert.Empty(Matcher.Match("The weather is pleasant today."));
    }

    [Fact]
    public void RespectsWordBoundaries()
    {
        // "aetheryte" must resolve to itself, never to "aether" plus a stray suffix.
        var matches = Matcher.Match("Attune to the aetheryte.");

        Assert.Equal(["aetheryte"], matches.Select(m => m.En));
    }

    [Fact]
    public void LongestSurfaceWinsAtTheSamePosition()
    {
        var matches = Matcher.Match("The Scions of the Seventh Dawn await you.");

        Assert.Equal(["Scions of the Seventh Dawn"], matches.Select(m => m.En));
    }

    [Fact]
    public void ShorterAliasStillMatchesWhenItStandsAlone()
    {
        var matches = Matcher.Match("The Scions have gathered.");

        Assert.Equal(["Scions of the Seventh Dawn"], matches.Select(m => m.En));
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        Assert.Single(Matcher.Match("LIMSA LOMINSA is a port city."));
    }

    [Fact]
    public void ApostropheNamesMatchAsOneUnit()
    {
        var matches = Matcher.Match("Speak with Y'shtola before you depart.");

        Assert.Equal(["Y'shtola"], matches.Select(m => m.En));
    }

    [Fact]
    public void ATermIsReportedOnlyOnceEvenWhenRepeated()
    {
        var matches = Matcher.Match("aether and more aether and yet more aether");

        Assert.Single(matches);
    }

    [Fact]
    public void ResultsAreCappedSoThePromptStaysSmall()
    {
        var line = "Y'shtola aether aetheryte Limsa Lominsa Warrior of Light Scions";

        Assert.Equal(2, Matcher.Match(line, max: 2).Count);
    }

    [Fact]
    public void MatchesAreReturnedInOrderOfAppearance()
    {
        var matches = Matcher.Match("In Limsa Lominsa, Y'shtola studied the aether.");

        Assert.Equal(["Limsa Lominsa", "Y'shtola", "aether"], matches.Select(m => m.En));
    }

    [Fact]
    public void EmptyGlossaryMatchesNothing()
    {
        Assert.Empty(new GlossaryMatcher(GlossaryStore.Empty).Match("Y'shtola"));
    }

    [Fact]
    public void PromptLineIsTheFormatTheSystemPromptExpects()
    {
        Assert.Equal("aether = الأثير", new GlossaryTerm("aether", "الأثير").ToPromptLine());
    }
}
