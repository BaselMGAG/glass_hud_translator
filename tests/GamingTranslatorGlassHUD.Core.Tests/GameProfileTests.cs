using GamingTranslatorGlassHUD.Core.Profiles;
using GamingTranslatorGlassHUD.Core.Translation;
using Xunit;

namespace GamingTranslatorGlassHUD.Core.Tests;

public class GameProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"profiles-{Guid.NewGuid():N}");

    private string WriteProfile(string id, string json, string? glossary = null)
    {
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, GameProfileStore.ProfileFileName), json);
        if (glossary is not null)
            File.WriteAllText(Path.Combine(dir, GameProfileStore.GlossaryFileName), glossary);
        return dir;
    }

    [Fact]
    public void DiscoversProfileFoldersAndSkipsUnderscored()
    {
        WriteProfile("ffxiv", """{"id":"ffxiv","displayName":"Final Fantasy XIV"}""");
        WriteProfile("witcher3", """{"id":"witcher3","displayName":"The Witcher 3"}""");
        WriteProfile("_template", """{"id":"_template","displayName":"Template"}""");

        var found = GameProfileStore.Discover(_root);

        Assert.Equal(["ffxiv", "witcher3"], found);
    }

    [Fact]
    public void LoadsGlossaryAndCorrectionsFromTheProfileFolder()
    {
        WriteProfile("ffxiv",
            """{"id":"ffxiv","displayName":"Final Fantasy XIV","styleHint":"Archaic high fantasy."}""",
            """[{"en":"aether","ar":"الأثير"}]""");

        var profile = GameProfileStore.Load(_root, "ffxiv");

        Assert.Equal("Final Fantasy XIV", profile.DisplayName);
        Assert.Equal("Archaic high fantasy.", profile.StyleHint);
        Assert.Equal(1, profile.Glossary.Count);
    }

    [Fact]
    public void ProfileRegionsBecomeFractionalRegionProfiles()
    {
        WriteProfile("ffxiv", """
            {"id":"ffxiv","displayName":"FFXIV",
             "regions":{"dialogue":{"x":0.22,"y":0.70,"w":0.56,"h":0.20}}}
            """);

        var region = GameProfileStore.Load(_root, "ffxiv").RegionOrDefault("dialogue");

        Assert.Equal(0.22, region.RelX, 3);
        Assert.Equal(0.56, region.RelWidth, 3);

        // 1920x1080 client -> pixels
        var resolved = region.Resolve(1920, 1080);
        Assert.Equal(422, resolved.X);
        Assert.Equal(1075, resolved.Width);
    }

    [Fact]
    public void MissingProfilesDirectoryDoesNotCrashTheApp()
    {
        // A misconfigured install should still translate, just without a glossary.
        var profile = GameProfileStore.LoadOrFallback(Path.Combine(_root, "nope"), "ffxiv");

        Assert.Equal("generic", profile.Id);
        Assert.Equal(0, profile.Glossary.Count);
    }

    [Fact]
    public void UnknownProfileIdFallsBackToWhatIsAvailable()
    {
        WriteProfile("ffxiv", """{"id":"ffxiv","displayName":"FFXIV"}""");

        var profile = GameProfileStore.LoadOrFallback(_root, "a-game-that-is-not-installed");

        Assert.Equal("ffxiv", profile.Id);
    }

    [Fact]
    public void ProfileVoiceReachesTheSystemPrompt()
    {
        // The whole point of the profile: a different game must produce a different instruction,
        // otherwise every title gets translated in the same flat register.
        var request = new TranslationRequest("Hold the line!", GameName: "Helldivers 2",
            StyleHint: "Terse military radio chatter.");

        var (system, _) = PromptBuilder.Build(request);

        Assert.Contains("Helldivers 2", system);
        Assert.Contains("Terse military radio chatter.", system);
        Assert.DoesNotContain("Final Fantasy", system);
    }

    [Fact]
    public void MissingStyleHintStillProducesAUsableInstruction()
    {
        var (system, _) = PromptBuilder.Build(new TranslationRequest("Hello.", GameName: "Some Game"));

        Assert.Contains("Some Game", system);
        Assert.Contains("Match the tone of the original", system);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

public class GeneralProfileTests
{
    private static string ProfilesDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "GamingTranslatorGlassHUD.slnx")))
            dir = Path.GetDirectoryName(dir);

        return Path.Combine(dir ?? ".", "profiles");
    }

    [Fact]
    public void ShippedProfilesLoad()
    {
        var found = GameProfileStore.Discover(ProfilesDirectory());

        Assert.Contains("ffxiv", found);
        Assert.Contains("general", found);
        Assert.DoesNotContain("_template", found);
    }

    [Fact]
    public void FfxivStaysTheDefaultWhenNoneIsChosen()
    {
        // Discovery is alphabetical, so a profile named before "ffxiv" would silently become the
        // default for everyone who has not picked one. "general" was named to sort after it.
        Assert.Equal("ffxiv", GameProfileStore.LoadOrFallback(ProfilesDirectory(), null).Id);
    }

    [Fact]
    public void GeneralProfileHasNoWindowTitles()
    {
        // An empty title list is what makes the region measure against the whole screen instead of
        // one application's window - that is the entire mechanism behind "works on anything".
        var general = GameProfileStore.Load(ProfilesDirectory(), "general");

        Assert.Empty(general.WindowTitles);
        Assert.False(string.IsNullOrWhiteSpace(general.StyleHint));
    }

    [Fact]
    public void FfxivProfileStillCarriesItsGlossaryAndWindowTitle()
    {
        var ffxiv = GameProfileStore.Load(ProfilesDirectory(), "ffxiv");

        Assert.NotEmpty(ffxiv.WindowTitles);
        Assert.True(ffxiv.Glossary.Count > 50, $"glossary had {ffxiv.Glossary.Count} terms");
    }
}
