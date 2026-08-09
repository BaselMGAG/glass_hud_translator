using Avalonia;
using Avalonia.Controls;
using Shape = Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace GlassHudTranslator.App.Views;

/// <summary>
/// The toolbar's icons, as path geometry written out here rather than as characters from a font.
///
/// <para>
/// That is not a stylistic preference. The bundled Arabic font contains no Latin and no symbols at
/// all — not <c>A</c>, not <c>%</c>, and none of <c>✓ ✗ ⚠ → · ⏎</c> — so every glyph outside Arabic
/// already depends on whatever the operating system substitutes. It works today and it is exactly
/// the dependency the font was bundled to remove. Worse, it has already failed once in a way that
/// was not local to the character: two Unicode isolate marks used for text direction resolved in no
/// available font, and that single unresolvable codepoint poisoned glyph fallback for an entire
/// window, so every Latin word in the interface rendered as an empty box. It reproduced on every
/// run.
/// </para>
///
/// <para>
/// A toolbar has no text on it. If its glyphs are the thing that fails, there is nothing left — so
/// they are geometry, they ship inside the assembly, and no font on the target machine can change
/// what they look like. Everything is drawn on a 24×24 box and scaled by
/// <see cref="Icons.Draw"/>, so one number changes the size of all of them.
/// </para>
/// </summary>
public sealed record IconGeometry(string Outline, string? Solid = null);

public static class Icons
{
    /// <summary>A speech bubble with an arrow through it: take this and turn it into that.</summary>
    public static readonly IconGeometry TranslateNow = new(
        "M 3,5.5 A 2.5,2.5 0 0 1 5.5,3 L 18.5,3 A 2.5,2.5 0 0 1 21,5.5 L 21,14.5 "
        + "A 2.5,2.5 0 0 1 18.5,17 L 9,17 L 5,21 L 5,17 A 2.5,2.5 0 0 1 3,14.5 Z",
        "M 7.5,9 L 13,9 L 13,6.5 L 17.5,10 L 13,13.5 L 13,11 L 7.5,11 Z");

    /// <summary>An eye. Watching, which is exactly what auto-watch does.</summary>
    public static readonly IconGeometry AutoWatch = new(
        "M 2,12 C 5,6.5 8.5,4.5 12,4.5 C 15.5,4.5 19,6.5 22,12 "
        + "C 19,17.5 15.5,19.5 12,19.5 C 8.5,19.5 5,17.5 2,12 Z",
        "M 12,9 A 3,3 0 1 0 12,15 A 3,3 0 1 0 12,9 Z");

    /// <summary>Four corner marks around a point: draw a box around one thing.</summary>
    public static readonly IconGeometry Snip = new(
        "M 3,8 L 3,4 L 7,4 M 17,4 L 21,4 L 21,8 M 21,16 L 21,20 L 17,20 M 7,20 L 3,20 L 3,16",
        "M 12,10 A 2,2 0 1 0 12,14 A 2,2 0 1 0 12,10 Z");

    /// <summary>A rectangle with grab handles: the region that gets read, every time.</summary>
    public static readonly IconGeometry PickRegion = new(
        "M 4,6 L 20,6 L 20,18 L 4,18 Z",
        "M 2.5,4.5 L 5.5,4.5 L 5.5,7.5 L 2.5,7.5 Z M 18.5,4.5 L 21.5,4.5 L 21.5,7.5 L 18.5,7.5 Z "
        + "M 18.5,16.5 L 21.5,16.5 L 21.5,19.5 L 18.5,19.5 Z M 2.5,16.5 L 5.5,16.5 L 5.5,19.5 L 2.5,19.5 Z");

    /// <summary>The same rectangle, drawn as a broken line: show me where it is.</summary>
    public static readonly IconGeometry CaptureFrame = new(
        "M 4,6 L 9,6 M 12,6 L 16,6 M 19,6 L 20,6 L 20,9 M 20,12 L 20,15 "
        + "M 20,17 L 20,18 L 16,18 M 12,18 L 9,18 M 6,18 L 4,18 L 4,15 M 4,12 L 4,9");

