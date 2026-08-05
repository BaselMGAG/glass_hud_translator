using System.Text.Json;
using System.Text.Json.Serialization;

namespace GamingTranslatorGlassHUD.Core.Config;

/// <summary>
/// One provider lane. <see cref="Models"/> is an ordered fallback list, never a single hardcoded
/// name: free model catalogues churn hard, and providers have been observed silently deleting free
/// models and breaking client code without warning (brief 12). A model-not-found falls through to
/// the next entry rather than failing the line.
/// </summary>
public sealed record ProviderConfig
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("baseUrl")] public required string BaseUrl { get; init; }

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
}
