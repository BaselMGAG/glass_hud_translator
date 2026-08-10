using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Diagnostics;
using GlassHudTranslator.Core.Translation;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The judgement half of the health check. Every rule here is a support conversation that already
/// happened once, so a regression is not a cosmetic bug - it is the app forgetting the answer to a
/// question a real user has already asked.
/// </summary>
public class HealthCheckTests
{
    private static HealthInputs Healthy => new()
    {
        SystemLanguage = "en",
        InterfaceLanguage = UiLanguage.English,
        GameWindowTitle = "FINAL FANTASY XIV",
        CanCapture = true,
        ProfileName = "Final Fantasy XIV",
        DisplayScaling = 1.0,
        Lanes = [new LaneHealth("gemini", KeyStatus.Working, "gemini-3.1-flash-lite")],
        ProcessorCount = 8,
        MemoryGb = 16,
        OcrAvailable = true,
        RegionSaved = true,
        LastOcrConfidence = 92f,
    };

    private static IReadOnlyList<HealthFinding> Run(HealthInputs inputs, UiText? text = null) =>
        HealthCheck.Run(inputs, text ?? UiText.En);

    [Fact]
    public void AHealthyInstallIsAllGreen()
    {
        var findings = Run(Healthy);

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal(HealthSeverity.Ok, f.Severity));
    }

    [Fact]
    public void NoKeysAtAllIsTheLoudestProblem()
    {
        // The v0.5.0 failure: no key, four rounds of chasing provider ghosts, and the log line
        // that named it read as a symptom. It must be a Problem and it must come first.
        var findings = Run(Healthy with { Lanes = [] });

        Assert.Equal(HealthSeverity.Problem, findings[0].Severity);
        Assert.Equal(UiText.En.HealthNoKeys, findings[0].Text);
    }

    [Fact]
    public void ARejectedKeyAndAnUnreachableOneAreDifferentSentences()
    {
        // Telling someone their key is wrong when their wifi is down sends them to regenerate a
        // key that was never the problem. Rejected is a Problem; unverifiable is a Warning.
        var findings = Run(Healthy with
        {
            Lanes =
            [
                new LaneHealth("gemini", KeyStatus.Rejected, "401"),
                new LaneHealth("groq", KeyStatus.Unknown, "timed out"),
            ],
        });

        Assert.Contains(findings, f =>
            f.Severity == HealthSeverity.Problem && f.Text.Contains("gemini"));
        Assert.Contains(findings, f =>
            f.Severity == HealthSeverity.Warning && f.Text.Contains("groq"));
    }

    [Fact]
    public void ExclusiveFullscreenIsAProblemAndCarriesThePlatformsOwnExplanation()
    {
        var findings = Run(Healthy with
        {
            CanCapture = false,
            CaptureBlocker = "FINAL FANTASY XIV is in exclusive fullscreen. Switch to borderless windowed.",
        });

        var blocked = Assert.Single(findings, f => f.Severity == HealthSeverity.Problem);
        Assert.Contains("borderless", blocked.Text);
    }

    [Fact]
    public void MissingOcrIsAProblemNotAWarning()
    {
        // The quarantine case: the app starts, looks whole, reads nothing. Before this check the
        // only symptom was an empty overlay.
        var findings = Run(Healthy with { OcrAvailable = false, OcrDetail = "tesseract55.dll not found" });

        Assert.Contains(findings, f =>
            f.Severity == HealthSeverity.Problem && f.Text.Contains("tesseract55.dll"));
    }

    [Fact]
    public void NoRegionDrawnYetIsAProblemThatNamesTheProfile()
    {
        var findings = Run(Healthy with { RegionSaved = false });

        Assert.Contains(findings, f =>
            f.Severity == HealthSeverity.Problem && f.Text.Contains("Final Fantasy XIV"));
    }

    [Theory]
    [InlineData(45f, HealthSeverity.Warning)]
    [InlineData(HealthCheck.LowConfidence, HealthSeverity.Ok)]
    [InlineData(92f, HealthSeverity.Ok)]
    public void ReadQualityIsJudgedAgainstTheThreshold(float confidence, HealthSeverity expected)
    {
        var findings = Run(Healthy with { LastOcrConfidence = confidence });

        Assert.Contains(findings, f =>
            f.Severity == expected && f.Text.Contains(Math.Round(confidence).ToString()));
    }

    [Fact]
    public void AWholeScreenProfileDoesNotComplainAboutAMissingGameWindow()
    {
        var findings = Run(Healthy with
        {
            GameWindowTitle = null,
            ProfileTargetsWholeScreen = true,
        });

        Assert.DoesNotContain(findings, f => f.Severity != HealthSeverity.Ok);
    }

    [Fact]
    public void ArabicWindowsWithAnEnglishInterfaceGetsAdviceInArabic()
    {
        // The advice is addressed to someone who reads Arabic; delivering it in English is the
        // bug it reports. It must appear even when the report is otherwise rendered in English.
        var findings = Run(Healthy with { SystemLanguage = "ar" });

        var advice = Assert.Single(findings, f => f.Severity == HealthSeverity.Warning);
        Assert.Contains("اللغة", advice.Text);
    }

    [Fact]
    public void AnArabicInterfaceOnEnglishWindowsIsAChoiceNotAFinding()
    {
        var findings = Run(Healthy with
        {
            SystemLanguage = "en",
            InterfaceLanguage = UiLanguage.Arabic,
        }, UiText.Ar);

        Assert.All(findings, f => Assert.Equal(HealthSeverity.Ok, f.Severity));
    }

    [Fact]
    public void ProblemsSortAboveTicks()
    {
        // Someone with one problem and six green ticks must not scroll past the ticks to find it.
        var findings = Run(Healthy with { RegionSaved = false, LastOcrConfidence = null });

        var severities = findings.Select(f => f.Severity).ToList();
        Assert.Equal([.. severities.OrderDescending()], severities);
    }

    [Fact]
    public void LaneListsAreMarkedAsMachineText()
    {
        // Lane order is the cost policy; mirrored in the Arabic layout it reads backwards, which
        // once reported the paid provider as the one tried first.
        var findings = Run(Healthy);

        Assert.Contains(findings, f => f.Text.Contains("gemini") && f.Machine);
    }

    [Fact]
    public void TheWholeReportSpeaksArabicWhenAsked()
    {
        var findings = HealthCheck.Run(Healthy with { RegionSaved = false }, UiText.Ar);

        Assert.Contains(findings, f => f.Text.Contains("منطقة"));
    }
}
