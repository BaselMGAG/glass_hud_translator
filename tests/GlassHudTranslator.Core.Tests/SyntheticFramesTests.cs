using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Diagnostics;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The synthetic corpus is the only thing OCR and change detection can be measured against until
/// real captures exist, so its properties are worth pinning. Chiefly: it has to be deterministic,
/// or it cannot be a regression benchmark, and it has to be genuinely hard, or a change that makes
/// OCR worse will pass here and fail in the wild.
/// </summary>
public class SyntheticFramesTests
{
    [Fact]
    public void TheCorpusIsDeterministic()
    {
        // A corpus that differs between runs measures the frame generator, not the change.
        foreach (var line in SyntheticFrames.AdversarialCorpus)
        {
            var a = SyntheticFrames.Render(line);
            var b = SyntheticFrames.Render(line);

            Assert.Equal(a.Bgra, b.Bgra);
        }
    }

    [Fact]
    public void ABusySceneIsMeasurablyHarderThanAGradient()
    {
        // Measured as local roughness - the mean absolute difference between horizontally adjacent
        // pixels - rather than as a count of distinct brightnesses. A smooth vertical gradient
        // already spans ~200 luma levels while being locally flat, so counting levels says nothing.
        // What breaks a single global Otsu threshold is variation between NEIGHBOURING pixels, and
        // that is what this measures.
        // Sampled in the margin above the box, where the scene is unobstructed. Inside the box the
        // clutter is damped to ~28% by the translucency, which is realistic and is exactly why the
        // whole-frame difference is modest - so measuring the whole frame would understate it.
        var gradient = Roughness(SyntheticFrames.Render(
            new SyntheticLine("Y'shtola", "Come, the aether stirs.")), sceneMarginOnly: true);
        var busy = Roughness(SyntheticFrames.Render(
            new SyntheticLine("Y'shtola", "Come, the aether stirs.", Scene: SceneDifficulty.Busy)),
            sceneMarginOnly: true);

        Assert.True(busy > gradient * 5, $"busy={busy:F2} gradient={gradient:F2}");
    }

    [Fact]
    public void ChangeDetectionStillSeesTwoIdenticalFramesAsIdentical()
    {
        // The busy scene must not be so noisy that the change detector stops working - that would
        // make every frame look new and turn the corpus into a quota bomb rather than a benchmark.
        var line = new SyntheticLine("Y'shtola", "Come, the aether stirs.", Scene: SceneDifficulty.Busy);

        var first = FrameSignature.Compute(SyntheticFrames.Render(line));
        var second = FrameSignature.Compute(SyntheticFrames.Render(line));

        Assert.True(second.LooksIdenticalTo(first));
    }

    [Fact]
    public void ChangeDetectionStillSeesADifferentLineAsDifferent()
    {
        var a = FrameSignature.Compute(SyntheticFrames.Render(
            new SyntheticLine("Y'shtola", "Come, the aether stirs.", Scene: SceneDifficulty.Busy)));
        var b = FrameSignature.Compute(SyntheticFrames.Render(
            new SyntheticLine("Y'shtola", "The gate is sealed against us.", Scene: SceneDifficulty.Busy)));

        Assert.False(b.LooksIdenticalTo(a));
    }

    // ── typewriter sequences ──────────────────────────────────────────────────────────────

    [Fact]
    public void ATypewriterSequenceRevealsTheLineAndThenSettles()
    {
        var line = new SyntheticLine("Y'shtola", "Come, the aether here grows unstable.");

        var frames = SyntheticFrames.Typewriter(line, steps: 6);

        Assert.Equal(6, frames.Count);

        // Monotonically longer, which is what makes a growing-string detector implementable.
        var lengths = frames.Select(f => f.VisibleBody.Length).ToList();
        Assert.Equal(lengths.OrderBy(n => n), lengths);

        // The first frame is a genuine fragment, not the whole line.
        Assert.True(frames[0].VisibleBody.Length < line.Body.Length);

        // And it ends settled - two identical frames is the signal the reveal has finished, and
        // the condition a stability check waits for before spending a request.
        Assert.Equal(line.Body, frames[^1].VisibleBody);
        Assert.Equal(frames[^2].VisibleBody, frames[^1].VisibleBody);
    }

    [Fact]
    public void TheSettledFramesAreIdenticalToTheChangeDetector()
    {
        // The property the stability reader depends on: once drawing stops, consecutive captures
        // compare equal. If they did not, nothing would ever be judged stable and every line would
        // wait for the full timeout.
        var frames = SyntheticFrames.Typewriter(
            new SyntheticLine("Y'shtola", "Come, the aether here grows unstable."), steps: 5);

        var last = FrameSignature.Compute(SyntheticFrames.Render(frames[^1]));
        var penultimate = FrameSignature.Compute(SyntheticFrames.Render(frames[^2]));

        Assert.True(last.LooksIdenticalTo(penultimate));
    }

    [Fact]
    public void AMidRevealFrameIsNotMistakenForTheSettledOne()
    {
        // If a half-drawn line compared equal to the finished one, the app would translate the
        // fragment - a different cache key, a wasted request, and half a sentence on the overlay.
        var frames = SyntheticFrames.Typewriter(
            new SyntheticLine("Y'shtola", "Come, the aether here grows unstable."), steps: 5);

        var mid = FrameSignature.Compute(SyntheticFrames.Render(frames[0]));
        var settled = FrameSignature.Compute(SyntheticFrames.Render(frames[^1]));

        Assert.False(settled.LooksIdenticalTo(mid));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ADegenerateStepCountStillProducesAUsableSequence(int steps)
    {
        var frames = SyntheticFrames.Typewriter(
            new SyntheticLine(null, "Short line."), steps);

        Assert.Equal(2, frames.Count);
        Assert.All(frames, f => Assert.Equal("Short line.", f.VisibleBody));
    }

    [Fact]
    public void EveryAdversarialFrameRendersAtTheExpectedSize()
    {
        foreach (var line in SyntheticFrames.AdversarialCorpus)
        {
            var frame = SyntheticFrames.Render(line);

            Assert.Equal(SyntheticFrames.Width, frame.Width);
            Assert.Equal(SyntheticFrames.Height, frame.Height);
        }
    }

    /// <summary>
    /// Mean absolute luma difference between horizontally adjacent pixels. High for clutter and
    /// grain, near zero for a smooth gradient however many brightnesses it spans - which is the
    /// distinction that matters to a global threshold.
    /// </summary>
    private static double Roughness(Frame frame, bool sceneMarginOnly = false)
    {
        double total = 0;
        var samples = 0;
        var lastRow = sceneMarginOnly ? 24 : frame.Height;

        for (var y = 0; y < lastRow; y++)
        {
            var row = y * frame.Width * Frame.BytesPerPixel;
            for (var x = 1; x < frame.Width; x++)
            {
                var here = Luma(frame.Bgra, row + x * Frame.BytesPerPixel);
                var left = Luma(frame.Bgra, row + (x - 1) * Frame.BytesPerPixel);

                total += Math.Abs(here - left);
                samples++;
            }
        }

        return samples == 0 ? 0 : total / samples;
    }

    private static int Luma(byte[] bgra, int i) =>
        (bgra[i] * 29 + bgra[i + 1] * 150 + bgra[i + 2] * 77) >> 8;
}
