using GlassHudTranslator.Core.Capture;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The settle gate over a scene that never stops moving, which is every real game.
///
/// <para>
/// <b>These exist because the rest of the corpus is static and that is what hid the defect.</b>
/// Every other frame in <c>test-frames/</c> and in <see cref="FrameSettleGateTests"/> puts the
/// dialogue box over an unchanging background, so consecutive polls of a finished line differ by
/// exactly zero cells and the gate settles instantly. On a real screen the box sits over foliage,
/// weather, an idling character — and the capture region is drawn by hand, so it always includes
/// some of that scene. Measured here: with the TEXT completely unchanged, mild motion moves 4 cells
/// of 1536 between consecutive polls, and heavier motion moves 50.
/// </para>
///
/// <para>
/// Reported from real use as "auto translate does not switch to the next sentence", and the trace
/// showed why: every release came from the three-second cap rather than from stillness, so every
/// frame OCR'd was caught mid-animation and read as fragments.
/// </para>
/// </summary>
public class MovingSceneSettleTests
{
    private const int W = 800, H = 300;

    /// <summary>
    /// A dialogue box over a scene, with <paramref name="patches"/> of foliage drifting per tick.
    /// The box covers the middle band only — a hand-drawn capture region always catches some scene,
    /// which is exactly what the user's own OCR showed when it read junk beside the dialogue.
    /// </summary>
    private static FrameSignature Scene(int words, int tick, int patches)
    {
        var scene = Rgb.DarkScene;
        var b = new FrameBuilder(W, H, scene);

        b.Rect(20, 70, W - 40, H - 150, FrameBuilder.Blend(Rgb.BoxDark, scene, 0.72));

        for (var i = 0; i < patches; i++)
        {
            var x = (i * 97 + tick * 13) % (W - 40);
            var y = i % 2 == 0 ? 24 + (i * 7 + tick * 3) % 34 : H - 70 + (i * 5 + tick * 3) % 44;
            b.Rect(x, y, 16, 16, FrameBuilder.Blend(Rgb.TextWhite, scene, 0.55));
        }

        for (var i = 0; i < words; i++)
            b.Rect(60 + i % 6 * 120, 100 + i / 6 * 50, 90, 22, Rgb.TextWhite);

        return FrameSignature.Compute(b.Build());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(30)]
    public void BackgroundMotionAloneMovesMoreCellsThanTheStillnessToleranceAllows(int patches)
    {
        // The measurement the fix has to answer. The text does not change at all between these two
        // frames; only the scene behind and around it does.
        var moved = Scene(8, 2, patches).DifferenceCount(Scene(8, 1, patches));

        Assert.True(moved > 2,
            $"with {patches} patches the scene moved {moved} cells - if this is <= 2 the test frame " +
            "is not modelling motion and the test below proves nothing");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    public void AFinishedLineOverAMovingSceneIsTreatedAsFinished(int patches)
    {
        // THE regression. The line is complete and unchanging from tick 2 onwards; only the scene
        // moves. The gate must conclude the text has stopped, and it must do so from STILLNESS
        // rather than by running out of patience - because a cap-forced release lands mid-animation
        // and reads as fragments, which is what the user saw.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new FrameSettleGate(clock: clock);

        // A line already on screen, sitting there. This is what production looks like and what the
        // real trace showed - long runs of the same line while the player reads it - and it is how
        // the gate gets to measure the scene. A test that starts cold measures the four seconds
        // before any measurement exists, which is a different thing and is asserted separately
        // below.
        var tick = 0;
        for (var i = 0; i < 12; i++)
        {
            gate.Offer(Scene(4, ++tick, patches));
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        // Now the player advances it: a new line appears and then holds still.
        var verdicts = new List<FrameVerdict>();
        var since = clock.GetUtcNow();

        foreach (var words in new[] { 6, 8, 8, 8, 8, 8 })
        {
            verdicts.Add(gate.Offer(Scene(words, ++tick, patches)));
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        Assert.Contains(FrameVerdict.Ready, verdicts);

        // And from STILLNESS rather than from running out of patience. The cap is three seconds and
        // these polls are half a second apart, so a Ready among the first five is the gate deciding
        // the text has stopped - which is the whole point, because a cap-forced release lands mid
        // animation and reads as fragments.
        Assert.Contains(FrameVerdict.Ready, verdicts.Take(5));
        Assert.True(gate.SceneMovement > 0 || patches == 0,
            "the gate never measured the scene, so it cannot have adapted to it");
    }

    [Fact]
    public void BeforeTheSceneHasBeenMeasuredTheGateStaysStrict()
    {
        // The cold start, stated rather than discovered. For the first few seconds of a run there
        // is no measurement, so the gate uses the tolerance that is right for a still image - which
        // over a moving scene means it will not settle and the cap releases the frame instead. That
        // costs at most the first line of a session, and the alternative is worse: trusting one or
        // two samples means trusting a number that may BE the change, which opens the tolerance
        // wide enough to swallow a whole word.
        var gate = new FrameSettleGate();

        gate.Offer(Scene(8, 1, patches: 12));
        gate.Offer(Scene(8, 2, patches: 12));

        Assert.Equal(0, gate.SceneMovement);
    }

    [Fact]
    public void ATypewriterRevealOverAMovingSceneIsStillTranslatedOnce()
    {
        // The other half, and the reason the tolerance cannot simply be raised without thought:
        // whatever makes a moving scene settle must NOT make a line that is still being typed look
        // finished. That is the defect the gate was built for in the first place.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new FrameSettleGate(clock: clock);

        var readies = 0;
        foreach (var (words, tick) in new[] { (1, 1), (3, 2), (5, 3), (7, 4), (8, 5), (8, 6), (8, 7) })
        {
            if (gate.Offer(Scene(words, tick, patches: 4)) == FrameVerdict.Ready) readies++;
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        Assert.Equal(1, readies);
    }
}
