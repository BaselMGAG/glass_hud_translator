using System.Reflection;
using System.Text.Json;
using GlassHudTranslator.Core.Config;

namespace GlassHudTranslator.Core.Update;

/// <summary>A release newer than the running build, and everything needed to describe it.</summary>
public sealed record AvailableUpdate(
    Version Version,
    string Tag,
    string ReleaseUrl,
    string AssetName);

public enum UpdateOutcome
{
    /// <summary>Nothing was asked - development build, or checking is switched off.</summary>
    NotChecked,

    UpToDate,
    UpdateAvailable,

    /// <summary>GitHub could not be reached, or answered with something unusable.</summary>
    Unreachable,
}

/// <summary>
/// The outcome of one check. <see cref="UpdateOutcome.UpToDate"/> and
/// <see cref="UpdateOutcome.Unreachable"/> are deliberately distinct: they look identical to an
/// automatic check, which says nothing either way, but a user who has just pressed "Check now" is
/// owed the difference between "you are current" and "I could not ask".
/// </summary>
public sealed record UpdateCheckResult(UpdateOutcome Outcome, AvailableUpdate? Update = null)
{
    public static readonly UpdateCheckResult NotChecked = new(UpdateOutcome.NotChecked);
    public static readonly UpdateCheckResult UpToDate = new(UpdateOutcome.UpToDate);
    public static readonly UpdateCheckResult Unreachable = new(UpdateOutcome.Unreachable);

    /// <summary>True only when GitHub answered, so a failed check does not reset the daily timer.</summary>
    public bool Reached => Outcome is UpdateOutcome.UpToDate or UpdateOutcome.UpdateAvailable;
}

/// <summary>
/// Tells the user a newer release exists. It does not download or install anything.
///
/// <para>
/// There is no update server, and there does not need to be one: GitHub publishes the releases as
/// JSON at a public, unauthenticated endpoint, so the check is one GET against infrastructure that
/// already hosts the download. The alternative - actually applying the update - was considered and
/// deliberately rejected: the app is folder-published with native OCR libraries beside it, Windows
/// will not let a running process overwrite its own DLLs, the binary is unsigned so an
/// auto-updater is both an antivirus heuristic and an unverifiable code path, and the whole thing
/// would ship untested because the Windows machine is borrowed. A wrong notification is a wasted
/// click; a wrong self-update is an install that no longer starts, belonging to someone who cannot
/// read the English error it fails with.
/// </para>
///
/// <para>
/// Like <c>ProviderRouter</c>, nothing here throws. A check that cannot reach GitHub, is rate
/// limited, or gets a response it does not understand returns null and says nothing at all. The
/// user did not ask for this, so it is not allowed to interrupt them with its own failures.
/// </para>
/// </summary>
public static class UpdateCheck
{
    public const string Owner = "basel2000de";
    public const string Repository = "glass_hud_translator";

