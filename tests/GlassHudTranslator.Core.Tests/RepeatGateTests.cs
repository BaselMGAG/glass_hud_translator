using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Glossary;
using GlassHudTranslator.Core.Pipeline;
using GlassHudTranslator.Core.Translation;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The second net, at the layer it has to be at. Everything here is about what happens BEFORE the
/// cache is consulted and before a provider is asked, because a line that reaches either of those
/// has already cost the thing the gate exists to save.
/// </summary>
public class RepeatGateTests
{
    private static readonly Frame AnyFrame = new FrameBuilder(8, 8, new Rgb(0, 0, 0)).Build();

    private static (TranslationPipeline Pipeline, ScriptedOcr Ocr, FakeProvider Provider, MemoryCache Cache)
        Build(int answers = 8)
    {
        var provider = new FakeProvider("fake");
        for (var i = 0; i < answers; i++) provider.Returns("ترجمة");

        var ocr = new ScriptedOcr();
        var cache = new MemoryCache();

        return (new TranslationPipeline(ocr, cache, new GlossaryMatcher(GlossaryStore.Empty),
            new ProviderRouter([(provider, 600)])), ocr, provider, cache);
    }

    private static Task<PipelineOutcome> Poll(TranslationPipeline pipeline, ScriptedOcr ocr, string line)
    {
        ocr.Reads(line);
        return pipeline.ProcessAsync(AnyFrame, options: ProcessOptions.Polled);
    }

    private static Task<PipelineOutcome> Press(TranslationPipeline pipeline, ScriptedOcr ocr, string line)
    {
        ocr.Reads(line);
        return pipeline.ProcessAsync(AnyFrame, options: ProcessOptions.Manual);
    }

    private static Task<PipelineOutcome> Snip(TranslationPipeline pipeline, ScriptedOcr ocr, string line)
    {
        ocr.Reads(line);
        return pipeline.ProcessAsync(AnyFrame, options: ProcessOptions.Isolated);
    }

    private const string Line = "Come, the aether here grows unstable.";
    private const string Jittered = "Come. the aether here grows unstabIe.";

    [Fact]
    public async Task AJitteredRereadNeverReachesTheCacheOrTheProvider()
    {
        var (pipeline, ocr, provider, cache) = Build();

        await Poll(pipeline, ocr, Line);
        var lookupsAfterFirst = cache.Lookups;

        var second = await Poll(pipeline, ocr, Jittered);

        Assert.True(second.Repeat);
        Assert.Null(second.Result);

        // Ahead of BOTH, which is the point. A gate that ran after the lookup would still spend the
        // request whenever OCR jitter produced a key that happened to miss - and jitter is exactly
        // what produces a missing key.
        Assert.Equal(lookupsAfterFirst, cache.Lookups);
        Assert.Single(provider.Calls);
    }

    [Fact]
    public async Task ARealNextLineGoesStraightThrough()
    {
        var (pipeline, ocr, provider, _) = Build();

        await Poll(pipeline, ocr, Line);
        var second = await Poll(pipeline, ocr, "We must reach Limsa Lominsa by nightfall.");

        Assert.False(second.Repeat);
        Assert.NotNull(second.Result);
        Assert.Equal(2, provider.Calls.Count);
    }

    [Fact]
    public async Task AHotkeyPressIsAnsweredEvenWhenItIsTheSameLine()
    {
        var (pipeline, ocr, provider, _) = Build();

        await Poll(pipeline, ocr, Line);
        var pressed = await Press(pipeline, ocr, Line);

        // A poll is one of dozens a minute; a press is a question. Suppressing it would leave the
        // overlay blank after the user asked for something, which reads as the hotkey not working.
        Assert.False(pressed.Repeat);
        Assert.NotNull(pressed.Result);

        // It is a cache hit, so it costs nothing - the point is that it produces an answer.
        Assert.True(pressed.Result!.FromCache);
        Assert.Single(provider.Calls);
    }

