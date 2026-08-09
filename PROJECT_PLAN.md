# Glass HUD Translator — Build Plan

Implementation companion to [`docs/BRIEF.md`](docs/BRIEF.md).

The brief settles **what and why**. This document settles **the file layout, the type contracts,
the schemas, and the order of work** — the things a coding session needs in order to not invent
its own structure. Where the two disagree, the deltas in §1 win; everything else in the brief stands.

Coding sessions and their prompts: [`CODING_SESSIONS.md`](CODING_SESSIONS.md).

---

## 1. Deltas against the brief

Five corrections, found by checking the brief against this actual machine.

### 1.1 `net10.0`, not `net9.0`

This Mac has **only the .NET 10 SDK** (10.0.105) and only the 10.0.5 runtime. There is no .NET 9
runtime to run against. Every TFM in the brief shifts up one:

```
net9.0          →  net10.0
net9.0-windows  →  net10.0-windows
```

CI `setup-dotnet` uses `10.0.x`. This is a find-and-replace, no design consequence.

### 1.2 Compile coverage is ~100%, not 50–70%

The brief estimates half the codebase builds on the Mac. With Avalonia it is effectively all of it.

`net10.0-windows` **without an OS version suffix** compiles fine on macOS — the TFM only applies
`[SupportedOSPlatform("windows")]`, which is an analyzer contract, not a build-time OS requirement.
P/Invoke declarations are just attributes. What actually blocks a macOS build is `UseWPF` /
`UseWindowsForms`, which pull in Windows-only MSBuild targets.

So with Avalonia: `Core`, `Interop`, `Windows`, `App`, and tests all compile locally. Win32 code
simply cannot *run* here. That is a meaningfully better position than the brief assumes — a typo in
`Win32FrameSource` is caught on the Mac, not on the borrowed laptop.

The App still multi-targets `net10.0;net10.0-windows` so the platform analyzer keeps the honest
line between "runs anywhere" and "Windows only":

```xml
<TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks>

<ItemGroup Condition="'$(TargetFramework)' == 'net10.0-windows'">
  <ProjectReference Include="..\GlassHudTranslator.Windows\GlassHudTranslator.Windows.csproj" />
</ItemGroup>
```

`WINDOWS` is auto-defined for the Windows TFM, so platform selection is:

```csharp
public static IFrameSource CreateFrameSource(AppSettings s) =>
#if WINDOWS
    new Win32FrameSource();
#else
    new FolderFrameSource(s.TestFramesPath);
#endif
```

### 1.3 Secret storage needs a non-Windows sibling

The brief specifies DPAPI (`ProtectedData.Protect`) for the API key. `ProtectedData` throws
`PlatformNotSupportedException` off Windows, so the settings screen would be undebuggable locally.
Put it behind a seam:

```csharp
public interface ISecretStore {
    string? Get(string name);
    void Set(string name, string value);
}
// DpapiSecretStore       — Windows, DataProtectionScope.CurrentUser   (shipped)
// DevPlainFileSecretStore— macOS dev only; writes 0600 secrets.dev.json, gitignored
```

`DevPlainFileSecretStore` must log a visible warning on construction so it can never be shipped by
accident.

### 1.4 `System.Drawing` is out; use SkiaSharp

`System.Drawing.Common` is Windows-only from .NET 7 onward, which would push the whole preprocessing
chain (greyscale, threshold, 2× upscale) into the Windows project and off the Mac. Avalonia already
depends on SkiaSharp, so it costs nothing to use `SKBitmap` and keep preprocessing in Core where it
can be unit-tested.

The frame type crossing the `IFrameSource` boundary is a plain buffer, not an `Image`:

```csharp
public sealed record Frame(int Width, int Height, byte[] Bgra);
```

`Win32FrameSource` converts the `BitBlt` HBITMAP to BGRA once at the boundary. Everything downstream
is platform-free.

### 1.5 Ordering fix inside the normalizer

The brief's §5 normalization order lowercases before hashing, which is right for the **cache key**
but must not touch the **text sent to the model** — Tesseract's casing is a real signal, and
`"limsa lominsa"` translates worse than `"Limsa Lominsa"`. Split them:

```
raw OCR
  → apply OCR correction dictionary
  → collapse whitespace, strip trailing cursor glyph / ellipsis
  ├─► Body        (case preserved)  → prompt, display, logging
  └─► lowercase → SHA-256           → cache key only
```

Same normalization, two outputs. The brief's intent is preserved; only the branch point moves.

---

## 2. Resolved open questions

Updating brief §15.

