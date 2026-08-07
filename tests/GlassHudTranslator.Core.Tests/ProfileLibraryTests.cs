using System.Text.Json;
using GlassHudTranslator.Core.Glossary;
using GlassHudTranslator.Core.Profiles;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The profile library exists so a non-programmer never has to touch JSON. Most of what can go
/// wrong is therefore invisible to the person it goes wrong for: a profile written where an update
/// will delete it, a display name that escapes the profiles directory, a deleted game that comes
/// back. These are the tests for those.
/// </summary>
public sealed class ProfileLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ghtp-{Guid.NewGuid():N}");
    private readonly string _bundled;
    private readonly string _user;

    public ProfileLibraryTests()
    {
        _bundled = Path.Combine(_root, "app", "profiles");
        _user = Path.Combine(_root, "appdata", "profiles");
        Directory.CreateDirectory(_bundled);
        Directory.CreateDirectory(_user);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ProfileLibrary Library() => new(_bundled, _user);

    private void WriteBundled(string id, string displayName = "Bundled game")
    {
        var directory = Path.Combine(_bundled, id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, GameProfileStore.ProfileFileName),
            $$"""{"id":"{{id}}","displayName":"{{displayName}}","windowTitles":["Bundled"]}""");
    }

    private static GameProfileDraft Draft(string name, string? existingId = null) => new()
    {
        ExistingId = existingId,
        DisplayName = name,
    };

    // ── where files land ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ANewProfileIsWrittenToTheUserDirectoryNotTheAppFolder()
    {
        // The whole reason this class exists. The app folder is replaced wholesale by an update -
        // the release notes say so - so a profile written there is deleted the first time the user
        // updates, taking their regions and glossary with it.
        var id = Library().Save(Draft("Baldur's Gate 3"));

        Assert.True(File.Exists(Path.Combine(_user, id, GameProfileStore.ProfileFileName)));
        Assert.False(Directory.Exists(Path.Combine(_bundled, id)));
    }

    [Fact]
    public void EditingABundledProfileWritesACopyAndLeavesTheOriginalAlone()
    {
        WriteBundled("ffxiv", "Final Fantasy XIV");
        var library = Library();

        library.Save(Draft("FFXIV, my edit", existingId: "ffxiv"));

        Assert.Equal(ProfileOrigin.Override, library.OriginOf("ffxiv"));
        Assert.Equal("FFXIV, my edit", library.Load("ffxiv").DisplayName);

        // The shipped file is untouched, so it keeps improving with each release underneath.
        Assert.Equal("Final Fantasy XIV", GameProfileStore.Load(_bundled, "ffxiv").DisplayName);
    }

    [Fact]
    public void TheUsersCopyWinsOverTheBundledOne()
    {
        WriteBundled("ffxiv", "Shipped");
        Library().Save(Draft("Mine", existingId: "ffxiv"));

        Assert.Equal("Mine", Library().Load("ffxiv").DisplayName);
        Assert.Single(Library().Discover(), "ffxiv");
    }

    [Fact]
    public void ResettingAnOverrideGoesBackToTheShippedProfile()
    {
        WriteBundled("ffxiv", "Shipped");
        var library = Library();
        library.Save(Draft("Mine", existingId: "ffxiv"));

        Assert.True(library.Reset("ffxiv"));
        Assert.Equal("Shipped", library.Load("ffxiv").DisplayName);
        Assert.Equal(ProfileOrigin.Bundled, library.OriginOf("ffxiv"));
    }

    // ── deletion ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeletingABundledProfileKeepsItDeletedRatherThanWaitingForAnUpdateToRestoreIt()
    {
        // Its files live in the app folder, which this class cannot write to and an update
        // replaces. A plain delete would work exactly until the next release.
        WriteBundled("ffxiv");
        var library = Library();

        library.Delete("ffxiv");

        Assert.DoesNotContain("ffxiv", library.Discover());
        Assert.DoesNotContain("ffxiv", Library().Discover());          // and after a restart
        Assert.True(File.Exists(Path.Combine(_bundled, "ffxiv", GameProfileStore.ProfileFileName)));
    }

    [Fact]
    public void AddingBackAProfileThatWasDeletedUndeletesIt()
    {
        WriteBundled("ffxiv");
        var library = Library();
        library.Delete("ffxiv");

        library.Save(Draft("Final Fantasy XIV", existingId: "ffxiv"));

        Assert.Contains("ffxiv", library.Discover());
    }

    [Fact]
    public void DeletingAUserProfileRemovesItsFolder()
    {
        var library = Library();
        var id = library.Save(Draft("Some Game"));

        library.Delete(id);

        Assert.False(Directory.Exists(Path.Combine(_user, id)));
        Assert.DoesNotContain(id, library.Discover());
    }

    [Fact]
    public void TheGeneralProfileCannotBeDeletedOrEdited()
    {
        // It is the fallback that reads anything on screen, and what the app falls back to when a
        // game profile is removed. Deleting the last profile would leave nothing to translate with.
        Assert.False(ProfileLibrary.CanDelete(ProfileLibrary.GeneralProfileId));
        Assert.True(ProfileLibrary.IsReadOnly(ProfileLibrary.GeneralProfileId));

        WriteBundled(ProfileLibrary.GeneralProfileId);
        Assert.Throws<InvalidOperationException>(
            () => Library().Delete(ProfileLibrary.GeneralProfileId));
    }

    [Fact]
    public void EverythingElseIncludingTheShippedGameCanBeDeleted()
    {
        Assert.True(ProfileLibrary.CanDelete("ffxiv"));
        Assert.False(ProfileLibrary.IsReadOnly("ffxiv"));
    }

    // ── ids from user-typed names ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Baldur's Gate 3", "baldur-s-gate-3")]
    [InlineData("  Elden Ring  ", "elden-ring")]
    [InlineData("Persona 5 Royal", "persona-5-royal")]
    [InlineData("NieR:Automata", "nier-automata")]
    public void NamesBecomeReadableFolderNames(string name, string expected)
    {
        Assert.Equal(expected, ProfileLibrary.SlugFor(name));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32")]
    [InlineData("/absolute/path")]
    [InlineData("C:\\Windows")]
    [InlineData("game/../../..")]
    public void ANameCannotEscapeTheProfilesDirectory(string hostile)
    {
        // This string comes from a text box and becomes a path. Nothing that could climb out of
        // the profiles folder may survive.
        var slug = ProfileLibrary.SlugFor(hostile);

        Assert.DoesNotContain("..", slug);
        Assert.DoesNotContain('/', slug);
        Assert.DoesNotContain('\\', slug);
        Assert.DoesNotContain(':', slug);
        Assert.Equal(slug, Path.GetFileName(slug));
    }

    [Fact]
    public void AHostileNameStillLandsInsideTheUserDirectory()
    {
        var library = Library();
        var id = library.Save(Draft("../../escaped"));

        var written = Path.GetFullPath(Path.Combine(_user, id));
        Assert.StartsWith(Path.GetFullPath(_user) + Path.DirectorySeparatorChar, written);
    }

    [Theory]
    [InlineData("ファイナルファンタジー")]
    [InlineData("لعبة عربية")]
    [InlineData("???")]
    [InlineData("")]
    public void ANameWithNoLatinLettersStillGetsAFolder(string name)
    {
        // Slugging an Arabic or Japanese title yields nothing. The profile still has to exist, and
        // the display name - which is what the user actually sees - keeps the original text.
        var slug = ProfileLibrary.SlugFor(name);

        Assert.NotEmpty(slug);
        Assert.Equal(slug, Path.GetFileName(slug));
    }

    [Fact]
    public void ANonLatinNameIsPreservedForDisplayEvenThoughTheFolderIsAscii()
    {
        var library = Library();
        var id = library.Save(Draft("لعبة عربية"));

        Assert.Equal("لعبة عربية", library.Load(id).DisplayName);
    }

    [Fact]
    public void TheProfileListCarriesDisplayNamesRatherThanFolderNames()
    {
        // What the user sees must be what they typed. The folder for "Baldur's Gate 3" is
        // baldur-s-gate-3, and showing that in the picker is the same defect as building a button
        // caption out of a stored key.
        var library = Library();
        library.Save(Draft("Baldur's Gate 3"));

        var listed = library.List().Single();

        Assert.Equal("baldur-s-gate-3", listed.Id);
        Assert.Equal("Baldur's Gate 3", listed.DisplayName);
    }

    [Fact]
    public void AnArabicNameSurvivesIntoTheProfileList()
    {
        var library = Library();
        library.Save(Draft("لعبة عربية"));

        Assert.Equal("لعبة عربية", library.List().Single().DisplayName);
    }

    [Fact]
    public void AProfileWithAnUnreadableFileStillAppearsInTheList()
    {
        // Falling back to the id keeps it selectable and fixable. Dropping it from the list would
        // leave the user with a profile that exists, is selected, and cannot be seen.
        var broken = Path.Combine(_user, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, GameProfileStore.ProfileFileName), "{ not json");

        Assert.Equal("broken", Library().List().Single().DisplayName);
    }

    [Fact]
    public void AnEditedBundledProfileListsUnderItsNewName()
    {
        WriteBundled("ffxiv", "Final Fantasy XIV");
        var library = Library();
        library.Save(Draft("FF14", existingId: "ffxiv"));

        Assert.Equal("FF14", library.List().Single().DisplayName);
    }

    [Fact]
    public void AnUnderscoreNameCannotProduceAHiddenProfile()
    {
        // Discover() skips "_"-prefixed folders, which is how _template stays out of the list. A
        // name that slugged to one would create a profile that immediately vanished.
        var slug = ProfileLibrary.SlugFor("_template");

        Assert.False(slug.StartsWith('_'));
    }

    [Fact]
    public void TwoGamesWithTheSameNameGetDifferentFolders()
    {
        var library = Library();

        var first = library.Save(Draft("Same Name"));
        var second = library.Save(Draft("Same Name"));

        Assert.NotEqual(first, second);
        Assert.Equal(2, library.Discover().Count);
    }

    [Fact]
    public void ANewProfileDoesNotCollideWithABundledOne()
    {
        WriteBundled("elden-ring");
        var id = Library().Save(Draft("Elden Ring"));

        Assert.NotEqual("elden-ring", id);
    }

    // ── what gets written ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ASavedProfileIsCompleteAndReloadable()
    {
        var library = Library();
        var id = library.Save(new GameProfileDraft
        {
            DisplayName = "Test Game",
            WindowTitles = ["Test Game", "  ", "Test Game"],
            ProcessNames = ["testgame.exe"],
            StyleHint = StylePreset.Epic.Hint,
            HasSpeakerNames = false,
            Terms = [new GlossaryTerm("Aldric", "ألدريك", "person", [])],
        });

        var loaded = library.Load(id);

        Assert.Equal("Test Game", loaded.DisplayName);
        Assert.False(loaded.HasSpeakerNames);
        Assert.Equal(StylePreset.Epic.Hint, loaded.StyleHint);

        // Blank and duplicate entries are dropped rather than stored - both would silently never
        // match a window.
        Assert.Single(loaded.WindowTitles, "Test Game");
        Assert.Single(loaded.ProcessNames, "testgame.exe");
        Assert.Single(loaded.Glossary.Terms);
        Assert.Equal("ألدريك", loaded.Glossary.Terms[0].Ar);

        // All three files, so the folder is a complete profile someone could share as-is.
        var directory = Path.Combine(_user, id);
        Assert.True(File.Exists(Path.Combine(directory, GameProfileStore.ProfileFileName)));
        Assert.True(File.Exists(Path.Combine(directory, GameProfileStore.GlossaryFileName)));
        Assert.True(File.Exists(Path.Combine(directory, GameProfileStore.CorrectionsFileName)));
    }

    [Fact]
    public void ArabicIsWrittenAsArabicRatherThanEscapes()
    {
        // The glossary is the file most likely to be corrected by a native speaker reading it in a
        // text editor. \u0623\u0644... on every line would make it useless to them.
        var id = Library().Save(new GameProfileDraft
        {
            DisplayName = "Test",
            Terms = [new GlossaryTerm("Aldric", "ألدريك", "person", [])],
        });

        var json = File.ReadAllText(Path.Combine(_user, id, GameProfileStore.GlossaryFileName));

        Assert.Contains("ألدريك", json);
        Assert.DoesNotContain("\\u", json);
    }

    [Fact]
    public void EmptyTermRowsAreNotWritten()
    {
        var id = Library().Save(new GameProfileDraft
        {
            DisplayName = "Test",
            Terms =
            [
                new GlossaryTerm("", "", "term", []),
                new GlossaryTerm("Kept", "محفوظ", "term", []),
                new GlossaryTerm("NoArabic", "  ", "term", []),
            ],
        });

        Assert.Single(Library().Load(id).Glossary.Terms);
    }

    [Fact]
    public void EditingKeepsTheStartingRectanglesAProfileAlreadyHad()
    {
        // Renaming a game must not throw away the rectangles that make a shared profile useful to
        // whoever imports it next.
        var directory = Path.Combine(_bundled, "ffxiv");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, GameProfileStore.ProfileFileName),
            """
            {"id":"ffxiv","displayName":"FFXIV",
             "regions":{"dialogue":{"x":0.22,"y":0.70,"w":0.56,"h":0.20}}}
            """);

        var library = Library();
        library.Save(Draft("Renamed", existingId: "ffxiv"));

        var regions = library.Load("ffxiv").Regions;
        Assert.True(regions.ContainsKey("dialogue"));
        Assert.Equal(0.22, regions["dialogue"].X, 3);
    }

    [Fact]
    public void AProfileWithNoWindowBindingIsScreenRelative()
    {
        var id = Library().Save(Draft("Anything"));

        Assert.False(Library().Load(id).IsWindowBound);
    }

    [Fact]
    public void AProfileBoundByProcessNameAloneCountsAsWindowBound()
    {
        // Process names were added after window titles, so a profile carrying only the newer field
        // must not be treated as "anything on screen".
        var id = Library().Save(new GameProfileDraft
        {
            DisplayName = "Test", ProcessNames = ["testgame"],
        });

        Assert.True(Library().Load(id).IsWindowBound);
    }

    // ── loading, when things are wrong ────────────────────────────────────────────────────

    [Fact]
    public void AProfileEditedIntoInvalidJsonDoesNotStopTheAppStarting()
    {
        WriteBundled("good", "Good");
        var broken = Path.Combine(_user, "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, GameProfileStore.ProfileFileName), "{ not json");

        var loaded = Library().LoadOrFallback("broken");

        Assert.NotNull(loaded);
    }

    [Fact]
    public void AMissingProfileFallsBackRatherThanThrowing()
    {
        WriteBundled("ffxiv");

        Assert.Equal("ffxiv", Library().LoadOrFallback("no-such-profile").Id);
    }

    [Fact]
    public void NoProfilesAtAllStillYieldsSomethingUsable()
    {
        var loaded = Library().LoadOrFallback(null);

        Assert.NotNull(loaded);
        Assert.NotEmpty(loaded.DisplayName);
    }

    [Fact]
    public void ACorruptTombstoneFileDoesNotHideEveryProfile()
    {
        // Better to show a profile the user deleted than to start with an empty list they have no
        // way to explain or recover from.
        WriteBundled("ffxiv");
        File.WriteAllText(Path.Combine(_user, "_removed.json"), "{{{ broken");

        Assert.Contains("ffxiv", Library().Discover());
    }

    [Fact]
    public void TheTombstoneFileIsNotItselfListedAsAProfile()
    {
        WriteBundled("ffxiv");
        var library = Library();
        library.Delete("ffxiv");

        Assert.DoesNotContain(library.Discover(), id => id.StartsWith('_'));
    }

    // ── style presets ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryStylePresetHasRealPromptText()
    {
        foreach (var preset in StylePreset.All)
        {
            Assert.NotEmpty(preset.Id);
            Assert.True(preset.Hint.Length > 40, $"{preset.Id} hint is too short to steer a model.");
        }
    }

    [Fact]
    public void AStoredHintIsMatchedBackToItsPreset()
    {
        // So editing a profile lights up the tile it was created with instead of silently falling
        // back to Custom and rewriting the hint on the next save.
        foreach (var preset in StylePreset.All)
            Assert.Equal(preset.Id, StylePreset.Match(preset.Hint)?.Id);
    }

    [Fact]
    public void AHandWrittenHintIsNotClaimedByAPreset()
    {
        Assert.Null(StylePreset.Match("terse military radio chatter"));
    }

    [Fact]
    public void NoHintAtAllMeansPlain()
    {
        Assert.Equal(StylePreset.PlainId, StylePreset.Match(null)?.Id);
        Assert.Equal(StylePreset.PlainId, StylePreset.Match("   ")?.Id);
    }

    [Fact]
    public void TheShippedProfilesParseAndAreConsistent()
    {
        // Guards the repository's own profiles/ folder, which the editor now has to round-trip.
        var repo = TestPaths.RepoRoot;
        var profilesDirectory = Path.Combine(repo, "profiles");
        if (!Directory.Exists(profilesDirectory)) return;

        foreach (var id in GameProfileStore.Discover(profilesDirectory))
        {
            var profile = GameProfileStore.Load(profilesDirectory, id);

            Assert.Equal(id, profile.Id);
            Assert.NotEmpty(profile.DisplayName);
        }
    }

    [Fact]
    public void TheGeneralProfileStaysScreenRelative()
    {
        var profilesDirectory = Path.Combine(TestPaths.RepoRoot, "profiles");
        if (!Directory.Exists(Path.Combine(profilesDirectory, ProfileLibrary.GeneralProfileId))) return;

        var general = GameProfileStore.Load(profilesDirectory, ProfileLibrary.GeneralProfileId);

        // Binding it to a window would break the one thing it is for: reading a browser, a PDF or
        // a video player, none of which this app knows the name of.
        Assert.False(general.IsWindowBound);
    }

    [Fact]
    public void WritingThenReadingSurvivesAJsonRoundTrip()
    {
        var id = Library().Save(new GameProfileDraft
        {
            DisplayName = "Round Trip",
            WindowTitles = ["A Window"],
            ProcessNames = ["proc"],
            StyleHint = StylePreset.Comic.Hint,
        });

        var text = File.ReadAllText(Path.Combine(_user, id, GameProfileStore.ProfileFileName));
        using var document = JsonDocument.Parse(text);

        Assert.Equal(id, document.RootElement.GetProperty("id").GetString());
        Assert.Equal("proc", document.RootElement.GetProperty("processNames")[0].GetString());
    }
}
