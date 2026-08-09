using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Glossary;
using GlassHudTranslator.Core.Pipeline;
using GlassHudTranslator.Core.Text;
using GlassHudTranslator.Core.Translation;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

public class ArabicDiacriticsTests
{
    [Fact]
    public void APlainSentenceIsHandedBackUntouched()
    {
        const string plain = "تعال، فالأثير هنا يزداد اضطرابا.";

        // Reference equality: most answers carry no marks at all, and the common path should not
        // allocate a copy to discover that.
        Assert.Same(plain, ArabicText.WithoutDiacritics(plain));
    }

    [Fact]
    public void EveryHarakaIsRemoved()
    {
        // fatha, damma, kasra, sukun, shadda, and all three tanween.
        const string vowelled = "مَرْحَبًا " +
                                "بِكُمْ " +
                                "جِدًّا";

        Assert.Equal("مرحبا بكم جدا", ArabicText.WithoutDiacritics(vowelled));
    }

    [Fact]
    public void TheSuperscriptAlefGoes()
    {
        Assert.Equal("هذا", ArabicText.WithoutDiacritics("هَٰذَا"));
    }

    [Fact]
    public void LettersSpelledWithACombiningHamzaSurvive()
    {
        // U+0653-U+0655 are how آ, أ and إ are written when a text does not use the precomposed
        // forms. Stripping them would change letters, not decoration - and a lost hamza under an
        // alef is a different word, not a plainer one.
        const string spelled = "آمن أخذ إلى";

        Assert.Equal(spelled, ArabicText.WithoutDiacritics(spelled));
    }

    [Fact]
    public void LatinAndPunctuationAreLeftAlone()
    {
        const string mixed = "اذهب إلى Limsa Lominsa — 42% منها.";

        Assert.Equal(mixed, ArabicText.WithoutDiacritics(mixed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInputIsNotACrash(string input)
    {
        Assert.Equal(input, ArabicText.WithoutDiacritics(input));
    }
}

/// <summary>
/// Where the strip happens, which matters more than how. It runs on the way OUT, so the cache and
/// the log keep what the provider actually said and the switch is instant in both directions.
/// </summary>
public class PipelineDiacriticsTests
{
    private static readonly Frame AnyFrame = new FrameBuilder(8, 8, new Rgb(0, 0, 0)).Build();

    private const string Vowelled = "مَرْحَبًا";
    private const string Plain = "مرحبا";

    private static (TranslationPipeline Pipeline, ScriptedOcr Ocr, MemoryCache Cache) Build()
    {
        var ocr = new ScriptedOcr();
        var cache = new MemoryCache();
        var router = new ProviderRouter(
            [(new FixedProvider(Vowelled), 600)], new RouterOptions());

        return (new TranslationPipeline(ocr, cache, new GlossaryMatcher(GlossaryStore.Empty), router),
            ocr, cache);
    }

    [Fact]
    public async Task TashkeelIsStrippedBeforeItReachesTheOverlay()
    {
        var (pipeline, ocr, _) = Build();
        ocr.Reads("Welcome to the Rising Stones.");

        var outcome = await pipeline.ProcessAsync(AnyFrame);

        Assert.Equal(Plain, outcome.Result!.Text);
    }

    [Fact]
    public async Task TheCacheKeepsWhatTheProviderActuallySaid()
    {
        // So that turning the setting on re-presents lines already translated, rather than only
        // affecting sentences the player has not reached yet. A display preference must not be
        // baked into a row that outlives it.
        var (pipeline, ocr, cache) = Build();
        ocr.Reads("Welcome to the Rising Stones.");

        await pipeline.ProcessAsync(AnyFrame);

        Assert.Equal(Vowelled, cache.Rows.Values.Single().Arabic);
    }

    [Fact]
    public async Task TurningItOnShowsTheMarksTheProviderSent()
    {
        var (pipeline, ocr, _) = Build();
        pipeline.Diacritics = true;
        ocr.Reads("Welcome to the Rising Stones.");

        var outcome = await pipeline.ProcessAsync(AnyFrame);

        Assert.Equal(Vowelled, outcome.Result!.Text);
    }

    [Fact]
    public async Task ACacheHitIsPresentedTheSameWayAsAFreshTranslation()
    {
        // Two return sites in ProcessAsync, and only one of them is the live path. A strip applied
        // to just the live one would leave the second reading of a line vowelled and the first not.
        var (pipeline, ocr, _) = Build();
        ocr.Reads("Welcome to the Rising Stones.");
        await pipeline.ProcessAsync(AnyFrame);

        ocr.Reads("Welcome to the Rising Stones.");
        var second = await pipeline.ProcessAsync(AnyFrame);

        Assert.True(second.Result!.FromCache);
        Assert.Equal(Plain, second.Result.Text);
    }

    [Fact]
    public void ThePromptAsksForPlainArabicByDefault()
    {
        // Belt as well as braces. Stripping is what guarantees the display; asking is what stops us
        // paying for the marks first - on Groq, output tokens are the metered resource.
        var (system, _) = PromptBuilder.Build(new TranslationRequest("Hello."));

        Assert.Contains("Do NOT add", system);
        Assert.Contains("تشكيل", system);
    }

    [Fact]
    public void ThePromptAsksForThemWhenTheyAreWanted()
    {
        var (system, _) = PromptBuilder.Build(new TranslationRequest("Hello.", Diacritics: true));

        Assert.Contains("تشكيل كامل", system);
        Assert.DoesNotContain("Do NOT add", system);
    }

    private sealed class FixedProvider(string arabic) : ITranslationProvider
    {
        public string Name => "fixed";

        public IReadOnlyList<string> Models => ["m"];

        public Task<string> TranslateAsync(TranslationRequest request, string model, CancellationToken ct) =>
            Task.FromResult(arabic);
    }
}

public class DiacriticsSettingTests
{
    [Fact]
    public void TashkeelIsOffUntilSomeoneAsksForIt()
    {
        Assert.False(new AppSettings().Diacritics);
    }

    [Fact]
    public void TheChoiceSurvivesASaveAndLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ghdia-{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings { Diacritics = true }.Save(path);

            Assert.True(AppSettings.Load(path).Diacritics);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ASettingsFileWrittenBeforeTheSwitchExistedKeepsTashkeelOff()
    {
        // Those files are in every existing user's AppData. The absent field must land on the
        // behaviour the release notes describe, not on whatever default(bool) happens to be.
        var path = Path.Combine(Path.GetTempPath(), $"ghdia-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"profile":"ffxiv","register":"Egyptian"}""");
            var loaded = AppSettings.Load(path);

            Assert.False(loaded.Diacritics);
            Assert.Equal(ArabicRegister.Egyptian, loaded.Register);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