| # | Question | Resolution |
|---|---|---|
| 1 | Avalonia vs WPF | **Unresolved by design — first 15 minutes of Session 1.** See §7 |
| 2 | Groq `qwen3-32b` real RPD | Don't block on it. Build the quota counter (brief §12) to *measure* it — one evening of play answers it with data instead of docs |
| 3 | Windows App SDK AI OCR Copilot+ gated | Treat as **yes, gated** — the Windows AI APIs target NPU-equipped Copilot+ hardware, which the target machine is not. Confirms Tesseract. Costs nothing to be wrong about; it was already rejected on MSIX grounds |
| 4 | Actual Gemini limits | Same as #2 — the counter measures it. Set the bucket to 13 RPM and observe |
| 5 | Tesseract accuracy on real frames | Measured in Session 3 against the Session 0 frame corpus. `tools/Replay` prints raw OCR next to expected text |
| 6 | BitBlt sufficient? | Ship BitBlt. It is 1–3 ms for one small rect. Escalate only if measured latency fails the <50 ms gate |
| 7 | MSA vs Egyptian | **Ask the primary user before Session 3.** Default MSA, register selectable in settings — it's one line of the system prompt, so being wrong is cheap |

Only #1 and #7 need a human. #1 is a 15-minute test; #7 is a text message.

---

## 3. Solution layout

```
GlassHudTranslator.sln
├── src/
│   ├── GlassHudTranslator.Core/           net10.0                  ← all logic, all tests
│   │   ├── Capture/        IFrameSource, Frame, FolderFrameSource, FrameHasher
│   │   ├── Ocr/            IOcrEngine, TesseractCliEngine, OcrPreprocessor, StableOcrReader
│   │   ├── Text/           TextNormalizer, DialogueParser, CacheKey
│   │   ├── Glossary/       GlossaryStore, GlossaryMatcher, GlossaryTerm
│   │   ├── Translation/    ITranslationProvider, OpenAiCompatibleProvider, AnthropicProvider,
│   │   │                   ProviderFactory, ProviderDiagnostics, StubProvider,
│   │   │                   PromptBuilder, ProviderRouter, TokenBucket, QuotaLedger
│   │   ├── Storage/        AppDatabase, TranslationCache, TranslationLog, ISecretStore
│   │   ├── Regions/        RegionProfile, RegionProfileStore
│   │   ├── Config/         AppSettings, ModelsConfig, UiText
│   │   └── Update/         UpdateCheck — notifies about new releases, never installs
│   ├── GlassHudTranslator.Interop/        net10.0-windows          ← P/Invoke declarations only
│   ├── GlassHudTranslator.Windows/        net10.0-windows          ← Win32 implementations
│   │   ├── Win32FrameSource, GameWindowLocator, DisplayModeGuard
│   │   ├── GlobalHotkeyService (RegisterHotKey + message-only window)
│   │   ├── OverlayWindowStyles (WS_EX_TRANSPARENT, topmost, WDA_EXCLUDEFROMCAPTURE)
│   │   ├── TesseractNativeEngine (nuget natives)
│   │   └── DpapiSecretStore
│   └── GlassHudTranslator.App/            net10.0;net10.0-windows  ← Avalonia
│       ├── Views/          OverlayWindow, RegionPickerWindow, SettingsWindow, FirstRunWindow
│       ├── ViewModels/
│       ├── PlatformServices.cs         ← the single #if WINDOWS switchboard
│       └── Assets/Fonts/NotoSansArabic-Regular.ttf
├── tests/GlassHudTranslator.Core.Tests/   net10.0   xunit
├── tools/Replay/                       net10.0   headless pipeline harness
├── data/    glossary.json · ocr-corrections.json · models.json · regions.default.json
├── test-frames/                        ← Session 0 corpus, ~40 PNGs + expected.json
├── docs/BRIEF.md
├── .github/workflows/build.yml
└── CLAUDE.md
```

**`PlatformServices.cs` is the only file in the App with `#if WINDOWS` in it.** If a second one
appears, the seam has leaked.

---

## 4. Contracts

Exact signatures for Session 1. Nothing here needs Windows to compile *or* run.

