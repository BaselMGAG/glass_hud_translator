using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Ocr;

namespace GlassHudTranslator.Core.Regions;

/// <summary>What a block of text on screen looks like it is for.</summary>
public enum TextRegionKind
{
    /// <summary>Text was found and grouped, but its shape and position say nothing in particular.</summary>
    Unknown,

    /// <summary>A wide multi-line block low on the screen. The usual shape of a dialogue box.</summary>
    Dialogue,

    /// <summary>One or two centred lines near the bottom edge. Cutscene subtitles.</summary>
    Subtitle,

    /// <summary>Narrow and tall against a side edge. A quest log or objective list.</summary>
    SidePanel,
}

/// <summary>
/// A rectangle the app is willing to suggest, with the evidence behind it.
///
/// <para>
/// <paramref name="Score"/> only orders candidates within one call. It is not a probability and it
/// is not comparable between frames, so nothing should render it as a percentage - a number shown
/// to a user is a claim about the world, and this one is a sorting key.
/// </para>
/// </summary>
public sealed record RegionCandidate(
    CaptureRegion Bounds,
    TextRegionKind Kind,
    int WordCount,
    int LineCount,
    float MeanConfidence,
    double Score);

public sealed record RegionFinderOptions
{
    /// <summary>
    /// Below this many words a block is a HUD label, a timer or a stray mark, not something anyone
    /// is reading a translation of. Three is deliberately forgiving: "Aye, my lord." is three.
    /// </summary>
    public int MinimumWords { get; init; } = 3;

    /// <summary>
    /// Two words share a line when their vertical extents overlap by at least this much of the
    /// shorter one. Baselines wobble by a pixel or two and a comma is half the height of a capital,
    /// so this compares overlap rather than tops.
    /// </summary>
    public double SameLineOverlap { get; init; } = 0.5;

    /// <summary>
    /// A horizontal gap wider than this many times the line height splits one row of words into
    /// two blocks. It is what keeps a left-hand HUD label and a right-hand clock, which share a row
    /// and nothing else, from being merged into one very wide "region".
    /// </summary>
    public double ColumnGapInLineHeights { get; init; } = 2.5;

    /// <summary>
    /// Lines closer together than this many line heights belong to the same block. Above roughly
    /// two, separate paragraphs start merging into one rectangle that spans the gap between them.
    /// </summary>
    public double LineGapInLineHeights { get; init; } = 1.6;

    /// <summary>Breathing room around a proposal, as a fraction of line height. A box cropped tight
    /// to the glyphs clips the descenders and marks that OCR needs on the next read.</summary>
    public double PaddingInLineHeights { get; init; } = 0.4;
}

