using GlassHudTranslator.Core.Diagnostics;
using GlassHudTranslator.Core.Ocr;
using GlassHudTranslator.Core.Regions;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// Layouts are written out by hand rather than rendered and OCR'd, on purpose. The corpus is
/// synthetic, so a ranking rule tuned against rendered frames would be measuring the frame
/// generator. Hand-built geometry states the layout the rule claims to handle, which is the thing
/// actually under test. Real captures are still owed - see CONTRIBUTING.
/// </summary>
public class RegionFinderTests
{
    private const int W = 1920;
    private const int H = 1080;

    /// <summary>A row of words with realistic spacing, laid out left to right from (x, y).</summary>
    private static IEnumerable<OcrWord> Row(
        int x, int y, int count, int wordWidth = 90, int height = 26, float confidence = 92f)
    {
        for (var i = 0; i < count; i++)
            yield return new OcrWord($"w{i}",
                new OcrBox(x + i * (wordWidth + 12), y, wordWidth, height), confidence, true);
    }

    private static List<OcrWord> Paragraph(int x, int y, int lines, int perLine, int lineStep = 34)
    {
        var words = new List<OcrWord>();
        for (var line = 0; line < lines; line++)
            words.AddRange(Row(x, y + line * lineStep, perLine));
        return words;
    }

    [Fact]
    public void AWideBlockLowAndCentredIsReadAsDialogue()
    {
        // The FFXIV dialogue box: three lines, roughly centred, in the bottom third.
        var words = Paragraph(x: 560, y: 830, lines: 3, perLine: 8);

        var best = RegionFinder.Propose(words, W, H)[0];

        Assert.Equal(TextRegionKind.Dialogue, best.Kind);
        Assert.Equal(3, best.LineCount);
        Assert.Equal(24, best.WordCount);

        // The rectangle has to actually contain the text it was derived from, with a little margin
        // and without leaving the frame.
        Assert.True(best.Bounds.FitsWithin(W, H));
        Assert.True(best.Bounds.X < 560 && best.Bounds.Y < 830);
        Assert.True(best.Bounds.X + best.Bounds.Width > 560 + 7 * 102 + 90);
    }

    [Fact]
    public void ASingleCentredLineAtTheBottomIsReadAsASubtitle()
    {
        var words = Row(700, 980, count: 6).ToList();

        var best = RegionFinder.Propose(words, W, H)[0];

        Assert.Equal(TextRegionKind.Subtitle, best.Kind);
        Assert.Equal(1, best.LineCount);
    }

    [Fact]
    public void ANarrowTallBlockAgainstTheRightEdgeIsReadAsASidePanel()
    {
        // A quest log: short lines stacked down the right-hand side.
        var words = new List<OcrWord>();
        for (var line = 0; line < 8; line++)
            words.AddRange(Row(1640, 300 + line * 34, count: 2, wordWidth: 60));

        var best = RegionFinder.Propose(words, W, H)[0];

        Assert.Equal(TextRegionKind.SidePanel, best.Kind);
    }

    [Fact]
    public void TheDialogueBoxOutranksTheChatLogOnAFullScreen()
    {
        // The case the whole ranking exists for. FFXIV puts a text-heavy chat log in the bottom
        // LEFT and the dialogue box bottom CENTRE, and the chat log has more words. Ranking on
        // volume alone proposes the chat log - which is text, is genuinely down there, and is not
        // what anyone wants translated.
        var words = new List<OcrWord>();
        words.AddRange(Paragraph(x: 40, y: 700, lines: 9, perLine: 5));        // chat log, 45 words
        words.AddRange(Paragraph(x: 700, y: 860, lines: 3, perLine: 8));       // dialogue, 24 words
        words.AddRange(Row(60, 60, count: 3, wordWidth: 50));                  // party list
        words.AddRange(Row(1700, 40, count: 2, wordWidth: 60));                // clock

        var ranked = RegionFinder.Propose(words, W, H);

        Assert.Equal(TextRegionKind.Dialogue, ranked[0].Kind);
        Assert.Equal(24, ranked[0].WordCount);

        // The chat log shares a horizontal band with the dialogue box and has nearly twice the
        // text. It must not merge into it, and it must not outrank it.
        Assert.Contains(ranked, c => c.WordCount == 45);
        Assert.True(ranked.Count >= 3, "Blocks were merged that should have stayed apart.");
    }

    [Fact]
    public void TwoElementsOverlappingInBothAxesCannotBeSeparated()
    {
        // The honest limit of a purely geometric method, written down rather than discovered later.
        // Words that overlap both horizontally and vertically have no signal left to separate them
        // by, so they become one block. Real interfaces do not draw two text elements on top of one
        // another, which is why this is acceptable - but if a proposal is ever inexplicably wide,
        // this is the reason, and the fix would be per-word colour or stroke evidence, not more
        // geometry.
        var words = new List<OcrWord>();
        words.AddRange(Row(40, 500, count: 5));                    // ends at 40 + 4*102 + 90 = 538
        words.AddRange(Row(520, 496, count: 5));                   // starts inside the first row

        Assert.Single(RegionFinder.Propose(words, W, H));
    }