```csharp
// ── Capture ────────────────────────────────────────────────────────────────
public readonly record struct CaptureRegion(int X, int Y, int Width, int Height);
public sealed record Frame(int Width, int Height, byte[] Bgra) {
    public static Frame FromPng(Stream s);
    public byte[] ToPng();
    public Frame Crop(CaptureRegion r);
}
public interface IFrameSource : IDisposable {
    Task<Frame?> GetFrameAsync(CaptureRegion region, CancellationToken ct);
}
// FolderFrameSource(string dir) — advances one PNG per call, wraps around
// brief §3 — the CPU saver. Replaces the 64-bit-hash sketch this section originally carried;
// see the type's own docs for why. Two reasons, both found while implementing:
//   1. 64 bits over a whole dialogue box is too coarse — one changed word often flips no bit,
//      and a false "unchanged" is a missed translation. A false "changed" costs one 80 ms OCR.
//   2. The box is translucent, so the scene bleeds through and raw grey levels drift constantly
//      as the camera moves. Binarising first anchors the signature to the near-white glyphs.
// Sampling is inset 8% from each edge: a hand-dragged region always catches some raw scene, and
// that margin tracks the scene undamped, so walking dark→bright zone would swamp the signature.
public sealed class FrameSignature {
    public const int Width = 64, Height = 24, CellCount = 1536;
    public static FrameSignature Compute(Frame f);      // Otsu-binarised 64×24 interior
    public int DifferenceCount(FrameSignature other);
    public bool LooksIdenticalTo(FrameSignature? previous, int maxDifferingCells = 6);
    public ulong Hash { get; }                          // diagnostics/logs only, never comparison
    public double InkRatio { get; }
}

// Auto-watch's gate. A CHANGED frame is not translated until it stops changing, so a line that
// types itself onto the screen costs one request rather than one per revealed chunk. Compares
// signatures, not OCR text, so deciding to wait costs nothing. The cap is not optional: without
// it a game whose subtitles animate continuously settles never and translates never.
// tools/Replay deliberately does NOT apply this - its corpus is distinct frames, not a time series.
public enum FrameVerdict { Unchanged, Settling, Ready }
public sealed record SettleOptions {
    public int RequiredStillTicks { get; init; } = 2;               // at 2 fps, half a second
    public TimeSpan Cap { get; init; } = TimeSpan.FromSeconds(3);
}
public sealed class FrameSettleGate {
    public FrameSettleGate(SettleOptions? o = null, TimeProvider? clock = null);
    public FrameVerdict Offer(FrameSignature signature);            // call on EVERY poll
    public void Reset();                                            // on auto-watch switch-on
    public void Retune(SettleOptions options);                      // adaptive; keeps frame state
}

// Auto-watch pacing. Two modes that disagree about every number, because there is no single set
// that is defensible for both a dialogue box and a subtitle over moving picture.
public enum WatchMode { Dialogue, Video }
public sealed record WatchPacing {
    double PollsPerSecond; int RequiredStillTicks; TimeSpan SettleCap; TimeSpan MinimumInterval;
    TimeSpan WarnAfter; int WarnAfterRequests; TimeSpan StopAfter; int StopAfterRequests;
    public static WatchPacing For(WatchMode mode);
    public TimeSpan PollInterval { get; }
}
public enum WatchVerdict { Run, Warn, Stop }        // Warn is returned ONCE, on the crossing poll
public sealed class WatchSession {
    public WatchSession(WatchPacing pacing, TimeProvider? clock = null);
    public bool Unbounded { get; init; }            // warns, never stops
    public void Start();                            // the cap is measured from HERE, not last change
    public bool MayTranslate();                     // the floor. Asked BEFORE the gate is offered a frame
    public void Translated();                       // records the request and folds in the gap
    public WatchVerdict Check();                    // time OR requests, whichever arrives first
    public SettleOptions Settle();                  // the mode's cap, tightened to cadence/3
    public TimeSpan? Cadence { get; }               // median of the last 8 gaps, null under 3
    public bool OutrunningTheFloor { get; }         // content faster than the floor = lines skipped
    public int Requests { get; }  public TimeSpan Elapsed { get; }
}

// ── OCR ────────────────────────────────────────────────────────────────────
// Boxes are in the coordinate space of the FRAME, not of the image the engine read. OCR runs on an
// upscaled copy (2× by default), so each engine divides back down before returning - a box left in
// the upscaled space is wrong by 100% and still looks like a box. See CLAUDE.md.
public readonly record struct OcrBox(int Left, int Top, int Width, int Height) {
    public int Right { get; } public int Bottom { get; } public bool IsEmpty { get; }
}
public sealed record OcrWord(string Text, OcrBox Box, float Confidence, bool Accepted);

public sealed record OcrResult(
    string RawText, float Confidence, int WordCount, int RejectedWordCount = 0) {
    public static readonly OcrResult Empty;      // returned ONLY for a genuinely blank read: a
    public bool IsEmpty { get; }                 // frame whose every word was rejected reports
                                                 // empty text WITH a reject count, because "no
                                                 // dialogue here" and "this is unreadable" call
                                                 // for opposite responses.
    public IReadOnlyList<OcrWord> Words { get; init; } = [];   // optional; empty is valid, since an
    public IEnumerable<OcrWord> AcceptedWords { get; }         // engine may have no geometry to give
}
public interface IOcrEngine : IDisposable {
    string Name { get; }
    string? Diagnostics => null;                 // how the engine started up; surfaced in Settings
    Task<OcrResult> RecognizeAsync(Frame frame, CancellationToken ct);
}
public static class OcrPreprocessor {
    public static Frame Prepare(Frame f, OcrPreprocessOptions? o = null);  // greyscale → contrast →
}                                                                          // 2× → optional threshold
// The typewriter fix, brief §7 — OCR is free, API calls are not:
// The typewriter fix, settling on OCR TEXT. Written in week one and never wired to anything;
// FrameSettleGate (Capture/) is the one that actually runs, and it settles on the frame SIGNATURE
// instead, which costs no OCR to decide. This class is kept for the manual-trigger path, which
// does not use it yet.
public sealed class StableOcrReader {
    public StableOcrReader(IOcrEngine ocr, TimeSpan interval, TimeSpan cap);  // 150 ms, 1.5 s
    public Task<string> ReadStableAsync(Func<Task<Frame>> grab, CancellationToken ct);
}

// ── Region proposal ────────────────────────────────────────────────────────
// Pure geometry over OCR word boxes: no capture, no OCR, no I/O, so the ranking is testable
// against layouts written by hand rather than against whatever the frame generator draws.
public enum TextRegionKind { Unknown, Dialogue, Subtitle, SidePanel }

public sealed record RegionCandidate(
    CaptureRegion Bounds, TextRegionKind Kind,
    int WordCount, int LineCount, float MeanConfidence,
    double Score);                               // orders candidates within ONE call. Not a
                                                 // probability; never show it as a percentage.
public static class RegionFinder {
    public static IReadOnlyList<RegionCandidate> Propose(   // ranked best first; EMPTY when the
        IReadOnlyList<OcrWord> words,                       // evidence is weak, because a wrong
        int frameWidth, int frameHeight,                    // first proposal costs the user's
        RegionFinderOptions? options = null);               // trust in every later one
}

// ── Text ───────────────────────────────────────────────────────────────────
public static class TextNormalizer {
    public static string Normalize(string raw, IReadOnlyDictionary<string,string> corrections);
}
public static class DialogueParser {
    public static (string? Speaker, string Body) Parse(string normalized);
}
// The Arabic side, as opposed to TextNormalizer, which cleans up what OCR read. Applied by
// TranslationPipeline on the way OUT, at BOTH return sites, so the cache and the log keep what the
// provider actually said and the switch re-presents lines already translated. It must stay above
// OverlayWindow: the overlay's own chrome («جارٍ الترجمة», «تعذّرت الترجمة») carries deliberate marks.
// U+0653-U+0655 are deliberately NOT stripped - they spell أ إ آ, so removing them changes letters.
public static class ArabicText {
    public static string WithoutDiacritics(string text);
}
// The canonical string is a FROZEN WIRE FORMAT, not an implementation detail: ~100 shipped
// caches are keyed by it. Register is a newline-delimited PREFIX, added in v0.2.0 because without
// it switching to Egyptian returned the Modern Standard translation straight from cache.
//   canonical = $"{register}\n{body.ToLowerInvariant()}"   register is "msa" | "eg"
//   key       = Convert.ToHexStringLower(SHA256(UTF8(canonical)))
// Golden vectors live in CacheKeyTests. Change nothing here without a migration.
public static class CacheKey {
    public static string For(string normalizedBody, string register = "msa");
    public static string For(string normalizedBody, ArabicRegister register);
}

// ── Glossary ───────────────────────────────────────────────────────────────
public sealed record GlossaryTerm(string En, string Ar, string Type, string[] Aliases);
public sealed class GlossaryStore  { public static GlossaryStore Load(string path); }
public sealed class GlossaryMatcher {                   // longest-first, word-boundary, case-insensitive
    public IReadOnlyList<GlossaryTerm> Match(string text, int max = 12);
}

// ── Translation ────────────────────────────────────────────────────────────
public enum ArabicRegister { ModernStandard, Egyptian }
public sealed record TranslationRequest(
    string Body, string? Speaker,
    IReadOnlyList<GlossaryTerm>? Glossary,
    IReadOnlyList<string>? PreviousLines,               // oldest first, capped at ContextWindow (3)
    ArabicRegister Register,
    DateTimeOffset RequestedAt,                         // for the >6 s staleness drop
    string GameName, string? StyleHint);                // from the active profile

public sealed record TranslationResult(
    string Arabic, string Provider, string Model,
    bool FromCache, TimeSpan Latency, bool IsFallbackEnglish);

public interface ITranslationProvider {
    string Name { get; }
    IReadOnlyList<string> Models { get; }               // ordered fallback list from models.json
    bool IsConfigured => true;                          // false = no key yet, skip in silence
    bool AnnouncesMissingKey => true;                   // false for key slots 2-3: empty is normal
    Task<string> TranslateAsync(TranslationRequest req, string model, CancellationToken ct);
}
public sealed class TokenBucket   { public TokenBucket(int perMinute); public bool TryTake(); }
public sealed class QuotaLedger   {                     // persisted, Pacific-midnight boundary
    public Task RecordAsync(string provider, CancellationToken ct);
    public Task<IReadOnlyList<QuotaSnapshot>> SnapshotAsync(CancellationToken ct);
}
public readonly record struct QuotaSnapshot(string Provider, int Used, int Limit);
public sealed class ProviderRouter { /* ordered chain, §5. Never throws. */ }

// One models.json entry → one lane. Shared by the app and tools/Replay so the headless harness
// exercises the same wiring the overlay does.
public static class ProviderFactory {
    public static ITranslationProvider Create(ProviderConfig c, HttpClient http, ISecretStore s);
    public static ITranslationProvider Create(ProviderConfig c, HttpClient http, ISecretStore s, int slot);

    // Every key slot, key or no key: a slot built only when a key existed at STARTUP would mean a
    // key pasted into Settings did nothing until restart.
    public static IEnumerable<(ITranslationProvider Provider, int Rpm)> CreateLanes(
        ProviderConfig c, HttpClient http, ISecretStore s);
}

// ── Interface language ─────────────────────────────────────────────────────
public enum UiLanguage { English, Arabic }

// Every user-facing string, in both languages. `required` properties rather than a key/value
// dictionary: a missing translation is a compile error, not a silent English leak. English is
// the default; Arabic mirrors the whole window and uses the bundled font.
public sealed class UiText {
    public required UiLanguage Language { get; init; }
    public bool IsRightToLeft => Language == UiLanguage.Arabic;
    public static UiText For(UiLanguage language);
    public string HotkeyDescription(HotkeyAction action);

    // Stored region keys are English identifiers - they name rows in the region store. Mapped to a
    // display name rather than interpolated, or the Arabic build shows "حدد dialogue" on a button.
    public string RegionName(string region);
    /* ~90 required string properties */
}

// Machine output - model ids, provider names, URLs, quota counts - is not translated and must not
// be mirrored: reversed, the quota line reports the lane order, which is the cost policy,
// backwards. Handled by FlowDirection on the control, never by Unicode isolates (U+2066…U+2069),
// which are absent from the bundled font and break glyph fallback for the whole window.

// ── Game profiles ──────────────────────────────────────────────────────────
// Two roots, because the app folder is replaced by an update: bundled profiles ship under
// profiles/, anything the user creates or edits is written to AppPaths.UserProfiles, and the
// user's copy always wins. Deleting a bundled profile is a tombstone in the user root - deleting
// the files would work only until the next release put them back. `general` is read-only and
// undeletable; it is the screen-relative fallback and what a deletion falls back to.
public enum ProfileOrigin { Bundled, User, Override }

public sealed record GameProfileDraft {
    public string? ExistingId { get; init; }            // null = create
    public required string DisplayName { get; init; }
    public string[] WindowTitles { get; init; }
    public string[] ProcessNames { get; init; }         // stabler than titles; either match wins
    public string? StyleHint { get; init; }
    public bool HasSpeakerNames { get; init; }
    public IReadOnlyList<GlossaryTerm> Terms { get; init; }
}

public sealed class ProfileLibrary(string bundledRoot, string userRoot) {
    public const string GeneralProfileId = "general";
    public IReadOnlyList<string> Discover();
    public GameProfile Load(string id);
    public GameProfile LoadOrFallback(string? preferredId);
    public ProfileOrigin OriginOf(string id);
    public static bool IsReadOnly(string id);
    public static bool CanDelete(string id);
    public string Save(GameProfileDraft draft);         // always writes to userRoot
    public void Delete(string id);
    public bool Reset(string id);                       // drop an override, back to shipped
    public static string SlugFor(string displayName);   // becomes a path: ASCII only, no traversal
}

// Ready-made styleHint text, so setting up a game is not a prompt-writing exercise. Hints stay
// English because the system prompt is; only the labels the user reads are translated.
public sealed record StylePreset(string Id, string Hint) {
    public static IReadOnlyList<StylePreset> All { get; }    // plain, epic, modern, comic, technical
    public static StylePreset? Match(string? hint);          // null = hand-written, so "Custom"
}

// ── Updates ────────────────────────────────────────────────────────────────
// Notifies only. No update server: GitHub's public releases/latest endpoint already sits next to
// the download. Never throws - every failure is Unreachable, which is deliberately NOT UpToDate,
// because a captive portal answering 200 must not be reported as "you have the latest version".
// Only a check that reached GitHub resets the daily timer. Never installs: see CLAUDE.md.
public enum UpdateOutcome { NotChecked, UpToDate, UpdateAvailable, Unreachable }

public sealed record AvailableUpdate(Version Version, string Tag, string ReleaseUrl, string AssetName);
public sealed record UpdateCheckResult(UpdateOutcome Outcome, AvailableUpdate? Update = null) {
    public bool Reached { get; }                        // UpToDate or UpdateAvailable
}

public static class UpdateCheck {
    public static Version? RunningVersion { get; }      // 0.0.0 from source; CI stamps the tag
    public static bool IsDevelopmentBuild(Version? v);
    public static bool IsDue(AppSettings s, DateTime utcNow);           // once per 20 h
    public static Task<UpdateCheckResult> FetchAsync(HttpClient h, Version? current, CancellationToken ct);
    public static AvailableUpdate? FromRememberedTag(string? tag, Version? current);
    public static AvailableUpdate? Parse(string json);
}

// ── Storage ────────────────────────────────────────────────────────────────
public interface ITranslationCache {
    Task<CachedTranslation?> TryGetAsync(string key, CancellationToken ct);
    Task PutAsync(CachedTranslation entry, CancellationToken ct);
    Task PutOverrideAsync(string key, string source, string arabic, CancellationToken ct);  // Ctrl+Shift+F
    Task<CacheStats> GetStatsAsync(CancellationToken ct);
}

// ── Orchestration ──────────────────────────────────────────────────────────
// `TranslationEngine` was planned and never built. The work split in two instead, and this is what
// exists:
//
//   Core/Pipeline/TranslationPipeline  - frame in, PipelineOutcome out. Owns OCR, normalisation,
//                                        parsing, cache, glossary and the router call. Holds
//                                        mutable per-profile state (glossary, corrections, style)
//                                        swapped by UseProfile; that state is still NOT safe to
//                                        swap concurrently, though the rolling context queue is
//                                        now lock-guarded because three threads reach it.
//   App/TranslationSession             - owns the loop: hotkeys, auto-watch (2 fps, 90 s
//                                        self-expiry), change detection, overlay updates, status.
public enum SourceKind { Screen, RecordedFrame }

public sealed record PipelineOutcome(
    string RawOcr, string Normalized, string? Speaker, string Body,
    IReadOnlyList<GlossaryTerm> GlossaryHits,
    TranslationResult? Result,              // null = nothing attempted: empty region, or too short
    float OcrConfidence, TimeSpan Total,
    string? RegionKey, SourceKind Source, int RejectedWordCount);

public sealed class TranslationPipeline {
    public const int ContextWindow = 3;          // previous lines sent; a CAP, not a tuning knob -
                                                 // cache hits replay with no context at all
    public TimeSpan ContextTtl { get; set; }     // 2 min; past that it is a different scene
    public int MinimumBodyCharacters { get; set; }   // guard sits BEFORE the cache lookup

    public Task<PipelineOutcome> ProcessAsync(Frame frame, string? regionKey = null,
        SourceKind source = SourceKind.Screen, CancellationToken ct = default);

    public void UseProfile(string game, string? style, GlossaryMatcher g, OcrCorrections c);
    public void ResetContext();
}
```

