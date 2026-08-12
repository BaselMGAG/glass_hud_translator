using GlassHudTranslator.Core.Capture;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The gate that stops auto-watch translating one sentence four times while it types itself out.
///
/// <para>
/// Frames here are the same synthetic dialogue box <see cref="FrameSignatureTests"/> uses, with
/// word count standing in for how much of the line has been revealed - so "settling" in these tests
/// is literally a line appearing a word at a time, which is what FFXIV does.
/// </para>
/// </summary>
public class FrameSettleGateTests
{
    private const int BoxWidth = 800;
    private const int BoxHeight = 300;

    private static FrameSignature Revealed(int words)
    {
        var scene = Rgb.DarkScene;
        var builder = new FrameBuilder(BoxWidth, BoxHeight, scene)
            .Rect(20, 20, BoxWidth - 40, BoxHeight - 40, FrameBuilder.Blend(Rgb.BoxDark, scene, 0.72));

        for (var i = 0; i < words; i++)
            builder.Rect(60 + i % 6 * 120, 70 + i / 6 * 60, 90, 26, Rgb.TextWhite);

        return FrameSignature.Compute(builder.Build());
    }

    [Fact]
    public void ALineThatTypesItselfOutIsTranslatedOnce()
    {
        // The whole point. Five polls across a reveal, then the finished line held still: one
        // Ready, not five. Before the gate this was five OCR passes, five different strings, five
        // cache misses and five API requests to show four progressively less wrong sentences.
        var gate = new FrameSettleGate();

        var verdicts = new[] { 2, 4, 6, 8, 8, 8 }.Select(w => gate.Offer(Revealed(w))).ToList();

        Assert.Equal(
        [
            FrameVerdict.Settling,   // first sight of a change
            FrameVerdict.Settling,   // still growing
            FrameVerdict.Settling,
            FrameVerdict.Settling,
            FrameVerdict.Ready,      // held still for a second poll
            FrameVerdict.Unchanged,  // and is now what the overlay shows
        ], verdicts);
    }

    [Fact]
    public void AStillScreenNeverReachesOcrAgain()
    {
        var gate = new FrameSettleGate();

        gate.Offer(Revealed(8));
        Assert.Equal(FrameVerdict.Ready, gate.Offer(Revealed(8)));

        for (var i = 0; i < 20; i++)
            Assert.Equal(FrameVerdict.Unchanged, gate.Offer(Revealed(8)));
    }

    [Fact]
    public void TheNextLineOfDialogueIsPickedUp()
    {
        var gate = new FrameSettleGate();
        gate.Offer(Revealed(8));
        gate.Offer(Revealed(8));

        Assert.Equal(FrameVerdict.Settling, gate.Offer(Revealed(3)));
        Assert.Equal(FrameVerdict.Ready, gate.Offer(Revealed(3)));
    }

    [Fact]
    public void AScreenThatNeverStopsMovingIsStillTranslatedAtTheCap()
    {
        // A game whose subtitles animate continuously would otherwise settle never and translate
        // never, which is a worse failure than translating one frame mid-change. The cap bounds how
        // long the player waits AND how often the quota is spent.
        var clock = new FakeTimeProvider();
        var gate = new FrameSettleGate(
            new SettleOptions { RequiredStillTicks = 2, Cap = TimeSpan.FromSeconds(3) }, clock);

        Assert.Equal(FrameVerdict.Settling, gate.Offer(Revealed(1)));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(FrameVerdict.Settling, gate.Offer(Revealed(2)));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(FrameVerdict.Settling, gate.Offer(Revealed(3)));

        // Three seconds since the change began, still moving, still nothing shown. Stop waiting -
        // but the cap no longer decides that this frame IS the line, because it cannot: it fires
        // mid-animation by construction. It hands over to the words instead.
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(FrameVerdict.Read, gate.Offer(Revealed(4)));

        // And the guarantee the cap was really making survives, now stated in the unit that can
        // actually carry it. A line whose OCR wobbles by more characters than the repeat budget
        // allows - "Y'shtola" read once with the apostrophe and once without, twice over - is still
        // one line and is still translated, because the second test is proportional rather than
        // absolute.
        var jittery = new[]
        {
            "The Warrior of Light approaches the aetheryte plaza at dusk",
            "The Warrior of Light approaehes the aetheryte plaza at dusk",
        };

        var verdicts = new List<ReadVerdict>();
        foreach (var reading in jittery)
        {
            verdicts.Add(gate.Confirm(reading, wordsSeenButIllegible: false));
            clock.Advance(TimeSpan.FromMilliseconds(500));
            gate.Offer(Revealed(5));
        }

        Assert.Equal(ReadVerdict.KeepReading, verdicts[0]);
        Assert.Equal(ReadVerdict.Translate, verdicts[1]);
    }

    [Fact]
    public void AScreenWhoseWordsNeverAgreeIsDroppedRatherThanTranslated()
    {
        // The other side of the same coin, and a DELIBERATE retreat from what the cap used to
        // promise. It used to translate whatever was on screen when the deadline expired, on the
        // grounds that showing nothing is worse than showing one frame caught mid-change. That
        // reasoning was about pixels never settling - and pixels that never settle turn out to be
        // ordinary, because a dialogue box over a windy field never settles either.
        //
        // What reaches this point now is a region whose TEXT does not read the same way twice half
        // a second apart, and there is no reading of that which is worth a request: it is scenery,
        // or it is changing faster than a person can read. The old behaviour spent a request on
        // each one and cached a confident Arabic sentence about pixels that were never a sentence.
        var clock = new FakeTimeProvider();
        var gate = new FrameSettleGate(
            new SettleOptions { RequiredStillTicks = 2, Cap = TimeSpan.FromSeconds(3) }, clock);

        gate.Offer(Revealed(1));
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(FrameVerdict.Read, gate.Offer(Revealed(2)));

        var verdicts = new List<ReadVerdict>();
        foreach (var reading in new[] { "one", "two", "three", "four", "five" })
        {
            verdicts.Add(gate.Confirm(reading, wordsSeenButIllegible: false));
            clock.Advance(TimeSpan.FromMilliseconds(500));
            gate.Offer(Revealed(3));
        }

        Assert.DoesNotContain(ReadVerdict.Translate, verdicts);
        Assert.Contains(ReadVerdict.Nothing, verdicts);
    }

