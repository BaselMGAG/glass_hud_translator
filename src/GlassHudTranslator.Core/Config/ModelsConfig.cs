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

/// <summary>
/// One entry in a lane's ordered model fallback list.
///
/// <para>
/// In JSON it is EITHER a plain string - <c>"gemini-3.5-flash"</c> - or an object carrying
/// per-model overrides. Both forms are read forever: the string form is what every models.json
/// already on a user's machine contains, and this file is explicitly meant to be hand-edited.
/// </para>
///
/// <para>
/// The overrides exist because the two things that decide whether a free lane works turned out to
/// be per model rather than per provider. Groq admits a request against
/// <c>prompt_tokens + max_tokens</c> - not against what the answer actually costs - so a lane-wide
/// <c>maxOutputTokens</c> of 4096 reserved more than half of gpt-oss's 8,000-tokens-a-minute
/// ceiling on every line, and the second line inside any minute was refused. And
/// <c>reasoning_effort</c> is what stops a reasoning model spending that budget thinking, but
/// <c>llama-3.3-70b-versatile</c> answers 400 to the very parameter its lane-mates need.
/// </para>
/// </summary>
[JsonConverter(typeof(ModelEntryConverter))]
public sealed record ModelEntry
{
    public required string Id { get; init; }

    /// <summary>
    /// Overrides <see cref="ProviderConfig.MaxOutputTokens"/> for this model alone. Sized to the
    /// answer plus whatever the model spends reasoning, and no larger: on Groq every unused token
    /// of this number is still withheld from the per-minute allowance.
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    /// Sent as <c>reasoning_effort</c> when set. Null means "do not send the parameter at all",
    /// which is required rather than merely tidy - a model that does not know the parameter
    /// rejects the whole request.
    /// </summary>
    public string? ReasoningEffort { get; init; }

    public static ModelEntry Named(string id) => new() { Id = id };

    public override string ToString() => Id;
}

/// <summary>
/// Reads a model entry written either as a bare string or as an object. Writing always produces
/// the object form; nothing in the app writes this file, so that only affects tests.
/// </summary>
public sealed class ModelEntryConverter : JsonConverter<ModelEntry>
{
    /// <summary>
    /// Without this, System.Text.Json short-circuits a JSON <c>null</c> before the converter is
    /// ever called and hands back a null <see cref="ModelEntry"/> — which then took
    /// <see cref="ProviderConfig.Models"/>, <see cref="ProviderConfig.ModelFor"/> and
    /// <see cref="ModelsConfig.Problems"/> down with a NullReferenceException at startup, leaving
    /// no Settings window and so no way to fix the file from inside the app. Commenting a model
    /// out by replacing it with <c>null</c> is an ordinary hand-edit, and before this change
    /// <c>models</c> was <c>string[]</c>, where a null was merely a null string and harmless.
    /// </summary>
    public override bool HandleNull => true;

    public override ModelEntry? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return ModelEntry.Named("");

        if (reader.TokenType == JsonTokenType.String)
            return ModelEntry.Named(reader.GetString() ?? "");