---

## 5. Router behaviour

The brief's requirements as one algorithm. Get this right and quota stops being a topic.

```
TranslateAsync(req):
  if now - req.RequestedAt > 6 s        → drop, return stale        (brief §5: never queue)
  for provider in models.json order:                                (free lanes first = cost policy)
      if !provider.IsConfigured        → collect name, continue     (no key: switched off, silent)
      if provider in cooldown          → continue
      if !bucket[provider].TryTake()    → continue                  (fail over on RPM, not just RPD)
      for model in provider.models:                                 (models.json ordered list)
          try: return await call(provider, model, timeout: 4 s)
          catch 404/model_not_found     → log LOUDLY, next model
          catch 429                     → cooldown 60 s, break to next provider
          catch 5xx/timeout/cancelled   → retry ≤2, exp backoff + jitter, then next provider
  return English + warning marker                                   (never blank, never crash)
       + name any lane skipped for a missing key                    (else first run says nothing)
```

Buckets: Gemini **13/min** (margin under the ~15 the docs no longer guarantee), Groq **28/min**,
the paid lanes **40/min**. Every outcome increments `QuotaLedger`.

Two things this shape is load-bearing for, both learned the hard way:

- **The per-attempt timeout must be caught here.** A provider that lets the 4 s cap surface as a
  bare `OperationCanceledException` escapes the router entirely — the obvious cancellation catch is
  guarded on the *outer* token, which is not the one a timeout cancels. `TryLaneAsync` therefore
  catches `OperationCanceledException` alongside `ProviderException` and treats it as transient.
