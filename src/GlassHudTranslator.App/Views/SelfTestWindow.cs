using System.Diagnostics;
using System.Text;
using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Ocr;
using GlassHudTranslator.Core.Regions;

namespace GlassHudTranslator.App;

/// <summary>
/// Everything the app believes about the machine it is on, written to a file the user can send.
///
/// <para>
/// <b>It exists because the last round of support was diagnosed by guessing.</b> A report of "auto
/// mode shows the previous sentence, and it says the game is not borderless when it is" can be
/// caused by half a dozen things, all of them on a Windows machine nobody here can reach, and the
/// only evidence available was a stack trace for a different bug entirely. Every question that was
/// asked and could not be answered is a line in here.
/// </para>
///
/// <para>
/// <b>It reports facts, never verdicts.</b> Which window was picked and HOW it was picked, what
/// the display guard concluded and from which numbers, what the region resolved to, what the OCR
/// actually read and how much of it was thrown away. A line saying "capture works" would be worth
/// nothing; a line saying which of five windows was chosen and why is worth the whole exercise.
/// </para>
/// </summary>
public static class SelfTest
{
    public static async Task<string> RunAsync(
        AppServices services, AppSettings settings, TranslationSession session, string directory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(directory);

        var report = new StringBuilder();
        var path = Path.Combine(directory, "self-test.txt");

        void Say(string line) => report.AppendLine(line);
        void Heading(string title) => report.AppendLine().AppendLine(title).AppendLine(new string('-', title.Length));

        Say($"Glass HUD Translator self-test, {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Say($"version   {Core.Update.UpdateCheck.RunningVersion?.ToString() ?? "0.0.0-dev"}");
        Say($"os        {Environment.OSVersion}, 64-bit {Environment.Is64BitOperatingSystem}, cores {Environment.ProcessorCount}");
        Say($"base      {AppContext.BaseDirectory}");
        Say($"profile   {services.Profile.Id} ({services.Profile.DisplayName})");
        Say($"mode      {settings.WatchMode}, region '{settings.LastRegionProfile}'");
        Say($"vision    reader configured: {services.CanReadImages}, switched on: {settings.ReadUnreadableLinesAgain}");

        // ── 1. what windows exist, and which one the app picks ───────────────────────────────
        Heading("1. Windows on screen");

        try
        {
            var open = PlatformServices.ListOpenWindows();
            Say($"{open.Count} visible windows with a title:");
            foreach (var w in open.Take(25)) Say($"   {w.ProcessName,-24} {w.Title}");
            if (open.Count > 25) Say($"   ... and {open.Count - 25} more");
        }
        catch (Exception e)
        {
            Say($"FAILED to list windows: {e.Message}");
        }

        Heading("2. Which one the app thinks is the game");

        Say($"profile looks for titles [{string.Join(", ", services.Profile.WindowTitles)}]");
        Say($"profile looks for processes [{string.Join(", ", services.Profile.ProcessNames)}]");

        if (!services.Profile.IsWindowBound)
        {
            Say("This profile is screen-relative - it does NOT look for a window, it uses whichever");
            Say("monitor the front-most window (not one of ours) is on. That is the 'general' case.");
        }

        try
        {
            var window = PlatformServices.FindGameWindow(
                services.Profile.WindowTitles, services.Profile.ProcessNames);

            if (window is null)
            {
                Say("RESULT: no window found at all.");
            }
            else
            {
                Say($"RESULT: '{window.Title}'");
                Say($"   client area   {window.ClientArea}");
                Say($"   scaling       {window.Scaling:P0}");
                Say($"   can capture   {window.CanCapture}");
                Say($"   guard says    {window.Message}");

                if (!window.CanCapture)
                {
                    Say("");
                    Say("   ^^ THIS is the message about borderless/fullscreen. Note WHICH window it");
                    Say("      is about: if the title above is not your game, the app picked the wrong");
                    Say("      window and the message is about that one.");
                }
            }
        }
        catch (Exception e)
        {
            Say($"FAILED: {e}");
        }

        Say($"virtual desktop  {PlatformServices.VirtualDesktop()}");

        // ── 3. the region, from the stored fractions ─────────────────────────────────────────
        Heading("3. The capture region");

        try
        {
            var profile = await services.Regions
                .LoadOrDefaultAsync(services.Profile.Id, settings.LastRegionProfile, CancellationToken.None);

            Say($"stored as     x{profile.RelX:F3} y{profile.RelY:F3} w{profile.RelWidth:F3} h{profile.RelHeight:F3}");
            Say($"drawn on      {profile.Resolution} at {profile.UiScale:P0} (provenance: {profile.HasProvenance})");

            var window = PlatformServices.FindGameWindow(
                services.Profile.WindowTitles, services.Profile.ProcessNames);

            if (window is not null)
            {
                var outcome = RegionResolver.Resolve(
                    profile, window.ClientArea, window.Scaling, PlatformServices.VirtualDesktop());

                Say($"resolves to   {(outcome.Region?.ToString() ?? "NOTHING")}");
                Say($"complaints    {(outcome.Warnings.Count == 0 ? "none" : string.Join(", ", outcome.Warnings))}");
                if (outcome.Failure is { } failure) Say($"refused       {failure}");
            }
        }
        catch (Exception e)
        {
            Say($"FAILED: {e}");
        }

        // ── 4. capture and read it, for real ─────────────────────────────────────────────────
        Heading("4. Capturing and reading that region, right now");

        try
        {
            var region = await ReadCurrentRegionAsync(services, settings);

            if (region is null)
            {
                Say("No region to capture - see above.");
            }
            else
            {
                // The LIVE session's frame source, never a second one. Creating another and
                // disposing it released the shared screen device context out from under this one,
                // and every capture afterwards - auto-watch and the translate hotkey alike -
                // silently returned nothing. A diagnostic that breaks what it is diagnosing is
                // worse than no diagnostic at all.
                var frame = await session.CaptureAsync(region.Value, CancellationToken.None);

                if (frame is null)
                {
                    Say("Capture returned nothing.");
                    Say($"   because     {session.LastCaptureFailure ?? "(the source did not say)"}");
                    Say("   ^^ THAT is the answer. Three unrelated Win32 failures used to arrive here");
                    Say("      as one silent null, and two support rounds were spent guessing between");
                    Say("      them. A device-context failure is a resource leak in this app; a BitBlt");
                    Say("      refusal is usually exclusive fullscreen or a rectangle off every screen.");
                }
                else
                {
                    var shot = Path.Combine(directory, "captured.png");
                    frame.SavePng(shot);
                    Say($"captured      {frame.Width}x{frame.Height}, saved beside this file as captured.png");
                    Say("   ^^ LOOK AT THAT IMAGE. If it is not the text you expected, the problem is");
                    Say("      the region or the window, and nothing after this point matters.");

                    var stopwatch = Stopwatch.StartNew();
                    var read = await services.Ocr.RecognizeAsync(frame, CancellationToken.None);
                    stopwatch.Stop();

                    Say($"ocr took      {stopwatch.ElapsedMilliseconds} ms");
                    Say($"confidence    {read.Confidence:F1}");
                    Say($"words kept    {read.WordCount}, thrown away {read.RejectedWordCount}");
                    Say($"read          {(read.RawText.Length == 0 ? "(nothing)" : read.RawText.ReplaceLineEndings(" / "))}");

                    var decision = EscalationPolicy.Decide(
                        read, settings.ReadUnreadableLinesAgain && services.CanReadImages);

                    Say($"second reader {(decision.Escalate ? "WOULD be asked" : "not asked")} - {decision.Why}");
                }
            }
        }
        catch (Exception e)
        {
            Say($"FAILED: {e}");
        }

        // ── 5. the pipeline itself, against known frames ─────────────────────────────────────
        Heading("5. The pipeline, against bundled test frames");
        Say("These are pictures shipped with the app, so this section tests translation WITHOUT");
        Say("depending on your screen, your game or your capture region at all. If this passes and");
        Say("section 4 fails, the problem is capture. If this fails too, it is the pipeline.");
        Say("");

        try
        {
            var frames = Directory.Exists(RepoPaths.TestFrames)
                ? Directory.GetFiles(RepoPaths.TestFrames, "*.png").OrderBy(f => f).Take(3).ToArray()
                : [];

            if (frames.Length == 0)
            {
                Say($"No test frames found at {RepoPaths.TestFrames}");
            }

            foreach (var file in frames)
            {
                var frame = Frame.FromFile(file);
                var outcome = await services.Pipeline.ProcessAsync(frame, ct: CancellationToken.None);

                Say($"{Path.GetFileName(file)}");
                Say($"   read       {outcome.Body.ReplaceLineEndings(" / ")}");
                Say($"   confidence {outcome.OcrConfidence:F1}");
                Say($"   result     {(outcome.Result is { } r ? $"{r.Provider}/{r.Model} -> {r.Text}" : "nothing attempted")}");
            }
        }
        catch (Exception e)
        {
            Say($"FAILED: {e}");
        }

        // ── 6. what the poll loop has been deciding ─────────────────────────────────────────
        Heading("6. The last two minutes of auto-watch, one line per poll");
        Say("Switch auto-watch ON, let it run through two or three lines of dialogue, then run this");
        Say("again. Every poll says what it decided. A healthy run alternates gate/read/SENT; a run");
        Say("that shows the same word over and over is the fault, and the word names it.");
        Say("");

        Say("The two numbers to read first are on every gate line. 'scene-moves' is how much this");
        Say("screen moves on its own, in cells of 1536, which no real game has ever been measured");
        Say("on. 'gave-up' counts lines the gate READ and then refused, because the words never");
        Say("read the same way twice - that is the only thing here that is genuinely a skipped");
        Say("line, so if it is 0 and a line was still missed, the fault is somewhere else.");
        Say("");

        var trace = Core.Diagnostics.PollTrace.Recent();
        if (trace.Count == 0) Say("(auto-watch has not run since the app started)");
        foreach (var line in trace) Say("   " + line);

        Heading("7. Recent provider log");
        var lines = services.RouterLog.TakeLast(30).ToList();
        if (lines.Count == 0) Say("(nothing yet)");
        foreach (var line in lines) Say("   " + line);

        await File.WriteAllTextAsync(path, report.ToString());
        return path;
    }

    private static async Task<CaptureRegion?> ReadCurrentRegionAsync(AppServices services, AppSettings settings)
    {
        var profile = await services.Regions
            .LoadOrDefaultAsync(services.Profile.Id, settings.LastRegionProfile, CancellationToken.None);

        var window = PlatformServices.FindGameWindow(
            services.Profile.WindowTitles, services.Profile.ProcessNames);

        if (window is null) return null;

        return RegionResolver
            .Resolve(profile, window.ClientArea, window.Scaling, PlatformServices.VirtualDesktop())
            .Region;
    }
}
