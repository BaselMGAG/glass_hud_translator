using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Glossary;
using GlassHudTranslator.Core.Ocr;
using GlassHudTranslator.Core.Pipeline;
using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Translation;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// When a second, paid reading of a frame is worth buying.
///
/// <para>
/// Several of these tests are adversarial findings written down: the design that preceded them was
/// attacked on correctness, cost and fit, and the attacks found that the flagship case could never
/// fire, that the free case would become the most expensive one in the app, and that the same
/// sentence would be re-bought once per poll. Each of those is a test here.
/// </para>
/// </summary>
public class EscalationPolicyTests
{
    private static OcrResult Read(string text, int words, int rejected, float confidence = 90) =>
        new(text, confidence, words, rejected);

    [Fact]
    public void SwitchedOffIsFree()
    {
        var illegible = Read("", words: 0, rejected: 8);

        var decision = EscalationPolicy.Decide(illegible, enabled: false);

        Assert.False(decision.Escalate);
        Assert.Equal(EscalationReason.SwitchedOff, decision.Why);
    }

    [Fact]
    public void AWellReadLineIsNotEscalated()
    {
        var clean = Read("The Scions of the Seventh Dawn stand ready.", words: 7, rejected: 0);

        Assert.False(EscalationPolicy.Decide(clean, enabled: true).Escalate);
    }

    [Fact]
    public void AnIllegibleFrameIsTheCaseTheFeatureExistsFor()
    {
        // Words were seen and none survived the confidence filter, so RawText is empty. This is the
        // flagship case, and an earlier version of this design could never reach it: the pipeline
        // returns early on an empty body, and "empty because nothing was there" and "empty because
        // none of it could be read" wear the same face until you look at what was thrown away.
        var illegible = Read("", words: 0, rejected: 6, confidence: 0);

        var decision = EscalationPolicy.Decide(illegible, enabled: true);

        Assert.True(decision.Escalate);
        Assert.Equal(EscalationReason.Illegible, decision.Why);
    }

    [Fact]
    public void AnEmptyRegionStaysFreeForeverEvenWithAStrayGlyphRejected()
    {
        // THE cost attack, and the reason there is a floor rather than "rejected > 0". A capture
        // region drawn slightly wide clips the edge of the dialogue frame, and that border reads as
        // one rejected fragment - this repository's own example is `|~` at confidence 8. With no
        // floor, an idle screen over an animated scene escalates about twenty times a minute,
        // which is the whole of Gemini's free day in under half an hour, to rediscover that there
        // is nothing on screen. Today that costs exactly nothing, and it has to stay that way.
        var idle = Read("", words: 0, rejected: 1);

        var decision = EscalationPolicy.Decide(idle, enabled: true);

        Assert.False(decision.Escalate);
        Assert.Equal(EscalationReason.NothingThere, decision.Why);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void BelowTheFloorIsAlwaysNothingThere(int rejected)
    {
        Assert.False(EscalationPolicy.Decide(Read("", 0, rejected), enabled: true).Escalate);
    }

    [Fact]
    public void MostOfTheWordsThrownAwayIsWorthASecondOpinion()
    {
        // Text came back, but two words of eight survived. The mean confidence of those two can be
        // excellent and says nothing about the six that were dropped.
        var mostly = Read("the ready", words: 2, rejected: 6, confidence: 92);

        var decision = EscalationPolicy.Decide(mostly, enabled: true);

        Assert.True(decision.Escalate);
        Assert.Equal(EscalationReason.TooMuchRejected, decision.Why);
    }

    [Fact]
    public void AnUnusualProperNounReadPerfectlyIsNotEscalated()
    {
        // The linkpearl case, from this project's own history: Tesseract read it correctly at
        // confidence 39.2, because it scores unusual proper nouns down - and unusual proper nouns
        // are most of what a game glossary contains. Escalating on low confidence would send
        // exactly these lines to the reader measured to be worst at them, since a multimodal model
        // reads by knowing what the word probably is and a game's invented vocabulary is precisely
        // the text that carries no such prior.
        var linkpearl = Read("Reach me on the linkpearl.", words: 5, rejected: 0, confidence: 39.2f);

        var decision = EscalationPolicy.Decide(linkpearl, enabled: true);

        Assert.False(decision.Escalate);
        Assert.Equal(EscalationReason.GoodEnough, decision.Why);
    }

    [Fact]
    public void TheSameUnreadableLineIsOnlyPaidForOnce()
    {
        // Without this the feature bills per POLL rather than per line. A dialogue box over an
        // animated scene never settles, so the same sentence is re-read every few seconds; each
        // re-read is a slightly different garble, and because the pipeline remembers the CORRECTED
        // text, the new garble differs from it by construction and nothing downstream suppresses
        // the second purchase. That is the "paying four times over for one sentence" defect from
        // v0.5.2, one layer up, where neither the settle gate nor the cache can see it.
        var illegible = Read("", words: 0, rejected: 6);

        Assert.True(EscalationPolicy.Decide(illegible, enabled: true, alreadyAsked: false).Escalate);

        var again = EscalationPolicy.Decide(illegible, enabled: true, alreadyAsked: true);

        Assert.False(again.Escalate);
        Assert.Equal(EscalationReason.AlreadyAsked, again.Why);
    }
}

/// <summary>
/// Deciding whether to believe the second reading. The danger being defended against is specific: a
/// vision model's mistake is a fluent, well-formed sentence that was never on screen, where a
/// Tesseract mistake is visible noise — and fluent wrong Arabic is undetectable to the reader this
/// app exists for.
/// </summary>
public class ReadingJudgeTests
{
    private const string Garbled = "Vou musL nnt heed lhe siren's calI";
    private const string Correct = "You must not heed the siren's call";

