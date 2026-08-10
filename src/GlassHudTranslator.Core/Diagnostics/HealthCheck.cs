using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Translation;

namespace GlassHudTranslator.Core.Diagnostics;

public enum HealthSeverity
{
    /// <summary>Working as intended. Said out loud anyway — a check that only ever reports
    /// problems reads as broken the day everything is fine.</summary>
    Ok,

    /// <summary>Works, but worse than it could. The app keeps going.</summary>
    Warning,

    /// <summary>Translation will not happen until this is fixed.</summary>
    Problem,
}

/// <summary>
/// One line of the report. <paramref name="Machine"/> marks a finding whose text is dominated by
/// identifiers — lane names, a version, a window title — which must stay left-to-right in the
/// mirrored layout or the order of the lanes reads backwards.
/// </summary>
public sealed record HealthFinding(HealthSeverity Severity, string Text, bool Machine = false);

/// <summary>The state of one provider lane, after actually asking it.</summary>
public sealed record LaneHealth(string Name, KeyStatus Status, string? Detail);

/// <summary>
/// The raw facts, gathered by the App layer, judged here.
///
/// <para>
/// This split is the whole design. Detection needs Win32 calls, live key probes and a running
/// window; judgement needs none of that, and judgement is the part with enough branches to get
/// wrong. A record of plain values in, a list of sentences out, and every rule testable on a
/// machine with no Windows and no game.
/// </para>
/// </summary>
public sealed record HealthInputs
{
    // ── detection 1: interface language vs the machine's ────────────────────────────────────
    /// <summary>Two-letter ISO code of the Windows display language, e.g. "ar".</summary>
    public string? SystemLanguage { get; init; }

    public UiLanguage InterfaceLanguage { get; init; } = UiLanguage.English;

    // ── detections 2 and 4: the game window ──────────────────────────────────────────────────
    /// <summary>Null when no game window was found at all.</summary>
    public string? GameWindowTitle { get; init; }

    /// <summary>False when the window was found but cannot be captured — exclusive fullscreen.</summary>
    public bool CanCapture { get; init; } = true;

    /// <summary>The platform's own explanation for why capture is blocked.</summary>
    public string? CaptureBlocker { get; init; }

    /// <summary>True when the active profile targets the whole screen rather than a window,
    /// in which case "no game window found" is the normal state and not a finding.</summary>
    public bool ProfileTargetsWholeScreen { get; init; }

    public string ProfileName { get; init; } = "";

    // ── detection 3: DPI ─────────────────────────────────────────────────────────────────────
    /// <summary>1.0 at 100%. Zero or negative means "could not be read".</summary>
    public double DisplayScaling { get; init; } = 1.0;

    // ── detection 11: keys, actually probed ──────────────────────────────────────────────────
    public IReadOnlyList<LaneHealth> Lanes { get; init; } = [];

    // ── detection 12: hardware class ─────────────────────────────────────────────────────────
    public int ProcessorCount { get; init; }

    public double MemoryGb { get; init; }

    // ── the pieces of this app that can silently be absent ───────────────────────────────────
    /// <summary>False when no OCR engine could be loaded — natives quarantined, tessdata gone.</summary>
    public bool OcrAvailable { get; init; } = true;

    public string? OcrDetail { get; init; }

    /// <summary>True once the user has drawn a capture region for the active profile themselves.</summary>
    public bool RegionSaved { get; init; }

    /// <summary>Mean OCR confidence of the most recent real read, if any line has been read.</summary>
    public float? LastOcrConfidence { get; init; }
}

/// <summary>
/// Turns the gathered facts into a short report in plain language, worst news first.
///
/// <para>
/// This is roadmap detection work, and the standing observation behind it: every failure that has
/// reached a real user so far — the missing key behind an off-screen Save button, the fullscreen
/// game that silently cannot be captured, the app that "does not open" — was something the app
/// could have known and said. Each check here is one of those conversations, had by the app, in
/// the user's language, before it becomes a support thread.
/// </para>
/// </summary>
public static class HealthCheck
{
    /// <summary>Below this mean confidence the region is readable but poorly, worth saying.</summary>
    public const float LowConfidence = 60f;

    public static IReadOnlyList<HealthFinding> Run(HealthInputs inputs, UiText text)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(text);

        var findings = new List<HealthFinding>();

        Keys(inputs, text, findings);
        Ocr(inputs, text, findings);
        GameWindow(inputs, text, findings);
        Region(inputs, text, findings);
        Scaling(inputs, text, findings);
        Language(inputs, findings);
        Hardware(inputs, text, findings);

