using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Glossary;
using GlassHudTranslator.Core.Ocr;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// What is said to the vision model, and how its answer is read back. Testable without a key, which
/// is the reason the wording lives apart from the transport: the wording is the part that decides
/// whether the feature works at all.
/// </summary>
public class VisionPromptTests
{
    private static readonly VisionImage AnyImage = new([1, 2, 3], 800, 200, 1.0);

    private static VisionRequest Ask(string local, params string[] vocabulary) =>
        new(AnyImage, local, [.. vocabulary.Select(v => new GlossaryTerm(v, "ع"))], "Final Fantasy XIV");

    [Fact]
    public void WithALocalReadingItAsksForACorrectionRatherThanAFreshTranscription()
    {
        // The whole design in one assertion. Reading a crop from scratch is generation, which is
        // where a language model's habit of guessing from context does the damage; deciding whether
        // an existing reading is right is verification.
        var system = VisionPrompt.System(Ask("Vou musL nnt heed lhe siren's calI"));

        Assert.Contains("CORRECT", system, StringComparison.Ordinal);
        Assert.Contains("change only what is genuinely wrong", system, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoLocalReadingItAsksForATranscription()
    {
        var system = VisionPrompt.System(Ask(""));

        Assert.Contains("Transcribe", system, StringComparison.Ordinal);
        Assert.DoesNotContain("CORRECT", system, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePreviousReadingIsHandedOverAsTheUserTurn()
    {
        Assert.Contains("Vou musL", VisionPrompt.User(Ask("Vou musL nnt heed")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheGamesProperNounsAreSuppliedBecauseThatIsTheModelsWorstCase()
    {
        // Multimodal OCR loses far more accuracy on text that carries no meaning than a supervised
        // recogniser does, because it reads by knowing what a word probably is - and a game's
        // invented vocabulary is exactly that kind of text. We already keep the list.
        var system = VisionPrompt.System(Ask("Reach me on the Iinkpear1.", "linkpearl", "Y'shtola"));

        Assert.Contains("linkpearl", system, StringComparison.Ordinal);
        Assert.Contains("Y'shtola", system, StringComparison.Ordinal);
    }

    [Fact]
    public void AbstentionIsOfferedAsPermissionNotDemandedSeverely()
    {
        // A severe "refuse unless certain" instruction measurably suppresses correct answers on
        // legible input, and this lane only ever sees marginal frames - so it would decline most of
        // what the feature exists to rescue.
        var system = VisionPrompt.System(Ask("something"));

        Assert.Contains(ReadingJudge.NothingToRead, system, StringComparison.Ordinal);
        Assert.DoesNotContain("must not guess", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("never guess", system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheArabicIsOnlyAskedForWhenItIsWanted()
    {
        Assert.DoesNotContain("arabic", VisionPrompt.System(Ask("x")), StringComparison.OrdinalIgnoreCase);

        var wanting = new VisionRequest(AnyImage, "x", [], "a game", WantsUnderstudy: true);
        Assert.Contains("arabic", VisionPrompt.System(wanting), StringComparison.OrdinalIgnoreCase);
    }

    // ── reading the answer back ───────────────────────────────────────────────────────────────

    [Fact]
    public void APlainAnswerIsTakenAsTheReading()
    {
        Assert.Equal("You must not heed the siren's call",
            VisionPrompt.Parse("You must not heed the siren's call").Text);
    }

    [Theory]
    [InlineData("```\nYou must not heed\n```")]
    [InlineData("```text\nYou must not heed\n```")]
    [InlineData("```json\nYou must not heed\n```")]
    public void MarkdownFencesAreStripped(string raw)
    {
        // Every model produces these sooner or later whatever it is told, and failing the request
        // over one would not get a better answer, it would get no answer.
        Assert.Equal("You must not heed", VisionPrompt.Parse(raw).Text);
    }

    [Fact]
    public void JsonCarriesBothTheReadingAndTheArabic()
    {
        var answer = VisionPrompt.Parse("""{"text": "Come with me.", "arabic": "تعال معي."}""");

        Assert.Equal("Come with me.", answer.Text);
        Assert.Equal("تعال معي.", answer.Arabic);
    }

    [Fact]
    public void BrokenJsonStillYieldsTheTextRatherThanNothing()
    {
        // The agreement gate is downstream and will reject it if it really is nonsense. Throwing
        // here would turn a recoverable answer into a failed request.
        var answer = VisionPrompt.Parse("""{"text": "Come with me.", "arabic": """);

        Assert.NotEqual("", answer.Text);
    }

    [Fact]
    public void TheSentinelSurvivesParsingSoTheJudgeCanSeeIt()
    {
        Assert.Equal(ReadingJudge.NothingToRead, VisionPrompt.Parse(ReadingJudge.NothingToRead).Text);
        Assert.Equal(ReadingJudge.NothingToRead,
            VisionPrompt.Parse($$"""{"text": "{{ReadingJudge.NothingToRead}}"}""").Text);
    }

    [Fact]
    public void NothingAtAllIsAnEmptyReadingRatherThanAThrow()
    {
        Assert.Equal("", VisionPrompt.Parse(null).Text);
        Assert.Equal("", VisionPrompt.Parse("   ").Text);
    }
}
