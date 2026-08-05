# GamingTranslatorGlassHUD — Build Plan

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
  <ProjectReference Include="..\GamingTranslatorGlassHUD.Windows\GamingTranslatorGlassHUD.Windows.csproj" />
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
| 7 | MSA vs Egyptian | **Ask the brother before Session 3.** Default MSA, register selectable in settings — it's one line of the system prompt, so being wrong is cheap |

Only #1 and #7 need a human. #1 is a 15-minute test; #7 is a text message.

---

## 3. Solution layout

```
GamingTranslatorGlassHUD.sln
├── src/
│   ├── GamingTranslatorGlassHUD.Core/           net10.0                  ← all logic, all tests
│   │   ├── Capture/        IFrameSource, Frame, FolderFrameSource, FrameHasher
│   │   ├── Ocr/            IOcrEngine, TesseractCliEngine, OcrPreprocessor, StableOcrReader
│   │   ├── Text/           TextNormalizer, DialogueParser, CacheKey
│   │   ├── Glossary/       GlossaryStore, GlossaryMatcher, GlossaryTerm
│   │   ├── Translation/    ITranslationProvider, OpenAiCompatibleProvider, StubProvider,
│   │   │                   PromptBuilder, ProviderRouter, TokenBucket, QuotaLedger
│   │   ├── Storage/        AppDatabase, TranslationCache, TranslationLog, ISecretStore
│   │   ├── Regions/        RegionProfile, RegionProfileStore
│   │   └── Config/         AppSettings, ModelsConfig
│   ├── GamingTranslatorGlassHUD.Interop/        net10.0-windows          ← P/Invoke declarations only
│   ├── GamingTranslatorGlassHUD.Windows/        net10.0-windows          ← Win32 implementations
│   │   ├── Win32FrameSource, GameWindowLocator, DisplayModeGuard
│   │   ├── GlobalHotkeyService (RegisterHotKey + message-only window)
│   │   ├── OverlayWindowStyles (WS_EX_TRANSPARENT, topmost, WDA_EXCLUDEFROMCAPTURE)
│   │   ├── TesseractNativeEngine (nuget natives)
│   │   └── DpapiSecretStore
│   └── GamingTranslatorGlassHUD.App/            net10.0;net10.0-windows  ← Avalonia
│       ├── Views/          OverlayWindow, RegionPickerWindow, SettingsWindow, FirstRunWindow
│       ├── ViewModels/
│       ├── PlatformServices.cs         ← the single #if WINDOWS switchboard
│       └── Assets/Fonts/NotoSansArabic-Regular.ttf
├── tests/GamingTranslatorGlassHUD.Core.Tests/   net10.0   xunit
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

// ── OCR ────────────────────────────────────────────────────────────────────
public sealed record OcrResult(string RawText, float Confidence);
public interface IOcrEngine : IDisposable {
    Task<OcrResult> RecognizeAsync(Frame frame, CancellationToken ct);
}
public static class OcrPreprocessor {
    public static Frame Prepare(Frame f);               // greyscale → contrast → 2× → threshold
}
// The typewriter fix, brief §7 — OCR is free, API calls are not:
public sealed class StableOcrReader {
    public StableOcrReader(IOcrEngine ocr, TimeSpan interval, TimeSpan cap);  // 150 ms, 1.5 s
    public Task<string> ReadStableAsync(Func<Task<Frame>> grab, CancellationToken ct);
}

// ── Text ───────────────────────────────────────────────────────────────────
public static class TextNormalizer {
    public static string Normalize(string raw, IReadOnlyDictionary<string,string> corrections);
}
public static class DialogueParser {
    public static (string? Speaker, string Body) Parse(string normalized);
}
public static class CacheKey { public static string For(string body); }   // lowercase → sha256 hex

// ── Glossary ───────────────────────────────────────────────────────────────
public sealed record GlossaryTerm(string En, string Ar, string Type, string[] Aliases);
public sealed class GlossaryStore  { public static GlossaryStore Load(string path); }
public sealed class GlossaryMatcher {                   // longest-first, word-boundary, case-insensitive
    public IReadOnlyList<GlossaryTerm> Match(string text, int max = 12);
}

// ── Translation ────────────────────────────────────────────────────────────
public enum ArabicRegister { Msa, Egyptian }
public sealed record TranslationRequest(
    string Body, string? Speaker,
    IReadOnlyList<GlossaryTerm> Glossary,
    string? PreviousLine,
    ArabicRegister Register,
    DateTimeOffset RequestedAt);                        // for the >6 s staleness drop

public sealed record TranslationResult(
    string Arabic, string Provider, string Model,
    bool FromCache, TimeSpan Latency, bool IsFallbackEnglish);

public interface ITranslationProvider {
    string Name { get; }
    Task<string> TranslateAsync(TranslationRequest req, CancellationToken ct);
}
public sealed class TokenBucket   { public TokenBucket(int perMinute); public bool TryTake(); }
public sealed class QuotaLedger   {                     // persisted, Pacific-midnight boundary
    public Task RecordAsync(string provider, CancellationToken ct);
    public Task<IReadOnlyList<QuotaSnapshot>> SnapshotAsync(CancellationToken ct);
}
public readonly record struct QuotaSnapshot(string Provider, int Used, int Limit);
public sealed class ProviderRouter : ITranslationProvider { /* ordered chain, §5 */ }

// ── Storage ────────────────────────────────────────────────────────────────
public interface ITranslationCache {
    Task<CachedTranslation?> TryGetAsync(string key, CancellationToken ct);
    Task PutAsync(CachedTranslation entry, CancellationToken ct);
    Task PutOverrideAsync(string key, string arabic, CancellationToken ct);   // Ctrl+Shift+F
    Task<CacheStats> GetStatsAsync(CancellationToken ct);
}

// ── Orchestration ──────────────────────────────────────────────────────────
public sealed class TranslationEngine {
    public event Action<TranslationResult>? Translated;
    public event Action<string>? Status;                // "جارٍ الترجمة..."
    public Task<TranslationResult> TranslateNowAsync(CaptureRegion r, CancellationToken ct);
    public void StartAutoWatch(CaptureRegion r);        // 2 fps, 90 s self-expiry
    public void StopAutoWatch();
}
```

