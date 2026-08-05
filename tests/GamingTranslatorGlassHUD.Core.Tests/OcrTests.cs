using GamingTranslatorGlassHUD.Core.Capture;
using GamingTranslatorGlassHUD.Core.Ocr;
using GamingTranslatorGlassHUD.Core.Text;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GamingTranslatorGlassHUD.Core.Tests;

public class OcrPreprocessorTests
{
    [Fact]
    public void UpscalesByTheConfiguredFactor()
    {
        var frame = new FrameBuilder(100, 40, Rgb.BoxDark).Build();

        var prepared = OcrPreprocessor.Prepare(frame, new OcrPreprocessOptions { UpscaleFactor = 2 });

        Assert.Equal(200, prepared.Width);
        Assert.Equal(80, prepared.Height);
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

        var prepared = OcrPreprocessor.Prepare(frame,
            new OcrPreprocessOptions { UpscaleFactor = 1, StretchContrast = true, AutoInvert = false });

        var grey = prepared.ToGreyscale();
        Assert.Equal(0, grey.Min());
        Assert.Equal(255, grey.Max());
    }

    [Fact]
    public void FlatImageIsLeftAloneRatherThanHavingNoiseAmplified()
    {
        var frame = new FrameBuilder(20, 20, new Rgb(128, 128, 128)).Build();

        var prepared = OcrPreprocessor.Prepare(frame,
            new OcrPreprocessOptions { UpscaleFactor = 1, AutoInvert = false });

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

/// <summary>Replays a fixed sequence of OCR outputs, imitating the typewriter reveal.</summary>
internal sealed class ScriptedOcrEngine(params string[] reads) : IOcrEngine
{
    private int _index;

    public string Name => "scripted";

    public int Calls { get; private set; }

    public Task<OcrResult> RecognizeAsync(Frame frame, CancellationToken ct)
    {
        Calls++;
        var text = reads[Math.Min(_index, reads.Length - 1)];
        _index++;
        return Task.FromResult(new OcrResult(text, 90f, text.Split(' ').Length));
    }

    public void Dispose() { }
}

public class StableOcrReaderTests
{
    private static Task<Frame?> Grab(CancellationToken ct) =>
        Task.FromResult<Frame?>(new FrameBuilder(40, 20, Rgb.BoxDark).Build());

    private static StableOcrOptions Instant => new() { PollInterval = TimeSpan.Zero };

    [Fact]
    public async Task WaitsForTheRevealToFinishBeforeSettling()
    {
        // Three intermediate states, then the finished line twice. Only the finished line should
        // come out - each intermediate state translated would be a wasted request and a wrong answer.
        var ocr = new ScriptedOcrEngine(
            "Come, the",
            "Come, the aeth",
            "Come, the aether stirs.",
            "Come, the aether stirs.");
        var reader = new StableOcrReader(ocr, options: Instant);

        var read = await reader.ReadAsync(Grab, CancellationToken.None);

        Assert.Equal("Come, the aether stirs.", read.Text);
        Assert.True(read.Stabilised);
        Assert.Equal(4, read.Attempts);
    }

    [Fact]
    public async Task SettlesImmediatelyWhenTheLineIsAlreadyComplete()
    {
        var ocr = new ScriptedOcrEngine("Come with me.", "Come with me.");
        var reader = new StableOcrReader(ocr, options: Instant);

        var read = await reader.ReadAsync(Grab, CancellationToken.None);

        Assert.Equal(2, read.Attempts);
        Assert.True(read.Stabilised);
    }

    [Fact]
    public async Task GivesUpAtTheCapRatherThanWaitingForever()
    {
        // Text that never settles - an animated background or a flickering capture. Returning
        // something beats hanging the overlay.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var ocr = new ScriptedOcrEngine("a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l");
        var reader = new StableOcrReader(ocr,
            options: new StableOcrOptions { PollInterval = TimeSpan.FromMilliseconds(150), Cap = TimeSpan.FromMilliseconds(450) },
            clock: clock);

        var task = reader.ReadAsync(Grab, CancellationToken.None);
        for (var i = 0; i < 10 && !task.IsCompleted; i++)
            clock.Advance(TimeSpan.FromMilliseconds(150));

        var read = await task;

        Assert.False(read.Stabilised);
        Assert.NotEqual(string.Empty, read.Text);
    }

    [Fact]
    public async Task AppliesNormalisationAndCorrectionsToEachRead()
    {
        var corrections = new OcrCorrections(new Dictionary<string, string> { ["Y shtola"] = "Y'shtola" });
        var ocr = new ScriptedOcrEngine("Y shtola  nods. ▼", "Y shtola  nods. ▼");
        var reader = new StableOcrReader(ocr, corrections, Instant);

        var read = await reader.ReadAsync(Grab, CancellationToken.None);

        Assert.Equal("Y'shtola nods.", read.Text);
    }

    [Fact]
    public async Task NoFrameAvailableReturnsWhatWasSeenSoFar()
    {
        var reader = new StableOcrReader(new ScriptedOcrEngine("x"), options: Instant);

        var read = await reader.ReadAsync(_ => Task.FromResult<Frame?>(null), CancellationToken.None);

        Assert.False(read.Stabilised);
        Assert.Equal(string.Empty, read.Text);
    }
}
