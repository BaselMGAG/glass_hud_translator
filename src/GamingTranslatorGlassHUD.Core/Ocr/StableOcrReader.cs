using GamingTranslatorGlassHUD.Core.Capture;
using GamingTranslatorGlassHUD.Core.Text;

namespace GamingTranslatorGlassHUD.Core.Ocr;

public sealed record StableOcrOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Give up waiting after this and use whatever is on screen.</summary>
    public TimeSpan Cap { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Consecutive identical reads required before the line counts as finished.</summary>
    public int RequiredRepeats { get; init; } = 2;
}

public sealed record StableOcrRead(string Text, float Confidence, int Attempts, bool Stabilised);

/// <summary>
/// The typewriter fix (brief 7).
///
/// <para>
/// FFXIV reveals dialogue character by character, so a hotkey pressed mid-reveal captures a
/// truncated line. This re-OCRs every 150 ms until two consecutive reads agree, then hands over
/// exactly one line to translate. The asymmetry is the point: extra OCR passes are free, extra API
/// calls are not - translating each intermediate state would burn several requests to produce
/// several wrong answers.
/// </para>
/// </summary>
public sealed class StableOcrReader(
    IOcrEngine ocr,
    OcrCorrections? corrections = null,
    StableOcrOptions? options = null,
    TimeProvider? clock = null)
{
    private readonly StableOcrOptions _options = options ?? new StableOcrOptions();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<StableOcrRead> ReadAsync(
        Func<CancellationToken, Task<Frame?>> grab, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(grab);

        var deadline = _clock.GetUtcNow() + _options.Cap;
        string? previous = null;
        var repeats = 1;
        var attempts = 0;
        var confidence = 0f;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var frame = await grab(ct).ConfigureAwait(false);
            if (frame is null)
                return new StableOcrRead(previous ?? string.Empty, confidence, attempts, false);

            var result = await ocr.RecognizeAsync(frame, ct).ConfigureAwait(false);
            var normalized = TextNormalizer.Normalize(result.RawText, corrections);
            attempts++;
            confidence = result.Confidence;

            if (normalized == previous)
            {
                repeats++;
                if (repeats >= _options.RequiredRepeats)
                    return new StableOcrRead(normalized, confidence, attempts, true);
            }
            else
            {
                previous = normalized;
                repeats = 1;
            }

            if (_clock.GetUtcNow() >= deadline)
                return new StableOcrRead(normalized, confidence, attempts, false);

            await Task.Delay(_options.PollInterval, _clock, ct).ConfigureAwait(false);
        }
    }
}
