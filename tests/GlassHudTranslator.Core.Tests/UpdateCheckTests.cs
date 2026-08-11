using System.Net;
using System.Text;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Update;
using Xunit;

namespace GlassHudTranslator.Core.Tests;

/// <summary>
/// The update check has no business failing loudly, so most of these assert that a bad answer
/// produces silence rather than an exception or a wrong claim. The one distinction it must get
/// right is "you are current" versus "I could not ask" - reporting the second as the first would
/// tell someone behind a captive portal that they are up to date forever.
/// </summary>
[Collection(SettingsStaticCollection.Name)]
public class UpdateCheckTests
{
    private static readonly Version Current = new(0, 2, 1, 0);

    // ── version parsing ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("v0.2.1", 0, 2, 1)]
    [InlineData("0.2.1", 0, 2, 1)]
    [InlineData("V1.0.0", 1, 0, 0)]
    [InlineData("  v10.20.30  ", 10, 20, 30)]
    public void TagsParseWithOrWithoutTheLeadingV(string tag, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build, 0), UpdateCheck.ParseTag(tag));
    }

    [Theory]
    [InlineData("nightly")]
    [InlineData("v0.3.0-beta.1")]
    [InlineData("release-2026-08")]
    [InlineData("v")]
    [InlineData("")]
    [InlineData("v-1.0.0")]
    public void ATagThatIsNotAVersionIsNotAnUpdate(string tag)
    {
        // Wrong in the safe direction: an unrecognised tag says nothing rather than nagging about
        // a release the user cannot compare against.
        Assert.Null(UpdateCheck.ParseTag(tag));
    }

    [Fact]
    public void ThreePartAndFourPartVersionsCompareAsEqual()
    {
        // Version leaves an unspecified component at -1, so 0.2.1 sorts below 0.2.1.0. A release
        // tagged one way against a build stamped the other would otherwise notify forever.
        Assert.False(UpdateCheck.IsNewer(new Version(0, 2, 1), new Version(0, 2, 1, 0)));
        Assert.False(UpdateCheck.IsNewer(new Version(0, 2, 1, 0), new Version(0, 2, 1)));
    }

    [Fact]
    public void OnlyAHigherVersionCounts()
    {
        Assert.True(UpdateCheck.IsNewer(new Version(0, 3, 0), Current));
        Assert.True(UpdateCheck.IsNewer(new Version(1, 0, 0), Current));
        Assert.False(UpdateCheck.IsNewer(new Version(0, 2, 0), Current));
        Assert.False(UpdateCheck.IsNewer(new Version(0, 2, 1), Current));
    }

    [Fact]
    public void TenComesAfterNineRatherThanAfterOne()
    {
        // The failure a string comparison would produce: "v0.10.0" < "v0.9.0" alphabetically.
        Assert.True(UpdateCheck.IsNewer(
            UpdateCheck.ParseTag("v0.10.0")!, UpdateCheck.ParseTag("v0.9.0")!));
    }

    // ── payload parsing ───────────────────────────────────────────────────────────────────

    private const string RealisticPayload = """
        {
          "tag_name": "v0.3.0",
          "html_url": "https://github.com/basel2000de/glass_hud_translator/releases/tag/v0.3.0",
          "draft": false,
          "prerelease": false,
          "assets": [
            {"name": "GlassHudTranslator-v0.3.0-win-x64.zip", "size": 57445420}
          ]
        }
        """;

    [Fact]
    public void AReleaseIsReadOutOfTheApiResponse()
    {
        var release = UpdateCheck.Parse(RealisticPayload);

        Assert.NotNull(release);
        Assert.Equal("v0.3.0", release.Tag);
        Assert.Equal(new Version(0, 3, 0, 0), release.Version);
        Assert.EndsWith("/releases/tag/v0.3.0", release.ReleaseUrl);

        // Named from the release itself, so the user is told to look for a file that exists.
        Assert.Equal("GlassHudTranslator-v0.3.0-win-x64.zip", release.AssetName);
    }

    [Fact]
    public void TheAssetNameFallsBackToThePatternWhenTheReleaseHasNoZip()
    {
        var release = UpdateCheck.Parse("""
            {"tag_name": "v0.3.0", "assets": []}
            """);

        Assert.Equal("GlassHudTranslator-v0.3.0-win-x64.zip", release!.AssetName);
    }

    [Fact]
    public void TheAssetNameIgnoresAssetsThatAreNotTheWindowsZip()
    {
        var release = UpdateCheck.Parse("""
            {"tag_name": "v0.3.0", "assets": [
              {"name": "checksums.txt"},
              {"name": "GlassHudTranslator-v0.3.0-win-x64.zip"}
            ]}
            """);

        Assert.Equal("GlassHudTranslator-v0.3.0-win-x64.zip", release!.AssetName);
    }

    [Fact]
    public void AReleaseWithNoUrlFallsBackToTheReleasesPage()
    {
        var release = UpdateCheck.Parse("""{"tag_name": "v0.3.0"}""");

        Assert.Equal(UpdateCheck.ReleasesPage.ToString(), release!.ReleaseUrl);
    }

    [Theory]
    [InlineData("""{"tag_name": "v0.3.0", "draft": true}""")]
    [InlineData("""{"tag_name": "v0.3.0", "prerelease": true}""")]
    public void DraftsAndPrereleasesAreNotOffered(string json)
    {
        // /releases/latest already excludes both. Checked anyway: this is the one place where
        // trusting the filter would put an unfinished build in front of someone who cannot easily
        // go back to the previous one.
        Assert.Null(UpdateCheck.Parse(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"tag_name": null}""")]
    [InlineData("""{"tag_name": ""}""")]
    [InlineData("""{"message": "API rate limit exceeded"}""")]
    public void AnUnusableResponseIsNotARelease(string json)
    {
        Assert.Null(UpdateCheck.Parse(json));
    }

    [Theory]
    [InlineData("""{"tag_name": "v0.3.0", "assets": "not an array"}""")]
    [InlineData("""{"tag_name": "v0.3.0", "assets": [null, 3, {"name": null}]}""")]
    [InlineData("""{"tag_name": "v0.3.0", "assets": [{"size": 1}]}""")]
    public void AMalformedAssetListDoesNotDiscardAnOtherwiseValidRelease(string json)
    {
        // The tag is the part that decides whether there is an update. Throwing the release away
        // because the asset list is odd would hide a real update over a cosmetic detail, so the
        // filename falls back to the pattern and the user still gets sent to the page.
        var release = UpdateCheck.Parse(json);

        Assert.NotNull(release);
        Assert.Equal("GlassHudTranslator-v0.3.0-win-x64.zip", release.AssetName);
    }

    // ── the throttle ──────────────────────────────────────────────────────────────────────

    private static readonly DateTime Now = new(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CheckingIsSkippedEntirelyWhenSwitchedOff()
    {
        Assert.False(UpdateCheck.IsDue(new AppSettings { CheckForUpdates = false }, Now));
    }

    [Fact]
    public void ACheckFromLongAgoIsDueAgain()
    {
        var settings = new AppSettings { LastUpdateCheckUtc = Now - TimeSpan.FromDays(3) };

        // Only meaningful on a released build; from source there is no version to compare.
        Assert.Equal(!UpdateCheck.IsDevelopmentBuild(UpdateCheck.RunningVersion),
            UpdateCheck.IsDue(settings, Now));
    }

    [Fact]
    public void ARecentCheckIsNotRepeated()
    {
        var settings = new AppSettings { LastUpdateCheckUtc = Now - TimeSpan.FromMinutes(5) };

        Assert.False(UpdateCheck.IsDue(settings, Now));
    }

    [Fact]
    public void ALastCheckedStampInTheFutureDoesNotDisableCheckingForever()
    {
        // A clock that moved, or a hand-edited settings file. Treating it as "not due" would mean
        // the check never runs again on that machine.
        var settings = new AppSettings { LastUpdateCheckUtc = Now + TimeSpan.FromDays(400) };

        Assert.Equal(!UpdateCheck.IsDevelopmentBuild(UpdateCheck.RunningVersion),
            UpdateCheck.IsDue(settings, Now));
    }

    [Fact]
    public void CheckingIsOnByDefault()
    {
        Assert.True(new AppSettings().CheckForUpdates);
        Assert.Null(new AppSettings().LastUpdateCheckUtc);
        Assert.Null(new AppSettings().LastSeenRelease);
    }

    [Fact]
    public void TheSettingSurvivesASaveAndLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ghtu-{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings { CheckForUpdates = false, LastSeenRelease = "v9.9.9" }.Save(path);
            var loaded = AppSettings.Load(path);

            Assert.False(loaded.CheckForUpdates);
            Assert.Equal("v9.9.9", loaded.LastSeenRelease);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ASettingsFileWrittenBeforeUpdateCheckingExistedStillLoads()
    {
        // Those files are sitting in users' AppData folders. Missing fields must take the default,
        // which for CheckForUpdates means on.
        var path = Path.Combine(Path.GetTempPath(), $"ghtu-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"profile":"ffxiv","language":"Arabic"}""");
            var loaded = AppSettings.Load(path);

            Assert.True(loaded.CheckForUpdates);
            Assert.Equal(UiLanguage.Arabic, loaded.Language);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── remembered tag ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ARememberedTagRebuildsTheNoticeWithoutAskingGitHubAgain()
    {
        var update = UpdateCheck.FromRememberedTag("v0.3.0", Current);

        Assert.NotNull(update);
        Assert.Equal("v0.3.0", update.Tag);
        Assert.Equal("GlassHudTranslator-v0.3.0-win-x64.zip", update.AssetName);
        Assert.EndsWith("/releases/tag/v0.3.0", update.ReleaseUrl);
    }

    [Theory]
    [InlineData("v0.2.1")]
    [InlineData("v0.1.0")]
    [InlineData("nonsense")]
    [InlineData("")]
    [InlineData(null)]
    public void ARememberedTagThatIsNoLongerNewerClearsItself(string? tag)
    {
        // This is what removes the notice after the user has actually updated: nothing has to go
        // back and delete the stored value.
        Assert.Null(UpdateCheck.FromRememberedTag(tag, Current));
    }

    // ── development builds ────────────────────────────────────────────────────────────────

    [Fact]
    public void ABuildFromSourceNeverNotifies()
    {
        Assert.True(UpdateCheck.IsDevelopmentBuild(new Version(0, 0, 0, 0)));
        Assert.True(UpdateCheck.IsDevelopmentBuild(null));
        Assert.False(UpdateCheck.IsDevelopmentBuild(new Version(0, 2, 1, 0)));
        Assert.False(UpdateCheck.IsDevelopmentBuild(new Version(1, 0, 0, 0)));
    }

    [Fact]
    public async Task ABuildFromSourceDoesNotEvenAsk()
    {
        var handler = new StubHandler(HttpStatusCode.OK, RealisticPayload);
        using var http = new HttpClient(handler);

        var result = await UpdateCheck.FetchAsync(http, new Version(0, 0, 0, 0), default);

        Assert.Equal(UpdateOutcome.NotChecked, result.Outcome);
        Assert.Equal(0, handler.Requests);
    }

    // ── fetching ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ANewerReleaseIsReported()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, RealisticPayload));

        var result = await UpdateCheck.FetchAsync(http, Current, default);

        Assert.Equal(UpdateOutcome.UpdateAvailable, result.Outcome);
        Assert.Equal("v0.3.0", result.Update!.Tag);
        Assert.True(result.Reached);
    }

    [Fact]
    public async Task TheSameVersionIsUpToDateRatherThanAnUpdate()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK,
            """{"tag_name": "v0.2.1"}"""));

        var result = await UpdateCheck.FetchAsync(http, Current, default);

        Assert.Equal(UpdateOutcome.UpToDate, result.Outcome);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task AnOlderReleaseIsNotAnUpdate()
    {
        // Happens when a release is deleted and the previous one becomes "latest" again.
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK,
            """{"tag_name": "v0.1.0"}"""));

        Assert.Equal(UpdateOutcome.UpToDate,
            (await UpdateCheck.FetchAsync(http, Current, default)).Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]        // rate limited
    [InlineData(HttpStatusCode.NotFound)]         // repository renamed, or no releases yet
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task AnErrorFromGitHubIsUnreachableRatherThanUpToDate(HttpStatusCode code)
    {
        using var http = new HttpClient(new StubHandler(code, ""));

        var result = await UpdateCheck.FetchAsync(http, Current, default);

        Assert.Equal(UpdateOutcome.Unreachable, result.Outcome);

        // The distinction that matters: a failed check must not reset the daily timer, or a user
        // who was offline once waits another day before anything tries again.
        Assert.False(result.Reached);
    }

    [Fact]
    public async Task ACaptivePortalIsUnreachableRatherThanUpToDate()
    {
        // Hotel wifi answering 200 with a login page. Claiming "you have the latest version" here
        // would be a confident lie.
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, "<html>Sign in</html>"));

        Assert.Equal(UpdateOutcome.Unreachable,
            (await UpdateCheck.FetchAsync(http, Current, default)).Outcome);
    }

    [Fact]
    public async Task NoNetworkIsUnreachableAndDoesNotThrow()
    {
        using var http = new HttpClient(new ThrowingHandler(new HttpRequestException("DNS")));

        Assert.Equal(UpdateOutcome.Unreachable,
            (await UpdateCheck.FetchAsync(http, Current, default)).Outcome);
    }

    [Fact]
    public async Task ATimeoutIsUnreachableAndDoesNotThrow()
    {
        // The lesson from ProviderRouter: a per-attempt cap surfacing as a bare
        // OperationCanceledException escaped a class documented as never throwing.
        using var http = new HttpClient(new ThrowingHandler(new TaskCanceledException()));

        Assert.Equal(UpdateOutcome.Unreachable,
            (await UpdateCheck.FetchAsync(http, Current, default)).Outcome);
    }

    [Fact]
    public async Task ShutdownDuringACheckDoesNotThrow()
    {
        // Fired from the constructor, so it can still be in flight when the window closes.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, RealisticPayload));

        Assert.Equal(UpdateOutcome.Unreachable,
            (await UpdateCheck.FetchAsync(http, Current, cts.Token)).Outcome);
    }

    [Fact]
    public async Task TheRequestCarriesAUserAgent()
    {
        // GitHub rejects API requests without one outright - 403, and the check would look like a
        // permanent rate limit.
        var handler = new StubHandler(HttpStatusCode.OK, RealisticPayload);
        using var http = new HttpClient(handler);

        await UpdateCheck.FetchAsync(http, Current, default);

        Assert.NotNull(handler.LastRequest);
        Assert.NotEmpty(handler.LastRequest.Headers.UserAgent);
        Assert.Contains(handler.LastRequest.Headers.Accept,
            h => h.MediaType == "application/vnd.github+json");
    }

    [Fact]
    public async Task TheCheckAsksTheRepositoryTheReleasesActuallyComeFrom()
    {
        var handler = new StubHandler(HttpStatusCode.OK, RealisticPayload);
        using var http = new HttpClient(handler);

        await UpdateCheck.FetchAsync(http, Current, default);

        // A renamed repository still redirects, but the download link put in front of the user
        // must be the current one.
        Assert.Equal("api.github.com", handler.LastRequest!.RequestUri!.Host);
        Assert.Contains($"/repos/{UpdateCheck.Owner}/{UpdateCheck.Repository}/releases/latest",
            handler.LastRequest.RequestUri.AbsolutePath);
        Assert.Contains(UpdateCheck.Repository, UpdateCheck.ReleasesPage.ToString());
    }

    [Fact]
    public void TheDownloadIsOverHttpsAndPointsAtGitHub()
    {
        Assert.Equal(Uri.UriSchemeHttps, UpdateCheck.ReleasesPage.Scheme);
        Assert.Equal(Uri.UriSchemeHttps, UpdateCheck.LatestReleaseApi.Scheme);
        Assert.Equal("github.com", UpdateCheck.ReleasesPage.Host);
        Assert.StartsWith("https://github.com/", UpdateCheck.ReleaseUrlFor("v1.2.3"));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            LastRequest = request;
            ct.ThrowIfCancellationRequested();

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler(Exception failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) => throw failure;
    }
}