- **Lane order is the cost policy.** Free lanes above paid ones, asserted by a test. A paid lane
  above a free one spends money on lines the free tier would have answered for nothing.

---

## 6. Schemas

### `data/models.json` — never hardcode model names (brief §12)

```json
{
  "providers": [
    { "name": "gemini", "displayName": "Google Gemini", "tier": "free",
      "keyUrl": "https://aistudio.google.com/apikey",
      "baseUrl": "https://generativelanguage.googleapis.com/v1beta/openai",
      "secret": "GeminiApiKey", "rpm": 14, "rpd": 540,
      "models": ["gemini-3.1-flash-lite", "gemini-3.5-flash", "gemini-2.5-flash"] },
    { "name": "groq", "displayName": "Groq", "tier": "free",
      "keyUrl": "https://console.groq.com/keys",
      "baseUrl": "https://api.groq.com/openai/v1",
      "secret": "GroqApiKey", "rpm": 28, "rpd": 3000, "maxOutputTokens": 4096,
      "models": [
        { "id": "openai/gpt-oss-120b", "maxOutputTokens": 700, "reasoningEffort": "low" },
        { "id": "openai/gpt-oss-20b",  "maxOutputTokens": 700, "reasoningEffort": "low" },
        { "id": "llama-3.3-70b-versatile", "maxOutputTokens": 1024 }
      ] },
    { "name": "openai", "displayName": "OpenAI", "tier": "paid",
      "keyUrl": "https://platform.openai.com/api-keys",
      "baseUrl": "https://api.openai.com/v1",
      "secret": "OpenAiApiKey", "rpm": 40, "rpd": 10000, "maxOutputTokens": 1200,
      "models": ["gpt-5.6", "gpt-5.6-terra", "gpt-5.6-luna"] },
    { "name": "anthropic", "displayName": "Anthropic Claude", "tier": "paid",
      "kind": "anthropic", "keyUrl": "https://console.anthropic.com/settings/keys",
      "baseUrl": "", "secret": "AnthropicApiKey",
      "rpm": 40, "rpd": 10000, "maxOutputTokens": 2048,
      "models": ["claude-opus-5", "claude-sonnet-5"] }
  ]
}
```

