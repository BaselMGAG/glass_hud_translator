using GlassHudTranslator.Core.Capture;

namespace GlassHudTranslator.Core.Ocr;

/// <summary>
/// Where something was found, in the coordinate space of the frame that was handed to the engine -
/// NOT the image the engine actually read.
///
/// <para>
/// That distinction is the whole reason this type is documented. OCR runs on a preprocessed copy
/// that is upscaled (2x by default, because Tesseract is markedly better on larger text), so every
/// box the engine reports is in doubled coordinates. An engine returning those unchanged would hand
/// back rectangles at twice the offset and twice the size - which look entirely plausible and point
/// at empty space below and to the right of the actual words. Each engine maps back before
/// returning, because the engine is the only thing that knows what it did to the image.
/// </para>
/// </summary>
public readonly record struct OcrBox(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// One word as the engine read it.
///
/// <para>
/// <paramref name="Accepted"/> is false for a word the confidence filter dropped. They are kept
/// rather than discarded because the two questions that need this data want opposite things: "where
/// is the dialogue on this screen" should cluster only accepted words, or a UI border read as
/// <c>|~</c> at confidence 8 becomes a phantom text region and the proposal is confidently wrong -
/// which is the failure that teaches people to distrust every suggestion after it. "Is this capture
/// region any good" wants the rejects specifically, since a region full of unreadable words is the
/// case worth reporting. Neither can reconstruct the other from a filtered list, and the caller
/// cannot filter by confidence itself without knowing the threshold.
/// </para>
/// </summary>
public sealed record OcrWord(string Text, OcrBox Box, float Confidence, bool Accepted);

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

    /// <summary>
    /// Every word the engine read, accepted or not, in reading order and in frame coordinates.
    ///
    /// <para>
    /// Optional, and empty is a valid answer: geometry is what a future engine may not be able to
    /// produce, while <see cref="RejectedWordCount"/> is a scalar every engine can manage. So the
    /// count stays a stored value rather than being derived from this list - an engine without
    /// geometry can still say how much it threw away. Where both are populated they must agree, and
    /// a test holds Tesseract to that.
    /// </para>
    /// </summary>
    public IReadOnlyList<OcrWord> Words { get; init; } = [];

    /// <summary>The words that made it into <see cref="RawText"/>.</summary>
    public IEnumerable<OcrWord> AcceptedWords => Words.Where(w => w.Accepted);
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
