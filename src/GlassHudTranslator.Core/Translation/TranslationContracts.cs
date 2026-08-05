using GlassHudTranslator.Core.Glossary;

namespace GlassHudTranslator.Core.Translation;

/// <summary>
/// FFXIV's English is deliberately archaic high fantasy. MSA fits the narrative voice; Egyptian
/// reads as comedy for Elezen nobility but lands well for merchants and comic relief. Default MSA,
/// expose the toggle (brief 6) - it is one line of the system prompt, so being wrong is cheap.
/// </summary>
public enum ArabicRegister
{
    ModernStandard,
    Egyptian,
}

public sealed record TranslationRequest(
    string Body,
    string? Speaker = null,
    IReadOnlyList<GlossaryTerm>? Glossary = null,
    string? PreviousLine = null,
    ArabicRegister Register = ArabicRegister.ModernStandard,
    DateTimeOffset RequestedAt = default,
    string GameName = "a video game",
    string? StyleHint = null)
{
    public IReadOnlyList<GlossaryTerm> GlossaryTerms => Glossary ?? [];

    public DateTimeOffset Requested => RequestedAt == default ? DateTimeOffset.UtcNow : RequestedAt;
}

public sealed record TranslationResult(
    string Text,
    string Provider,
    string Model,
    bool FromCache,
    TimeSpan Latency,
    string Outcome)
{
    /// <summary>True when the user is looking at English with a warning marker, not Arabic.</summary>
    public bool IsFallbackEnglish => Provider == ProviderNames.Fallback;
}

public static class ProviderNames
{
    public const string Cache = "cache";
    public const string Fallback = "fallback";
    public const string Stub = "stub";
}

public interface ITranslationProvider
{
    string Name { get; }

    /// <summary>Ordered fallback list from models.json. Never hardcoded (brief 12).</summary>
    IReadOnlyList<string> Models { get; }

    Task<string> TranslateAsync(TranslationRequest request, string model, CancellationToken ct);
}

public enum ProviderFailure
{
    /// <summary>The model name is gone from the catalogue. Try the next model, and say so loudly.</summary>
    ModelNotFound,

    /// <summary>429. Stop using this provider for a while and move to the next lane.</summary>
    RateLimited,

    /// <summary>5xx, timeout, socket error. Worth a bounded retry.</summary>
    Transient,

    /// <summary>Bad or missing key, malformed request. Retrying cannot help.</summary>
    Fatal,
}

public sealed class ProviderException(
    string provider, string model, ProviderFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Provider { get; } = provider;
    public string Model { get; } = model;
    public ProviderFailure Failure { get; } = failure;
}