    [Fact]
    public void AReadingThatMatchesIsConfirmed()
    {
        var reading = ReadingJudge.Judge(Correct, Correct);

        Assert.Equal(ReadingVerdict.Confirmed, reading.Verdict);
        Assert.Equal(1.0, reading.Agreement!.Value, 3);
    }

    [Fact]
    public void APlausibleCorrectionOfAGarbleIsAdopted()
    {
        // Both are readings of the same pixels, so they still share most of their characters.
        var reading = ReadingJudge.Judge(Garbled, Correct);

        Assert.Equal(ReadingVerdict.Corrected, reading.Verdict);
        Assert.Equal(Correct, reading.Text);
    }

    [Fact]
    public void AnInventedSentenceIsRejected()
    {
        // THE hallucination defence. A fluent sentence with no relationship to what the first
        // reader saw is far likelier to be invention than correction - and if it were adopted it
        // would be translated, displayed as confident Arabic with no English beside it, and cached
        // permanently under its own key.
        var invented = "The Warrior of Light must return to the Rising Stones before nightfall.";

        var reading = ReadingJudge.Judge(Garbled, invented);

        Assert.Equal(ReadingVerdict.Rejected, reading.Verdict);
        Assert.Equal("", reading.Text);
    }

    [Fact]
    public void ARejectedReadingDoesNotKeepItsTranslation()
    {
        // The correctness attack that this test is named after. The Arabic offered alongside a
        // reading is used when the text router later fails every lane - but a rejected reading is
        // one just proved untrustworthy, so displaying its translation would put the system's most
        // confident wrong answer on screen as fluent Arabic. Rejecting a reading must reject its
        // translation with it, and the safest way is for the field not to survive the verdict.
        var reading = ReadingJudge.Judge(Garbled, "Something else entirely, quite unrelated indeed.",
            understudy: "ترجمة لجملة لم تكن على الشاشة قط");

        Assert.Equal(ReadingVerdict.Rejected, reading.Verdict);
        Assert.Null(reading.Understudy);
    }

    [Fact]
    public void AnAcceptedReadingKeepsItsTranslation()
    {
        var reading = ReadingJudge.Judge(Garbled, Correct, understudy: "لا تُصغِ إلى نداء الحورية");

        Assert.Equal(ReadingVerdict.Corrected, reading.Verdict);
        Assert.NotNull(reading.Understudy);
    }

    [Fact]
    public void DecliningToReadIsAnAnswerAndNotAFailure()
    {
        Assert.Equal(ReadingVerdict.Rejected, ReadingJudge.Judge(Garbled, ReadingJudge.NothingToRead).Verdict);
        Assert.Equal(ReadingVerdict.Rejected, ReadingJudge.Judge(Garbled, "").Verdict);
        Assert.Equal(ReadingVerdict.Rejected, ReadingJudge.Judge(Garbled, null).Verdict);
    }

    [Fact]
    public void AnIllegibleFrameHasNoAgreementToMeasureAndSaysSo()
    {
        // Reachable only when the frame was escalated as illegible, which means glyphs WERE seen -
        // so there is text and a reading is worth adopting. There is simply nothing to compare it
        // with, and null says that where a zero would read as total disagreement and reject
        // exactly the frames the feature exists to rescue.
        var reading = ReadingJudge.Judge("", Correct);

        Assert.Equal(ReadingVerdict.Corrected, reading.Verdict);
        Assert.Null(reading.Agreement);
        Assert.Equal(Correct, reading.Text);
    }

