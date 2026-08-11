using GlassHudTranslator.Core.Capture;

namespace GlassHudTranslator.Core.Regions;

/// <summary>
/// Something worth saying about a capture region. Returned as a REASON rather than a sentence, so
/// the rules can be tested without asserting on translated text — and so the App decides how loud
/// each one is, which is a presentation question this layer has no business answering.
/// </summary>
public enum RegionProblem
{
    /// <summary>The region was drawn against a differently sized window, so it may not line up.</summary>
    LayoutChanged,

    /// <summary>Part of the region hangs off the desktop and was cut back to fit.</summary>
    TrimmedToDesktop,

    /// <summary>All of it is off the desktop. There is nothing left to capture.</summary>
    EntirelyOffScreen,
}

/// <summary>
/// What to capture, and what to say about it.
/// </summary>
/// <param name="Region">Null when there is nothing capturable, in which case <paramref name="Failure"/> says why.</param>
/// <param name="Failure">Set only when <paramref name="Region"/> is null.</param>
/// <param name="Warnings">Worth reporting, but the capture goes ahead regardless.</param>
public sealed record RegionOutcome(
    CaptureRegion? Region,
    RegionProblem? Failure,
    IReadOnlyList<RegionProblem> Warnings)
{
    public static RegionOutcome Blocked(RegionProblem why) => new(null, why, []);
}

/// <summary>
/// Turns a stored fractional profile into the screen pixels to capture, and decides what is wrong
/// with the result.
///
/// <para>
/// <b>Split out of the App for the reason <c>HealthCheck</c> was: the rules are judgement over
/// plain facts, and judgement is the part that can be tested here.</b> What is left in the App is
/// the gathering — asking Win32 which window the game is, what the desktop spans — which is the
/// part that needs Windows and a running game and can only ever be rehearsed. Two of the three
/// defects this code has shipped were in the judgement rather than the gathering: a region on a
/// monitor left of the primary was refused because its origin is negative, and a region hanging
/// off the desktop was captured anyway, BitBlt'ing undefined pixels into OCR where it read as the
/// model getting worse. Neither had a test, and neither could have had one.
/// </para>
///
/// <para>
/// <b>Nothing here mutates.</b> "Warn once per layout" is deliberately NOT this type's job: it
/// returns every warning that applies, every time, and the caller decides what it has already said.
/// Keeping the once-only bookkeeping out of a pure function is what lets the same facts be asserted
/// twice in a test and get the same answer.
/// </para>
/// </summary>
public static class RegionResolver
{
    /// <summary>
    /// <paramref name="desktop"/> is the whole virtual desktop, which may start at a negative
    /// coordinate when a monitor sits left of or above the primary. Pass
    /// <see cref="CaptureRegion.Empty"/> when it is unknown and the bounds check is skipped —
    /// that is the honest behaviour off Windows, where there is no desktop to be off the edge of.
    /// </summary>
    public static RegionOutcome Resolve(
        RegionProfile profile, CaptureRegion client, double scaling, CaptureRegion desktop)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var warnings = new List<RegionProblem>();

        // Said whenever it applies. Whether the user has heard it before is the caller's business.
        if (!profile.MatchesLayout(client.Width, client.Height, scaling))
            warnings.Add(RegionProblem.LayoutChanged);

        var region = profile.Resolve(client.Width, client.Height).Translate(client.X, client.Y);

        // Contains, never FitsWithin. Two questions that look alike and are not: FitsWithin asks
        // "is this inside a pixel buffer", so it requires a non-negative origin because there is no
        // pixel at -1; Contains asks "is this on the desktop", whose origin is wherever the monitors
        // put it. Asking the first question about the second thing is what made a game on a monitor
        // left of the primary uncapturable.
        if (desktop.IsEmpty || desktop.Contains(region))
            return new RegionOutcome(region, null, warnings);

        var trimmed = region.ClampTo(desktop);
        if (trimmed.IsEmpty) return RegionOutcome.Blocked(RegionProblem.EntirelyOffScreen);

        warnings.Add(RegionProblem.TrimmedToDesktop);
        return new RegionOutcome(trimmed, null, warnings);
    }
}
