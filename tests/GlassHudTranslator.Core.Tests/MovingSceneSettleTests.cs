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
    /// Every test in here advances the clock by 500 ms per poll, so it is describing two polls a
    /// second and the gate has to be told that. The scene-motion window is an amount of TIME now,
    /// and a queue of samples can only work out how much time it holds from how far apart they are —
    /// which is the whole point, and would be untested if these silently used the product's rate
    /// while ticking at half of it.
    /// </summary>
    private static readonly SettleOptions TwoPolls = new() { PollsPerSecond = 2 };

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

    /// <summary>What the words on the frame read as. A finished line reads the same every time.</summary>
    private static string Words(int words) =>
        string.Join(" ", Enumerable.Range(0, words).Select(i => $"word{i}"));

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(30)]
    public void AFinishedLineOverAMovingSceneIsTranslatedOnceAndFromItsWords(int patches)
    {
        // THE regression, and the shape of the fix is in what it asserts. The line is complete and
        // unchanging from tick 2 onwards; only the scene moves. The gate is NOT asked to conclude
        // that from the pixels - it cannot, at any threshold, because one more revealed word and a
        // few more moving leaves cost the same handful of cells. It is asked to stop guessing and
        // read, and to release when the words agree with themselves.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new FrameSettleGate(TwoPolls, clock);

        // A line already on screen, sitting there while the player reads it. This is what the real
        // trace showed, and it is how the gate gets to measure the scene at all.
        var tick = 0;
        for (var i = 0; i < 12; i++)
        {
            gate.Offer(Scene(4, ++tick, patches));
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        // Now the player advances it: a new line appears and then holds still.
        var translations = 0;
        var reads = 0;

        foreach (var words in new[] { 6, 8, 8, 8, 8, 8, 8, 8 })
        {
            if (gate.Offer(Scene(words, ++tick, patches)) == FrameVerdict.Read)
            {
                reads++;
                if (gate.Confirm(Words(words), wordsSeenButIllegible: false) == ReadVerdict.Translate)
                    translations++;
            }

            clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        Assert.Equal(1, translations);

        // Two readings half a second apart is the release. More than four would mean the pixels
        // were still being waited on, which is the defect.
        Assert.InRange(reads, 2, 4);

        Assert.True(gate.SceneMovement > 0,
            "the gate never measured the scene, so nothing here proves it adapted to one");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void TheSceneIsMeasuredOverTheSameAmOUNTOfTimeAtAnyPollRate(double pollsPerSecond)
    {
        // <b>The defect this pins has now been found three times, twice in one commit.</b> The
        // scene-motion window was counted in POLLS, so raising the dialogue rate from two a second
        // to four silently halved it - and that is not a harmless loss of precision, because the
        // floor is a MINIMUM. A shorter window has fewer chances to contain a still moment, so the
        // measured floor comes out HIGHER, the stillness tolerance widens, and the "is this the line
        // already on screen" test widens with it. Wide enough and a genuinely new line reads as the
        // old one: translate once, then report nothing changed for as long as it is left running.
        //
        // Asserted as an equivalence between two rates rather than against a number, because a
        // number is the thing that went stale.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        // A cap long enough that the gate never reaches for the reading path, because movement is
        // deliberately not sampled during a reading stretch - the pixels have already been asked
        // and are not being consulted again. That is correct and it is not what is under test here.
        var gate = new FrameSettleGate(
            new SettleOptions { PollsPerSecond = pollsPerSecond, Cap = TimeSpan.FromMinutes(1) },
            clock);

        var step = TimeSpan.FromSeconds(1 / pollsPerSecond);

        // Six seconds of a still line over moving scenery, whatever the rate.
        var tick = 0;
        for (var elapsed = TimeSpan.Zero; elapsed < TimeSpan.FromSeconds(6); elapsed += step)
        {
            gate.Offer(Scene(8, ++tick, patches: 12));
            clock.Advance(step);
        }

        // Six seconds is past the warm-up at either rate, so the scene has been measured...
        Assert.True(gate.SceneMovement > 0,
            $"at {pollsPerSecond} polls a second the gate had still not measured the scene after "
            + "six seconds of watching it");

        // ...and it measured the SAME scene, so it must have arrived at a comparable answer. A
        // window that shortens with the poll rate fails this by reading high.
        Assert.InRange(gate.SceneMovement, 8, 22);
    }

    [Fact]
    public void ALineTheGateWronglyThinksIsUnchangedIsCaughtWithinFifteenSeconds()
    {
        // <b>The watchdog, and the failure it exists for is the only one this gate cannot recover
        // from on its own.</b> Every other mistake here self-corrects within a few seconds because
        // the next frame gets another opinion. Unchanged does not: it is the verdict that skips the
        // reading, so if it is ever wrong in that direction the app translates one line and then
        // reports nothing changed for as long as it is left running. Reported exactly that way.
        //
        // Modelled by handing the gate the SAME frame forever after it has committed one - which is
        // what a wrongly-wide comparison looks like from inside - and requiring that it stops
        // believing itself and goes to the words.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new FrameSettleGate(TwoPolls, clock);

        var frame = Scene(8, 1, patches: 0);
        gate.Offer(frame);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(FrameVerdict.Ready, gate.Offer(frame));

        // The poll loop, faithfully: a Read is always answered with what the frame said. Leaving
        // that out is what a first version of this test did, and it stuck the gate in a reading
        // stretch and then blamed the watchdog for the readings.
        var reads = 0;
        var verdicts = new List<FrameVerdict>();

        for (var i = 0; i < 40; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(500));

            var verdict = gate.Offer(frame);
            verdicts.Add(verdict);

            if (verdict != FrameVerdict.Read) continue;

            reads++;
            gate.Confirm("The line that has been on screen this whole time", wordsSeenButIllegible: false);
        }

        Assert.Contains(FrameVerdict.Read, verdicts);

        // Within fifteen seconds, which is thirty polls at this rate.
        Assert.Contains(FrameVerdict.Read, verdicts.Take(31));

        // And it is a check rather than a new habit. Twenty seconds is one window and a bit, and a
        // window costs the two readings it takes for the words to agree with themselves - so four
        // is the ceiling, not one per poll. A player sitting still reading a long line must not be
        // paying Tesseract twice a second.
        Assert.InRange(reads, 1, 4);
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
        var gate = new FrameSettleGate(TwoPolls);

        gate.Offer(Scene(8, 1, patches: 12));
        gate.Offer(Scene(8, 2, patches: 12));

        Assert.Equal(0, gate.SceneMovement);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(30)]
    public void ATypewriterRevealOverAMovingSceneIsStillTranslatedOnce(int patches)
    {
        // The other half, and the reason no pixel tolerance can be raised to fix the first one:
        // whatever lets a moving scene through must NOT let a line that is still being typed look
        // finished. That is the defect this gate was built for.
        //
        // Two consecutive readings is what separates them, and it does so for a reason that holds
        // at every motion level rather than at the ones that happen to be in this corpus: a reveal
        // is a GROWING PREFIX, so no two consecutive readings of one are ever the same string,
        // however still or restless the leaves behind it are.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new FrameSettleGate(TwoPolls, clock);

        var translations = 0;
        var translatedAt = 0;
        var tick = 0;

        foreach (var words in new[] { 1, 3, 5, 7, 8, 8, 8, 8, 8 })
        {
            var verdict = gate.Offer(Scene(words, ++tick, patches));

            if (verdict == FrameVerdict.Read
                && gate.Confirm(Words(words), wordsSeenButIllegible: false) == ReadVerdict.Translate)
            {
                translations++;
                translatedAt = words;
            }
            else if (verdict == FrameVerdict.Ready)
            {
                translations++;
                translatedAt = words;
            }

            clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        Assert.Equal(1, translations);

        // And the once has to be the FINISHED line. Translating a half-typed one exactly once is
        // the original defect wearing the fix's clothes.
        Assert.Equal(8, translatedAt);
    }

    [Fact]
    public void AGarbleThatDiffersEveryTimeIsNeverTranslatedNoMatterHowLongItPersists()
    {
        // The frames the cap used to release, and the reason the give-in refuses an illegible
        // reading however many times it is taken. A garbled capture produces a DIFFERENT garble
        // every time - that is this project's own documented rule, and it is what makes "twice the
        // same" a filter rather than a delay. Under the old gate every one of these was translated:
        // a request spent, a cache row written that can never be hit, and a confident Arabic
        // sentence about pixels that were never a sentence.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new FrameSettleGate(TwoPolls, clock);

        var garbles = new[]
        {
            "an gp - ESS BF OE Ri, SI iat ee SES mia kyo ee 1",
            "SS Ch Gen, eee ee OS 2 ere eA ee an, a, - 4 : oe",
            "y= aoe ES ee mem SC oe | ee 3 ee",
            "= | | s = . = a @ s (R) =o @ | | | ee a =",
            "oe 4 : a, an ee Ae ere 2 SO ee ,neG hC SS",
            "1 ee oyk aim SES ee tai IS ,iR EO FB SSE - pg na",
        };

        var translations = 0;
        var tick = 0;

        // A DIFFERENT garble on every poll, which is the whole point - the same pixels re-read
        // produce a fresh misreading each time, so nothing here ever agrees with anything.
        for (var poll = 0; poll < 24; poll++)
        {
            if (gate.Offer(Scene(6, ++tick, patches: 12)) == FrameVerdict.Read
                && gate.Confirm(garbles[poll % garbles.Length], wordsSeenButIllegible: false)
                    == ReadVerdict.Translate)
                translations++;

            clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        Assert.Equal(0, translations);
    }

    [Fact]
    public void WordsSeenAndNoneLegibleIsReleasedOnlyOnceItHasBeenStablyIllegible()
    {
        // The vision lane's flagship case, and the one thing the garble rule must not swallow with
        // it. "Ten words seen, none read" is exactly what a frame worth escalating looks like - but
        // it is also what a frame captured mid-fade looks like for a moment, and only one of those
        // is worth paying a multimodal request for. Stably illegible is the difference.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new FrameSettleGate(TwoPolls, clock);

        var answers = new List<ReadVerdict>();
        var tick = 0;

        for (var poll = 0; poll < 12 && !answers.Contains(ReadVerdict.Translate); poll++)
        {
            if (gate.Offer(Scene(6, ++tick, patches: 12)) == FrameVerdict.Read)
                answers.Add(gate.Confirm("", wordsSeenButIllegible: true));

            clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        // The first reading buys nothing - one mid-change capture looks exactly like this. The
        // second one, saying the same thing, is what makes it worth a second reader's attention.
        Assert.Equal(ReadVerdict.KeepReading, answers[0]);
        Assert.Equal(ReadVerdict.Translate, answers[1]);
    }

    [Fact]
    public void AnEmptyRegionCostsOneReadingAndNothingElse()
    {
        // The commonest frame there is - the gap between two lines - and it must not start a
        // countdown to translating nothing. Free to establish, and it restarts the deadline rather
        // than retrying immediately, so a blank screen does not OCR itself twice a second forever.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new FrameSettleGate(TwoPolls, clock);

        var tick = 0;
        var reads = 0;

        for (var poll = 0; poll < 10; poll++)
        {
            if (gate.Offer(Scene(0, ++tick, patches: 12)) == FrameVerdict.Read)
            {
                reads++;
                Assert.Equal(ReadVerdict.Nothing, gate.Confirm("", wordsSeenButIllegible: false));
            }

            clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        // One per cap, not one per poll: ten polls is five seconds, and the dialogue cap is three.
        Assert.InRange(reads, 1, 3);
    }
}