| Field | Why it exists |
|---|---|
| `kind` | `anthropic` selects the SDK-based lane; anything else (or absent) is the OpenAI shape. Kept as a raw string so a typo degrades one lane instead of failing the whole file to parse. |
| `tier` | `free` / `paid` / `local`. Drives the label beside the key box, so the cost choice is explicit rather than buried in a paragraph. |
| `displayName`, `keyUrl` | Settings generates its key fields from this file, so a new lane gets a labelled box and a "where to get one" line with no code change. |
| `maxOutputTokens` | Was hardcoded at 300. Fine for one subtitle, truncates any model that spends output tokens reasoning before it answers. Overridable **per model**, because on Groq it is what a request RESERVES against an 8,000-token-a-minute ceiling, not what the answer costs — the lane-wide 4096 allowed one request a minute. |
| `reasoningEffort` | Per model, `low`/`medium`/`high`, omitted from the request entirely when unset. What makes a small `maxOutputTokens` safe: gpt-oss spends ~360 tokens thinking about a subtitle by default and 5 at `low`. Per model rather than per lane because `llama-3.3-70b-versatile` answers 400 to the parameter its two lane-mates need. |
| `devOnly` | Skipped unless `--dev` — the shipped app must never wait on a localhost port that does not exist (brief §2.7). |