    [Fact]
    public void AgreementIsNormalisedByTheLongerStringSoAShortInventionCannotScoreWell()
    {
        // Normalising by the shorter string would let "Yes." score respectably against a whole
        // garbled sentence, because only a few of ITS characters need to match.
        var againstLong = ReadingJudge.Agreement("You must not heed the siren's call at all", "Yes.");

        Assert.True(againstLong < ReadingJudge.Unrelated,
            $"a four-character invention scored {againstLong:F2} against a forty-character line");
    }

    [Fact]
    public void CaseAloneIsNotDisagreement()
    {
        Assert.Equal(ReadingVerdict.Confirmed, ReadingJudge.Judge(Correct, Correct.ToUpperInvariant()).Verdict);
    }
}

/// <summary>Answers whatever it is told to, and counts how often it was asked.</summary>
internal sealed class StubVisionReader(params string[] answers) : IVisionReader
{
    private readonly Queue<string> _answers = new(answers);

    public string Name => "stub-vision";

    public bool IsConfigured { get; set; } = true;

    public int Calls { get; private set; }

    public List<VisionRequest> Requests { get; } = [];

    public Func<VisionRequest, VisionAnswer>? Answer { get; set; }

    public Task<VisionAnswer> ReadAsync(VisionRequest request, CancellationToken ct)
    {
        Calls++;
        Requests.Add(request);

        if (Answer is not null) return Task.FromResult(Answer(request));

        return Task.FromResult(new VisionAnswer(_answers.Count > 0 ? _answers.Dequeue() : ""));
    }
}

/// <summary>
/// The second reader, through the whole pipeline. These are the cases the design was attacked on:
/// the flagship frame being unreachable, an idle screen becoming the most expensive thing in the
/// app, and one sentence being bought once per poll.
/// </summary>
public class VisionEscalationPipelineTests
{
    private static readonly Frame AnyFrame = new FrameBuilder(240, 80, new Rgb(0, 0, 0)).Build();

