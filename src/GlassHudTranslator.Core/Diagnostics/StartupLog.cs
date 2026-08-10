using System.Text;

namespace GlassHudTranslator.Core.Diagnostics;

/// <summary>
/// The black box: a small text file that records whether the app started, and if it did not, why.
///
/// <para>
/// It exists because of one support conversation. "Nothing opens after Run anyway" — and there was
/// no way for the user, or for anyone helping them, to learn anything more. The process may never
/// have started (antivirus ate the exe), may have died loading a DLL (antivirus ate a dependency),
/// may have failed initialising graphics, or may be running perfectly with its error written on a
/// transparent window nobody can see. Four different problems, one identical symptom, zero
/// evidence. This file is the evidence: if it does not exist, the process never ran; if it ends at
/// "starting", something killed it before the UI; if it holds an exception, that is the answer.
/// </para>
///
/// <para>
/// Written next to the exe first, because that is the folder the user is already looking at, and
/// falling back to the app data directory when that folder is read-only. Overwritten per run, not
/// appended: the interesting run is the failing one, and on a failing run the file stays.
/// </para>
///
/// <para>
/// Never throws, buffered in memory, flushed whole on every entry. A diagnostic that can take the
/// app down is worse than no diagnostic, and a log that loses its tail on a hard crash records
/// everything except the part that mattered.
/// </para>
/// </summary>
public static class StartupLog
{
    public const string FileName = "startup.log";

    private static readonly object Gate = new();
    private static readonly StringBuilder Buffer = new();
    private static string? _path;

    /// <summary>Where the log actually landed, for showing to the user. Null until Begin ran.</summary>
    public static string? Path => _path;

    /// <summary>
    /// Opens the box. Records the version, the OS, and a census of the payload — how many
    /// assemblies are present and whether the OCR natives survived — because the most common cause
    /// of "nothing opens" is an antivirus quietly removing files after the user clicked Run anyway,
    /// and a count that says "3 DLLs" against a payload of 140 is that diagnosis made in one line.
    /// </summary>
    public static void Begin(string version)
    {
        lock (Gate)
        {
            Buffer.Clear();
            _path = null;

            Note($"Glass HUD Translator {version} starting, {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
            Note($"os: {Environment.OSVersion.VersionString}, 64-bit: {Environment.Is64BitProcess}, "
                 + $"cores: {Environment.ProcessorCount}");
            Note($"base: {AppContext.BaseDirectory}");
            Census();
        }
    }

    /// <summary>One line, flushed immediately. Safe from any thread, safe before Begin.</summary>
    public static void Note(string message)
    {
        lock (Gate)
        {
            Buffer.Append(message).Append('\n');
            Flush();
        }
    }

    /// <summary>The reason the app is not going to start, with the full stack.</summary>
    public static void Fail(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Note($"FAILED: {error}");
    }

    private static void Census()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var dlls = Directory.GetFiles(baseDir, "*.dll").Length;
            var natives = File.Exists(System.IO.Path.Combine(baseDir, "x64", "tesseract55.dll"));
            var tessdata = File.Exists(System.IO.Path.Combine(baseDir, "tessdata", "eng.traineddata"));

            Note($"payload: {dlls} assemblies, ocr natives {(natives ? "present" : "MISSING")}, "
                 + $"tessdata {(tessdata ? "present" : "MISSING")}");
        }
        catch (Exception e)
        {
            Note($"payload: census failed ({e.GetType().Name})");
        }
    }

    private static void Flush()
    {
        // The exe's folder first - it is where the user is looking - then the data directory,
        // which always exists and is always writable, for the Program Files case.
        foreach (var candidate in Candidates())
        {
            try
            {
                File.WriteAllText(candidate, Buffer.ToString());
                _path = candidate;
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                          or System.Security.SecurityException or NotSupportedException)
            {
                // Try the next home. Silence is the contract; the log must never be the crash.
            }
        }
    }

    private static IEnumerable<string> Candidates()
    {
        // Sticky: once a location has accepted a write, keep using it, so one file holds the whole
        // story rather than the head landing beside the exe and the tail landing in AppData.
        if (_path is not null)
        {
            yield return _path;
            yield break;
        }

        yield return System.IO.Path.Combine(AppContext.BaseDirectory, FileName);

        string? fallback = null;
        try
        {
            Directory.CreateDirectory(Storage.AppPaths.DataDirectory);
            fallback = System.IO.Path.Combine(Storage.AppPaths.DataDirectory, FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // No home at all. The buffer keeps accumulating in memory in case one appears.
        }

        if (fallback is not null) yield return fallback;
    }
}