---

## 5. Router behaviour

The brief's requirements as one algorithm. Get this right and quota stops being a topic.

```
TranslateAsync(req):
  if now - req.RequestedAt > 6 s        → drop, return stale        (brief §5: never queue)
  for provider in [gemini, groq]:
      if !bucket[provider].TryTake()    → continue                  (fail over on RPM, not just RPD)
      for model in provider.models:                                 (models.json ordered list)
          try: return await call(provider, model, timeout: 4 s)
          catch 404/model_not_found     → log LOUDLY, next model
          catch 429                     → cooldown 60 s, break to next provider
          catch 5xx/timeout             → retry ≤2, exp backoff + jitter, then next provider
  return English + warning marker                                   (never blank, never crash)
```

Buckets: Gemini **13/min** (margin under the ~15 the docs no longer guarantee), Groq **28/min**.
Every outcome increments `QuotaLedger`, which is what actually answers open questions #2 and #4.

---

## 6. Schemas

### `data/models.json` — never hardcode model names (brief §12)

```json
{
  "providers": [
    { "name": "gemini", "baseUrl": "https://generativelanguage.googleapis.com/v1beta/openai",
      "secret": "GeminiApiKey", "rpm": 13, "rpd": 1000,
      "models": ["gemini-2.5-flash-lite", "gemini-2.5-flash"] },
    { "name": "groq", "baseUrl": "https://api.groq.com/openai/v1",
      "secret": "GroqApiKey", "rpm": 28, "rpd": 14400,
      "models": ["qwen/qwen3-32b", "llama-3.3-70b-versatile"] },
    { "name": "ollama", "baseUrl": "http://localhost:11434/v1",
      "secret": null, "rpm": 120, "rpd": 1000000, "devOnly": true,
      "models": ["qwen3:8b"] }
  ]
}
```

`devOnly: true` entries are skipped unless `--dev` — the shipped app must never wait on a
localhost port that does not exist (brief §2.7).

### SQLite — one file, `%APPDATA%\GamingTranslatorGlassHUD\glasshud.db`, WAL

```sql
CREATE TABLE translations (            -- the cache
  key         TEXT PRIMARY KEY,        -- sha256(lowercased normalized body)
  source      TEXT NOT NULL,           -- normalized body, case preserved
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
  outcome     TEXT NOT NULL            -- ok | stale | fallback_english | error:<kind>
);

CREATE TABLE quota (
  provider    TEXT NOT NULL,
  day_pacific TEXT NOT NULL,           -- 'YYYY-MM-DD' at UTC-8/-7
  used        INTEGER NOT NULL,
  PRIMARY KEY (provider, day_pacific)
);

CREATE TABLE region_profiles (
  name        TEXT PRIMARY KEY,        -- 'dialogue' | 'subtitle' | 'quest'
  resolution  TEXT NOT NULL,           -- '2560x1440'
  ui_scale    REAL NOT NULL,
  rel_x REAL, rel_y REAL, rel_w REAL, rel_h REAL   -- fractions of the FFXIV client rect
);
```

Regions stored as **fractions of the client rect** (brief §8) so they survive window moves and
resolution changes.

---

## 7. The 15-minute go/no-go — RESOLVED: Avalonia passes

**Verdict (Session 1): Avalonia. All four criteria met.** Run it again any time with:

```bash
dotnet run --project src/GamingTranslatorGlassHUD.App -f net10.0 -- --render-test
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