    private static TranslationPipeline Build(
        ScriptedOcr ocr, MemoryCache cache, FakeProvider provider, IVisionReader? vision) =>
        new(ocr, cache, new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]), vision: vision);

    /// <summary>An illegible frame: words were seen, none survived the confidence filter.</summary>
    private static ScriptedOcr Illegible(ScriptedOcr ocr)
    {
        ocr.Reads("", confidence: 0, rejected: 6);
        return ocr;
    }

    [Fact]
    public async Task AnIllegibleFrameIsRescued()
    {
        // The flagship case, and the one an earlier placement of this feature could never reach:
        // all words rejected means an EMPTY reading, and the pipeline returns early on empty
        // bodies. The decision therefore sits ABOVE that guard - which is only safe because the
        // policy itself refuses to spend anything on a frame with nothing on it.
        var ocr = Illegible(new ScriptedOcr());
        var vision = new StubVisionReader("You must not heed the siren's call");
        var provider = new FakeProvider("fake").Returns("لا تُصغِ");
        var pipeline = Build(ocr, new MemoryCache(), provider, vision);

        var outcome = await pipeline.ProcessAsync(AnyFrame);

        Assert.Equal(1, vision.Calls);
        Assert.Equal("You must not heed the siren's call", outcome.Body);

        // Tashkeel is stripped on the way out, which is the default and correct: the marks are a
        // display preference applied at the last moment, never baked into what was returned.
        Assert.Equal("لا تصغ", outcome.Result!.Text);
    }

    [Fact]
    public async Task AnIdleScreenNeverCostsAnything()
    {
        // THE cost attack. A capture region drawn a little wide clips a UI border, which reads as
        // one rejected fragment on a screen with no dialogue on it. At the dialogue pacing that is
        // about twenty frames a minute; paying for each would be the whole free daily allowance in
        // under half an hour, to keep rediscovering that the screen is empty.
        var ocr = new ScriptedOcr();
        ocr.Reads("", confidence: 0, rejected: 1);

        var vision = new StubVisionReader("Something invented");
        var pipeline = Build(ocr, new MemoryCache(), new FakeProvider("fake"), vision);

        var outcome = await pipeline.ProcessAsync(AnyFrame);

        Assert.Equal(0, vision.Calls);
        Assert.Null(outcome.Result);
    }

    [Fact]
    public async Task TheSameUnreadableLineIsBoughtOnceNotOncePerPoll()
    {
        // A dialogue box over an animated scene never settles, so the same sentence is re-read
        // every few seconds and each read is a slightly different garble. The cache cannot absorb
        // that - it is keyed on the CORRECTED text - and neither can the repeat guard. Without a
        // memo of its own this feature bills per poll, which is the v0.5.2 defect one layer up.
        var ocr = new ScriptedOcr();
        var vision = new StubVisionReader();
        vision.Answer = _ => new VisionAnswer("You must not heed the siren's call");

        var provider = new FakeProvider("fake");
        for (var i = 0; i < 4; i++) provider.Returns("لا تُصغِ");

        var pipeline = Build(ocr, new MemoryCache(), provider, vision);

        // Four polls of one sentence, each garbled slightly differently, as OCR actually behaves.
        foreach (var garble in new[]
                 {
                     "Vou musL nnt heed lhe siren's calI",
                     "Vou musL not heed lhe siren's calI",
                     "You musL nnt heed the siren's calI",
                     "Vou must nnt heed lhe siren's call",
                 })
        {
            ocr.Reads(garble, confidence: 30, rejected: 5);
            await pipeline.ProcessAsync(AnyFrame);
        }

        Assert.Equal(1, vision.Calls);
    }

    [Fact]
    public async Task AnInventedReadingIsRefusedAndTheLocalOneStands()
    {
        var ocr = new ScriptedOcr();
        ocr.Reads("Vou musL nnt heed lhe siren's calI", confidence: 30, rejected: 5);

        var vision = new StubVisionReader("The Warrior of Light returns to the Rising Stones tonight.");
        var provider = new FakeProvider("fake").Returns("ترجمة");
        var pipeline = Build(ocr, new MemoryCache(), provider, vision);

        var outcome = await pipeline.ProcessAsync(AnyFrame);

        Assert.Equal(1, vision.Calls);
        Assert.Equal("Vou musL nnt heed lhe siren's calI", outcome.Body);
    }

    [Fact]
    public async Task TheReaderIsGivenTheLocalReadingAndTheVocabulary()
    {
        // Correction rather than fresh transcription is the design; the previous reading is what
        // makes it correction. The vocabulary is the other half - a model's documented weakness is
        // text that carries no meaning, and a game's invented names are exactly that.
        var store = new GlossaryStore([new GlossaryTerm("linkpearl", "لينك بيرل")]);
        var ocr = new ScriptedOcr();
        ocr.Reads("Reach me on the Iinkpear1", confidence: 30, rejected: 4);

        var vision = new StubVisionReader("Reach me on the linkpearl");
        var pipeline = new TranslationPipeline(ocr, new MemoryCache(), new GlossaryMatcher(store),
            new ProviderRouter([(new FakeProvider("f").Returns("ترجمة"), 600)]), vision: vision);

        await pipeline.ProcessAsync(AnyFrame);

        Assert.Equal("Reach me on the Iinkpear1", vision.Requests[0].LocalReading);
        Assert.Contains(vision.Requests[0].Vocabulary, t => t.En == "linkpearl");
    }

    [Fact]
    public async Task AReaderThatThrowsIsNotTheReasonALineFailsToAppear()
    {
        // Same contract as the router. This is an accuracy improvement bolted onto a pipeline whose
        // promise is that something always appears.
        var ocr = new ScriptedOcr();
        ocr.Reads("Vou musL nnt heed", confidence: 30, rejected: 5);

        var pipeline = Build(ocr, new MemoryCache(), new FakeProvider("f").Returns("ترجمة"), new ThrowingReader());

        var outcome = await pipeline.ProcessAsync(AnyFrame);

        Assert.Equal("ترجمة", outcome.Result!.Text);
        Assert.Equal("Vou musL nnt heed", outcome.Body);
    }

    [Fact]
    public async Task WithNoReaderConfiguredNothingChanges()
    {
        var ocr = Illegible(new ScriptedOcr());
        var pipeline = Build(ocr, new MemoryCache(), new FakeProvider("f"), vision: null);

        Assert.Null((await pipeline.ProcessAsync(AnyFrame)).Result);
    }

    private sealed class ThrowingReader : IVisionReader
    {
        public string Name => "throws";
        public bool IsConfigured => true;
        public Task<VisionAnswer> ReadAsync(VisionRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("the lane is on fire");
    }
}
