using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace GlassHudTranslator.App.Views;

/// <summary>
/// Renders a window's content to a PNG for the documentation.
///
/// <para>
/// Screenshots are generated from the running UI rather than taken by hand, because a hand-taken
/// one is stale the next time the layout moves - which is exactly what happened to the first
/// settings screenshot, within a day of the tabs landing.
/// </para>
/// </summary>
internal static class WindowSnapshot
{
    public static void Save(Control root, double width, double height, bool rightToLeft, string path)
    {
        var size = new PixelSize((int)width, (int)height);
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(root);

        if (!rightToLeft)
        {
            bitmap.Save(path);
            return;
        }

        // Rendering a right-to-left subtree on its own loses the compensating transform the window
        // applies around it, so the bitmap comes out mirrored - letters and all - even though the
        // window on screen is correct. Flipping it back is exact, because what was applied was a
        // single flip of the whole surface. Documentation-only: nothing here affects the live UI.
        using var buffer = new MemoryStream();
        bitmap.Save(buffer);
        buffer.Position = 0;

        using var rendered = SkiaSharp.SKBitmap.Decode(buffer);
        using var flipped = new SkiaSharp.SKBitmap(rendered.Width, rendered.Height);
        using (var canvas = new SkiaSharp.SKCanvas(flipped))
        {
            canvas.Scale(-1, 1, rendered.Width / 2f, 0);
            canvas.DrawBitmap(rendered, 0, 0);
        }

        using var image = SkiaSharp.SKImage.FromBitmap(flipped);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        encoded.SaveTo(file);
    }
}
