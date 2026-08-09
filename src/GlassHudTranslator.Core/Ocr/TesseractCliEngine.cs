using System.Diagnostics;
using System.Globalization;
using System.Text;
using GlassHudTranslator.Core.Capture;

namespace GlassHudTranslator.Core.Ocr;

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

    public string? Diagnostics =>
        (_options.ExecutablePath ?? Locate()) is { } path ? $"tesseract binary: {path}" : InstallHint;

    /// <summary>
    /// Finds a tesseract binary. Looks beside the app first, so a copy shipped with the release
    /// wins over whatever happens to be installed.
    ///
    /// <para>
    /// This used to search only Unix paths, which meant that on Windows - where it is the fallback
    /// for the native engine failing - it always came up empty and reported "install it with brew",
    /// on a machine that has no brew. That produced a silent hang rather than a usable error.
    /// </para>
    /// </summary>
    public static string? Locate()
    {
        var exe = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "tesseract", exe),
            Path.Combine(AppContext.BaseDirectory, exe),
        };

        candidates.AddRange(OperatingSystem.IsWindows()
            ? [
                @"C:\Program Files\Tesseract-OCR\tesseract.exe",
                @"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Tesseract-OCR", "tesseract.exe"),
            ]
            : ["/opt/homebrew/bin/tesseract", "/usr/local/bin/tesseract", "/usr/bin/tesseract"]);

        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        // Finally whatever is on PATH.
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry; skip it.
            }
        }

        return null;
    }

    public static string InstallHint => OperatingSystem.IsWindows()
        ? "No Tesseract found. The bundled native engine should normally handle this - if you are "
          + "seeing this, put a tesseract.exe in a 'tesseract' folder next to the app, or install "
          + "Tesseract-OCR from https://github.com/UB-Mannheim/tesseract/wiki."
        : "No Tesseract found. Install it with: brew install tesseract";

    public async Task<OcrResult> RecognizeAsync(Frame frame, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var prepared = OcrPreprocessor.Prepare(frame, _options.Preprocess);
        var input = Path.Combine(Path.GetTempPath(), $"glasshud-ocr-{Guid.NewGuid():N}.png");

        try
        {
            await File.WriteAllBytesAsync(input, prepared.ToPng(), ct).ConfigureAwait(false);
            var tsv = await RunAsync(input, ct).ConfigureAwait(false);
            return ParseTsv(tsv, _options.MinWordConfidence, _options.Preprocess.UpscaleFactor,
                OcrPreprocessor.PaddingFor(_options.Preprocess));
        }
        finally
        {
            try { File.Delete(input); } catch (IOException) { /* temp file; nothing to do */ }
        }
    }

    private async Task<string> RunAsync(string inputPath, CancellationToken ct)
    {
        var exe = _options.ExecutablePath ?? Locate()
            ?? throw new InvalidOperationException(InstallHint);

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
    ///
    /// <para>
    /// <paramref name="upscaleFactor"/> is what the preprocessor multiplied the image by before
    /// the engine saw it, and every reported box is divided back down by it. It defaults to 1 so a
    /// caller parsing raw TSV gets the coordinates that are literally in the file; both real
    /// engines pass their own <see cref="OcrPreprocessOptions.UpscaleFactor"/>, because a box left
    /// in the upscaled space is wrong by a factor of two in a way that still looks like a box.
    /// </para>
    ///
    /// <para>
    /// <paramref name="padPixels"/> is the blank margin the preprocessor added, measured in the
    /// same upscaled space, and it comes off BEFORE the division — the padding was added after the
    /// upscale, so it is not scaled by it. Getting that order wrong shifts every word by half the
    /// margin, which is small enough to look like nothing and large enough to matter to anything
    /// that reasons about where the words are.
    /// </para>
    /// </summary>
    public static OcrResult ParseTsv(
        string tsv, float minWordConfidence, int upscaleFactor = 1, int padPixels = 0)
    {
        var scale = Math.Max(1, upscaleFactor);
        var pad = Math.Max(0, padPixels);
        var lines = new Dictionary<(int Block, int Par, int Line), List<string>>();
        var order = new List<(int Block, int Par, int Line)>();
        var confidences = new List<float>();
        var words = new List<OcrWord>();
        var rejected = 0;

        foreach (var row in tsv.Split('\n'))
        {
            var columns = row.Split('\t');
            if (columns.Length < 12) continue;
            if (!int.TryParse(columns[2], out var block)) continue;   // skips the header row

            var text = columns[11].Trim();
            if (text.Length == 0) continue;

            if (!float.TryParse(columns[10], CultureInfo.InvariantCulture, out var confidence)) continue;

            var accepted = confidence >= minWordConfidence;
            words.Add(new OcrWord(text, BoxOf(columns, scale, pad), confidence, accepted));

            if (!accepted)
            {
                // Counted, not just dropped. The reject count is what tells an empty region apart
                // from an illegible one, and it is the per-region signal a future second OCR engine
                // would be chosen by - mean confidence cannot serve there, because it is averaged
                // over the words that survived this very filter.
                rejected++;
                continue;
            }

            var key = (block, int.Parse(columns[3], CultureInfo.InvariantCulture),
                int.Parse(columns[4], CultureInfo.InvariantCulture));

            if (!lines.TryGetValue(key, out var lineWords))
            {
                lines[key] = lineWords = [];
                order.Add(key);
            }

            lineWords.Add(text);
            confidences.Add(confidence);
        }

        // Not the shared Empty when words were seen and all distrusted: that frame is illegible,
        // not blank, and collapsing the two was hiding exactly the frames a better engine is for.
        if (order.Count == 0)
        {
            return rejected == 0
                ? OcrResult.Empty
                : new OcrResult(string.Empty, 0, 0, rejected) { Words = words };
        }

        var builder = new StringBuilder();
        foreach (var key in order)
        {
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(string.Join(' ', lines[key]));
        }

        return new OcrResult(builder.ToString(), confidences.Average(), confidences.Count, rejected)
        {
            Words = words,
        };
    }

    /// <summary>
    /// Columns 6-9 are left, top, width and height, in the upscaled image the engine read.
    ///
    /// <para>
    /// Width and height floor to 1 rather than 0. Integer division of a 1px mark - a full stop, an
    /// apostrophe in the proper nouns this glossary is full of - at 2x would otherwise produce a
    /// zero-width box, and a zero-width box is not a smaller rectangle, it is a rectangle that
    /// every geometric test quietly declines to match.
    /// </para>
    /// </summary>
    private static OcrBox BoxOf(string[] columns, int scale, int pad)
    {
        if (!int.TryParse(columns[6], CultureInfo.InvariantCulture, out var left) ||
            !int.TryParse(columns[7], CultureInfo.InvariantCulture, out var top) ||
            !int.TryParse(columns[8], CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(columns[9], CultureInfo.InvariantCulture, out var height))
        {
            return default;
        }

        // Padding off first, then the upscale divided out. The margin was added after the image was
        // enlarged, so it lives in enlarged units and is not itself scaled.
        return new OcrBox((left - pad) / scale, (top - pad) / scale,
            Math.Max(1, width / scale), Math.Max(1, height / scale));
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
