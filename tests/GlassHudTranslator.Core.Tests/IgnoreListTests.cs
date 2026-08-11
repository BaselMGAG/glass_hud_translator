using GlassHudTranslator.Core.Text;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The list of lines the user has said never to translate. Every test here is about the thing that
/// makes it worth having rather than merely tidy: an ignored line must cost NOTHING, and a rule the
/// user believes is working while it quietly lets lines through is worse than no rule at all,
/// because they are being charged for something they switched off.
/// </summary>
public class IgnoreListTests
{
    [Fact]
    public void AnEmptyListIgnoresNothing()
    {
        Assert.False(IgnoreList.Empty.ShouldSkip("Press E to continue"));
        Assert.Equal(0, IgnoreList.Empty.Count);
    }

    [Fact]
    public void AnExactLineIsSkipped()
    {
        var list = new IgnoreList(["Press E to continue"]);

        Assert.True(list.ShouldSkip("Press E to continue"));
    }

    [Theory]
    [InlineData("press e to continue")]
    [InlineData("PRESS E TO CONTINUE")]
    [InlineData("  Press E to continue  ")]
    public void CaseAndSurroundingSpaceDoNotMatter(string body)
    {
        // The user types the phrase once, from memory or from the history list. Making them match
        // the game's capitalisation is a rule they cannot see and would not guess.
        Assert.True(new IgnoreList(["Press E to continue"]).ShouldSkip(body));
    }

    [Theory]
    [InlineData("Press E to continue.")]
    [InlineData("Press E ta continue")]
    [InlineData("Press E to contlnue")]
    public void OcrJitterStillMatches(string asRead)
    {
        // THE reason this is not a string equality check. OCR is not repeatable: the line that read
        // cleanly when the user added it comes back a character different on the next frame, and an
        // exact-match rule would let that through - so the phrase they switched off would keep
        // being translated and keep being charged for, while the settings box insisted it was
        // handled. Same tolerance as the repeat guard, and the same reasoning.
        Assert.True(new IgnoreList(["Press E to continue"]).ShouldSkip(asRead));
    }

    [Fact]
    public void ADifferentLineIsNotSkipped()
    {
        var list = new IgnoreList(["Press E to continue"]);

        Assert.False(list.ShouldSkip("The Scions of the Seventh Dawn stand ready."));
    }

    [Fact]
    public void APhraseInsideALongerLineDoesNotSwallowIt()
    {
        // Whole-line, not substring. A dialogue line that happens to contain the phrase is still
        // dialogue, and the alternative - matching anywhere - means one careless entry silences
        // most of the game with no way for the user to work out which entry did it.
        var list = new IgnoreList(["continue"]);

        Assert.False(list.ShouldSkip("Continue north until you reach the aetheryte."));
    }

    [Fact]
    public void AShortPhraseStillNeedsAnExactMatch()
    {
        // The jitter budget is proportional to the shorter string, so it does not blur short
        // entries into each other. "Yes" and "No" are three edits apart and must stay distinct.
        var list = new IgnoreList(["Yes"]);

        Assert.True(list.ShouldSkip("Yes"));
        Assert.False(list.ShouldSkip("No"));
    }

    [Fact]
    public void BlankEntriesAreDroppedRatherThanMatchingEverySilentFrame()
    {
        // A text box with a trailing newline produces one, and a blank phrase would match the empty
        // body of every frame between two lines of dialogue.
        var list = IgnoreList.Parse("Press E to continue\n\n   \n");

        Assert.Equal(1, list.Count);
        Assert.False(list.ShouldSkip(""));
        Assert.False(list.ShouldSkip("   "));
    }

    [Fact]
    public void DuplicatesCollapseSoTheEditorDoesNotGrowForever()
    {
        var list = IgnoreList.Parse("Press E\npress e\nPRESS E");

        Assert.Equal(1, list.Count);
    }

    [Fact]
    public void ItRoundTripsThroughTheSettingsBoxForm()
    {
        var list = IgnoreList.Parse("Press E to continue\nOpen map");

        Assert.Equal(list.Phrases, IgnoreList.Parse(list.ToString()).Phrases);
    }

    [Fact]
    public void NothingIsSkippedWhenThereIsNothingToRead()
    {
        var list = new IgnoreList(["Press E to continue"]);

        Assert.False(list.ShouldSkip(null));
        Assert.False(list.ShouldSkip(""));
    }
}