    [Fact]
    public async Task AManualPressIsTheLineLaterPollsAreComparedAgainst()
    {
        var (pipeline, ocr, provider, _) = Build();

        await Press(pipeline, ocr, Line);
        var polled = await Poll(pipeline, ocr, Jittered);

        // Being unsuppressible and being the reference are different questions, and this test is
        // what forced them apart. A press puts a line on the overlay; the poll half a second later
        // reads the same pixels with one comma misread. If the press left no reference that poll is
        // a fresh translation - and the cache does not save it either, because a different string
        // is a different key. One press, one request, and the jitter costs nothing.
        Assert.True(polled.Repeat);
        Assert.Single(provider.Calls);
    }

    [Fact]
    public async Task ASnipDoesNotPoisonTheWatchedRegionsReference()
    {
        var (pipeline, ocr, provider, _) = Build();

        await Poll(pipeline, ocr, Line);
        await Snip(pipeline, ocr, "Restores 400 HP to the target.");

        // The dialogue box has not changed, so the next poll of it is still a repeat. If the snip
        // had overwritten the reference, this would translate the same line a second time.
        var again = await Poll(pipeline, ocr, Jittered);

        Assert.True(again.Repeat);
        Assert.Equal(2, provider.Calls.Count);
    }

    [Fact]
    public async Task ASnipIsNeverSuppressedAsARepeatOfItself()
    {
        var (pipeline, ocr, provider, _) = Build();

        await Snip(pipeline, ocr, "Restores 400 HP to the target.");
        var again = await Snip(pipeline, ocr, "Restores 400 HP to the target.");

        // Dragging a box around the same tooltip twice is a deliberate act both times.
        Assert.False(again.Repeat);
        Assert.NotNull(again.Result);
    }

    [Fact]
    public async Task ASnipNeitherReadsNorWritesTheConversation()
    {
        var (pipeline, ocr, provider, _) = Build();

        await Poll(pipeline, ocr, "The first line spoken.");
        await Poll(pipeline, ocr, "The second line spoken.");

        await Snip(pipeline, ocr, "Restores 400 HP to the target.");

        // Read: a menu tooltip translated with two lines of unrelated dialogue behind it is steered
        // by a conversation it is not part of.
        Assert.Empty(provider.Requests[^1].ContextLines);

        await Poll(pipeline, ocr, "The third line spoken.");

        // Write: and the dialogue that follows must not have a tooltip in its history, which is
        // where the pronouns of the next sentence come from.
        Assert.Equal(
            ["The first line spoken.", "The second line spoken."],
            provider.Requests[^1].ContextLines);
    }

    [Fact]
    public async Task ForgettingTheReferenceMakesTheNextPollNewAgain()
    {
        var (pipeline, ocr, provider, _) = Build();

        await Poll(pipeline, ocr, Line);
        pipeline.ResetRepeatGuard();

        var again = await Poll(pipeline, ocr, Line);

        // This is what switching auto-watch off and on again has to do. Without it the toggle looks
        // broken: the screen has not changed, so nothing is translated, so nothing appears.
        Assert.False(again.Repeat);
        Assert.NotNull(again.Result);
    }

    [Fact]
    public async Task ForgettingTheConversationForgetsTheReferenceToo()
    {
        var (pipeline, ocr, _, _) = Build();

        await Poll(pipeline, ocr, Line);
        pipeline.ResetContext();

        Assert.False((await Poll(pipeline, ocr, Line)).Repeat);
    }

    [Fact]
    public async Task ARepeatIsNotMistakenForAnEmptyRegion()
    {
        var (pipeline, ocr, _, _) = Build();

        await Poll(pipeline, ocr, Line);
        var repeat = await Poll(pipeline, ocr, Jittered);

        // Both have a null Result, and the App branches on that to decide whether to clear the
        // overlay and start counting toward "your capture region is probably wrong". A repeat is
        // the opposite situation - the region is working perfectly - so it has to be distinguishable.
        Assert.True(repeat.Repeat);
        Assert.NotEqual(0, repeat.Body.Trim().Length);
    }
}
