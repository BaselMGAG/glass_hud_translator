using GlassHudTranslator.Core.Diagnostics;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

public class StartupLogTests
{
    [Fact]
    public void TheBlackBoxRecordsTheWholeStoryInOneFile()
    {
        // Static and sticky by design, so this is one journey rather than isolated cases: begin,
        // a note, a failure - the exact sequence a crashing start writes.
        StartupLog.Begin("9.9.9-test");

        Assert.NotNull(StartupLog.Path);
        Assert.True(File.Exists(StartupLog.Path));

        StartupLog.Note("first window built");
        StartupLog.Fail(new InvalidOperationException("the antivirus ate a DLL"));

        var written = File.ReadAllText(StartupLog.Path!);

        // The header answers "did it even run, and what was it": version, OS, and the payload
        // census that turns a quarantine into a one-line diagnosis.
        Assert.Contains("9.9.9-test", written);
        Assert.Contains("payload:", written);
        Assert.Contains("assemblies", written);

        // And the story survives in order, ending at the reason.
        Assert.Contains("first window built", written);
        Assert.Contains("FAILED", written);
        Assert.Contains("the antivirus ate a DLL", written);
        Assert.Contains(nameof(InvalidOperationException), written);
    }

    [Fact]
    public void ANewRunOverwritesRatherThanAppends()
    {
        StartupLog.Begin("1.0.0-first");
        StartupLog.Fail(new Exception("old failure"));

        StartupLog.Begin("2.0.0-second");

        var written = File.ReadAllText(StartupLog.Path!);

        // The interesting run is the failing one, and on a failing run the file stays. A log that
        // appends forever buries this run's answer under every run before it.
        Assert.Contains("2.0.0-second", written);
        Assert.DoesNotContain("old failure", written);
    }
}