        // Worst first, stable within a severity. Someone with one problem and six green ticks
        // must not have to scroll past the ticks to find it.
        return [.. findings.OrderByDescending(f => f.Severity)];
    }

    private static void Keys(HealthInputs inputs, UiText text, List<HealthFinding> findings)
    {
        var configured = inputs.Lanes.Where(l => l.Status != KeyStatus.NotSet).ToList();

        // No keys at all is the v0.5.0 failure, word for word: the app looked configured, nothing
        // translated, and the log line that named the cause was read as a symptom. Loudest first.
        if (configured.Count == 0)
        {
            findings.Add(new HealthFinding(HealthSeverity.Problem, text.HealthNoKeys));
            return;
        }

        var working = configured.Where(l => l.Status == KeyStatus.Working).Select(l => l.Name).ToList();
        var rejected = configured.Where(l => l.Status == KeyStatus.Rejected).Select(l => l.Name).ToList();
        var unknown = configured.Where(l => l.Status == KeyStatus.Unknown).Select(l => l.Name).ToList();

        if (working.Count > 0)
            findings.Add(new HealthFinding(HealthSeverity.Ok,
                string.Format(text.HealthKeysWorking, string.Join(" · ", working)), Machine: true));

        if (rejected.Count > 0)
            findings.Add(new HealthFinding(HealthSeverity.Problem,
                string.Format(text.HealthKeysRejected, string.Join(" · ", rejected)), Machine: true));

        // Unreachable is not rejected, and the distinction is load-bearing: telling someone their
        // key is wrong when their wifi is down sends them to regenerate a key that was fine.
        if (unknown.Count > 0)
            findings.Add(new HealthFinding(HealthSeverity.Warning,
                string.Format(text.HealthKeysUnknown, string.Join(" · ", unknown)), Machine: true));
    }

    private static void Ocr(HealthInputs inputs, UiText text, List<HealthFinding> findings)
    {
        if (inputs.OcrAvailable)
        {
            findings.Add(new HealthFinding(HealthSeverity.Ok, text.HealthOcrReady));
            return;
        }

        // The quarantine case. When the antivirus removes the natives the app starts, looks whole,
        // and reads nothing — and until now the only symptom was an empty overlay.
        findings.Add(new HealthFinding(HealthSeverity.Problem,
            inputs.OcrDetail is { Length: > 0 } detail
                ? $"{text.HealthOcrMissing}  ({detail})"
                : text.HealthOcrMissing));
    }

    private static void GameWindow(HealthInputs inputs, UiText text, List<HealthFinding> findings)
    {
        if (inputs.ProfileTargetsWholeScreen)
        {
            findings.Add(new HealthFinding(HealthSeverity.Ok, text.HealthWholeScreen));
            return;
        }

        if (inputs.GameWindowTitle is not { Length: > 0 } title)
        {
            findings.Add(new HealthFinding(HealthSeverity.Warning,
                string.Format(text.HealthGameNotFound, inputs.ProfileName)));
            return;
        }

        if (!inputs.CanCapture)
        {
            // The number-one silent failure, previously a README paragraph. The blocker text comes
            // from the platform and names the fix (borderless windowed).
            findings.Add(new HealthFinding(HealthSeverity.Problem,
                inputs.CaptureBlocker is { Length: > 0 } why
                    ? why
                    : string.Format(text.HealthGameBlocked, title)));
            return;
        }

        findings.Add(new HealthFinding(HealthSeverity.Ok,
            string.Format(text.HealthGameFound, title), Machine: true));
    }

    private static void Region(HealthInputs inputs, UiText text, List<HealthFinding> findings)
    {
        if (!inputs.RegionSaved)
        {
            // A default region exists and mostly reads the wrong thing. This is the finding the
            // auto-proposal answers; the message points at the picker, which now proposes.
            findings.Add(new HealthFinding(HealthSeverity.Problem,
                string.Format(text.HealthNoRegion, inputs.ProfileName)));
            return;
        }

        if (inputs.LastOcrConfidence is { } confidence)
        {
            findings.Add(confidence >= LowConfidence
                ? new HealthFinding(HealthSeverity.Ok,
                    string.Format(text.HealthReadingWell, Math.Round(confidence)))
                : new HealthFinding(HealthSeverity.Warning,
                    string.Format(text.HealthReadingPoorly, Math.Round(confidence))));
            return;
        }

        findings.Add(new HealthFinding(HealthSeverity.Ok, text.HealthRegionSaved));
    }

    private static void Scaling(HealthInputs inputs, UiText text, List<HealthFinding> findings)
    {
        if (inputs.DisplayScaling <= 0) return;   // unreadable; silence beats a made-up number

        // Reassurance, not alarm. Scaling used to be a silent misalignment bug; now that it is
        // handled, saying "seen and handled" is what stops it being suspected forever.
        if (Math.Abs(inputs.DisplayScaling - 1.0) > 0.01)
            findings.Add(new HealthFinding(HealthSeverity.Ok,
                string.Format(text.HealthScaling, Math.Round(inputs.DisplayScaling * 100))));
    }

    private static void Language(HealthInputs inputs, List<HealthFinding> findings)
    {
        // Windows says Arabic, the interface is showing English: the person this app was built
        // for is looking at the wrong language. The advice is IN ARABIC, deliberately and always —
        // it is addressed to someone who reads Arabic, and showing it in English is the bug it
        // reports. The reverse case is silence: an Arabic interface on an English Windows was a
        // choice someone made.
        if (string.Equals(inputs.SystemLanguage, "ar", StringComparison.OrdinalIgnoreCase)
            && inputs.InterfaceLanguage == UiLanguage.English)
        {
            findings.Add(new HealthFinding(HealthSeverity.Warning,
                "يبدو أن ويندوز لديك بالعربية — يمكنك تحويل واجهة البرنامج إلى العربية من "
                + "الإعدادات ← المزوّدون ← «Language · اللغة»."));
        }
    }

    private static void Hardware(HealthInputs inputs, UiText text, List<HealthFinding> findings)
    {
        if (inputs.ProcessorCount <= 0) return;

        // Informational: translation is cloud-side, so hardware almost never matters. Recorded
        // anyway because it rides into the diagnostics people paste into a bug report, and "how
        // big is the machine" is the second question every report gets asked.
        findings.Add(new HealthFinding(HealthSeverity.Ok,
            string.Format(text.HealthHardware, inputs.ProcessorCount, Math.Round(inputs.MemoryGb)),
            Machine: true));
    }
}
