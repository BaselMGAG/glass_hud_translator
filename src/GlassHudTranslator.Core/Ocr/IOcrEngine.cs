using GlassHudTranslator.Core.Capture;

namespace GlassHudTranslator.Core.Ocr;

/// <summary>
/// <paramref name="Confidence"/> is the mean per-word confidence, 0-100. Session 3 uses it to
/// decide whether Tesseract is good enough on real frames or whether PaddleOCR-via-ONNX is
/// warranted - which is a decision to make from numbers, not impressions.
///
/// <para>
/// <paramref name="RejectedWordCount"/> is how many words the engine read but the confidence
/// filter dropped. It is the number that distinguishes an empty region (nothing there, nothing
/// rejected) from an illegible one (words seen, none trusted) - two situations that both produce
/// empty text and call for opposite responses. Confidence cannot make that distinction, because it
/// is a mean over the *surviving* words only: a frame where nine words of ten were dropped can
/// still report a serene 90.
/// </para>
/// </summary>
public sealed record OcrResult(string RawText, float Confidence, int WordCount, int RejectedWordCount = 0)
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
