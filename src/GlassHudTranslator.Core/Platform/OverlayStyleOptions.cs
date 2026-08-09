namespace GlassHudTranslator.Core.Platform;

/// <summary>
/// Which of the four overlay window bits a given top-level window actually wants.
///
/// <para>
/// It used to be one method that unconditionally OR'd all four, which was correct while there was
/// exactly one such window. There are three now — the translation panel, the toolbar and the
/// capture frame — and only the first of them wants clicks to fall through. A toolbar you cannot
/// click is not a toolbar.
/// </para>
///
/// <para>
/// In Core rather than in the Windows project because the App names this type at the call site,
/// outside its <c>#if WINDOWS</c> guard. The implementation stays behind the seam; only the
/// vocabulary crosses it.
/// </para>
/// </summary>
public sealed record OverlayStyleOptions
{
    /// <summary>The translation panel: invisible to the mouse and to screen capture alike.</summary>
    public static readonly OverlayStyleOptions Panel = new();

    /// <summary>
    /// The toolbar and the capture frame in adjust mode: clickable, still never stealing focus,
    /// still invisible to our own capture.
    /// </summary>
    public static readonly OverlayStyleOptions Interactive = new() { ClickThrough = false };

    /// <summary>
    /// <c>WS_EX_TRANSPARENT</c>. True lets clicks reach the game underneath.
    ///
    /// <para>
    /// Set and cleared, never only set. The old code OR'd it in and left it there, so a window that
    /// had been click-through once could never become clickable again — which is exactly what the
    /// capture frame does every time the user asks to move it.
    /// </para>
    /// </summary>
    public bool ClickThrough { get; init; } = true;

    /// <summary>
    /// <c>WS_EX_NOACTIVATE</c>. True means clicking the window never pulls keyboard focus off the
    /// game.
    ///
    /// <para>
    /// True is right and true is unverified. A no-activate window still receives mouse messages at
    /// the Win32 level — it is <c>WS_EX_TRANSPARENT</c>, not this, that makes a window invisible to
    /// the pointer — but whether Avalonia's input stack delivers them to a window it never
    /// activates has not been tested on hardware. Everything that would depend on activation is
    /// avoided deliberately: the toolbar drags itself by moving its own <c>Position</c> rather than
    /// calling <c>BeginMoveDrag</c>, and nothing on it takes keyboard input. If it does turn out to
    /// be dead to the mouse, <see cref="Config.AppSettings.ToolbarCanTakeFocus"/> switches this off
    /// without a new build.
    /// </para>
    /// </summary>
    public bool NoActivate { get; init; } = true;

    /// <summary>
    /// <c>WDA_EXCLUDEFROMCAPTURE</c>. True keeps the window out of every screen capture, including
    /// our own — which is what stops the pipeline reading its own output back — and out of the
    /// user's recordings, which is the cost.
    /// </summary>
    public bool HideFromCapture { get; init; } = true;
}

/// <summary>What actually happened. <paramref name="Warning"/> is null when everything applied.</summary>
public sealed record OverlayStyleResult(bool StylesApplied, bool ExcludedFromCapture, string? Warning);
