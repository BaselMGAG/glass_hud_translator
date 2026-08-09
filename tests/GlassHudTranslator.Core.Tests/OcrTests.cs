using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Diagnostics;
using GlassHudTranslator.Core.Ocr;
using GlassHudTranslator.Core.Text;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

public class OcrPreprocessorTests
{
    [Fact]
    public void UpscalesByTheConfiguredFactor()
    {
        var frame = new FrameBuilder(100, 40, Rgb.BoxDark).Build();

        var prepared = OcrPreprocessor.Prepare(frame,
            new OcrPreprocessOptions { UpscaleFactor = 2, PadPixels = 0 });

        Assert.Equal(200, prepared.Width);
        Assert.Equal(80, prepared.Height);
    }

    [Fact]
    public void PaddingIsAddedAfterTheUpscaleAndIsReportedInThoseUnits()
    {
        // Which unit the margin is in decides whether the word boxes come back in the right place,
        // because ParseTsv subtracts this number before dividing the upscale out. Eight source
        // pixels at 2x is sixteen on every side of a 200x80 image.
        var frame = new FrameBuilder(100, 40, Rgb.BoxDark).Build();
        var options = new OcrPreprocessOptions { UpscaleFactor = 2, PadPixels = 8 };

        var prepared = OcrPreprocessor.Prepare(frame, options);

        Assert.Equal(16, OcrPreprocessor.PaddingFor(options));
        Assert.Equal(200 + 32, prepared.Width);
        Assert.Equal(80 + 32, prepared.Height);
    }

    [Fact]
    public void ThePaddingIsBlankPageRatherThanAFrameAroundTheText()
    {
        // It goes on after the auto-invert, so the image is dark-on-light by then and white is
        // continuous with the background. Padding before the invert would draw a bright border
        // around the text, which is worse than not padding at all.
        var frame = new FrameBuilder(40, 20, Rgb.BoxDark)
            .Rect(5, 5, 20, 8, Rgb.TextWhite)
            .Build();

        var prepared = OcrPreprocessor.Prepare(frame,
            new OcrPreprocessOptions { UpscaleFactor = 1, AutoInvert = true, PadPixels = 4 });

        var grey = prepared.ToGreyscale();

        Assert.Equal(255, grey[0]);                       // top-left of the margin
        Assert.Equal(255, grey[^1]);                      // bottom-right of the margin
        Assert.Equal(48, prepared.Width);
        Assert.Equal(28, prepared.Height);
    }

    [Fact]
    public void InvertsLightTextOnDarkSoTesseractSeesDarkOnLight()
    {
        // FFXIV draws light glyphs on a dark box; Tesseract expects the opposite.
        var frame = new FrameBuilder(100, 40, Rgb.BoxDark)
            .Rect(10, 10, 20, 10, Rgb.TextWhite)
            .Build();

        var prepared = OcrPreprocessor.Prepare(frame,
            new OcrPreprocessOptions { UpscaleFactor = 1, AutoInvert = true });

        var grey = prepared.ToGreyscale();
        Assert.True(grey[0] > 200, "background should be light after inversion");
    }

    [Fact]
    public void DoesNotInvertDarkTextOnLight()
    {
        // The quest-accept window is lighter; the same code path must leave it alone.
        var frame = new FrameBuilder(100, 40, Rgb.White)
            .Rect(10, 10, 20, 10, Rgb.Black)
            .Build();

        var prepared = OcrPreprocessor.Prepare(frame,
            new OcrPreprocessOptions { UpscaleFactor = 1, AutoInvert = true });

        var grey = prepared.ToGreyscale();
        Assert.True(grey[0] > 200, "background should stay light");
    }

    [Fact]
    public void StretchesContrastOnADimCapture()
    {
        var frame = new FrameBuilder(60, 30, new Rgb(40, 40, 40))
            .Rect(5, 5, 20, 10, new Rgb(90, 90, 90))
            .Build();

        var prepared = OcrPreprocessor.Prepare(frame, new OcrPreprocessOptions
        {
            UpscaleFactor = 1, StretchContrast = true, AutoInvert = false, PadPixels = 0,
        });

        var grey = prepared.ToGreyscale();
        Assert.Equal(0, grey.Min());
        Assert.Equal(255, grey.Max());
    }

