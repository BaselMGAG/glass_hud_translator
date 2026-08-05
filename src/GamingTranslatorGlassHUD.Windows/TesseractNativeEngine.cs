using System.Runtime.Versioning;
using GamingTranslatorGlassHUD.Core.Capture;
using GamingTranslatorGlassHUD.Core.Ocr;
using TesseractOCR;
using TesseractOCR.Enums;

namespace GamingTranslatorGlassHUD.Windows;

/// <summary>
/// Tesseract through the bundled native libraries, so the user installs nothing.
///
/// <para>
/// This is what preserves the delivery model: send a zip, double-click the exe, done. Asking
/// someone to install Tesseract separately would be an entire support conversation, and a folder
/// of loose native binaries is exactly the shape antivirus dislikes.
/// </para>
///
/// <para>
/// If the native libraries fail to load - a single-file extraction problem, a missing VC++ runtime,
/// an antivirus quarantine - this falls back to shelling out to a <c>tesseract.exe</c> shipped
/// alongside the app. Slower per call, but the difference between degraded and broken.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TesseractNativeEngine : IOcrEngine
{
    private readonly TesseractOptions _options;
    private readonly Lock _gate = new();

    private Engine? _engine;
    private IOcrEngine? _fallback;
    private bool _disposed;

    public TesseractNativeEngine(TesseractOptions? options = null)
    {
        _options = options ?? new TesseractOptions();
        TryInitialiseNative();
    }

    public string Name => _engine is not null ? "tesseract-native" : "tesseract-cli-fallback";

    /// <summary>Non-null when the native path failed and the exe fallback is carrying the load.</summary>
    public string? InitialisationWarning { get; private set; }

    private void TryInitialiseNative()
    {
        try
        {
            var dataPath = ResolveTessdata();
            _engine = new Engine(dataPath, ToLanguage(_options.Language), EngineMode.Default)
            {
                DefaultPageSegMode = ToPageSegMode(_options.PageSegmentationMode),
            };

            if (_options.CharacterWhitelist is { Length: > 0 } whitelist)
                _engine.SetVariable("tessedit_char_whitelist", whitelist);
        }
        catch (Exception e)
        {
            InitialisationWarning =
                $"Native Tesseract failed to start ({e.GetType().Name}: {e.Message}). " +
                "Falling back to tesseract.exe. OCR will be slower but still works.";

            _engine = null;
            _fallback = new TesseractCliEngine(_options with { ExecutablePath = ResolveBundledExe() });
        }
    }

    public async Task<OcrResult> RecognizeAsync(Frame frame, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_engine is null)
            return await _fallback!.RecognizeAsync(frame, ct).ConfigureAwait(false);

        var prepared = OcrPreprocessor.Prepare(frame, _options.Preprocess);
        var png = prepared.ToPng();

        return await Task.Run(() =>
        {
            lock (_gate)
            {
                // LoadFromMemory keeps this off the filesystem entirely, unlike the CLI path which
                // has to write a temp PNG for every single capture.
                using var image = TesseractOCR.Pix.Image.LoadFromMemory(png);
                using var page = _engine.Process(image);

                // Same TSV parsing as the CLI engine, so both produce identical output for the same
                // input - which is what makes macOS development a faithful rehearsal.
                return TesseractCliEngine.ParseTsv(page.TsvText, _options.MinWordConfidence);
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>tessdata ships next to the exe; eng.traineddata alone is about 4 MB.</summary>
    private static string ResolveTessdata()
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "tessdata"),
                     Path.Combine(Directory.GetCurrentDirectory(), "tessdata"),
                 })
        {
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException(
            $"No tessdata folder found next to {AppContext.BaseDirectory}. It must contain eng.traineddata.");
    }

    private static string? ResolveBundledExe()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "tesseract", "tesseract.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static Language ToLanguage(string code) => code.ToLowerInvariant() switch
    {
        "eng" => Language.English,
        "ara" => Language.Arabic,
        "deu" or "ger" => Language.German,
        "fra" or "fre" => Language.French,
        "spa" => Language.SpanishCastilian,
        "jpn" => Language.Japanese,
        "kor" => Language.Korean,
        "chi_sim" => Language.ChineseSimplified,
        "chi_tra" => Language.ChineseTraditional,
        "rus" => Language.Russian,
        "ita" => Language.Italian,
        "por" => Language.Portuguese,
        _ => Language.English,
    };

    private static PageSegMode ToPageSegMode(int psm) => psm switch
    {
        3 => PageSegMode.Auto,
        4 => PageSegMode.SingleColumn,
        6 => PageSegMode.SingleBlock,   // a dialogue box is a uniform block of text
        7 => PageSegMode.SingleLine,
        8 => PageSegMode.SingleWord,
        11 => PageSegMode.SparseText,
        12 => PageSegMode.SparseTextOsd,
        13 => PageSegMode.RawLine,
        _ => PageSegMode.SingleBlock,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _engine?.Dispose();
        _fallback?.Dispose();
    }
}
