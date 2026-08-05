using GamingTranslatorGlassHUD.Core.Capture;

namespace GamingTranslatorGlassHUD.Core.Ocr;

/// <summary>
/// <paramref name="Confidence"/> is the mean per-word confidence, 0-100. Session 3 uses it to
/// decide whether Tesseract is good enough on real frames or whether PaddleOCR-via-ONNX is
/// warranted - which is a decision to make from numbers, not impressions.
/// </summary>
public sealed record OcrResult(string RawText, float Confidence, int WordCount)
{
    public static readonly OcrResult Empty = new(string.Empty, 0, 0);

    public bool IsEmpty => string.IsNullOrWhiteSpace(RawText);
}

public interface IOcrEngine : IDisposable
{
    string Name { get; }

    /// <summary>
    /// How this engine started up, surfaced in Settings. An engine that quietly fell back to a
    /// slower path, or cannot work at all, has to say so somewhere the user will actually look -
    /// otherwise the only symptom is that nothing happens.
    /// </summary>
    string? Diagnostics => null;

    Task<OcrResult> RecognizeAsync(Frame frame, CancellationToken ct);
}