    [Fact]
    public void OneBrightOutlierDoesNotTurnTheStretchIntoANoOp()
    {
        // The failure that made this percentile-based. A capture of dim text with a single specular
        // highlight in it - a glint on a sword, one pixel of a white UI border clipped into the
        // corner - used to pin the range at 0..255 and rescale the text by a factor of one. Real
        // frames nearly always have such a pixel; the synthetic corpus almost never does, which is
        // exactly why this survived so long unnoticed.
        var frame = new FrameBuilder(60, 30, new Rgb(70, 70, 70))
            .Rect(5, 5, 20, 10, new Rgb(110, 110, 110))
            .Rect(59, 29, 1, 1, Rgb.White)
            .Rect(0, 0, 1, 1, Rgb.Black)
            .Build();

        var prepared = OcrPreprocessor.Prepare(frame, new OcrPreprocessOptions
        {
            UpscaleFactor = 1, StretchContrast = true, AutoInvert = false, PadPixels = 0,
        });

        var grey = prepared.ToGreyscale();

        // The text and its background started 40 levels apart out of 255. After a stretch that
        // ignores the outliers they are separated by most of the range.
        var background = grey[10 * 60 + 40];   // outside the text rectangle
        var text = grey[8 * 60 + 10];          // inside it

        Assert.True(text - background > 180,
            $"outliers swallowed the stretch: background {background}, text {text}");
    }

    [Fact]
    public void FlatImageIsLeftAloneRatherThanHavingNoiseAmplified()
    {
        var frame = new FrameBuilder(20, 20, new Rgb(128, 128, 128)).Build();

        var prepared = OcrPreprocessor.Prepare(frame,
            new OcrPreprocessOptions { UpscaleFactor = 1, AutoInvert = false, PadPixels = 0 });

        Assert.All(prepared.ToGreyscale(), g => Assert.InRange(g, 120, 136));
    }
}

public class TesseractTsvParsingTests
{
    private const string Header =
        "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext";

    private static string Row(int block, int par, int line, int word, float conf, string text) =>
        $"5\t1\t{block}\t{par}\t{line}\t{word}\t0\t0\t10\t10\t{conf}\t{text}";

    [Fact]
    public void RegroupsWordsIntoTheirOriginalLines()
    {
        // The line structure has to survive, because DialogueParser uses the first line to find
        // the speaker name.
        var tsv = string.Join('\n', [
            Header,
            Row(1, 1, 1, 1, 96f, "Y'shtola"),
            Row(1, 1, 2, 1, 94f, "Come,"),
            Row(1, 1, 2, 2, 92f, "the"),
            Row(1, 1, 2, 3, 90f, "aether"),
            Row(1, 1, 2, 4, 88f, "stirs."),
        ]);

        var result = TesseractCliEngine.ParseTsv(tsv, minWordConfidence: 40f);

        Assert.Equal("Y'shtola\nCome, the aether stirs.", result.RawText);
        Assert.Equal(5, result.WordCount);
        Assert.Equal(92f, result.Confidence, 1);
    }

    [Fact]
    public void DropsWordsBelowTheConfidenceFloor()
    {
        var tsv = string.Join('\n', [
            Header,
            Row(1, 1, 1, 1, 95f, "Come"),
            Row(1, 1, 1, 2, 12f, "|~"),
            Row(1, 1, 1, 3, 91f, "with"),
        ]);

        var result = TesseractCliEngine.ParseTsv(tsv, minWordConfidence: 40f);

        Assert.Equal("Come with", result.RawText);
        Assert.Equal(2, result.WordCount);
        Assert.Equal(1, result.RejectedWordCount);
    }

    [Fact]
    public void AnIllegibleFrameIsNotReportedAsAnEmptyOne()
    {
        // Both produce empty text, and they call for opposite responses: an empty region has
        // nothing to do, an illegible one is the case a better OCR engine exists for. The reject
        // count is the only number that can tell them apart - confidence cannot, because it is
        // averaged over the words that survived the filter.
        var tsv = string.Join('\n', [
            Header,
            Row(1, 1, 1, 1, 20f, "sm~ared"),
            Row(1, 1, 1, 2, 15f, "t3xt"),
        ]);

        var illegible = TesseractCliEngine.ParseTsv(tsv, minWordConfidence: 40f);

        Assert.True(illegible.IsEmpty);
        Assert.Equal(2, illegible.RejectedWordCount);
        Assert.Equal(0, TesseractCliEngine.ParseTsv(Header, 40f).RejectedWordCount);
    }

    [Fact]
    public void EmptyOutputYieldsEmptyResult()
    {
        Assert.True(TesseractCliEngine.ParseTsv(Header, 40f).IsEmpty);
        Assert.True(TesseractCliEngine.ParseTsv("", 40f).IsEmpty);
    }