    [Fact]
    public void TwoThingsSharingARowAreNotOneVeryWideRegion()
    {
        // A label at the far left and a timer at the far right, on the same row. Merging them
        // produces a rectangle spanning the whole screen and containing mostly nothing.
        var words = new List<OcrWord>();
        words.AddRange(Row(40, 500, count: 3, wordWidth: 70));
        words.AddRange(Row(1600, 500, count: 3, wordWidth: 70));

        var ranked = RegionFinder.Propose(words, W, H);

        Assert.Equal(2, ranked.Count);
        Assert.All(ranked, c => Assert.True(c.Bounds.Width < W / 2,
            $"A region {c.Bounds.Width}px wide swallowed the gap between two separate elements."));
    }

    [Fact]
    public void ParagraphsSeparatedByAGapStayApart()
    {
        var words = new List<OcrWord>();
        words.AddRange(Paragraph(x: 600, y: 200, lines: 2, perLine: 6));
        words.AddRange(Paragraph(x: 600, y: 700, lines: 2, perLine: 6));

        Assert.Equal(2, RegionFinder.Propose(words, W, H).Count);
    }

    [Fact]
    public void WrappedLinesOfOneParagraphStayTogether()
    {
        var words = Paragraph(x: 600, y: 400, lines: 4, perLine: 7);

        var ranked = RegionFinder.Propose(words, W, H);

        Assert.Single(ranked);
        Assert.Equal(4, ranked[0].LineCount);
    }

    [Fact]
    public void RejectedWordsCannotConjureARegion()
    {
        // Low-confidence noise is exactly what invents a text region where there is no text, and a
        // first proposal that circles a UI border costs the user's trust in every later one.
        var noise = Row(40, 40, count: 8)
            .Select(w => w with { Confidence = 9f, Accepted = false })
            .ToList();

        Assert.Empty(RegionFinder.Propose(noise, W, H));
    }

    [Fact]
    public void TooLittleTextProducesNoProposalRatherThanAGuess()
    {
        // Blank beats confidently wrong. Two words is a timer or a button label.
        Assert.Empty(RegionFinder.Propose(Row(600, 900, count: 2).ToList(), W, H));
        Assert.Empty(RegionFinder.Propose([], W, H));
    }

    [Fact]
    public void ADegenerateFrameIsRefusedRatherThanDividedBy()
    {
        var words = Paragraph(x: 600, y: 830, lines: 3, perLine: 8);

        Assert.Empty(RegionFinder.Propose(words, 0, H));
        Assert.Empty(RegionFinder.Propose(words, W, 0));
    }

    [Fact]
    public void PaddingNeverPushesAProposalOutsideTheFrame()
    {
        // Text hard against the top-left corner. The padding has to be clipped, because a region
        // failing FitsWithin is one the capture layer refuses - a suggestion that cannot be
        // accepted is worse than no suggestion.
        var words = Row(0, 0, count: 5).ToList();

        var candidate = RegionFinder.Propose(words, W, H)[0];

        Assert.Equal(0, candidate.Bounds.X);
        Assert.Equal(0, candidate.Bounds.Y);
        Assert.True(candidate.Bounds.FitsWithin(W, H));
    }

    [Fact]
    public void TheOrderIsStableForIdenticalInput()
    {
        // Two blocks of the same shape at mirrored positions score identically. Without a tiebreak
        // the "best" proposal would depend on dictionary ordering and change between runs, so the
        // app would highlight a different rectangle each time the user pressed the button.
        var words = new List<OcrWord>();
        words.AddRange(Paragraph(x: 300, y: 400, lines: 2, perLine: 5));
        words.AddRange(Paragraph(x: 300, y: 700, lines: 2, perLine: 5));

        var first = RegionFinder.Propose(words, W, H).Select(c => c.Bounds).ToList();
        var again = RegionFinder.Propose([.. words.AsEnumerable().Reverse()], W, H)
            .Select(c => c.Bounds).ToList();

        Assert.Equal(first, again);
    }

    /// <summary>
    /// The two halves together on real OCR output, which hand-built geometry cannot exercise: word
    /// boxes that came out of Tesseract, through the upscale mapping, into the clusterer. Needs the
    /// binary, which CI installs; returns early without it rather than failing a contributor's
    /// build.
    /// </summary>
    [Fact]
    public async Task RealOcrOutputClustersIntoOneBlockAroundTheText()
    {
        if (TesseractCliEngine.Locate() is null) return;

        var frame = SyntheticFrames.Render(
            new SyntheticLine("Y'shtola", "Come, the aether here grows unstable."));

        using var engine = new TesseractCliEngine();
        var read = await engine.RecognizeAsync(frame, CancellationToken.None);
        var proposals = RegionFinder.Propose(read.Words, frame.Width, frame.Height);

        var only = Assert.Single(proposals);
        Assert.True(only.Bounds.FitsWithin(frame.Width, frame.Height));
        Assert.Equal(read.Words.Count(w => w.Accepted), only.WordCount);

        // Unknown, and that is the right answer. These frames are crops of a dialogue box, not
        // screens - there is no bottom third for the text to sit in, so every positional rule
        // declines. A change that starts confidently labelling this "Dialogue" is a change that has
        // learned to pattern-match on nothing, which is the failure that costs a first-time user
        // their trust in every later suggestion.
        Assert.Equal(TextRegionKind.Unknown, only.Kind);
    }

    [Fact]
    public void MeanConfidenceIsReportedSoAProposalCanBeQualified()
    {
        var words = Paragraph(x: 600, y: 830, lines: 2, perLine: 5);
        words[0] = words[0] with { Confidence = 40f };

        var candidate = RegionFinder.Propose(words, W, H)[0];

        Assert.InRange(candidate.MeanConfidence, 80f, 92f);
    }
}