/// <summary>
/// Turns the word boxes from a full-frame OCR into a few rectangles worth offering as capture
/// regions, so the answer to "where is the dialogue?" can be a question with a picture rather than
/// an instruction to drag a box over something.
///
/// <para>
/// Pure geometry: no OCR, no capture, no I/O. It takes words and a frame size and returns
/// rectangles, which is what makes the ranking testable against layouts written out by hand rather
/// than against whatever the synthetic frame generator happens to draw. That distinction matters
/// here - tuning these rules against rendered test frames would measure the renderer.
/// </para>
///
/// <para>
/// It returns an empty list rather than a weak guess, and that is the central decision. A proposal
/// is offered to someone who cannot check it against the English, at the first moment they use the
/// app; one confident rectangle around a health bar teaches them that the green ticks in this
/// program are not to be trusted, and nothing offered afterwards recovers that. Blank is a worse
/// experience and a better outcome.
/// </para>
/// </summary>
public static class RegionFinder
{
    /// <summary>Ranked best first. Callers should show two or three, never a long list.</summary>
    public static IReadOnlyList<RegionCandidate> Propose(
        IReadOnlyList<OcrWord> words, int frameWidth, int frameHeight,
        RegionFinderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(words);
        var opts = options ?? new RegionFinderOptions();

        if (frameWidth <= 0 || frameHeight <= 0) return [];

        // Accepted words only. A UI border read as "|~" at confidence 8 is exactly the evidence
        // that invents a text region where there is no text, and the whole value of a proposal is
        // that it is right the first time.
        var usable = words
            .Where(w => w.Accepted && !w.Box.IsEmpty && w.Text.Length > 0)
            .OrderBy(w => w.Box.Top).ThenBy(w => w.Box.Left)
            .ToList();

        if (usable.Count < opts.MinimumWords) return [];

        var blocks = GroupIntoBlocks(GroupIntoLines(usable, opts), opts);

        return blocks
            .Where(b => b.Words.Count >= opts.MinimumWords)
            .Select(b => Describe(b, frameWidth, frameHeight, opts))
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Bounds.Y)          // deterministic when two blocks score identically
            .ThenBy(c => c.Bounds.X)
            .ToList();
    }

    private sealed record Line(List<OcrWord> Words)
    {
        public int Top => Words.Min(w => w.Box.Top);
        public int Bottom => Words.Max(w => w.Box.Bottom);
        public int Left => Words.Min(w => w.Box.Left);
        public int Right => Words.Max(w => w.Box.Right);
        public int Height => Math.Max(1, Bottom - Top);
    }

    private sealed record Block(List<Line> Lines)
    {
        public List<OcrWord> Words => [.. Lines.SelectMany(l => l.Words)];
    }

    /// <summary>
    /// Rows first, because reading order is the one piece of structure that is reliable. Words join
    /// a row when they vertically overlap it, then a row splits wherever the horizontal gap is wide
    /// enough to be a different thing rather than a wider space.
    /// </summary>
    private static List<Line> GroupIntoLines(List<OcrWord> words, RegionFinderOptions opts)
    {
        // Against the row's whole vertical span, not against the word most recently added to it.
        // Comparing to the last word lets the band walk down the screen a pixel at a time: each
        // word overlaps its predecessor, none overlaps the row it started as, and a long row ends
        // up spanning text that was never on the same line.
        var rows = new List<(int Top, int Bottom, List<OcrWord> Words)>();

        foreach (var word in words)
        {
            var index = rows.FindIndex(r =>
                Overlaps(r.Top, r.Bottom, word.Box.Top, word.Box.Bottom, word.Box.Height,
                    opts.SameLineOverlap));

            if (index < 0)
            {
                rows.Add((word.Box.Top, word.Box.Bottom, [word]));
                continue;
            }

            var row = rows[index];
            row.Words.Add(word);
            rows[index] = (Math.Min(row.Top, word.Box.Top), Math.Max(row.Bottom, word.Box.Bottom),
                row.Words);
        }

        var lines = new List<Line>();
        foreach (var (top, bottom, row) in rows)
        {
            row.Sort((a, b) => a.Box.Left.CompareTo(b.Box.Left));

            var height = Math.Max(1, bottom - top);
            var maxGap = height * opts.ColumnGapInLineHeights;

            var current = new List<OcrWord> { row[0] };
            var reach = row[0].Box.Right;

            for (var i = 1; i < row.Count; i++)
            {
                // Against the furthest right edge reached so far, not the previous word's, so one
                // long word does not hide the gap that follows a short one beside it.
                if (row[i].Box.Left - reach > maxGap)
                {
                    lines.Add(new Line(current));
                    current = [];
                }

                current.Add(row[i]);
                reach = Math.Max(reach, row[i].Box.Right);
            }

            lines.Add(new Line(current));
        }

        return [.. lines.OrderBy(l => l.Top).ThenBy(l => l.Left)];
    }

    private static bool Overlaps(int topA, int bottomA, int topB, int bottomB, int heightB,
        double required)
    {
        var overlap = Math.Min(bottomA, bottomB) - Math.Max(topA, topB);
        if (overlap <= 0) return false;

        var shorter = Math.Max(1, Math.Min(bottomA - topA, heightB));
        return (double)overlap / shorter >= required;
    }

    /// <summary>
    /// Lines stack into a block when they are vertically close AND horizontally overlapping. Both
    /// conditions are needed: proximity alone merges a subtitle with the hotbar underneath it, and
    /// overlap alone merges two paragraphs at opposite ends of the screen that happen to share a
    /// column.
    /// </summary>
    private static List<Block> GroupIntoBlocks(List<Line> lines, RegionFinderOptions opts)
    {
        var blocks = new List<Block>();

        foreach (var line in lines)
        {
            var host = blocks.FirstOrDefault(b =>
            {
                var last = b.Lines[^1];
                var gap = line.Top - last.Bottom;
                var allowed = Math.Max(last.Height, line.Height) * opts.LineGapInLineHeights;
                return gap <= allowed && HorizontallyOverlaps(last, line);
            });

            if (host is null) blocks.Add(new Block([line]));
            else host.Lines.Add(line);
        }

        return blocks;
    }

    private static bool HorizontallyOverlaps(Line a, Line b)
    {
        var overlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        return overlap > 0;
    }

    private static RegionCandidate Describe(
        Block block, int frameWidth, int frameHeight, RegionFinderOptions opts)
    {
        var words = block.Words;
        var lineHeight = block.Lines.Max(l => l.Height);
        var pad = (int)Math.Round(lineHeight * opts.PaddingInLineHeights);

        var left = block.Lines.Min(l => l.Left) - pad;
        var top = block.Lines.Min(l => l.Top) - pad;
        var right = block.Lines.Max(l => l.Right) + pad;
        var bottom = block.Lines.Max(l => l.Bottom) + pad;

        // Padding must not push the proposal off the frame: a region that fails FitsWithin is one
        // the capture layer will refuse, which would present the user with a suggestion that
        // cannot be accepted.
        left = Math.Max(0, left);
        top = Math.Max(0, top);
        right = Math.Min(frameWidth, right);
        bottom = Math.Min(frameHeight, bottom);

        var bounds = new CaptureRegion(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        var kind = Classify(bounds, block.Lines.Count, frameWidth, frameHeight);

        return new RegionCandidate(
            bounds, kind, words.Count, block.Lines.Count,
            words.Average(w => w.Confidence),
            Score(bounds, kind, words.Count, frameWidth, frameHeight));
    }

    private static TextRegionKind Classify(
        CaptureRegion bounds, int lineCount, int frameWidth, int frameHeight)
    {
        var centreX = bounds.X + bounds.Width / 2.0;
        var offCentre = Math.Abs(centreX - frameWidth / 2.0) / (frameWidth / 2.0);
        var bottomEdge = (bounds.Y + bounds.Height) / (double)frameHeight;
        var widthFraction = bounds.Width / (double)frameWidth;
        var heightFraction = bounds.Height / (double)frameHeight;

        // Narrow, tall and hugging a side: an objective list or a chat log, not the line the player
        // is trying to read. Checked first because such a block can also sit low enough to satisfy
        // the dialogue test.
        var hugsASide = bounds.X > frameWidth * 0.6 || bounds.X + bounds.Width < frameWidth * 0.4;
        if (hugsASide && widthFraction < 0.3 && heightFraction > 0.15)
            return TextRegionKind.SidePanel;

        if (bottomEdge <= 0.6 || offCentre >= 0.25) return TextRegionKind.Unknown;

        // A dialogue box is a box: wide, and more than one line of it.
        if (lineCount >= 2 && widthFraction > 0.35) return TextRegionKind.Dialogue;

        // A subtitle is as wide as its sentence, not as wide as a container, so it does not get the
        // dialogue box's width test - it would fail it on any short line. What it does need is to
        // be near the bottom edge rather than merely in the lower half, which is what separates it
        // from a stray centred label.
        if (lineCount <= 2 && bottomEdge > 0.75 && widthFraction > 0.15)
            return TextRegionKind.Subtitle;

        return TextRegionKind.Unknown;
    }

    /// <summary>
    /// Additive and deliberately blunt. Every term is something that can be pointed at in a frame,
    /// because a scoring function nobody can explain is one nobody can debug when it proposes the
    /// minimap.
    /// </summary>
    private static double Score(
        CaptureRegion bounds, TextRegionKind kind, int wordCount, int frameWidth, int frameHeight)
    {
        var score = 0.0;

        // Recognised shapes win. The classifier is the part carrying actual knowledge of how games
        // lay text out; the rest of this only breaks ties.
        score += kind switch
        {
            TextRegionKind.Dialogue => 3.0,
            TextRegionKind.Subtitle => 2.0,
            TextRegionKind.SidePanel => 1.0,
            _ => 0.0,
        };

        // More words is more prose and less chrome, with a ceiling so a wall of chat log cannot
        // outrank a correctly shaped dialogue box on volume alone.
        score += Math.Min(1.0, wordCount / 20.0);

        // Story text sits low. A block in the top strip is a party list or a buff bar.
        score += (bounds.Y + bounds.Height) / (double)frameHeight;

        // Corners are where HUD lives. Distance from the vertical centreline, cheaply.
        var centreX = bounds.X + bounds.Width / 2.0;
        score -= Math.Abs(centreX - frameWidth / 2.0) / frameWidth;

        return score;
    }
}