    [Fact]
    public void IgnoresBlankTextCells()
    {
        var tsv = string.Join('\n', [Header, Row(1, 1, 1, 1, 95f, "  "), Row(1, 1, 1, 2, 95f, "Come")]);

        Assert.Equal("Come", TesseractCliEngine.ParseTsv(tsv, 40f).RawText);
    }
}

public class OcrWordGeometryTests
{
    private const string Header =
        "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext";

    private static string RowAt(int line, float conf, string text,
        int left, int top, int width, int height) =>
        $"5\t1\t1\t1\t{line}\t1\t{left}\t{top}\t{width}\t{height}\t{conf}\t{text}";

    [Fact]
    public void WordsCarryTheirPositionInReadingOrder()
    {
        var tsv = string.Join('\n', [
            Header,
            RowAt(1, 96f, "Y'shtola", 40, 12, 120, 26),
            RowAt(2, 94f, "Come,", 40, 50, 70, 26),
            RowAt(2, 92f, "the", 118, 50, 44, 26),
        ]);

        var result = TesseractCliEngine.ParseTsv(tsv, minWordConfidence: 40f);

        Assert.Equal(["Y'shtola", "Come,", "the"], result.Words.Select(w => w.Text));
        Assert.Equal(new OcrBox(40, 12, 120, 26), result.Words[0].Box);
        Assert.Equal(160, result.Words[0].Box.Right);
        Assert.Equal(38, result.Words[0].Box.Bottom);
        Assert.All(result.Words, w => Assert.True(w.Accepted));
    }

    [Fact]
    public void BoxesAreMappedBackOutOfTheUpscaledImage()
    {
        // The one that would be silently wrong. OCR runs on a 2x copy, so Tesseract reports a word
        // at (80, 24) sized 240x52 for a word that is at (40, 12) sized 120x26 in the frame. Handed
        // back unmapped, the box is a plausible rectangle pointing below and right of the text.
        var tsv = string.Join('\n', [Header, RowAt(1, 96f, "Y'shtola", 80, 24, 240, 52)]);

        var mapped = TesseractCliEngine.ParseTsv(tsv, 40f, upscaleFactor: 2);
        Assert.Equal(new OcrBox(40, 12, 120, 26), mapped.Words[0].Box);

        // Default 1 leaves the file's own coordinates alone, so a caller parsing raw TSV is not
        // silently given something else.
        var raw = TesseractCliEngine.ParseTsv(tsv, 40f);
        Assert.Equal(new OcrBox(80, 24, 240, 52), raw.Words[0].Box);
    }

    [Fact]
    public void AOnePixelMarkSurvivesTheMappingAsAVisibleBox()
    {
        // A full stop or an apostrophe is a pixel or two wide. Integer division at 2x would take a
        // 1px box to zero, and a zero-width box is not a small rectangle - it is one that every
        // geometric test declines to match, so the mark vanishes from any clustering.
        var tsv = string.Join('\n', [Header, RowAt(1, 90f, "'", 100, 20, 1, 3)]);

        var box = TesseractCliEngine.ParseTsv(tsv, 40f, upscaleFactor: 2).Words[0].Box;

        Assert.False(box.IsEmpty);
        Assert.Equal(1, box.Width);
    }

    [Fact]
    public void RejectedWordsKeepTheirGeometryAndAreMarked()
    {
        var tsv = string.Join('\n', [
            Header,
            RowAt(1, 95f, "Come", 10, 10, 60, 20),
            RowAt(1, 12f, "|~", 300, 200, 8, 18),
        ]);

        var result = TesseractCliEngine.ParseTsv(tsv, minWordConfidence: 40f);

        // Both present, so "is this region any good" can see the noise; only one accepted, so
        // "where is the dialogue" can exclude it rather than proposing a region around a UI border.
        Assert.Equal(2, result.Words.Count);
        Assert.Equal(["Come"], result.AcceptedWords.Select(w => w.Text));
        Assert.Equal(new OcrBox(300, 200, 8, 18), result.Words[1].Box);
        Assert.False(result.Words[1].Accepted);
    }

    [Fact]
    public void TheWordListAndTheScalarCountsAgree()
    {
        // Two representations of the same reading, and nothing but a test keeps them consistent.
        var tsv = string.Join('\n', [
            Header,
            RowAt(1, 95f, "Come", 10, 10, 60, 20),
            RowAt(1, 91f, "with", 80, 10, 60, 20),
            RowAt(1, 12f, "|~", 300, 200, 8, 18),
            RowAt(1, 8f, "..", 320, 200, 8, 18),
        ]);

        var result = TesseractCliEngine.ParseTsv(tsv, minWordConfidence: 40f);

        Assert.Equal(result.WordCount, result.AcceptedWords.Count());
        Assert.Equal(result.RejectedWordCount, result.Words.Count(w => !w.Accepted));
        Assert.Equal(result.RawText, string.Join(' ', result.AcceptedWords.Select(w => w.Text)));
    }

