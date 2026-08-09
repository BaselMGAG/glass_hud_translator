using GlassHudTranslator.Core.Text;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

public class TextSimilarityTests
{
    // ── the distance itself ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("a", "a", 0)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("flaw", "lawn", 2)]
    [InlineData("abc", "", 3)]
    [InlineData("", "abc", 3)]
    public void TheDistanceIsTheTextbookOne(string a, string b, int expected)
    {
        Assert.Equal(expected, TextSimilarity.DistanceAtMost(a, b, 10));
    }

    [Fact]
    public void PastTheBudgetItReportsNothingRatherThanANumber()
    {
        // The banded version cannot report a distance it never computed, and saying "further than
        // three" is the only question the caller asks anyway.
        Assert.Null(TextSimilarity.DistanceAtMost("kitten", "sitting", 2));
        Assert.Equal(3, TextSimilarity.DistanceAtMost("kitten", "sitting", 3));
    }

    [Fact]
    public void AVeryDifferentLengthIsRejectedWithoutComputingAnything()
    {
        // Not an optimisation so much as the common case: one subtitle replaced by a longer one.
        Assert.Null(TextSimilarity.DistanceAtMost("short", new string('x', 400), 3));
    }

    [Fact]
    public void TheBandedResultAgreesWithTheFullMatrixOnRealisticLines()
    {
        // The band is the part that could be wrong in a way no single example exposes: cells
        // outside it are skipped, and skipping one that mattered would under-report. Checked
        // against a plain full-matrix Levenshtein over a spread of small edits.
        var baseline = "Come, the aether here grows unstable. We must reach Limsa Lominsa by nightfall.";

        string[] variants =
        [
            baseline,
            baseline.Replace(',', '.'),
            baseline.Replace("Limsa", "LImsa"),
            baseline + " ",
            baseline.Replace("unstable.", "unstable"),
            baseline.Replace("We must", "we musl"),
            "Entirely different sentence with nothing whatsoever in common.",
        ];

        foreach (var variant in variants)
        {
            var full = FullMatrix(baseline, variant);
            var banded = TextSimilarity.DistanceAtMost(baseline, variant, 6);

            if (full <= 6) Assert.Equal(full, banded);
            else Assert.Null(banded);
        }
    }

    private static int FullMatrix(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));

        return d[a.Length, b.Length];
    }

    // ── what counts as the same line ──────────────────────────────────────────────────────────

    [Fact]
    public void OcrJitterOnASentenceIsTheSameLine()
    {
        const string shown = "Come, the aether here grows unstable.";

        // The three ways the same pixels come back differently one poll later.
        Assert.True(TextSimilarity.LooksLikeARepeat("Come. the aether here grows unstable.", shown));
        Assert.True(TextSimilarity.LooksLikeARepeat("Come, the aether here grows unstabIe.", shown));
        Assert.True(TextSimilarity.LooksLikeARepeat("Come, the aether here grows unstable. ", shown));
    }

    [Fact]
    public void ARealNextLineIsNotARepeat()
    {
        Assert.False(TextSimilarity.LooksLikeARepeat(
            "We must reach Limsa Lominsa by nightfall.",
            "Come, the aether here grows unstable."));
    }

    [Theory]
    [InlineData("yes", "no")]
    [InlineData("Open", "Exit")]
    [InlineData("HP", "MP")]
    public void ShortLabelsHaveToMatchExactly(string current, string previous)
    {
        // An absolute budget of three is right for a sentence and absurd for a word - "yes" and
        // "no" are three edits apart. The proportional cap is what stops a menu label being
        // suppressed as a repeat of a completely different menu label.
        Assert.False(TextSimilarity.LooksLikeARepeat(current, previous));
    }

    [Fact]
    public void IdenticalShortLabelsStillCount()
    {
        Assert.True(TextSimilarity.LooksLikeARepeat("Exit", "Exit"));
    }

    [Fact]
    public void CaseIsIgnored()
    {
        // The cache key lowercases before hashing, so treating these as different lines here would
        // contradict what the row they both land in already says.
        Assert.True(TextSimilarity.LooksLikeARepeat(
            "Come, the aether here grows unstable.",
            "come, the aether here grows unstable."));
    }

    [Fact]
    public void NothingIsARepeatOfNothing()
    {
        Assert.False(TextSimilarity.LooksLikeARepeat("anything at all", null));
        Assert.False(TextSimilarity.LooksLikeARepeat("anything at all", ""));
        Assert.False(TextSimilarity.LooksLikeARepeat(null, "anything at all"));
    }

    [Fact]
    public void ADriftingCaptionEventuallyGetsTranslated()
    {
        // A scoreboard or a timer inside the captured rectangle changes a character at a time. Each
        // step is within the budget of the one before it, so a guard that advanced its reference on
        // every near-match would let an arbitrarily large change through and translate none of it.
        // The reference is the last line SHOWN, so the drift is measured from there.
        const string shown = "Round 1 of 12 - score 000000";

        Assert.True(TextSimilarity.LooksLikeARepeat("Round 1 of 12 - score 000010", shown));
        Assert.True(TextSimilarity.LooksLikeARepeat("Round 1 of 12 - score 001010", shown));
        Assert.False(TextSimilarity.LooksLikeARepeat("Round 3 of 12 - score 471290", shown));
    }
}
