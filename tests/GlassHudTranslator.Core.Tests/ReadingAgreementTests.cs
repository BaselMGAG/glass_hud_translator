using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Ocr;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// When two readings of one screen count as the same words — measured against readings that came
/// off a real screen, rather than against strings written to make a threshold look good.
///
/// <para>
/// <b>Every number below is from a support trace.</b> The first release of the read-and-confirm gate
/// borrowed <see cref="ReadingJudge.SameThing"/> (0.90), which is the correct threshold for the
/// question it was borrowed FROM — whether a vision model's reading is the same line the local
/// engine saw, two readers looking at identical pixels. It is the wrong threshold here, where two
/// readings are a third of a second apart with moving picture behind the words. Real captions scored
/// 0.79 and 0.88 against themselves, so nothing ever agreed, and video mode translated nothing at
/// all.
/// </para>
/// </summary>
public class ReadingAgreementTests
{
    /// <summary>
    /// Three consecutive captures of ONE caption, from the trace. The words are the same; the noise
    /// is scattered through them, which is what OCR over moving picture does.
    /// </summary>
    private static readonly string[] OneCaptionReadThreeTimes =
    [
        "allows you to click with your middle ~ Mouse ™ m",
        "allows you to click with your middle mouse in th",
        "allows you to click with your middle \"age Mouse ",
    ];

    /// <summary>
    /// Four consecutive captures of a region with nothing readable in it, from the earlier trace.
    /// A garbled capture produces a DIFFERENT garble every time — that is the property the whole
    /// design leans on, and these are what it looks like.
    /// </summary>
    private static readonly string[] GarbleReadFourTimes =
    [
        "an gp - ESS BF OE Ri, SI iat ee SES mia kyo ee 1",
        "SS Ch Gen, eee ee OS 2 ere eA ee an, a, - 4 : oe",
        "y= aoe ES ee mem SC oe | ee 3 ee",
        "= | | s = . = a @ s (R) =o @ | | | ee a =",
    ];

    [Fact]
    public void TheThresholdSitsBetweenTheTwoPopulationsAndNotAtTheEdgeOfEither()
    {
        // The measurement, asserted directly, because the behavioural tests below would all still
        // pass with the threshold anywhere in a wide band and this is what actually pins it.
        var sameCaption = Pairs(OneCaptionReadThreeTimes).Min();
        var differentGarbles = Pairs(GarbleReadFourTimes).Max();

        Assert.True(sameCaption > FrameSettleGate.SameText,
            $"the worst pair of readings of ONE caption scored {sameCaption:F3}, at or below the "
            + $"threshold of {FrameSettleGate.SameText} - real text will not agree with itself");

        Assert.True(differentGarbles < FrameSettleGate.SameText,
            $"the best pair of DIFFERENT garbles scored {differentGarbles:F3}, at or above the "
            + $"threshold of {FrameSettleGate.SameText} - noise will buy its way through");

        // And it is not perched on the edge of either population.
        Assert.InRange(FrameSettleGate.SameText, differentGarbles + 0.1, sameCaption - 0.1);

        static IEnumerable<double> Pairs(string[] readings) =>
            readings.Zip(readings.Skip(1), ReadingJudge.Agreement);
    }

    [Fact]
    public void ACaptionReadTwiceOverMovingPictureIsTranslated()
    {
        // The regression, at the level the user experiences it: video mode translating nothing.
        var gate = Reading(out var clock);

        Assert.Equal(ReadVerdict.KeepReading,
            gate.Confirm(OneCaptionReadThreeTimes[0], wordsSeenButIllegible: false));

        clock.Advance(TimeSpan.FromMilliseconds(250));

        Assert.Equal(ReadVerdict.Translate,
            gate.Confirm(OneCaptionReadThreeTimes[1], wordsSeenButIllegible: false));
    }

    [Fact]
    public void AGarbleIsStillNeverTranslatedHoweverManyTimesItIsRead()
    {
        // The other half, and the reason the threshold could not simply be dropped to the floor.
        var gate = Reading(out var clock);
        var verdicts = new List<ReadVerdict>();

        foreach (var garble in GarbleReadFourTimes)
        {
            verdicts.Add(gate.Confirm(garble, wordsSeenButIllegible: false));
            clock.Advance(TimeSpan.FromMilliseconds(250));
        }

        Assert.DoesNotContain(ReadVerdict.Translate, verdicts);
    }

    [Theory]
    [InlineData("This one apprec", "This one appreciates hav")]
    [InlineData("This one appreciates hav", "This one appreciates having a safe")]
    [InlineData("Come, the", "Come, the aether here grows")]
    public void ALineStillAppearingIsNotTheSameLineTwice(string earlier, string later)
    {
        // <b>The case a similarity threshold alone can never catch.</b> Measured on the same data: a
        // typewriter reveal scores 0.71 and 0.87 between consecutive readings while a jittering
        // caption scores 0.79 and 0.88 - the two OVERLAP, so no single number separates them. What
        // does separate them completely is shape. A reveal is a growing PREFIX and scores 1.00
        // against the longer reading's opening; the caption's noise is scattered through the middle
        // and scores well under that.
        var gate = Reading(out var clock);

        Assert.Equal(ReadVerdict.KeepReading, gate.Confirm(earlier, wordsSeenButIllegible: false));
        clock.Advance(TimeSpan.FromMilliseconds(500));

        Assert.Equal(ReadVerdict.KeepReading, gate.Confirm(later, wordsSeenButIllegible: false));
    }

    [Fact]
    public void AndIsTranslatedOnceItStopsAppearing()
    {
        // The other end of the same behaviour: waiting has to end when the line does.
        var gate = Reading(out var clock);
        var full = "This one appreciates having a safe place to stay";

        Assert.Equal(ReadVerdict.KeepReading, gate.Confirm("This one appreciates hav", false));
        clock.Advance(TimeSpan.FromMilliseconds(500));

        Assert.Equal(ReadVerdict.KeepReading, gate.Confirm(full, false));
        clock.Advance(TimeSpan.FromMilliseconds(500));

        Assert.Equal(ReadVerdict.Translate, gate.Confirm(full, false));
    }

    /// <summary>A gate already in a reading stretch, which is the only state Confirm answers in.</summary>
    private static FrameSettleGate Reading(out FakeTimeProvider clock)
    {
        var fake = new FakeTimeProvider(DateTimeOffset.UtcNow);
        clock = fake;

        var gate = new FrameSettleGate(
            new SettleOptions { Cap = TimeSpan.Zero, ReadsBeforeGivingUp = 99 }, fake);

        // A zero cap means the first frame that is not the displayed one goes straight to Read.
        Assert.Equal(FrameVerdict.Read, gate.Offer(Signature(1)));
        return gate;
    }

    private static FrameSignature Signature(int words)
    {
        var b = new FrameBuilder(400, 120, Rgb.BoxDark);
        for (var i = 0; i < words; i++) b.Rect(20 + i % 4 * 90, 20 + i / 4 * 40, 70, 20, Rgb.TextWhite);
        return FrameSignature.Compute(b.Build());
    }
}