    [Fact]
    public void AnIllegibleFrameStillReportsWhereTheUnreadableWordsWere()
    {
        // Empty text, but the geometry is the evidence that something was there - which is what
        // separates "no dialogue box on screen" from "the region is on the wrong thing".
        var tsv = string.Join('\n', [
            Header,
            RowAt(1, 20f, "sm~ared", 10, 10, 60, 20),
            RowAt(1, 15f, "t3xt", 80, 10, 60, 20),
        ]);

        var result = TesseractCliEngine.ParseTsv(tsv, minWordConfidence: 40f);

        Assert.True(result.IsEmpty);
        Assert.Equal(2, result.Words.Count);
        Assert.Empty(result.AcceptedWords);
    }

    /// <summary>
    /// Runs the real tesseract binary, which CI installs for the test job and a contributor on
    /// macOS gets from <c>brew install tesseract</c>. Without it the body returns early rather than
    /// failing: a missing dev tool is not a broken build. It still runs on every CI push, which is
    /// the run that matters.
    /// </summary>
    [Fact]
    public async Task TheSameFrameReadAtOneXAndTwoXPutsWordsInTheSamePlace()
    {
        if (TesseractCliEngine.Locate() is null) return;

        // The invariant the arithmetic test cannot reach: it checks ParseTsv in isolation, so an
        // engine that simply forgot to pass its upscale factor through would still satisfy it. This
        // goes through RecognizeAsync, where the preprocessor really does double the image. If the
        // mapping is skipped, every 2x box lands at twice the offset - outside an 880x240 frame
        // entirely - while still looking like a perfectly ordinary rectangle.
        //
        // It covers the padding transform too, and for free: the blank margin is added AFTER the
        // upscale, so it is 8 pixels at 1x and 16 at 2x. Subtracting it in the wrong order - after
        // dividing rather than before - leaves the 2x boxes eight pixels adrift of the 1x ones,
        // which is inside no tolerance worth having.
        var frame = SyntheticFrames.Render(
            new SyntheticLine("Y'shtola", "Come, the aether here grows unstable."));

        var atOne = await ReadAsync(frame, upscale: 1);
        var atTwo = await ReadAsync(frame, upscale: 2);

        Assert.NotEmpty(atOne.Words);
        Assert.All(atTwo.Words, w =>
        {
            Assert.InRange(w.Box.Right, 1, frame.Width);
            Assert.InRange(w.Box.Bottom, 1, frame.Height);
        });

        // Same words, same places. Grouped rather than keyed, because one frame legitimately
        // contains the same token twice - the apostrophe in "Y'shtola" is read as its own word, and
        // so is a stray mark - and a dictionary throws on the second one. A few pixels of slack
        // because upscaling genuinely changes what the engine sees at the edges of a glyph; a
        // missing mapping is out by 100%, not by three.
        var one = atOne.Words
            .GroupBy(w => w.Text)
            .ToDictionary(g => g.Key, g => g.Select(w => w.Box).ToList());

        foreach (var word in atTwo.Words.Where(w => one.ContainsKey(w.Text)))
        {
            Assert.Contains(one[word.Text], box =>
                Math.Abs(word.Box.Left - box.Left) <= 4 && Math.Abs(word.Box.Top - box.Top) <= 4);
        }
    }

    private static async Task<OcrResult> ReadAsync(Frame frame, int upscale)
    {
        using var engine = new TesseractCliEngine(new TesseractOptions
        {
            Preprocess = new OcrPreprocessOptions { UpscaleFactor = upscale },
        });

        return await engine.RecognizeAsync(frame, CancellationToken.None);
    }

    [Fact]
    public void AnEngineWithoutGeometryIsStillAValidResult()
    {
        // Geometry is optional per engine; the reject count is not. A vision-model lane returning
        // text and nothing else must remain constructible.
        var plain = new OcrResult("Come with me.", 90f, 3, RejectedWordCount: 1);

        Assert.Empty(plain.Words);
        Assert.Empty(plain.AcceptedWords);
        Assert.Equal(1, plain.RejectedWordCount);
        Assert.Empty(OcrResult.Empty.Words);
    }
}