    /// <summary>The translation panel with a line through it.</summary>
    public static readonly IconGeometry HideOverlay = new(
        "M 3,7 L 21,7 L 21,17 L 3,17 Z M 4.5,20 L 19.5,4");

    /// <summary>Sliders. A gear says "machinery"; sliders say "things you can change".</summary>
    public static readonly IconGeometry Settings = new(
        "M 3.5,7 L 20.5,7 M 3.5,12 L 20.5,12 M 3.5,17 L 20.5,17",
        "M 8,5 L 10.4,5 L 10.4,9 L 8,9 Z M 14,10 L 16.4,10 L 16.4,14 L 14,14 Z "
        + "M 6,15 L 8.4,15 L 8.4,19 L 6,19 Z");

    public static readonly IconGeometry More = new("M 8,6 L 14,12 L 8,18 M 14,6 L 20,12 L 14,18");

    public static readonly IconGeometry Less = new("M 16,6 L 10,12 L 16,18 M 10,6 L 4,12 L 10,18");

    /// <summary>A strip of film: this is a video, not a dialogue box.</summary>
    public static readonly IconGeometry WatchMode = new(
        "M 3,5 L 21,5 L 21,19 L 3,19 Z M 7.5,5 L 7.5,19 M 16.5,5 L 16.5,19 M 3,12 L 21,12");

    /// <summary>Two marks above a baseline, which is what tashkeel looks like.</summary>
    public static readonly IconGeometry Diacritics = new(
        "M 4,15.5 L 20,15.5 M 7,10 L 10.5,6.5 M 13.5,10 L 17,6.5");

    public static readonly IconGeometry PinCorrection = new(
        "M 4,20 L 4,16.5 L 15.5,5 L 19,8.5 L 7.5,20 Z M 13.5,7 L 17,10.5");

    public static readonly IconGeometry Quit = new(
        "M 12,3 L 12,11 M 7.4,6.6 A 6.6,6.6 0 1 0 16.6,6.6");

    public static readonly IconGeometry Collapse = new("M 14,6 L 8,12 L 14,18 M 19,5 L 19,19");

    /// <summary>Three columns of dots. The one control on the toolbar you grab rather than press.</summary>
    public static readonly IconGeometry Grip = new(
        "",
        "M 9,5 L 11,5 L 11,7 L 9,7 Z M 13,5 L 15,5 L 15,7 L 13,7 Z "
        + "M 9,9.5 L 11,9.5 L 11,11.5 L 9,11.5 Z M 13,9.5 L 15,9.5 L 15,11.5 L 13,11.5 Z "
        + "M 9,14 L 11,14 L 11,16 L 9,16 Z M 13,14 L 15,14 L 15,16 L 13,16 Z "
        + "M 9,18.5 L 11,18.5 L 11,20.5 L 9,20.5 Z M 13,18.5 L 15,18.5 L 15,20.5 L 13,20.5 Z");

    /// <summary>
    /// Renders one icon at <paramref name="size"/> device-independent pixels.
    ///
    /// <para>
    /// Stroke thickness is scaled with the icon rather than fixed, so the shapes keep their weight
    /// if the size ever changes. <c>StrokeLineCap</c> round is what stops the open paths — the
    /// broken rectangle, the chevrons — looking chipped at small sizes.
    /// </para>
    /// </summary>
    public static Control Draw(IconGeometry icon, double size, IBrush brush)
    {
        ArgumentNullException.ThrowIfNull(icon);

        var canvas = new Canvas { Width = 24, Height = 24 };

        if (icon.Outline.Length > 0)
        {
            canvas.Children.Add(new Shape.Path
            {
                Data = Geometry.Parse(icon.Outline),
                Stroke = brush,
                StrokeThickness = 1.7,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
            });
        }

        if (icon.Solid is { Length: > 0 } solid)
            canvas.Children.Add(new Shape.Path { Data = Geometry.Parse(solid), Fill = brush });

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = canvas,
        };
    }
}