    [Fact]
    public void TheCapMeasuresTheWholeChangeNotTheLatestFrame()
    {
        // Each new frame restarts the still-count but must NOT restart the clock, or a reveal that
        // keeps producing new frames pushes its own deadline back forever.
        var clock = new FakeTimeProvider();
        var gate = new FrameSettleGate(
            new SettleOptions { RequiredStillTicks = 5, Cap = TimeSpan.FromSeconds(2) }, clock);

        for (var words = 1; words <= 3; words++)
        {
            Assert.Equal(FrameVerdict.Settling, gate.Offer(Revealed(words)));
            clock.Advance(TimeSpan.FromMilliseconds(700));
        }

        // Two seconds of a change that never stopped producing new frames: the deadline arrives on
        // schedule rather than being pushed back by each one. Read rather than Ready because the
        // cap has stopped claiming this frame is the line - see the test above.
        Assert.Equal(FrameVerdict.Read, gate.Offer(Revealed(4)));
    }

    [Fact]
    public void SwitchingItOffAndOnAgainStartsFresh()
    {
        // Otherwise the first frame after a restart is "unchanged" and nothing happens until the
        // player advances the dialogue, which reads as the toggle having done nothing at all.
        var gate = new FrameSettleGate();
        gate.Offer(Revealed(8));
        gate.Offer(Revealed(8));
        Assert.Equal(FrameVerdict.Unchanged, gate.Offer(Revealed(8)));

        gate.Reset();

        Assert.Equal(FrameVerdict.Settling, gate.Offer(Revealed(8)));
        Assert.Equal(FrameVerdict.Ready, gate.Offer(Revealed(8)));
    }

    [Fact]
    public void OneRequiredTickTranslatesOnTheFirstChange()
    {
        // The escape hatch, and a statement of what the default costs: RequiredStillTicks 1 is the
        // pre-v0.5.2 behaviour exactly, so the difference between them is one poll of latency.
        var gate = new FrameSettleGate(new SettleOptions { RequiredStillTicks = 1 });

        Assert.Equal(FrameVerdict.Ready, gate.Offer(Revealed(4)));
        Assert.Equal(FrameVerdict.Unchanged, gate.Offer(Revealed(4)));
        Assert.Equal(FrameVerdict.Ready, gate.Offer(Revealed(9)));
    }
    /// <summary>A word-sized bar added to a finished line, for the near-miss cases below.</summary>
    private static FrameSignature RevealedPlus(int words, int extraWidth, int extraHeight)
    {
        var scene = Rgb.DarkScene;
        var builder = new FrameBuilder(BoxWidth, BoxHeight, scene)
            .Rect(20, 20, BoxWidth - 40, BoxHeight - 40, FrameBuilder.Blend(Rgb.BoxDark, scene, 0.72));

        for (var i = 0; i < words; i++)
            builder.Rect(60 + i % 6 * 120, 70 + i / 6 * 60, 90, 26, Rgb.TextWhite);

        if (extraWidth > 0)
            builder.Rect(60 + words % 6 * 120, 70 + words / 6 * 60, extraWidth, extraHeight, Rgb.TextWhite);

        return FrameSignature.Compute(builder.Build());
    }

    [Fact]
    public void StillnessIsAStricterQuestionThanChange()
    {
        // FrameSignature's six-cell tolerance answers "is this a different line", and is sized to
        // absorb a translucent box drifting over a moving 3D scene. Reused as a stillness test it
        // is far too loose: measured against a rendered dialogue box, six cells is about three to
        // six revealed characters, so a slow reveal - or a typewriter pausing on a full stop across
        // one poll - reads as finished while it is still typing. The line then gets translated
        // twice: once as a fragment, which is cached under its own key forever.
        var settled = Revealed(8);
        var stillGrowing = RevealedPlus(8, extraWidth: 20, extraHeight: 20);

        var cells = settled.DifferenceCount(stillGrowing);
        Assert.InRange(cells, 3, FrameSignature.DefaultChangeThreshold);

        // Under the CHANGE threshold these two are "the same line" - correctly, it is one line.
        Assert.True(stillGrowing.LooksIdenticalTo(settled));

        // Under the STILLNESS threshold they are not the same frame, so the gate keeps waiting.
        var gate = new FrameSettleGate();
        Assert.Equal(FrameVerdict.Settling, gate.Offer(settled));
        Assert.Equal(FrameVerdict.Settling, gate.Offer(stillGrowing));
        Assert.Equal(FrameVerdict.Ready, gate.Offer(stillGrowing));
    }

    [Fact]
    public void TheStillnessToleranceIsConfigurableAndTightByDefault()
    {
        Assert.Equal(2, new SettleOptions().MaxDifferingCells);

        // Loosened back to the change threshold, the near-miss above settles immediately - which is
        // the behaviour this default exists to prevent, kept here so the difference is visible.
        var loose = new FrameSettleGate(
            new SettleOptions { MaxDifferingCells = FrameSignature.DefaultChangeThreshold });

        Assert.Equal(FrameVerdict.Settling, loose.Offer(Revealed(8)));
        Assert.Equal(FrameVerdict.Ready, loose.Offer(RevealedPlus(8, 20, 20)));
    }
}
