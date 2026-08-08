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
    IReadOnlyList<string>? PreviousLines = null,
    ArabicRegister Register = ArabicRegister.ModernStandard,
    DateTimeOffset RequestedAt = default,
    string GameName = "a video game",
    string? StyleHint = null)
{
    public IReadOnlyList<GlossaryTerm> GlossaryTerms => Glossary ?? [];

    /// <summary>
    /// Oldest first. The pipeline caps this at <see cref="Pipeline.TranslationPipeline.ContextWindow"/>
    /// lines; it must stay small, because cached translations replay WITHOUT this context - the
    /// cache key deliberately hashes the body alone - and the window is what keeps that
    /// approximation tolerable.
    /// </summary>
    public IReadOnlyList<string> ContextLines => PreviousLines ?? [];

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

    /// <summary>
    /// False when the lane has no key yet and so cannot be tried at all.
    ///
    /// <para>
    /// This is what makes a paid lane safe to ship enabled: without a key it is skipped in silence
    /// rather than failing once per translated line and burying the router log - which is the log
    /// the user reads when something is actually wrong. It is re-read per request, so pasting a key
    /// into Settings brings the lane to life without a restart.
    /// </para>
    /// </summary>
    bool IsConfigured => true;

    Task<string> TranslateAsync(TranslationRequest request, string model, CancellationToken ct);
}

public enum ProviderFailure
{
    /// <summary>The model name is gone from the catalogue. Try the next model, and say so loudly.</summary>
    ModelNotFound,

    /// <summary>429. Stop using this provider for a while and move to the next lane.</summary>
    RateLimited,

    /// <summary>5xx, socket error. Worth a bounded retry — the next attempt may genuinely differ.</summary>
    Transient,

    /// <summary>
    /// The per-attempt cap fired. Distinct from <see cref="Transient"/> because the right response
    /// is the opposite: a 500 is worth the same model again, but a model that could not answer in
    /// ten seconds will not answer in the next ten either, and retrying it turns one slow model
    /// into half a minute of overlay silence. Move to the next model.
    /// </summary>
    Timeout,

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