A `models[]` entry is **either a plain string or an object** with `id` plus the overrides above.
Both forms are read forever: the string form is what every installed copy already has, and
`ProviderConfig.Models` stays a computed `string[]` so no reader had to change. A malformed entry
degrades to a `Problems()` warning rather than failing the load — this file is meant to be edited
by hand.

**Order is the cost policy**, not a preference: the router walks the list top to bottom.

**One provider becomes one lane per key.** Up to `ProviderConfig.MaxKeys` (3). Slot 1 reads the
plain `secret` name and is called by the provider's own name; slots 2 and 3 read `<secret>#2` /
`<secret>#3` and are called `<name>#2` / `<name>#3`. Slot 1 keeping the unsuffixed name is
load-bearing — every existing installation has a key filed under it. Expansion happens in
`ProviderFactory.CreateLanes`, so the router is unchanged: an ordered list of lanes already gives
"every Gemini key, then Groq".

### SQLite — one file, `%APPDATA%\GlassHudTranslator\glasshud.db`, WAL

`AppDatabase.SchemaVersion` is **3**. Migrations are additive **forever**: never rename a column,
never drop one. There is deliberately no self-updater, so re-unzipping an older release is a
supported recovery, and an older build opening a newer database proceeds without complaint.

```sql
CREATE TABLE translations (            -- the cache
  key         TEXT PRIMARY KEY,        -- sha256("{register}\n" + lowercased normalized body)
  source      TEXT NOT NULL,           -- normalized body, case preserved. INVARIANT: this is
                                       -- byte-for-byte what was hashed, so a key can always be
                                       -- recomputed from the row. CacheKeyTests asserts it; it is
                                       -- what makes a future key change migratable rather than
                                       -- destructive.
  arabic      TEXT NOT NULL,
  provider    TEXT NOT NULL,
  model       TEXT NOT NULL,
  is_override INTEGER NOT NULL DEFAULT 0,   -- Ctrl+Shift+F correction; always wins
  created_at  INTEGER NOT NULL,
  hits        INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE translation_log (         -- brief §12: the correction/analysis dataset
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  at          INTEGER NOT NULL,
  raw_ocr     TEXT NOT NULL,
  normalized  TEXT NOT NULL,
  speaker     TEXT,
  provider    TEXT,
  model       TEXT,
  arabic      TEXT,
  latency_ms  INTEGER,
  from_cache  INTEGER NOT NULL,
  outcome     TEXT NOT NULL,           -- ok | stale | fallback_english | error:<kind>
  game        TEXT,                    -- v3. NULL on rows written before the column existed:
  region      TEXT                     -- unknown provenance, not a game named nothing.
);

CREATE TABLE quota (
  provider    TEXT NOT NULL,
  day_pacific TEXT NOT NULL,           -- 'YYYY-MM-DD' at UTC-8/-7
  used        INTEGER NOT NULL,
  PRIMARY KEY (provider, day_pacific)
);

CREATE TABLE region_profiles (
  profile     TEXT NOT NULL,           -- game profile id; added in schema v2
  name        TEXT NOT NULL,           -- 'dialogue' | 'subtitle' | 'quest'
  resolution  TEXT NOT NULL,           -- '2560x1440'. Provenance, not part of the key: compared
  ui_scale    REAL NOT NULL,           -- on load by RegionProfile.MatchesLayout, which warns once
  rel_x REAL NOT NULL, rel_y REAL NOT NULL, rel_w REAL NOT NULL, rel_h REAL NOT NULL,
  PRIMARY KEY (profile, name)
);
```

Regions stored as **fractions of the client rect** (brief §8) so they survive window moves and
resolution changes.

**`resolution` and `ui_scale` are provenance, not a key.** They record what the rectangle was drawn
against. `RegionProfile.MatchesLayout` compares them on load and warns once per (profile, region,
layout); it never discards anything, because the fractions remain the user's best guess and are
usually close. Making them part of the primary key would be worse — a user changing resolution
would lose the region outright. Provenance is all-or-nothing: a shipped starting rectangle carries
`"unknown"` and a placeholder scale, and must never report a mismatch.

---

