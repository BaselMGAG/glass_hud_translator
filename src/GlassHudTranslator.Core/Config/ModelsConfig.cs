using System.Text.Json;
using System.Text.Json.Serialization;

namespace GlassHudTranslator.Core.Config;

/// <summary>Which wire protocol a lane speaks.</summary>
public enum ProviderKind
{
    /// <summary>
    /// OpenAI chat-completions. Gemini, Groq, OpenAI itself and Ollama all expose this shape, so
    /// they share one client class and differ only by base URL, key and model name (brief 4.1).
    /// </summary>
    OpenAiCompatible,

    /// <summary>
    /// Anthropic's Messages API. A genuinely different request: <c>x-api-key</c> rather than a
    /// bearer token, <c>anthropic-version</c>, a top-level system parameter instead of a system
    /// message, and content returned as typed blocks. Handled by the official SDK, not by bending
    /// the OpenAI client into shape.
    /// </summary>
    Anthropic,
}

/// <summary>Cost expectation for a lane, shown next to its key field so the choice is explicit.</summary>
public static class ProviderTiers
{
    /// <summary>Free tier, no credit card. What the project is designed around.</summary>
    public const string Free = "free";

    /// <summary>Billed per token. The user already has the key and chooses to spend on it.</summary>
    public const string Paid = "paid";

    /// <summary>Runs on the machine. No key, no network, no cost.</summary>
    public const string Local = "local";
}

/// <summary>
/// One provider lane. <see cref="Models"/> is an ordered fallback list, never a single hardcoded
/// name: free model catalogues churn hard, and providers have been observed silently deleting free
/// models and breaking client code without warning (brief 12). A model-not-found falls through to
/// the next entry rather than failing the line.
/// </summary>
public sealed record ProviderConfig
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("baseUrl")] public string BaseUrl { get; init; } = "";

    /// <summary>Key name in the secret store, or null for a provider that needs no key.</summary>
    [JsonPropertyName("secret")] public string? Secret { get; init; }

    [JsonPropertyName("rpm")] public int Rpm { get; init; } = 10;
    [JsonPropertyName("rpd")] public int Rpd { get; init; } = 1000;
    [JsonPropertyName("models")] public string[] Models { get; init; } = [];

    /// <summary>
    /// Skipped unless running with --dev. Ollama is a development convenience on the Mac only -
    /// the target PC is too weak to hold a local model alongside the game (brief 2.7), and the
    /// shipped app must never wait on a localhost port that does not exist.
    /// </summary>
    [JsonPropertyName("devOnly")] public bool DevOnly { get; init; }

    /// <summary>
    /// Raw <c>kind</c> string. Kept as text rather than a JSON-bound enum so that an entry written
    /// with a typo, or by a future version of the app, degrades to the OpenAI shape instead of
    /// failing to parse the whole file and taking every other lane down with it.
    /// </summary>
    [JsonPropertyName("kind")] public string? KindName { get; init; }

    // JsonIgnore, and not optional: the loader matches names case-insensitively, so a computed
    // 'Kind' collides with the 'kind' field it is derived from and the whole file fails to parse.
    [JsonIgnore]
    public ProviderKind Kind => string.Equals(KindName, "anthropic", StringComparison.OrdinalIgnoreCase)
        ? ProviderKind.Anthropic
        : ProviderKind.OpenAiCompatible;

    // ── presentation, so the settings screen is generated from this file rather than hardcoded ──

    /// <summary>Human name for the settings screen. Falls back to <see cref="Name"/>.</summary>
    [JsonPropertyName("displayName")] public string? DisplayName { get; init; }

    /// <summary>One of <see cref="ProviderTiers"/>. Drives the free/paid label next to the key box.</summary>
    [JsonPropertyName("tier")] public string Tier { get; init; } = ProviderTiers.Free;

    /// <summary>Where the user goes to get a key. Shown verbatim; no link is opened for them.</summary>
    [JsonPropertyName("keyUrl")] public string? KeyUrl { get; init; }

    /// <summary>
    /// Output token ceiling. Was hardcoded at 300, which is ample for one subtitle but truncates
    /// any model that spends output tokens on reasoning before it answers - so it belongs per lane.
    /// </summary>
    [JsonPropertyName("maxOutputTokens")] public int MaxOutputTokens { get; init; } = 300;

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;

    [JsonIgnore]
    public bool IsPaid => string.Equals(Tier, ProviderTiers.Paid, StringComparison.OrdinalIgnoreCase);
}

public sealed class ModelsConfig
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    [JsonPropertyName("providers")]
    public List<ProviderConfig> Providers { get; init; } = [];

    public static ModelsConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"models.json not found at {path}", path);

        return JsonSerializer.Deserialize<ModelsConfig>(File.ReadAllText(path), Options)
               ?? new ModelsConfig();
    }

    public IEnumerable<ProviderConfig> Enabled(bool includeDevOnly) =>
        Providers.Where(p => includeDevOnly || !p.DevOnly);

    /// <summary>
    /// Configuration mistakes worth telling the user about, rather than throwing on load.
    ///
    /// <para>
    /// This file is meant to be edited by hand - that is the whole point of keeping model names out
    /// of the code. Refusing to start because one lane is malformed would punish the edit it exists
    /// to invite, so the app starts, skips nothing silently, and shows these in Settings instead.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        foreach (var duplicate in Providers.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            problems.Add($"'{duplicate.Key}' is listed {duplicate.Count()} times. Only lane order " +
                         "distinguishes them, so the quota readout will merge them.");
        }

        foreach (var provider in Providers)
        {
            if (provider.Models.Length == 0)
                problems.Add($"'{provider.Name}' lists no models, so it can never be tried.");

            if (provider.Kind == ProviderKind.OpenAiCompatible && string.IsNullOrWhiteSpace(provider.BaseUrl))
                problems.Add($"'{provider.Name}' has no baseUrl.");

            if (provider.KindName is { Length: > 0 } kind &&
                !kind.Equals("anthropic", StringComparison.OrdinalIgnoreCase) &&
                !kind.Equals("openai", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"'{provider.Name}' has kind '{kind}', which is not recognised. " +
                             "Treating it as an OpenAI-compatible endpoint.");
            }
        }

        return problems;
    }
}