    public static readonly Uri LatestReleaseApi =
        new($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");

    /// <summary>Where the user is sent. Resolves to the newest release, whatever it is.</summary>
    public static readonly Uri ReleasesPage =
        new($"https://github.com/{Owner}/{Repository}/releases/latest");

    /// <summary>
    /// Once a day, near enough. Checking on every launch would be pointless traffic - releases here
    /// are days apart - and GitHub allows 60 unauthenticated requests an hour per address, which a
    /// user restarting the app repeatedly could otherwise burn through.
    /// </summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(20);

    /// <summary>
    /// Shorter than any provider timeout. This runs at startup, and a slow network must not be
    /// visible to someone who only wanted to open Settings.
    /// </summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The asset a release publishes. Derived only as a fallback - the name is read out of the
    /// release itself where possible, so the notification names a file that demonstrably exists.
    /// </summary>
    public static string AssetNameFor(string tag) => $"GlassHudTranslator-{tag}-win-x64.zip";

    /// <summary>
    /// The running build's version, or null when it has none to compare.
    ///
    /// <para>
    /// Release builds are stamped by CI from the tag (<c>-p:Version=</c>). A local
    /// <c>dotnet build</c> gets 0.0.0 from Directory.Build.props, which is the signal to stay quiet:
    /// a developer running from source is not the person this feature is for, and every release
    /// would look like an update to them.
    /// </para>
    /// </summary>
    public static Version? RunningVersion { get; } = ResolveRunningVersion();

    public static bool IsDevelopmentBuild(Version? version) =>
        version is null || (version.Major == 0 && version.Minor == 0 && version.Build == 0);

    /// <summary>
    /// Whether a check is due. Separate from performing one so the throttle is testable without a
    /// network, and so the caller can be a plain fire-and-forget.
    /// </summary>
    public static bool IsDue(AppSettings settings, DateTime utcNow)
    {
        if (!settings.CheckForUpdates) return false;
        if (IsDevelopmentBuild(RunningVersion)) return false;
        if (settings.LastUpdateCheckUtc is not { } last) return true;

        // A last-checked stamp in the future means the clock moved, or the settings file was hand
        // edited. Treating it as "not due" would disable the check permanently.
        return last > utcNow || utcNow - last >= MinimumInterval;
    }

    /// <summary>
    /// Rebuilds a notice from a tag remembered by an earlier check, so it is on screen the moment
    /// the window opens rather than only in the one session a day where the check actually runs.
    /// Returns null once the tag is no longer newer than the running build - which is what clears
    /// the remembered value after the user has actually updated.
    /// </summary>
    public static AvailableUpdate? FromRememberedTag(string? tag, Version? current)
    {
        if (string.IsNullOrWhiteSpace(tag) || IsDevelopmentBuild(current)) return null;
        if (ParseTag(tag) is not { } version || !IsNewer(version, current!)) return null;

        return new AvailableUpdate(version, tag, ReleaseUrlFor(tag), AssetNameFor(tag));
    }

    public static string ReleaseUrlFor(string tag) =>
        $"https://github.com/{Owner}/{Repository}/releases/tag/{Uri.EscapeDataString(tag)}";

    /// <summary>
    /// Asks GitHub for the newest release. Never throws: every failure becomes
    /// <see cref="UpdateOutcome.Unreachable"/>, and every success is either up to date or an update.
    /// </summary>
    public static async Task<UpdateCheckResult> FetchAsync(
        HttpClient http, Version? current, CancellationToken ct)
    {
        if (IsDevelopmentBuild(current)) return UpdateCheckResult.NotChecked;

        try
        {
            // Its own budget, linked to the caller's: the shared HttpClient is configured for
            // translation requests and its timeout is far too long to sit behind at startup.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Budget);

            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);

            // GitHub rejects API requests with no User-Agent outright - 403, no body worth reading.
            // Set per request rather than on the client, which is shared with the providers.
            request.Headers.UserAgent.ParseAdd($"GlassHudTranslator/{current}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await http.SendAsync(request, timeout.Token);

            // 404 before the first release, 403 when rate limited, 5xx when GitHub is unwell. None
            // of them are the user's problem, and none is evidence that the build is current.
            if (!response.IsSuccessStatusCode) return UpdateCheckResult.Unreachable;

            var json = await response.Content.ReadAsStringAsync(timeout.Token);

            // A response we cannot parse is not proof of being up to date either - reporting it as
            // such would tell someone on a captive-portal wifi, whose "GitHub" is a login page,
            // that they are on the newest version.
            if (Parse(json) is not { } release) return UpdateCheckResult.Unreachable;

            return IsNewer(release.Version, current!)
                ? new UpdateCheckResult(UpdateOutcome.UpdateAvailable, release)
                : UpdateCheckResult.UpToDate;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException
                                      or JsonException or UriFormatException or NotSupportedException
                                      or InvalidOperationException)
        {
            return UpdateCheckResult.Unreachable;
        }
    }

    /// <summary>
    /// Reads one release out of the API response. Public so the parsing is testable against real
    /// captured payloads without a network.
    /// </summary>
    public static AvailableUpdate? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            // /releases/latest already excludes both, but the flags are checked anyway: this is the
            // one place where trusting an endpoint's filter would put an unfinished build in front
            // of a user who cannot easily go back.
            if (Flag(root, "draft") || Flag(root, "prerelease")) return null;

            if (!root.TryGetProperty("tag_name", out var tagElement)) return null;
            if (tagElement.GetString() is not { Length: > 0 } tag) return null;

            if (ParseTag(tag) is not { } version) return null;

            var url = root.TryGetProperty("html_url", out var urlElement)
                      && urlElement.GetString() is { Length: > 0 } published
                ? published
                : ReleasesPage.ToString();

            return new AvailableUpdate(version, tag, url, FindAsset(root) ?? AssetNameFor(tag));
        }
        catch (JsonException)
        {
            return null;
        }

        static bool Flag(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }

    /// <summary>The Windows zip, by name, so the user is told what to look for on the page.</summary>
    private static string? FindAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;
            if (!asset.TryGetProperty("name", out var name)) continue;
            if (name.GetString() is not { Length: > 0 } text) continue;

            if (text.EndsWith("win-x64.zip", StringComparison.OrdinalIgnoreCase)) return text;
        }

        return null;
    }

    /// <summary>
    /// <c>v0.2.1</c> to a version. Anything else - a prerelease suffix, a name rather than a
    /// number - returns null, which the caller reads as "no update", the safe direction to be wrong.
    /// </summary>
    public static Version? ParseTag(string tag)
    {
        var trimmed = tag.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V')) trimmed = trimmed[1..];

        return Version.TryParse(trimmed, out var version) ? Normalise(version) : null;
    }

    /// <summary>
    /// Version treats an unspecified component as -1, so 0.2.1 sorts *below* 0.2.1.0 and a release
    /// tagged one way against a build stamped the other would notify forever.
    /// </summary>
    public static Version Normalise(Version version) => new(
        version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    public static bool IsNewer(Version candidate, Version current) =>
        Normalise(candidate) > Normalise(current);

    private static Version? ResolveRunningVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateCheck).Assembly;

        // Informational version carries what CI was told; AssemblyVersion is the fallback for a
        // host that strips it. Either may carry a "+sha" or "-suffix" that ParseTag rejects, so the
        // build metadata is cut off first rather than losing the version entirely.
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (informational is { Length: > 0 })
        {
            var cut = informational.IndexOfAny(['+', '-']);
            if (ParseTag(cut < 0 ? informational : informational[..cut]) is { } parsed) return parsed;
        }

        return assembly.GetName().Version is { } assemblyVersion ? Normalise(assemblyVersion) : null;
    }
}