        // Anything else - a number, true, null - is swallowed into an entry with no id, which
        // Problems() then reports in Settings. Throwing here would be the first way a single
        // mistyped models[] element takes the WHOLE file down, and this file exists to be
        // hand-edited: the contract two hundred lines below is that a malformed lane is reported,
        // not fatal. An unstartable app is a poor answer to a stray comma.
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return ModelEntry.Named("");
        }

        string? id = null;
        int? maxOutputTokens = null;
        string? reasoningEffort = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return ModelEntry.Named(id ?? "") with
                {
                    MaxOutputTokens = maxOutputTokens,
                    ReasoningEffort = reasoningEffort,
                };

            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var name = reader.GetString();
            reader.Read();

            // Every read is guarded on the token type, and a value of the wrong type is left null
            // rather than thrown. Not defensiveness for its own sake: this file's own instructions
            // tell people to tune maxOutputTokens by hand, and quoting the number - "700" beside
            // the quoted "low" next to it - is the likeliest slip there is. Unguarded, GetInt32 on
            // a string threw out of ModelsConfig.Load, past a Load that catches nothing, and the
            // app came up as a bare overlay reading "Startup failed" with no Settings window and
            // so no way to reach the Problems() list that would have named the field.
            //
            // Unknown keys are skipped for the same reason 'kind' is kept as free text: a file
            // written by a future version must not take every lane down with it.
            if (Match(name, "id"))
                id = reader.TokenType == JsonTokenType.String ? reader.GetString() : Skipped(ref reader);
            else if (Match(name, "maxOutputTokens"))
                maxOutputTokens = reader.TokenType == JsonTokenType.Number &&
                                  reader.TryGetInt32(out var tokens) ? tokens : null;
            else if (Match(name, "reasoningEffort"))
                reasoningEffort = reader.TokenType == JsonTokenType.String ? reader.GetString() : Skipped(ref reader);
            else reader.Skip();
        }

        throw new JsonException("Unterminated models[] entry.");
    }

    public override void Write(Utf8JsonWriter writer, ModelEntry value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        if (value.MaxOutputTokens is { } tokens) writer.WriteNumber("maxOutputTokens", tokens);
        if (value.ReasoningEffort is { } effort) writer.WriteString("reasoningEffort", effort);
        writer.WriteEndObject();
    }

    private static bool Match(string? name, string expected) =>
        string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Consumes a value of the wrong type and yields null. An object or array has to be skipped
    /// wholesale or the reader is left mid-structure and every key after it is misread.
    /// </summary>
    private static string? Skipped(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }
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

    /// <summary>
    /// The ordered fallback list as written in the file, with any per-model overrides. Most callers
    /// want <see cref="Models"/>, which is just the ids.
    /// </summary>
    [JsonPropertyName("models")] public ModelEntry[] ModelEntries { get; init; } = [];

    /// <summary>
    /// Models on this lane that can read an image, in fallback order. Empty — the default and the
    /// state of every lane in an existing installation — means the lane cannot be asked to read
    /// one, and it is skipped in silence.
    ///
    /// <para>
    /// A separate list rather than a flag on <see cref="ModelEntries"/>, because the two orders are
    /// answering different questions. The text list is ordered by daily allowance, which is the
    /// quota policy; multimodal models are a different, usually shorter, and differently metered
    /// set, and the model that leads a lane for text is not necessarily one that accepts an image
    /// at all.
    /// </para>
    /// </summary>
    [JsonPropertyName("visionModels")] public string[] VisionModels { get; init; } = [];

    /// <summary>Whether this lane can be asked to read an image.</summary>
    [JsonIgnore] public bool CanSee => VisionModels.Length > 0;

    /// <summary>
    /// The model ids, in order. Computed rather than stored so that every existing reader - the
    /// providers, the settings screen, the router - keeps working unchanged now that an entry may
    /// carry more than a name.
    /// </summary>
    [JsonIgnore]
    public string[] Models => [.. ModelEntries.Select(m => m.Id)];

    /// <summary>
    /// The overrides for one model, or null if it is not in this lane. Matched on the id, because
    /// the id is all the router passes back down to the provider.
    /// </summary>
    public ModelEntry? ModelFor(string id) =>
        ModelEntries.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

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

    // ── more than one key for the same provider ───────────────────────────────────────────

    /// <summary>
    /// How many keys one provider may hold. Three, because the reason to add a second is that a
    /// free tier ran out, and past three the honest answer is that the game is being played more
    /// than a free tier is meant to cover.
    /// </summary>
    public const int MaxKeys = 3;

    /// <summary>
    /// The secret-store name for one key slot. Slot 1 is the plain name and MUST stay that way:
    /// every installation already has a key filed under it, and renaming it would silently log
    /// every existing user out of their own provider.
    /// </summary>
    public string SecretSlot(int slot) =>
        Secret is null ? "" : slot <= 1 ? Secret : $"{Secret}#{slot}";

    /// <summary>
    /// What this key's lane is called in the router log and in the quota ledger. Distinct per slot,
    /// so "which of my three keys is the exhausted one" is answerable - which is the entire reason
    /// somebody adds a second one.
    /// </summary>
    public string LaneName(int slot) => slot <= 1 ? Name : $"{Name}#{slot}";

    /// <summary>
    /// The inverse of <see cref="LaneName"/>: "gemini#2" is gemini. Nothing else in the app puts a
    /// '#' in a provider name, and models.json would report a name containing one as a duplicate
    /// long before it reached here.
    /// </summary>
    public static string ProviderNameOf(string laneName)
    {
        ArgumentNullException.ThrowIfNull(laneName);

        var hash = laneName.IndexOf('#', StringComparison.Ordinal);
        return hash > 0 ? laneName[..hash] : laneName;
    }

    /// <summary>
    /// Slot numbers this provider could hold a key in, lowest first. A lane with no secret at all
    /// (Ollama) has exactly one slot and no key, which is how it stays a single lane.
    /// </summary>
    public IEnumerable<int> KeySlots() =>
        Secret is null ? [1] : Enumerable.Range(1, MaxKeys);
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
    /// The daily allowance to show beside each lane, for the quota readout.
    ///
    /// <para>
    /// Read per LANE rather than per provider, because usage is recorded per lane - a second key
    /// spends its own account's budget, so "gemini 412/540" and "gemini#2 0/540" are two true
    /// statements where a single merged "gemini 412/540" would be a false one. Each slot gets the
    /// provider's full allowance rather than a share of it, which is the whole point of the key
    /// belonging to a different account.
    /// </para>
    /// </summary>
    public IReadOnlyList<(string Lane, int Rpd)> LimitsFor(IEnumerable<string> laneNames) =>
    [
        .. laneNames.Select(lane => (lane,
            Providers.FirstOrDefault(p => p.Name == ProviderConfig.ProviderNameOf(lane))?.Rpd ?? 0)),
    ];

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
            // '#' is how a key slot is spelled - "gemini#2" is gemini's second key. A provider
            // literally named that would collide with it in the quota ledger, merging two
            // different accounts' daily usage into one row, and LimitsFor would show it the wrong
            // provider's allowance. Cheap to forbid, invisible to diagnose.
            if (provider.Name.Contains('#', StringComparison.Ordinal))
            {
                problems.Add($"'{provider.Name}' contains '#', which is reserved for key slots. " +
                             "Rename it, or its quota will be merged with another lane's.");
            }

            if (provider.Models.Length == 0)
                problems.Add($"'{provider.Name}' lists no models, so it can never be tried.");

            if (provider.Kind == ProviderKind.OpenAiCompatible && string.IsNullOrWhiteSpace(provider.BaseUrl))
                problems.Add($"'{provider.Name}' has no baseUrl.");

            foreach (var entry in provider.ModelEntries)
            {
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    problems.Add($"'{provider.Name}' has a models[] entry with no id.");
                    continue;
                }

                // Only the three values every provider that takes this parameter accepts. A typo
                // here is a 400 on every line of that model, which reads as the model being dead.
                if (entry.ReasoningEffort is { Length: > 0 } effort &&
                    !effort.Equals("low", StringComparison.OrdinalIgnoreCase) &&
                    !effort.Equals("medium", StringComparison.OrdinalIgnoreCase) &&
                    !effort.Equals("high", StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"'{provider.Name}/{entry.Id}' has reasoningEffort '{effort}'. " +
                                 "Expected low, medium or high.");
                }

                // A reasoning model spends this budget thinking before it writes anything, so too
                // small is not a shorter answer - it is an empty one, on every single line.
                if (entry.MaxOutputTokens is { } tokens && tokens < 64)
                {
                    problems.Add($"'{provider.Name}/{entry.Id}' allows only {tokens} output tokens, " +
                                 "which is too few for a translation.");
                }
            }

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
