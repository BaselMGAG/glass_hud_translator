using GlassHudTranslator.Core.Capture;
using SkiaSharp;

namespace GlassHudTranslator.Core.Diagnostics;

public sealed record SyntheticLine(string? Speaker, string Body, bool BrightScene = false, bool Truncated = false);

/// <summary>
/// Draws FFXIV-shaped dialogue frames so the pipeline can be exercised before anyone has been to
/// the Windows machine.
///
/// <para>
/// These are scaffolding, not a substitute for the real corpus: they are rendered in a system font
/// at a chosen size against a flat background, so they say nothing about how Tesseract handles
/// FFXIV's actual typeface, its translucency, or a moving scene behind the box. What they do is
/// let every stage downstream of capture be built and tested on the Mac, and be swapped for real
/// PNGs later without a code change (CODING_SESSIONS.md, Session 1 task 3).
/// </para>
/// </summary>
public static class SyntheticFrames
{
    public const int Width = 880;
    public const int Height = 240;

    public static readonly IReadOnlyList<SyntheticLine> Corpus =
    [
        new("Y'shtola", "Come, the aether here grows unstable."),

        // Byte-identical repeat: the second poll of a box the player has not advanced yet. Should
        // be dropped by change detection before it ever reaches OCR.
        new("Y'shtola", "Come, the aether here grows unstable."),

        new("Y'shtola", "We must reach Limsa Lominsa before nightfall.", BrightScene: true),
        new("Alphinaud", "The Scions of the Seventh Dawn stand ready."),
        new("Thancred", "I have seen enough of the Garlean Empire for one lifetime."),
        new("G'raha Tia", "The Crystarium welcomes you, Warrior of Light.", BrightScene: true),
        new("Tataru", "Do not forget your linkpearl this time!"),
        new(null, "The Warrior of Light draws near to the aetheryte."),
        new(null, "A chill wind blows across Coerthas.", BrightScene: true),
        new("Estinien", "But I thought-"),
        new("Urianger", "Mine own counsel doth suggest otherwise, my friend."),
        new("Alisaie", "Enough talk. Let us be about it."),
        new("Y'shtola", "Come, the aether here grows uns", Truncated: true),

        // Same line as frame 1, re-encountered later against a different scene. Not consecutive,
        // so change detection lets it through - and it must then come back from the cache rather
        // than costing a second request. This is the hit rate the whole quota argument rests on.
        new("Y'shtola", "Come, the aether here grows unstable.", BrightScene: true),
    ];

    /// <summary>Writes the corpus plus an expected.json that turns it into an accuracy benchmark.</summary>
    public static IReadOnlyList<string> WriteCorpus(string directory)
    {
        Directory.CreateDirectory(directory);

        var written = new List<string>();
        var expected = new List<string>();

        for (var i = 0; i < Corpus.Count; i++)
        {
            var line = Corpus[i];
            var name = $"{i + 1:D2}-{Slug(line)}.png";
            var path = Path.Combine(directory, name);

            Render(line).SavePng(path);
            written.Add(path);

            expected.Add($$"""
                  {
                    "file": {{Quote(name)}},
                    "speaker": {{(line.Speaker is null ? "null" : Quote(line.Speaker))}},
                    "body": {{Quote(line.Body)}},
                    "synthetic": true
                  }
                """);
        }

        File.WriteAllText(Path.Combine(directory, "expected.json"),
            "[\n" + string.Join(",\n", expected) + "\n]\n");

        return written;
    }

    public static Frame Render(SyntheticLine line)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        PaintScene(canvas, line.BrightScene);
        PaintBox(canvas);
        PaintText(canvas, line);

        canvas.Flush();
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        return Frame.FromSkBitmap(bitmap);
    }

    /// <summary>
    /// A gradient rather than a flat fill, so the translucent box sits over varying brightness -
    /// which is the condition that decides whether change detection actually works.
    /// </summary>
    private static void PaintScene(SKCanvas canvas, bool bright)
    {
        var (top, bottom) = bright
            ? (new SKColor(214, 196, 148), new SKColor(158, 176, 190))
            : (new SKColor(28, 32, 46), new SKColor(12, 14, 20));

        using var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, Height), [top, bottom], null, SKShaderTileMode.Clamp),
        };
        canvas.DrawRect(new SKRect(0, 0, Width, Height), paint);
    }

    private static void PaintBox(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(10, 12, 20, 184),   // ~72% opaque, like the game's box
            IsAntialias = true,
        };
        canvas.DrawRoundRect(new SKRect(24, 24, Width - 24, Height - 24), 10, 10, paint);
    }

    private static void PaintText(SKCanvas canvas, SyntheticLine line)
    {
        using var typeface = SKTypeface.FromFamilyName("Helvetica")
                             ?? SKTypeface.CreateDefault();

        var y = 82f;

        if (line.Speaker is not null)
        {
            using var speakerPaint = new SKPaint
            {
                Color = new SKColor(233, 205, 138),   // the game tints speaker names gold
                IsAntialias = true,
                Typeface = typeface,
                TextSize = 26,
                IsStroke = false,
            };
            canvas.DrawText(line.Speaker, 56, y, speakerPaint);
            y += 44;
        }

        using var bodyPaint = new SKPaint
        {
            Color = new SKColor(240, 240, 240),
            IsAntialias = true,
            Typeface = typeface,
            TextSize = 24,
        };

        foreach (var wrapped in Wrap(line.Body, bodyPaint, Width - 112))
        {
            canvas.DrawText(wrapped, 56, y, bodyPaint);
            y += 36;
        }
    }

    private static IEnumerable<string> Wrap(string text, SKPaint paint, float maxWidth)
    {
        var current = "";
        foreach (var word in text.Split(' '))
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (paint.MeasureText(candidate) > maxWidth && current.Length > 0)
            {
                yield return current;
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0) yield return current;
    }

    private static string Slug(SyntheticLine line)
    {
        var basis = (line.Speaker ?? line.Body).ToLowerInvariant();
        var slug = new string(basis.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray())
            .Trim().Replace(' ', '-');
        if (slug.Length > 24) slug = slug[..24].TrimEnd('-');

        var suffix = line.Truncated ? "-truncated" : line.BrightScene ? "-bright" : "";
        return slug + suffix;
    }

    private static string Quote(string text) =>
        System.Text.Json.JsonSerializer.Serialize(text);
}
