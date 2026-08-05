using GlassHudTranslator.Core.Text;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

public class TextNormalizerTests
{
    private static readonly OcrCorrections Corrections = new(new Dictionary<string, string>
    {
        ["Y shtola"] = "Y'shtola",
        ["Scions ot the Seventh Dawn"] = "Scions of the Seventh Dawn",
    });

    [Fact]
    public void CollapsesRunsOfSpacesButKeepsLineBreaks()
    {
        var result = TextNormalizer.Normalize("Y'shtola\n\n  Come,   the   aether  stirs. ");

        Assert.Equal("Y'shtola\nCome, the aether stirs.", result);
    }

    [Theory]
    [InlineData("Y’shtola")]   // curly apostrophe
    [InlineData("Y´shtola")]   // acute accent
    [InlineData("Y`shtola")]        // backtick
    public void FoldsApostropheVariantsOntoOneSpelling(string raw)
    {
        Assert.Equal("Y'shtola", TextNormalizer.Normalize(raw));
    }

    [Fact]
    public void FoldsNonBreakingSpaces()
    {
        Assert.Equal("Limsa Lominsa", TextNormalizer.Normalize("Limsa Lominsa"));
    }

    [Fact]
    public void StripsTheAdvanceCursor()
    {
        Assert.Equal("Come with me.", TextNormalizer.Normalize("Come with me. ▼"));
    }

    [Fact]
    public void KeepsTrailingDashes_BecauseInterruptedSpeechIsMeaningful()
    {
        // FFXIV ends lines with a dash constantly when a speaker is cut off. Treating that as
        // cursor noise would silently change what gets translated.
        Assert.Equal("But I thought-", TextNormalizer.Normalize("But I thought—"));
    }

    [Fact]
    public void KeepsEllipses()
    {
        Assert.Equal("I cannot say...", TextNormalizer.Normalize("I cannot say..."));
    }

    [Fact]
    public void AppliesCorrectionsAfterWhitespaceIsUnified()
    {
        var result = TextNormalizer.Normalize("The  Scions   ot the Seventh Dawn await.", Corrections);

        Assert.Equal("The Scions of the Seventh Dawn await.", result);
    }

    [Fact]
    public void PreservesCase_BecauseTheModelNeedsIt()
    {
        // PROJECT_PLAN.md 1.5 - lowercasing belongs to the cache key, not to the prompt input.
        Assert.Equal("Limsa Lominsa", TextNormalizer.Normalize("Limsa Lominsa"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void EmptyInputProducesEmptyOutput(string raw)
    {
        Assert.Equal(string.Empty, TextNormalizer.Normalize(raw));
    }


    [Theory]
    [InlineData("| have seen enough.", "I have seen enough.")]
    [InlineData("But | thought-", "But I thought-")]
    [InlineData("Y'shtola\n| agree.", "Y'shtola\nI agree.")]
    public void RepairsALonePipeIntoThePronounI(string raw, string expected)
    {
        // Tesseract confuses these constantly - twice in twelve frames on the first real run.
        Assert.Equal(expected, TextNormalizer.Normalize(raw));
    }

    [Fact]
    public void DoesNotTouchAPipeThatIsPartOfAWord()
    {
        Assert.Equal("a|b", TextNormalizer.Normalize("a|b"));
    }

    [Fact]
    public void LongestCorrectionRuleWinsOverShorterOverlappingOne()
    {
        var corrections = new OcrCorrections(new Dictionary<string, string>
        {
            ["Limsa"] = "WRONG",
            ["Limsa Lominsa"] = "Limsa Lominsa",
        });

        Assert.Equal("Limsa Lominsa", TextNormalizer.Normalize("Limsa Lominsa", corrections));
    }
}

public class CacheKeyTests
{
    [Fact]
    public void OcrVariantsOfTheSameLineCollapseToOneKey()
    {
        // The quota guard (brief 5). If these two produce different keys, the same line of
        // dialogue is paid for twice, and that - not session length - is what exhausts a daily
        // budget. This test is the reason the correction dictionary runs before hashing.
        var corrections = new OcrCorrections(new Dictionary<string, string> { ["Y shtola"] = "Y'shtola" });

        var clean = TextNormalizer.Normalize("Y shtola nods slowly.", corrections);
        var mangled = TextNormalizer.Normalize("Y’shtola   nods slowly. ▼", corrections);

        Assert.Equal(CacheKey.For(clean), CacheKey.For(mangled));
    }

    [Fact]
    public void CaseDoesNotAffectTheKey()
    {
        Assert.Equal(CacheKey.For("Come, the aether stirs."), CacheKey.For("come, THE aether STIRS."));
    }

    [Fact]
    public void DifferentLinesProduceDifferentKeys()
    {
        Assert.NotEqual(CacheKey.For("Come with me."), CacheKey.For("Come with us."));
    }

    [Fact]
    public void KeyIsLowercaseHexSha256()
    {
        var key = CacheKey.For("anything");

        Assert.Equal(64, key.Length);
        Assert.Matches("^[0-9a-f]{64}$", key);
    }
}

public class DialogueParserTests
{
    [Fact]
    public void SplitsSpeakerFromBodyInTheNpcDialogueBox()
    {
        var (speaker, body) = DialogueParser.Parse("Y'shtola\nCome, the aether here grows unstable.");

        Assert.Equal("Y'shtola", speaker);
        Assert.Equal("Come, the aether here grows unstable.", body);
    }

    [Fact]
    public void HandlesMultiWordTitlesWithParticles()
    {
        var (speaker, body) = DialogueParser.Parse("The Crystal Exarch\nYou have come at last.");

        Assert.Equal("The Crystal Exarch", speaker);
        Assert.Equal("You have come at last.", body);
    }

    [Fact]
    public void CutsceneSubtitleHasNoSpeaker()
    {
        var (speaker, body) = DialogueParser.Parse("The Warrior of Light draws near.");

        Assert.Null(speaker);
        Assert.Equal("The Warrior of Light draws near.", body);
    }

    [Fact]
    public void WrappedSentenceIsNotMistakenForASpeaker()
    {
        // The first line ends with a comma, so it is mid-sentence, not a name.
        var (speaker, body) = DialogueParser.Parse("When the aether stirs,\nthe beast tribes grow restless.");

        Assert.Null(speaker);
        Assert.Equal("When the aether stirs, the beast tribes grow restless.", body);
    }

    [Fact]
    public void JoinsBodyLinesIntoOneLine()
    {
        var (speaker, body) = DialogueParser.Parse("Alphinaud\nWe must go.\nAt once.");

        Assert.Equal("Alphinaud", speaker);
        Assert.Equal("We must go. At once.", body);
    }

    [Fact]
    public void LowercaseFirstLineIsNotASpeaker()
    {
        var (speaker, _) = DialogueParser.Parse("and then he vanished\ninto the mist");

        Assert.Null(speaker);
    }

    [Fact]
    public void EmptyInputIsHandled()
    {
        var (speaker, body) = DialogueParser.Parse("");

        Assert.Null(speaker);
        Assert.Equal(string.Empty, body);
    }
}
