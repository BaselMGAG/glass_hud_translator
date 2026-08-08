using GlassHudTranslator.Core.Capture;
using SkiaSharp;

namespace GlassHudTranslator.Core.Diagnostics;

/// <summary>How hard the scene behind the dialogue box is to read through.</summary>
public enum SceneDifficulty
{
    /// <summary>A vertical gradient. Enough to prove change detection is not comparing flat fills.</summary>
    Gradient,

    /// <summary>
    /// Foliage-ish clutter: overlapping translucent shapes at many brightnesses, plus per-pixel
    /// noise. This is what a real 3D scene does behind a 72%-opaque box, and it is the condition
    /// the flat corpus never tested — Otsu binarisation has to separate glyphs from a background
    /// that is no longer one tone, and the change detector has to ignore a scene that is moving
    /// while the text is not.
    /// </summary>
    Busy,
}

/// <param name="Outlined">
/// Draw the body with a dark stroke under the fill, which is how most games keep text legible over
/// arbitrary scenery. It is also the single most common reason OCR tuned on clean UI fonts
/// degrades: the outline thickens strokes and closes counters, so <c>e</c> and <c>c</c> and <c>o</c>
/// start converging.
/// </param>
/// <param name="Revealed">
/// Characters of <paramref name="Body"/> to draw, for typewriter frames. Null means all of it.
/// </param>
public sealed record SyntheticLine(
    string? Speaker,
    string Body,
    bool BrightScene = false,
    bool Truncated = false,
    SceneDifficulty Scene = SceneDifficulty.Gradient,
    bool Outlined = false,
    float TextSize = 24,
    int? Revealed = null)
{
    /// <summary>What is actually on screen in this frame, once a partial reveal is applied.</summary>
    public string VisibleBody =>
        Revealed is { } n && n < Body.Length ? Body[..Math.Max(0, n)] : Body;
}

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

    /// <summary>
    /// The frames the flat corpus never covered, and the reason OCR accuracy here means nothing yet.
    ///
    /// <para>
    /// Still synthetic — no synthetic frame will ever tell you how Tesseract handles a particular
    /// game's typeface, and real captures remain the gap. But these stop the corpus being trivially
    /// easy in ways real frames never are: a busy scene bleeding through a translucent box, outlined
    /// glyphs, small text, and the apostrophes and mixed case that are most of what a game glossary
    /// contains. If a change makes OCR worse, it should show up here rather than in the wild.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<SyntheticLine> AdversarialCorpus =
    [
        // The scene the flat corpus never had: clutter at many brightnesses behind the box.
        new("Y'shtola", "The aether here is thick with malice.", Scene: SceneDifficulty.Busy),

        // Outlined text over clutter, which is how most games actually draw dialogue.
        new("Alphinaud", "We must reach the Crystarium before the Sin Eaters do.",
            Scene: SceneDifficulty.Busy, Outlined: true),

        // Apostrophe-heavy proper nouns are the documented OCR failure mode - "Y shtola" for
        // "Y'shtola" is the reason the corrections dictionary exists at all.
        new("G'raha Tia", "Y'shtola and G'raha Tia spoke of the Ala Mhigan resistance.",
            Scene: SceneDifficulty.Busy, Outlined: true),

        // Small text over a bright busy scene: the worst realistic combination, because
        // binarisation has the least contrast to work with.
        new("Tataru", "Don't forget your linkpearl - I'll not be reminding you again!",
            BrightScene: true, Scene: SceneDifficulty.Busy, Outlined: true, TextSize: 18),

        // Long enough to wrap three times, so block grouping and line joining are exercised.
        new("Urianger", "Mine own counsel doth suggest that we tarry not, for the hour groweth "
                        + "late and the road to Limsa Lominsa is long and beset with peril.",
            Scene: SceneDifficulty.Busy, Outlined: true),

        // Digits and punctuation, which normalisation treats differently from letters.
        new(null, "The gate closes in 3 minutes, 20 seconds. Hurry!",
            Scene: SceneDifficulty.Busy, TextSize: 20),

        // No speaker, bright, outlined - the subtitle shape rather than the dialogue-box shape.
        new(null, "A chill wind blows across the Coerthas highlands.",
            BrightScene: true, Scene: SceneDifficulty.Busy, Outlined: true),
    ];

    /// <summary>
    /// One line revealed character by character, as a game's typewriter effect does it.
    ///
    /// <para>
    /// There were no frame <em>sequences</em> in this project at all, only independent stills — so
    /// nothing downstream of capture could be tested against the behaviour that actually costs
    /// money. A hotkey pressed mid-reveal captures a truncated line, and a truncated line is a
    /// different cache key, a wasted request, and a translation of half a sentence. Both the
    /// stability reader and any future typewriter detection need this shape to be testable at all.
    /// </para>
    ///
    /// <para>
    /// The last two frames are identical on purpose: that is the signal "the line has finished
    /// drawing" and the condition a stability check waits for.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SyntheticLine> Typewriter(SyntheticLine line, int steps = 6)
    {
        if (steps < 2) steps = 2;

        var frames = new List<SyntheticLine>(steps);

        // steps - 1 partial reveals, then the settled line twice.
        for (var i = 1; i < steps - 1; i++)
            frames.Add(line with { Revealed = (int)(line.Body.Length * (i / (double)(steps - 1))) });

        frames.Add(line with { Revealed = null });
        frames.Add(line with { Revealed = null });
        return frames;
    }

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

        PaintScene(canvas, line.BrightScene, line.Scene);
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
    private static void PaintScene(SKCanvas canvas, bool bright, SceneDifficulty difficulty)
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

        if (difficulty == SceneDifficulty.Busy) PaintClutter(canvas, bright);
    }

    /// <summary>
    /// Overlapping shapes and per-pixel noise, standing in for a 3D scene behind the box.
    ///
    /// <para>
    /// Deterministic — a fixed seed, because a corpus that differs between runs cannot be a
    /// regression benchmark. The point is not photorealism, it is that the background is no longer
    /// one tone: Otsu picks a single global threshold, so a scene with bright and dark regions
    /// under the same translucent box is what actually breaks binarisation.
    /// </para>
    /// </summary>
    private static void PaintClutter(SKCanvas canvas, bool bright)
    {
        var random = new Random(20260808);

        for (var i = 0; i < 40; i++)
        {
            var shade = (byte)random.Next(bright ? 120 : 20, bright ? 255 : 110);
            using var blob = new SKPaint
            {
                Color = new SKColor(shade, (byte)(shade * 0.92), (byte)(shade * 0.78), 150),
                IsAntialias = true,
            };

            float x = random.Next(-40, Width + 40);
            float y = random.Next(-40, Height + 40);
            float w = random.Next(30, 190);
            float h = random.Next(20, 130);

            if (i % 3 == 0) canvas.DrawOval(new SKRect(x, y, x + w, y + h), blob);
            else canvas.DrawRect(new SKRect(x, y, x + w, y + h), blob);
        }

        // Fine grain on top. Real capture always carries some, and a corpus without it lets a
        // preprocessing change look better than it is.
        using var speck = new SKPaint { IsAntialias = false };
        for (var i = 0; i < 5000; i++)
        {
            var v = (byte)random.Next(0, 255);
            speck.Color = new SKColor(v, v, v, 34);
            canvas.DrawRect(random.Next(0, Width), random.Next(0, Height), 2, 2, speck);
        }
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
            TextSize = line.TextSize,
        };

        // A dark stroke under the fill, the way games keep text readable over arbitrary scenery.
        // It is also why OCR tuned on clean UI text degrades: the outline thickens strokes and
        // closes counters, so e, c and o start converging.
        using var outlinePaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 220),
            IsAntialias = true,
            Typeface = typeface,
            TextSize = line.TextSize,
            IsStroke = true,
            StrokeWidth = Math.Max(1.5f, line.TextSize / 12f),
            StrokeJoin = SKStrokeJoin.Round,
        };

        var lineHeight = line.TextSize * 1.5f;

        foreach (var wrapped in Wrap(line.VisibleBody, bodyPaint, Width - 112))
        {
            if (line.Outlined) canvas.DrawText(wrapped, 56, y, outlinePaint);
            canvas.DrawText(wrapped, 56, y, bodyPaint);
            y += lineHeight;
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
        if (line.Scene == SceneDifficulty.Busy) suffix += "-busy";
        if (line.Revealed is { } shown) suffix += $"-reveal{shown:D3}";
        return slug + suffix;
    }

    private static string Quote(string text) =>
        System.Text.Json.JsonSerializer.Serialize(text);
}
