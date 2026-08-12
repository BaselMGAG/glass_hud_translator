using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Config;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// Which poll rate a run actually uses, which turned out not to be the one the mode asks for.
///
/// <para>
/// <c>autoWatchFps</c> shipped with a default of <c>2</c>, and <c>Save</c> writes every field — so
/// every installation in existence has that number in its settings file, the override was always
/// active, and Video mode's four polls a second had never once run on a real machine. It showed up
/// in a support trace as <c>START mode=Video fps=2</c> and would have been invisible in any test
/// that constructed <see cref="AppSettings"/> fresh, because a fresh one has the modern default of
/// zero.
/// </para>
/// </summary>
[Collection(nameof(SettingsStaticCollection))]
public class PollRateTests
{
    [Theory]
    [InlineData(WatchMode.Dialogue)]
    [InlineData(WatchMode.Video)]
    public void TheModeDecidesWhenTheSettingHoldsTheValueItUsedToShipWith(WatchMode mode)
    {
        var pacing = WatchPacing.For(mode);
        var legacy = new AppSettings { AutoWatchFps = AppSettings.LegacyDefaultFps };

        Assert.Equal(pacing.PollInterval, legacy.PollIntervalFor(pacing));
    }

    [Theory]
    [InlineData(WatchMode.Dialogue)]
    [InlineData(WatchMode.Video)]
    public void BothModesPollAtTheirOwnRateOnASettingsFileFromAnOlderRelease(WatchMode mode)
    {
        // The claim in the file made concrete. A settings file written by any earlier release pins
        // autoWatchFps at 2, and while that was honoured every mode polled every 500 ms whatever it
        // asked for - which is how video's four-a-second went a whole release without ever running.
        var stored = new AppSettings { AutoWatchFps = AppSettings.LegacyDefaultFps };
        var interval = stored.PollIntervalFor(WatchPacing.For(mode));

        Assert.Equal(TimeSpan.FromMilliseconds(250), interval);

        Assert.True(interval < TimeSpan.FromMilliseconds(AppSettings.LegacyDefaultFps * 250),
            $"{mode} polls every {interval.TotalMilliseconds:F0} ms - the stored override is back");
    }

    [Fact]
    public void ZeroMeansTheModeDecides()
    {
        var pacing = WatchPacing.For(WatchMode.Video);
        Assert.Equal(pacing.PollInterval, new AppSettings { AutoWatchFps = 0 }.PollIntervalFor(pacing));
    }

    [Fact]
    public void AHandEditedValueIsStillHonoured()
    {
        // The escape hatch has to keep working, or this is not a fix, it is a different override.
        var settings = new AppSettings { AutoWatchFps = 8 };

        Assert.Equal(TimeSpan.FromMilliseconds(125),
            settings.PollIntervalFor(WatchPacing.For(WatchMode.Dialogue)));
    }

    [Fact]
    public void EveryModeProducesAUsablePacing()
    {
        // Auto is not a pacing of its own - it resolves to Dialogue until the detector decides -
        // and the thing that must never happen is a mode whose numbers are zero or negative,
        // because a zero poll interval is a spin and a zero settle cap translates every frame.
        foreach (var mode in WatchModes.InOrder)
        {
            var pacing = WatchPacing.For(mode);

            Assert.True(pacing.PollInterval > TimeSpan.Zero, $"{mode} poll interval");
            Assert.True(pacing.SettleCap > TimeSpan.Zero, $"{mode} settle cap");
            Assert.True(pacing.ReadsBeforeGivingUp >= 2,
                $"{mode} needs at least two readings, or nothing can ever agree with anything");
            Assert.True(pacing.StopAfter > TimeSpan.Zero, $"{mode} stop-after");
            Assert.True(pacing.MinimumInterval >= TimeSpan.Zero, $"{mode} floor");
        }
    }
}
