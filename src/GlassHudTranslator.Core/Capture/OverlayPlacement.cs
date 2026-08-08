namespace GlassHudTranslator.Core.Capture;

/// <summary>
/// Where the translation panel sits inside the game window.
///
/// <para>
/// Core rather than App, and pure arithmetic, so the part that can be got wrong is testable on a
/// machine with no Windows and no game. What the App layer keeps is only "which rectangle is the
/// game" and "move the window there".
/// </para>
///
/// <para>
/// The position used to be two constants - horizontally centred, top edge at 72% of the game's
/// height - tuned against one dialogue box in one game. Anywhere else that lands on top of
/// something, and there was no way to move it: the overlay is click-through by design, so it
/// cannot be dragged, and it has no Alt-Tab entry to grab.
/// </para>
/// </summary>
public static class OverlayPlacement
{
    /// <summary>Roughly where the old fixed position put it, and now guaranteed to stay inside.</summary>
    public const double DefaultVertical = 0.85;

    /// <summary>Centred, which is where a dialogue box usually is.</summary>
    public const double DefaultHorizontal = 0.5;

    /// <summary>
    /// Both fractions run 0 to 1 across the space the panel does not itself occupy, so 0 is flush
    /// with the top or left edge, 1 is flush with the bottom or right, and 0.5 is centred - and
    /// every value in between leaves the whole panel inside <paramref name="area"/>.
    ///
    /// <para>
    /// Measuring against the free space rather than against the window is what makes that true.
    /// The old formula placed the panel's top edge at a fraction of the window height, which says
    /// nothing about where its bottom edge lands: at 0.72 of a short window, or with a long line
    /// wrapping to four rows, the panel simply hung off the bottom of the game and the text was
    /// cut off by the screen.
    /// </para>
    /// </summary>
    public static (int X, int Y) Within(
        CaptureRegion area, int panelWidth, int panelHeight, double horizontal, double vertical)
    {
        // A panel wider or taller than the game window has no free space to distribute, and the
        // fraction would multiply a negative. Pin it to the top-left corner instead: some of it is
        // going to be off-screen whatever we do, and the start of the text is the part worth
        // keeping.
        var freeX = Math.Max(0, area.Width - panelWidth);
        var freeY = Math.Max(0, area.Height - panelHeight);

        return (area.X + (int)Math.Round(freeX * Clamp(horizontal)),
                area.Y + (int)Math.Round(freeY * Clamp(vertical)));
    }

    /// <summary>
    /// Settings files are hand-editable and a value from one is not to be trusted. A fraction
    /// outside 0-1 would put the panel off the screen entirely, which looks exactly like the app
    /// having stopped working.
    /// </summary>
    public static double Clamp(double fraction) =>
        double.IsNaN(fraction) ? 0.5 : Math.Clamp(fraction, 0, 1);
}