## 7. The 15-minute go/no-go — RESOLVED: Avalonia passes

**Verdict (Session 1): Avalonia. All four criteria met.** Run it again any time with:

```bash
dotnet run --project src/GlassHudTranslator.App -f net10.0 -- --render-test
```

Verified: contextual joining · RTL flow · embedded Latin LTR inside RTL (`Limsa Lominsa`,
`Y'shtola`) · sentence-final punctuation at the left end · Western digits in correct positions ·
two-line wrap · diacritics (`جارٍ`, `بِسْمِ`). The font manager reports the bundled face actually
resolving to `Noto Sans Arabic` rather than an OS substitution — eyeballing cannot tell those
apart on macOS, which would have hidden a missing-font bug until it reached Windows.

### Rule this surfaced: never set an explicit `LineHeight` on Arabic text

Arabic hangs marks *below* the baseline — kasra/kasratan, the dot under `ج`, and the two dots of
final `ي`. A tight line box clips them silently. Measured at 26 px with Noto Sans Arabic:

| `LineHeight` | ratio | below-baseline marks |
|---|---|---|
| 40 | 1.54× | **clipped** |
| 44 | 1.69× | **clipped** |
| 48 | 1.85× | correct |
| 52 | 2.00× | correct |
| auto (54.9 px) | 2.11× | correct |

This is not cosmetic: clipping the dots turns `ي` into `ى`, which changes the word. Use
`LineSpacing` to add air — it adds to the natural height instead of replacing it, so it stays
correct when the user changes the overlay font size in settings.

```csharp
// wrong - clips diacritics at any font size below ~1.9x
new TextBlock { FontSize = 26, LineHeight = 40, ... }

// right
new TextBlock { FontSize = 26, LineSpacing = 8, ... }
```

### The original test, for reference

Render exactly this in an Avalonia window with the bundled Noto Sans Arabic, `FlowDirection=RightToLeft`,
and **look at it**:

```
اذهب إلى Limsa Lominsa وتحدث مع Y'shtola.
```

Then the harder case from brief §6 — a line *ending* in an English proper noun plus punctuation:

```
هذا هو مكان لقائنا مع Y'shtola.
```

Pass = letters joined into connected words, Latin names running left-to-right inside the
right-to-left sentence, and the final `.` at the **left** end of the line.

Fail → stop, switch the UI to WPF, accept Windows-only UI development, and re-plan Session 1
(Core still lands on the Mac; the UI moves to Session 2).

---

## 8. Sessions

| # | Where | Scope | Done when |
|---|---|---|---|
| **0** | Human, ~45 min | Keys, tools, fonts, repo, **frame corpus** | 40 PNGs in `test-frames/`, both API keys in hand |
| **1** | Mac, ~3–4 h | Everything platform-neutral: Core, Avalonia UI, Replay, tests, CI | `dotnet test` green · Replay translates all 40 frames · overlay renders §7 correctly |
| **2** | Windows, ~2–3 h | Interop, capture, hotkeys, click-through, first-run, packaging | CI artifact runs on the laptop · live in-game translation on `Ctrl+Shift+T` |
| **3** | Mixed, ~2–3 h | Quality from real logs: OCR dictionary, glossary to 200, corrections, quota UI, auto-watch | 4 h session, no crash, hit rate ≥ 15 %, measured RPD written back into `models.json` |

Session 0 is the only hard dependency ordering: **Session 1 needs the frame corpus.** If the laptop
isn't available, Session 1 opens by generating synthetic FFXIV-style dialogue PNGs with SkiaSharp
(dark rounded box, white text, a speaker line) — enough to build the whole pipeline against, swapped
for real frames later without touching code.

Full prompts: [`CODING_SESSIONS.md`](CODING_SESSIONS.md).

---

## 9. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Avalonia Arabic shaping wrong | Invalidates the UI choice | §7, first 15 minutes of Session 1 |
| Laptop unavailable | Blocks Sessions 0/2 | Synthetic frames unblock Session 1; CI compiles + publishes Windows on every push |
| Tesseract accuracy poor on real frames | Garbage in, garbage out | Measured in Session 3; PaddleOCR-via-ONNX is the escalation, behind the same `IOcrEngine` |
| OCR apostrophe errors | Cache misses **and** bad translation | `ocr-corrections.json` populated from `translation_log` in Session 3 |
| Free model deleted upstream | Silent outage | Ordered `models.json`, fall through on 404, log loudly |
| Overlay OCRs itself | Feedback loop | `WDA_EXCLUDEFROMCAPTURE` + overlay positioned outside the capture rect |
| Forgotten auto-watch during AFK | Quota leak | 90 s self-expiry, non-negotiable |
| SmartScreen blocks the .exe | Support call | Warn him in advance; *More info → Run anyway*, once |

---

## 10. Anti-patterns (from brief §16 — do not regress on these)

No Dalamud / injection / memory reads · no `Windows.Media.Ocr` · no `WH_KEYBOARD_LL` ·
no F1–F12 hotkeys · no embedded API key · no hardcoded model names · no queued stale requests ·
no local LLM on the target PC · no classic MT APIs · **never hash before normalizing**.
