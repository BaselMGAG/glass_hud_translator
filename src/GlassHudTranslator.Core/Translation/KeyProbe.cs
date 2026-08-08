namespace GlassHudTranslator.Core.Translation;

/// <summary>What a key turned out to be, once something actually asked.</summary>
public enum KeyStatus
{
    /// <summary>No key entered, so nothing was asked.</summary>
    NotSet,

    Working,

    /// <summary>The provider rejected the key. Retrying will not help; it needs a different key.</summary>
    Rejected,

    /// <summary>
    /// The key may well be fine — the provider could not be reached, was rate limited, or has
    /// retired every model configured for it. Deliberately distinct from <see cref="Rejected"/>:
    /// telling someone their key is wrong when their wifi is down sends them to regenerate a key
    /// that was never the problem.
    /// </summary>
    Unknown,
}

public sealed record KeyProbeResult(KeyStatus Status, string? Detail = null)
{
    public static readonly KeyProbeResult NotSet = new(KeyStatus.NotSet);
}

/// <summary>
/// Answers "does this key actually work?" before the user is in a game.
///
/// <para>
/// Until now a mistyped or expired key was indistinguishable from a correct one right up to the
/// first translation, where the symptom is the OCR'd English on the overlay with a warning marker
/// and the real reason buried in an English router log. For someone who does not read English
/// comfortably that is not a diagnosable failure — it is just a broken app.
/// </para>
///
/// <para>
/// The probe is a real translation through the real lane, not a bespoke health endpoint. Providers
/// differ on what an unauthenticated request returns, and a lane that answers a HEAD request
/// happily can still reject a completion; the only thing that proves a key works for this app is
/// doing what this app does. It is one request of a few tokens, which is the cheapest honest
/// answer available.
/// </para>
/// </summary>
public static class KeyProbe
{
    /// <summary>Short and unambiguous, so a working lane comes back fast and cheap.</summary>
    private const string Probe = "Hello.";

    /// <summary>
    /// Never throws — this is a diagnostic, and a diagnostic that fails loudly while reporting on
    /// something else is worse than useless. Everything unexpected becomes
    /// <see cref="KeyStatus.Unknown"/>.
    /// </summary>
    public static async Task<KeyProbeResult> TestAsync(
        ITranslationProvider provider, TimeSpan budget, CancellationToken ct)
    {
        if (!provider.IsConfigured) return KeyProbeResult.NotSet;
        if (provider.Models.Count == 0) return new KeyProbeResult(KeyStatus.Unknown, "no models configured");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(budget);

        var request = new TranslationRequest(
            Probe, Speaker: null, Glossary: [], PreviousLines: null,
            Register: ArabicRegister.ModernStandard, RequestedAt: DateTimeOffset.UtcNow);

        // Walk the model list exactly as the router does. A provider whose models have been retired
        // is a live situation - the whole Gemini 2.x list went in August 2026 - and reporting that
        // as a bad key would send the user to regenerate a key that was never the problem.
        //
        // Every retired model is collected rather than the message being overwritten by whichever
        // failed last. That is not tidiness: when all of them were gone, the report named only the
        // final entry, so a lane that had lost its entire catalogue looked like one stale model. It
        // sent the investigation after the one name it printed, which was the least interesting of
        // the three, and hid that the list needed replacing rather than trimming.
        var gone = new List<string>();
        KeyProbeResult? other = null;

        foreach (var model in provider.Models)
        {
            try
            {
                var reply = await provider.TranslateAsync(request, model, timeout.Token)
                    .ConfigureAwait(false);

                return string.IsNullOrWhiteSpace(reply)
                    ? new KeyProbeResult(KeyStatus.Unknown, $"{model} returned nothing")
                    : new KeyProbeResult(KeyStatus.Working, model);
            }
            catch (ProviderException e) when (e.Failure == ProviderFailure.Fatal)
            {
                // The one verdict worth stating plainly. Every provider here returns 401/403 for a
                // bad key, and OpenAiCompatibleProvider and AnthropicProvider both map that to Fatal.
                return new KeyProbeResult(KeyStatus.Rejected, e.Message);
            }
            catch (ProviderException e) when (e.Failure == ProviderFailure.ModelNotFound)
            {
                gone.Add(model);
            }
            catch (ProviderException e)
            {
                // Rate limited or transient. The key is probably fine and we cannot prove it.
                other = new KeyProbeResult(KeyStatus.Unknown, e.Message);
            }
            catch (OperationCanceledException)
            {
                other = new KeyProbeResult(KeyStatus.Unknown, "timed out");
            }
            catch (Exception e)
            {
                // Deliberately broad, and the one place in this codebase where that is right. A
                // provider SDK can throw anything - a JSON shape it did not expect, an
                // InvalidOperationException from deep inside a client - and this is a button in a
                // settings window whose entire job is to report on something else. An unhandled
                // exception here would take down the window the user is trying to configure, to
                // tell them nothing. The router has the same contract for the same reason.
                other = new KeyProbeResult(KeyStatus.Unknown, e.Message);
            }
        }

        // Every model retired and nothing else went wrong: say so as one fact rather than as the
        // name of whichever happened to be tried last. This is a configuration problem in
        // models.json, not a problem with the key, and the message has to make that obvious enough
        // that nobody goes looking for a new key.
        if (gone.Count > 0 && gone.Count == provider.Models.Count)
            return new KeyProbeResult(KeyStatus.Unknown, $"no model left: {string.Join(", ", gone)}");

        if (other is not null) return other;

        return gone.Count > 0
            ? new KeyProbeResult(KeyStatus.Unknown, $"gone: {string.Join(", ", gone)}")
            : new KeyProbeResult(KeyStatus.Unknown, "no model answered");
    }
}
