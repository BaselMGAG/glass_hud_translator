using System.Diagnostics;
using System.Globalization;
using System.Text;
using GamingTranslatorGlassHUD.Core.Capture;

namespace GamingTranslatorGlassHUD.Core.Ocr;

/// <summary>
/// Shells out to the tesseract binary. This is the development engine on macOS
/// (<c>brew install tesseract</c>); the shipped Windows build uses the nuget natives behind the
/// same interface so the user installs nothing (brief 10).
///
/// <para>
/// Requests TSV rather than plain text. It costs nothing extra and yields per-word confidence,
/// which is the number Session 3 needs to decide whether Tesseract is accurate enough on real
/// frames - a decision that should not rest on impressions.
/// </para>
/// </summary>
public sealed class TesseractCliEngine(TesseractOptions? options = null) : IOcrEngine
{
    private readonly TesseractOptions _options = options ?? new TesseractOptions();

    public string Name => "tesseract-cli";

    public static string? Locate()
    {
        foreach (var candidate in new[]
                 {
                     "/opt/homebrew/bin/tesseract", "/usr/local/bin/tesseract", "/usr/bin/tesseract",
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    public async Task<OcrResult> RecognizeAsync(Frame frame, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var prepared = OcrPreprocessor.Prepare(frame, _options.Preprocess);
        var input = Path.Combine(Path.GetTempPath(), $"glasshud-ocr-{Guid.NewGuid():N}.png");

        try
        {
            await File.WriteAllBytesAsync(input, prepared.ToPng(), ct).ConfigureAwait(false);
            var tsv = await RunAsync(input, ct).ConfigureAwait(false);
            return ParseTsv(tsv, _options.MinWordConfidence);
        }
        finally
        {
            try { File.Delete(input); } catch (IOException) { /* temp file; nothing to do */ }
        }
    }

    private async Task<string> RunAsync(string inputPath, CancellationToken ct)
    {
        var exe = _options.ExecutablePath ?? Locate()
            ?? throw new InvalidOperationException(
                "tesseract was not found. Install it with 'brew install tesseract', or set " +
                "TesseractOptions.ExecutablePath.");

        var start = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(inputPath);
        start.ArgumentList.Add("stdout");
        start.ArgumentList.Add("--psm");
        start.ArgumentList.Add(_options.PageSegmentationMode.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("-l");
        start.ArgumentList.Add(_options.Language);
        if (_options.CharacterWhitelist is { Length: > 0 } whitelist)
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add($"tessedit_char_whitelist={whitelist}");
        }

        start.ArgumentList.Add("tsv");

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {exe}.");

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"tesseract exited {process.ExitCode}: {await stderr.ConfigureAwait(false)}");

        return await stdout.ConfigureAwait(false);
    }

    /// <summary>
    /// TSV columns: level page block par line word left top width height conf text.
    /// Words are regrouped into their original lines so the speaker name stays on its own line for
    /// <see cref="Text.DialogueParser"/>.
    /// </summary>
    internal static OcrResult ParseTsv(string tsv, float minWordConfidence)
    {
        var lines = new Dictionary<(int Block, int Par, int Line), List<string>>();
        var order = new List<(int Block, int Par, int Line)>();
        var confidences = new List<float>();

        foreach (var row in tsv.Split('\n'))
        {
            var columns = row.Split('\t');
            if (columns.Length < 12) continue;
            if (!int.TryParse(columns[2], out var block)) continue;   // skips the header row

            var text = columns[11].Trim();
            if (text.Length == 0) continue;

            if (!float.TryParse(columns[10], CultureInfo.InvariantCulture, out var confidence)) continue;
            if (confidence < minWordConfidence) continue;

            var key = (block, int.Parse(columns[3], CultureInfo.InvariantCulture),
                int.Parse(columns[4], CultureInfo.InvariantCulture));

            if (!lines.TryGetValue(key, out var words))
            {
                lines[key] = words = [];
                order.Add(key);
            }

            words.Add(text);
            confidences.Add(confidence);
        }

        if (order.Count == 0) return OcrResult.Empty;

        var builder = new StringBuilder();
        foreach (var key in order)
        {
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(string.Join(' ', lines[key]));
        }

        return new OcrResult(builder.ToString(), confidences.Average(), confidences.Count);
    }

    public void Dispose() { }
}

public sealed record TesseractOptions
{
    public string? ExecutablePath { get; init; }

    public string Language { get; init; } = "eng";

    /// <summary>6 = assume a uniform block of text, which is what a dialogue box is.</summary>
    public int PageSegmentationMode { get; init; } = 6;

    /// <summary>
    /// Drops words Tesseract itself has no confidence in, rather than translating noise.
    ///
    /// <para>
    /// Deliberately low. An earlier value of 40 silently deleted "linkpearl" from a test frame,
    /// which Tesseract had read perfectly at confidence 39.2 - it scores unusual proper nouns down,
    /// and unusual proper nouns are exactly this game's vocabulary. A dropped word is the worse
    /// failure in both directions that matter: the sentence loses its meaning with no visible sign,
    /// and the line hashes differently from the same line read correctly, so it is paid for twice.
    /// An uncertain word that survives is merely a slightly wrong translation the reader can see.
    /// </para>
    /// </summary>
    public float MinWordConfidence { get; init; } = 25f;

    /// <summary>
    /// Null by default. A whitelist helps on constrained text but FFXIV uses apostrophes, accents
    /// and em dashes, and an over-tight list silently mangles exactly the proper nouns that matter
    /// most. Session 3 decides from the corpus.
    /// </summary>
    public string? CharacterWhitelist { get; init; }

    public OcrPreprocessOptions Preprocess { get; init; } = new();
}
