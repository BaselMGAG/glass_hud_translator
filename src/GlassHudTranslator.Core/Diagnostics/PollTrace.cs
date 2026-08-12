namespace GlassHudTranslator.Core.Diagnostics;

/// <summary>
/// One line per poll, in memory, so that "auto mode shows the previous sentence" can be answered
/// with a record instead of a theory.
///
/// <para>
/// <b>Why this exists.</b> The static self-test proved that the window, the region, the capture,
/// the text recognition and the translation all work — every one of them, on the machine where the
/// app was misbehaving. What it could not see is the POLL LOOP, which is where the failure actually
/// lives: a loop that decides thirty times a minute not to display anything looks identical, from
/// outside, to a loop that is broken. Each of those decisions is legitimate on its own — the frame
/// did not change, the line is a repeat, the region went empty — and the only way to tell a correct
/// run from a stuck one is to see WHICH decision is being taken over and over.
/// </para>
///
/// <para>
/// In memory rather than a file, and capped. A poll happens twice a second for as long as the
/// feature is on, so writing to disk per poll would put file IO in the path of a loop whose whole
/// design is that deciding to do nothing must be nearly free.
/// </para>
/// </summary>
public static class PollTrace
{
    /// <summary>
    /// Roughly two minutes at the dialogue rate, which is far longer than it takes to reproduce
    /// "it is showing the wrong line" and short enough to read.
    /// </summary>
    public const int Kept = 240;

    private static readonly Queue<string> Lines = new();
    private static readonly Lock Gate = new();

    /// <summary>Records one decision. The timestamp is what makes a stuck loop visible.</summary>
    public static void Write(string line)
    {
        lock (Gate)
        {
            Lines.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {line}");
            while (Lines.Count > Kept) Lines.Dequeue();
        }
    }

    public static IReadOnlyList<string> Recent()
    {
        lock (Gate) return [.. Lines];
    }

    /// <summary>Auto-watch starting: the previous run's decisions are about a different session.</summary>
    public static void Clear()
    {
        lock (Gate) Lines.Clear();
    }
}
