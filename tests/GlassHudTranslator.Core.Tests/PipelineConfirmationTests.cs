using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Glossary;
using GlassHudTranslator.Core.Ocr;
using GlassHudTranslator.Core.Pipeline;
using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Translation;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The caller's veto, and where it sits.
///
/// <para>
/// <see cref="FrameSettleGate"/> can only finish its decision by looking at the words, and the words
/// are produced in here. So the poll loop hands the pipeline a question rather than re-implementing
/// half of it, and the answer arrives four lines after OCR — which is the last point before anything
/// is spent.
/// </para>
///
/// <para>
/// <b>Every test below is about placement rather than behaviour.</b> A guard that runs in the wrong
/// order still returns the right verdict and still costs everything it was written to save, and this
/// project has shipped that exact mistake twice: the too-short guard ran after the translation, and
/// the vision escalation was hoisted above the guards that were supposed to protect it.
/// </para>
/// </summary>
public class PipelineConfirmationTests
{
    private static readonly Frame AnyFrame = new FrameBuilder(240, 80, new Rgb(0, 0, 0)).Build();

    private static TranslationPipeline Build(
        ScriptedOcr ocr, MemoryCache cache, FakeProvider provider, IVisionReader? vision = null) =>
        new(ocr, cache, new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)]), vision: vision)
        {
            ReadAgainWhenUnreadable = vision is not null,
        };

    [Fact]
    public async Task ARefusedReadingCostsNoRequestNoLookupAndNoCacheRow()
    {
        var ocr = new ScriptedOcr().Reads("Come, the aether here grows unstable.");
        var cache = new MemoryCache();
        var provider = new FakeProvider("fake").Returns("ترجمة");

        var outcome = await Build(ocr, cache, provider).ProcessAsync(AnyFrame,
            options: ProcessOptions.Polled with { Confirm = (_, _) => false });

        Assert.True(outcome.Unconfirmed);

        // Null because nothing was ATTEMPTED, which is a different fact from something failing -
        // the same distinction an empty region and a too-short body already draw.
        Assert.Null(outcome.Result);

        Assert.Empty(provider.Calls);
        Assert.Equal(0, cache.Lookups);
        Assert.Empty(cache.Rows);
    }

    [Fact]
    public async Task ARefusedReadingDoesNotEnterContextAndIsNotTheRepeatReference()
    {
        var ocr = new ScriptedOcr();
        var cache = new MemoryCache();
        var provider = new FakeProvider("fake").Returns("ترجمة").Returns("ترجمة أخرى");
        var pipeline = Build(ocr, cache, provider);

        ocr.Reads("A line that was never confirmed.");
        await pipeline.ProcessAsync(AnyFrame,
            options: ProcessOptions.Polled with { Confirm = (_, _) => false });

        ocr.Reads("The line that actually appeared.");
        var real = await pipeline.ProcessAsync(AnyFrame, options: ProcessOptions.Polled);

        Assert.False(real.Unconfirmed);
        Assert.NotNull(real.Result);

        // The refused line must not be sitting in the prompt as "the past". A frame the gate was
        // still deciding about is not something the player saw.
        Assert.Empty(provider.Requests[^1].ContextLines);
    }

    [Fact]
    public async Task ARefusedReadingNeverReachesTheSecondReader()
    {
        // THE placement test. Every other guard in the pipeline sits ahead of the cache lookup,
        // because that is where the spending used to start. It is not any more: the vision
        // escalation was deliberately hoisted ABOVE those guards so that the flagship "words seen,
        // none legible" frame could reach it at all - and that frame is exactly what a capture
        // taken mid-change looks like for a moment. So a confirmation placed where the others sit
        // would refuse the translation and pay for the picture anyway.
        var ocr = new ScriptedOcr().Reads("", confidence: 12f, rejected: 9);
        var cache = new MemoryCache();
        var provider = new FakeProvider("fake").Returns("ترجمة");
        var vision = new StubVisionReader("A fluent sentence that was never on screen");

        var outcome = await Build(ocr, cache, provider, vision).ProcessAsync(AnyFrame,
            options: ProcessOptions.Polled with { Confirm = (_, _) => false });

        Assert.True(outcome.Unconfirmed);
        Assert.Equal(0, vision.Calls);
    }

    [Fact]
    public async Task TheConfirmationSeesTheParsedBodyAndNotTheRawReading()
    {
        // It has to be the same string the gate will compare against next time, or the agreement
        // test is measuring the speaker's name and the OCR's stray punctuation as well as the line.
        var ocr = new ScriptedOcr().Reads("Y'shtola\nCome, the aether here grows unstable.");
        var seen = new List<string>();

        await Build(ocr, new MemoryCache(), new FakeProvider("fake").Returns("ترجمة"))
            .ProcessAsync(AnyFrame, options: ProcessOptions.Polled with
            {
                Confirm = (body, _) => { seen.Add(body); return true; },
            });

        Assert.Equal(["Come, the aether here grows unstable."], seen);
    }

    [Fact]
    public async Task TheConfirmationIsToldWhenWordsWereSeenButNoneWereLegible()
    {
        // The one distinction that lets "illegible twice running" mean something. Both of these
        // produce an empty body and they mean opposite things: nothing is there, versus ten words
        // are there and every one was thrown away.
        var answers = new List<bool>();

        var blank = new ScriptedOcr().Reads("", confidence: 0f, rejected: 0);
        await Build(blank, new MemoryCache(), new FakeProvider("f")).ProcessAsync(AnyFrame,
            options: ProcessOptions.Polled with
            {
                Confirm = (_, illegible) => { answers.Add(illegible); return false; },
            });

        var unreadable = new ScriptedOcr().Reads("", confidence: 11f, rejected: 9);
        await Build(unreadable, new MemoryCache(), new FakeProvider("f")).ProcessAsync(AnyFrame,
            options: ProcessOptions.Polled with
            {
                Confirm = (_, illegible) => { answers.Add(illegible); return false; },
            });

        Assert.Equal([false, true], answers);
    }

    [Fact]
    public async Task ConfirmingProceedsExactlyAsIfNoHookWereThere()
    {
        var ocr = new ScriptedOcr().Reads("Come, the aether here grows unstable.");
        var cache = new MemoryCache();
        var provider = new FakeProvider("fake").Returns("ترجمة");

        var outcome = await Build(ocr, cache, provider).ProcessAsync(AnyFrame,
            options: ProcessOptions.Polled with { Confirm = (_, _) => true });

        Assert.False(outcome.Unconfirmed);
        Assert.NotNull(outcome.Result);
        Assert.Single(provider.Calls);
        Assert.Single(cache.Rows);
    }

    [Fact]
    public async Task EveryOtherCallerIsUnaffected()
    {
        // Null for a hotkey press, a snip, a retry, Replay and Settings - all of which ask a
        // question the user just asked out loud, and none of which has a gate to answer to.
        Assert.Null(ProcessOptions.Manual.Confirm);
        Assert.Null(ProcessOptions.Polled.Confirm);
        Assert.Null(ProcessOptions.Isolated.Confirm);

        var ocr = new ScriptedOcr().Reads("Come, the aether here grows unstable.");
        var outcome = await Build(ocr, new MemoryCache(), new FakeProvider("f").Returns("ترجمة"))
            .ProcessAsync(AnyFrame, options: ProcessOptions.Manual);

        Assert.False(outcome.Unconfirmed);
        Assert.NotNull(outcome.Result);
    }
}
