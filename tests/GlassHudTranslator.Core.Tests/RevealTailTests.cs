using GlassHudTranslator.Core.Capture;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The last reading of a line that is still appearing, which is where the gate was getting it wrong
/// in the most expensive way available to it.
///
/// <para>
/// <b>The defect, measured on four real FFXIV lines before it was fixed.</b> <c>Agrees</c> asked
/// <c>TextSimilarity.LooksLikeARepeat</c> FIRST, whose budget is <c>min(3, shorter/4)</c> absolute
/// edits, and only consulted the prefix test afterwards — which needed four characters of growth.
/// So the reveal one character short of finished and the finished line scored as THE SAME WORDS:
/// the fragment was released, translated, and written to the cache permanently, and the finished
/// sentence that arrived next was then thrown away by the pipeline's own repeat guard as a repeat
/// of the fragment.
/// </para>
///
/// <para>
/// That is a fluent, confident, wrong Arabic line shown to somebody who cannot check it against the
/// English — the one answer this project is most emphatic about never giving — and it is also why
/// automatic mode looked erratic rather than merely slow: whether it bit depended on where the
/// reveal happened to be when two readings landed.
/// </para>
/// </summary>
public class RevealTailTests
{
    private static readonly SettleOptions Reading = new()
    {
        Cap = TimeSpan.Zero,          // the first changed frame opens a reading stretch at once
        ReadsBeforeGivingUp = 4,
        LongestArrival = TimeSpan.FromSeconds(4),
        PollsPerSecond = 4,
    };

    private static (FrameSettleGate Gate, FakeTimeProvider Clock) Open()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new FrameSettleGate(Reading, clock);

        Assert.Equal(FrameVerdict.Read, gate.Offer(Signature(1)));
        return (gate, clock);
    }

    private static FrameSignature Signature(int words)
    {
        var b = new FrameBuilder(400, 120, Rgb.BoxDark);
        for (var i = 0; i < words; i++) b.Rect(20 + i % 4 * 90, 20 + i / 4 * 40, 70, 20, Rgb.TextWhite);
        return FrameSignature.Compute(b.Build());
    }

    [Theory]
    [InlineData("Come, the aether here grows unstabl", "Come, the aether here grows unstable.")]
    [InlineData("Dellexia This one appreciates having a safe place to sta",
                "Dellexia This one appreciates having a safe place to stay")]
    [InlineData("We must reach Limsa Lominsa before nightfal",
                "We must reach Limsa Lominsa before nightfall.")]
    public void TheLastPartialReadingOfARevealIsNotTheFinishedLine(string partial, string finished)
    {
        // A one-to-three character tail: inside LooksLikeARepeat's absolute budget and under the
        // four characters the prefix test used to demand, which is exactly the gap the fragment
        // escaped through.
        var (gate, clock) = Open();

        Assert.Equal(ReadVerdict.KeepReading, gate.Confirm(partial, wordsSeenButIllegible: false));
        clock.Advance(TimeSpan.FromMilliseconds(250));

        // NOT Translate. The line grew, so it was still arriving.
        Assert.Equal(ReadVerdict.KeepReading, gate.Confirm(finished, wordsSeenButIllegible: false));
        clock.Advance(TimeSpan.FromMilliseconds(250));

        // And the finished line, read again unchanged, is what gets translated.
        Assert.Equal(ReadVerdict.Translate, gate.Confirm(finished, wordsSeenButIllegible: false));
    }

    [Fact]
    public void AGrowingPrefixIsNotAStepTowardGivingUp()
    {
        // The other half of the same defect, and the multi-second half. Every disagreement moved
        // the give-up counter, so a long reveal exhausted the budget, gave up, and restarted the
        // WHOLE three-second cap - the line being long was itself the reason it was dropped.
        var (gate, clock) = Open();

        var line = "The Warrior of Light approaches the aetheryte plaza at dusk to meet the others";

        // Six growing readings against ReadsBeforeGivingUp = 4.
        foreach (var upTo in new[] { 12, 22, 33, 45, 58, 70 })
        {
            Assert.Equal(ReadVerdict.KeepReading, gate.Confirm(line[..upTo], wordsSeenButIllegible: false));
            clock.Advance(TimeSpan.FromMilliseconds(250));
        }

        Assert.Equal(0, gate.GaveUp);

        // Then it stops growing and is translated, once.
        Assert.Equal(ReadVerdict.KeepReading, gate.Confirm(line, wordsSeenButIllegible: false));
        clock.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(ReadVerdict.Translate, gate.Confirm(line, wordsSeenButIllegible: false));
    }

    [Fact]
    public void AStretchThatKeepsGrowingIsStillBoundedByLongestArrival()
    {
        // Growth no longer counts against the read budget, so something else has to stop a screen
        // that grows forever - a scrolling log, a karaoke caption - from holding the stretch open
        // and the rest of the region unwatched.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new FrameSettleGate(
            Reading with { LongestArrival = TimeSpan.FromSeconds(2) }, clock);

        Assert.Equal(FrameVerdict.Read, gate.Offer(Signature(1)));

        var grown = "x";
        var verdicts = new List<ReadVerdict>();

        for (var i = 0; i < 20 && !verdicts.Contains(ReadVerdict.Nothing); i++)
        {
            grown += "yyyyy";
            verdicts.Add(gate.Confirm(grown, wordsSeenButIllegible: false));
            clock.Advance(TimeSpan.FromMilliseconds(250));
        }

        Assert.Contains(ReadVerdict.Nothing, verdicts);
        Assert.Equal(1, gate.GaveUp);
    }

    [Fact]
    public void TrailingPunctuationJitterStillReleasesWithinThreeReadings()
    {
        // The one loop the deferral could have created. Tesseract finding and losing a full stop
        // looks exactly like the tail of a reveal, so the first small growth is given the benefit
        // of the doubt - but only the first, or a line whose punctuation flickers would never be
        // translated at all.
        var (gate, clock) = Open();

        var without = "The aetheryte hums beneath your hand";
        var with = "The aetheryte hums beneath your hand.";

        Assert.Equal(ReadVerdict.KeepReading, gate.Confirm(without, wordsSeenButIllegible: false));
        clock.Advance(TimeSpan.FromMilliseconds(250));

        Assert.Equal(ReadVerdict.KeepReading, gate.Confirm(with, wordsSeenButIllegible: false));
        clock.Advance(TimeSpan.FromMilliseconds(250));

        Assert.Equal(ReadVerdict.Translate, gate.Confirm(without, wordsSeenButIllegible: false));
    }

    [Fact]
    public void ATailThatGoesMissingIsADisagreementAndNotAnArrival()
    {
        // A reveal never runs backwards. A reading that is the previous one with the END CHOPPED
        // OFF is two readings of something that could not be read the same way twice, and must move
        // the give-up counter like any other disagreement - otherwise a region flickering between a
        // long and a short reading holds the stretch open forever.
        var (gate, clock) = Open();

        var full = "The Warrior of Light approaches the aetheryte plaza";
        var truncated = full[..^12];

        Assert.Equal(ReadVerdict.KeepReading, gate.Confirm(full, wordsSeenButIllegible: false));
        clock.Advance(TimeSpan.FromMilliseconds(250));

        // Shrinking by twelve characters: a disagreement, not an arrival, so it does not release.
        Assert.Equal(ReadVerdict.KeepReading, gate.Confirm(truncated, wordsSeenButIllegible: false));
    }
}
