using System.Reflection;
using GlassHudTranslator.Core.Capture;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// Relationships between constants that live in different files, asserted directly.
///
/// <para>
/// <b>This file exists because the same defect has now been found four times.</b> A quantity counted
/// in polls silently changes meaning whenever the poll rate does, and every instance was invisible
/// to behavioural tests: <c>MinimumReads</c> against the read budget, <c>ContentRhythm.Window</c>
/// when the dialogue rate doubled, persistence measured in polls rather than seconds, and the
/// gate's own scene-motion window. Each was two constants chosen independently in two files, neither
/// wrong on its own. Asserting the RELATIONSHIP is the only version of this that survives the next
/// tuning pass.
/// </para>
/// </summary>
public class PacingArithmeticTests
{
    [Theory]
    [InlineData(WatchMode.Dialogue)]
    [InlineData(WatchMode.Video)]
    public void AReadingStretchCanSitThroughAWholeRevealInEveryMode(WatchMode mode)
    {
        // Two ceilings bound a reading stretch and they bound different things: ReadsBeforeGivingUp
        // bounds readings that never AGREE, LongestArrival bounds one that keeps GROWING. If the
        // clock could expire before the disagreement budget, the time ceiling would be doing the
        // other one's job on a region that simply cannot be read - and a genuine reveal would be
        // abandoned on a timer rather than on evidence.
        var pacing = WatchPacing.For(mode);
        var poll = TimeSpan.FromSeconds(1 / pacing.PollsPerSecond);

        Assert.True(pacing.LongestArrival >= poll * pacing.ReadsBeforeGivingUp * 2,
            $"{mode}: LongestArrival is {pacing.LongestArrival.TotalMilliseconds:F0} ms but the "
            + $"disagreement budget alone runs to {(poll * pacing.ReadsBeforeGivingUp).TotalMilliseconds:F0} ms - "
            + "the clock would end a stretch before the readings had had their say");
    }

    [Fact]
    public void TheStillnessMarginIsHalfASecondForDialogueAndAQuarterForVideo()
    {
        // RequiredStillTicks is a COUNT, so it silently halves in wall-clock terms every time the
        // poll rate doubles. The margin is what stands between a reveal pausing on a comma and a
        // half-written sentence being translated, so it is the wall-clock number that matters.
        static TimeSpan Margin(WatchMode mode)
        {
            var p = WatchPacing.For(mode);
            return TimeSpan.FromSeconds(1 / p.PollsPerSecond) * (p.RequiredStillTicks - 1);
        }

        Assert.Equal(TimeSpan.FromMilliseconds(500), Margin(WatchMode.Dialogue));
        Assert.Equal(TimeSpan.FromMilliseconds(250), Margin(WatchMode.Video));
    }

    /// <summary>
    /// Every field of <see cref="SettleOptions"/> that a mode is entitled to decide, and the check
    /// that stops the list going stale.
    ///
    /// <para>
    /// <c>AutoWatch</c> calls <c>Retune(watch.Settle())</c> four times a second, so a field
    /// <c>Settle()</c> omits is not "left alone" — it is reset to its default four times a second. A
    /// value handed to the gate's constructor therefore survives every unit test written against it
    /// and dies on the first poll in production, which is exactly how <c>MaxDifferingCells</c> spent
    /// its whole life unreachable from configuration.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(WatchMode.Dialogue)]
    [InlineData(WatchMode.Video)]
    public void EveryFieldAModeOwnsIsCarriedThroughSettle(WatchMode mode)
    {
        var pacing = WatchPacing.For(mode);
        var settle = new WatchSession(pacing).Settle();

        Assert.Equal(pacing.RequiredStillTicks, settle.RequiredStillTicks);
        Assert.Equal(pacing.SettleCap, settle.Cap);
        Assert.Equal(pacing.ReadsBeforeGivingUp, settle.ReadsBeforeGivingUp);
        Assert.Equal(pacing.LongestArrival, settle.LongestArrival);
        Assert.Equal(pacing.PollsPerSecond, settle.PollsPerSecond);
    }

    [Fact]
    public void NoOneHasAddedASettleOptionsFieldWithoutDecidingWhetherSettleCarriesIt()
    {
        // The guard on the test above. Growing SettleOptions is fine; growing it without deciding
        // whether a mode owns the new field is how the last one was lost. Bump the count here and
        // either add it to Settle() or write down why the modes do not differ about it.
        var settable = typeof(SettleOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Count(p => p.SetMethod is not null);

        Assert.Equal(8, settable);
    }
}
