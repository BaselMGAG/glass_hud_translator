# Glass HUD Translator — Project Brief

**Purpose:** Real-time Arabic translation overlay for games that ship without Arabic support (Windows)
**Reference game:** Final Fantasy XIV — what everything was designed and tested against
**Primary user:** One person I built this for. Windows, modest hardware, reads Arabic more comfortably than English
**Author:** Basel — Frankfurt, Germany. Develops on macOS, Apple Silicon, C#/.NET
**Status:** Architectural decisions below are settled unless marked OPEN.

> This is my design document. I wrote it before any code existed, and it is the source of truth for
> *why* the project is shaped the way it is. The implementation notes live in
> [PROJECT_PLAN.md](../PROJECT_PLAN.md).
>
> It talks about Final Fantasy XIV throughout because that is the game I built this for and tested
> against. Nothing in the approach is specific to it — anything that differs between games lives in
> a profile folder (see `profiles/`). Read "FFXIV" below as "the game I had in front of me".

## What has changed since I wrote this

This document is deliberately not rewritten as the code moves — it records what I decided and why,
before I knew whether any of it would work. The decisions below all held. Seven things have since
been *added* that it does not mention, so read it with these in mind:

| | |
|---|---|
| **Four providers, not two.** | Gemini and Groq on their free tiers, then OpenAI and Anthropic for people who already pay for one. Lane order in `data/models.json` is the cost policy: free first, and a lane with no key is skipped in silence. §2.7's "API-only, no local model" is unchanged. |
| **The interface is available in Arabic.** | The single largest omission in this document. It specifies Arabic *output* in detail and never once asks what language the app's own buttons are in — for an app whose entire premise is that its user does not read English comfortably. English remains the default. |
| **Settings is tabbed.** | Providers / Translating / Overlay / Hotkeys / Diagnostics, rather than the one long panel implied here. |
| **Five hotkeys, not four.** | Show/hide overlay was added after the first real play session. §2.6's table lists four. |
| **Games are added from inside the app.** | §8 treats a profile as a file to be authored, which is the same blind spot as the English-only interface: it assumes the person adding a game reads JSON. There is now an editor — pick the window from a list, choose a voice from a dropdown, drag a box. Two consequences this document could not have anticipated: profiles bind to the *process name* as well as the title, because titles change while a program runs; and anything the user writes goes under their own data directory, since the app folder is replaced by an update. |
| **The app notices its own updates.** | This document assumes every outbound request is a translation, and that is no longer true: once a day it asks GitHub's public releases endpoint whether a newer version exists, and shows a notice naming the file and the steps. Nothing is sent with the request, it is switchable off, and it never installs anything — self-updating is refused on exactly the grounds §16 refuses `WH_KEYBOARD_LL`, that an unsigned binary doing it is an antivirus heuristic. |
| **The Arabic interface needs a native reader, not a test.** | §6 is careful about the Arabic the app *produces* and says nothing about the Arabic it *displays*. The first review round found buttons reading `حدد dialogue`, a key field labelled "not set" where an instruction belonged, a linguist's term where a plain one belonged, and explanatory text sized as though nobody had to read it. None of it was reachable from a test — but two of the four are now, and reviewing interface strings is in [CONTRIBUTING.md](../CONTRIBUTING.md). |

Everything else below still describes the shipped app. Current contracts and schemas are in
[PROJECT_PLAN.md](../PROJECT_PLAN.md); the rules that are easy to break are in
[CLAUDE.md](../CLAUDE.md).

---

## 1. Problem statement

Most games ship without Arabic, and the ones that do rarely translate their narrative text. FFXIV is English-only for story content, and the person I built this for reads Arabic far more comfortably — they were losing the plot entirely.

Even if a game exposed its text, its own renderer almost certainly cannot draw Arabic correctly: no contextual letter shaping, no bidirectional layout. That blocks any in-game solution at the rendering layer regardless of how good the translation is. So the answer has to live outside the game.

The solution is an **external, standalone Windows application** that:

1. Captures a user-defined rectangle of the FFXIV window
2. Runs OCR on that region to extract English dialogue
3. Sends the extracted text to a cloud LLM for Arabic translation
4. Renders the Arabic in a separate transparent, always-on-top window positioned above the game's dialogue box
5. Caches every translation locally so repeated content is free and instant

The app **never touches the game process**. No injection, no memory reads, no packet inspection, no file modification.

---

## 2. Decision log — what was chosen and why

### 2.1 External OCR overlay, NOT a Dalamud plugin

**Decision:** Build a standalone screen-capture application.

**Rationale, in priority order:**

1. **Terms of Service.** Third-party plugins (Dalamud/XIVLauncher for FFXIV, and their equivalents elsewhere) are against most games' ToS. A screenshot-reading external app has no relationship to the game client and carries no account risk. This is the decisive argument.
2. **Patch resilience.** Plugin approaches break on every game patch when addon node structures change. An OCR approach breaks only if the dialogue box physically moves, and the fix is the user re-dragging a rectangle.
3. **Rendering.** Even with perfect text extraction, the game's own renderer cannot draw Arabic. A separate window is required regardless, so the plugin route would only have replaced OCR — not the overlay.
4. **Development environment.** Plugin development requires Windows plus the game running. I develop on macOS.

5. **It generalises.** A plugin is per-game and dies with that game's modding scene. Reading pixels works on anything rendered in a borderless window, which is the whole reason this became a general tool rather than an FFXIV utility.

**Consequences accepted:** OCR introduces character-recognition errors (especially on apostrophe-heavy FFXIV names), cannot know quest/NPC context, and depends on stable UI positioning.

### 2.2 Borderless windowed is mandatory

Exclusive fullscreen breaks both screen capture and always-on-top overlay behaviour. The app must **detect the game's window mode at startup and refuse to run with a clear message** rather than silently producing black frames.

### 2.3 Tesseract, NOT Windows.Media.Ocr

**Decision:** Tesseract (or PaddleOCR-via-ONNX if accuracy is insufficient).

**Rationale:**
- `Windows.Media.Ocr` requires **packaged app identity (MSIX)**, which forces an installer and kills the "send him a zip with an .exe" delivery model.
- Newer Windows AI text-recognition APIs in the Windows App SDK appear to be gated to **Copilot+ NPU hardware**. VERIFY THIS before relying on it — the target machine is not Copilot+ class.
- FFXIV dialogue is a fixed font, high contrast, on a dark semi-transparent box. This is close to the ideal case for Tesseract.

**Configuration:** character whitelist, 2× upscale, grayscale + threshold preprocessing. Ship `eng.traineddata` (start with `fast`, ~4 MB; escalate to `best`, ~15 MB, only if needed).

### 2.4 GDI BitBlt before Windows.Graphics.Capture

**Decision:** Start with plain GDI `BitBlt` from the desktop DC.

**Rationale:** The workload is one small rectangle at ≤3 fps. `BitBlt` is 1–3 ms, works unpackaged, has zero packaging implications, and draws no capture border. `Windows.Graphics.Capture` draws a yellow border by default and disabling it goes through `GraphicsCaptureAccess.RequestAccessAsync` — an unnecessary complication at this scale. Escalate to WGC only if BitBlt proves inadequate.

### 2.5 UI framework — OPEN DECISION, must be made before writing any UI code

| | WPF | Avalonia |
|---|---|---|
| Builds on macOS | **No** — XAML markup compiler is Windows-only | **Yes** |
| Arabic RTL/bidi | Mature, reliable | HarfBuzz-based, good — must verify |
| Transparency + topmost on Windows | Native | Works, some P/Invoke |
| Click-through (`WS_EX_TRANSPARENT`) | P/Invoke | P/Invoke |
| Future native macOS version | Full rewrite | Mostly free |

**Deciding test — run this on day one, before building any UI:**

Render this exact string in a window on macOS (Avalonia) and inspect it:

```
اذهب إلى Limsa Lominsa وتحدث مع Y'shtola.
```

Check: contextual letter joining, right-to-left flow, English proper nouns embedded correctly, and the final period's position. If correct → **Avalonia**. If broken → **WPF**, accept Windows-only UI development.

**Why this matters so much:** with WPF, every typo in overlay code requires physical access to the Windows test machine to discover. That machine belongs to someone else and isn't always available.

### 2.6 Hotkey-triggered, NOT continuous polling

**Decision:** Manual trigger is the default mode; auto-watch is an explicit opt-in toggle.

**Rationale:**
- Continuous polling generates most of the original design's complexity: 3–5 fps OCR, similarity thresholds, stability windows, self-capture exclusion.
- It also does not fit the API quota. At ~15 RPM, translating once per second fails immediately.
- Manual trigger produces roughly one request per dialogue advance — 3–6/min in normal play.

**Hotkey map** (avoid F1–F12 — FFXIV binds those to party-member targeting by default):

| Key | Action |
|---|---|
| `Ctrl+Shift+R` | Region picker — drag rectangle, save to profile |
| `Ctrl+Shift+T` | Translate what's on screen now (manual mode) |
| `Ctrl+Shift+A` | Toggle auto-watch (polls at 2–3 fps) |
| `Ctrl+Shift+F` | Flag/correct the current translation |

**Implementation:** use `RegisterHotKey` (user32). Do **not** use a low-level keyboard hook (`WH_KEYBOARD_LL`) — that is the pattern antivirus heuristics flag, and `RegisterHotKey` already fires while FFXIV has focus.

**Auto-watch must self-expire** after 60–90 seconds with no text change. A forgotten toggle during an AFK is the primary quota-leak risk.

### 2.7 API-only, no local model at runtime

**Decision:** The shipped app calls cloud APIs. No Ollama on the target machine.

**Rationale:** The target PC is weak. `qwen3:8b` holds ~6 GB resident and competes directly with FFXIV for RAM and CPU. The result would be lost frames in exchange for translations slower than the network round-trip.

**Ollama remains in use on the developer's Mac** for local pipeline development.

**Consequence:** the network is now a hard dependency. This makes the following mandatory rather than optional:
- 429 handling with exponential backoff + jitter, max 2 retries
- 4-second hard timeout (past that, the dialogue has advanced and the answer is worthless)
- Graceful degradation: on total failure, show the OCR'd English with a warning marker. Never blank, never crash.
- The cache becomes load-bearing, not an optimisation.

---

## 3. Client-side performance budget

| Step | Cost on weak CPU |
|---|---|
| BitBlt small region | 1–3 ms |
| Preprocess (grayscale, 2× upscale, threshold) | 5–15 ms |
| Tesseract on ~800×300 px | 30–100 ms |
| **Total per translation** | **~120 ms** |

Manual mode: ~120 ms once every 10–30 seconds. Negligible.

Auto-watch at 3 fps: 120 ms every 333 ms ≈ 40% of one core, sustained. **Visible on weak hardware.**

**Required mitigation — hash before OCR:**

```
capture (2ms) → downsample to 64×24 grayscale → hash → compare to previous
                                              → identical? skip entirely
                                              → different?  run OCR (80ms)
```

During dialogue, 85–90% of frames are identical. Average cost drops from ~120 ms to ~15 ms per tick (~5% of a core). Additionally: poll at 2 fps rather than 3, and set the worker thread to `ThreadPriority.BelowNormal` so the game's render thread always wins.

---

## 4. Translation provider stack

### 4.1 Runtime chain (shipped app)

```
1. SQLite cache              → instant, no network
2. Gemini 2.5 Flash-Lite     → primary
3. Groq qwen3-32b            → on 429 / 5xx / timeout
4. English + warning marker  → both unavailable
```

**Both APIs are OpenAI-compatible.** One HTTP client with a swappable base URL covers all three (including local Ollama during dev).

```
Gemini:  https://generativelanguage.googleapis.com/v1beta/openai/
Groq:    https://api.groq.com/openai/v1/
Ollama:  http://localhost:11434/v1/
```

**Important:** fail over on 429 *immediately*, not only on daily exhaustion. Running both lanes in parallel gives ~45 RPM combined, which clears the densest cutscene. Groq is a second lane, not a reserve tank.

### 4.2 Why Gemini is primary

Best Arabic quality among free-tier options. FFXIV's deliberately archaic high-fantasy register punishes weak translators. Groq's open-weights models (qwen3 family — strongest open option for Arabic) are the fallback because Groq's request ceiling is roughly 10× Gemini's.

### 4.3 Free-tier limits (verify in each dashboard — these change frequently)

| Provider | RPD | RPM | Card required |
|---|---|---|---|
| Gemini Flash-Lite | ~1,000–1,500 | ~15 | No |
| Groq qwen3-32b | ~14,400 (headline; larger models may be lower — VERIFY) | ~30 | No |
| **Combined** | **~15,400** | **~45** | |

Google's official rate-limit docs no longer publish a guaranteed per-model free-tier table; they direct developers to check active limits in AI Studio and state that specified limits are not guaranteed.

RPD resets at **midnight Pacific = 09:00 Frankfurt**, so evening sessions always start on a full budget.

### 4.4 Rejected providers and why

| Option | Rejected because |
|---|---|
| **Google Cloud Translation** | 500K chars/month ≈ 185 lines/day. ~6× worse than Gemini. Requires billing account. No glossaries on basic v2. |
| **Azure Translator** | 2M chars/month ≈ 740 lines/day. Still below Gemini. Requires credit card. Custom dictionary is a paid feature. |
| **DeepL** | API Free has been retired; now a one-time 1M-character developer grant, not a recurring allowance. |
| **Cerebras** | Token-generous but the workload is request-limited, not token-limited. Model catalogue has been observed to be very thin. |
| **Mistral** | 2 RPM makes it unusable for live dialogue regardless of monthly token volume. |
| **LibreTranslate** | Self-hosting puts load on the target PC, which is explicitly ruled out. Argos-model Arabic quality is weak. |
| **MyMemory** | Rate-limited, translation-memory based, poor on novel narrative sentences. |
| **Unofficial Google Translate endpoints** | ToS violation, breaks without warning, IP bans. |
| **Ollama on target PC** | Insufficient hardware — see 2.7. |

### 4.5 The cost inversion worth understanding

Classic MT bills per character regardless of string triviality. LLM token pricing on small models has undercut it. At 300 lines/day:

- Azure Translator: ~$8/month
- Google Cloud Translation: ~$16/month
- Gemini Flash-Lite (paid tier): **~$0.45/month**

So even after outgrowing free tiers, the LLM path stays ~16× cheaper *and* produces better output. There is no scenario where switching to classic MT is correct.

---

## 5. Quota mathematics

**Per request:** ~250 input tokens (system prompt + glossary subset + speaker + up to three previous lines + current line) + ~80 output tokens ≈ 330 tokens.

**Conclusion: the workload is request-limited, not token-limited.** Tokens never approach any ceiling. Design around RPM and RPD only.

### Dialogue density

| Activity | Lines/hour |
|---|---|
| Heavy MSQ (cutscene-dense) | 100–200 |
| Normal mixed play | 40–80 |
| Dungeons / raids | 10–25 |
| Gathering / crafting / grinding | 0–10 |

### Playtime available (combined ~15,400 RPD, 20% cache hit rate)

| Activity | Hours/day of budget |
|---|---|
| Heavy MSQ | ~128 h |
| Normal play | ~320 h |
| Dungeon grinding | ~960 h |

Even under the pessimistic assumption that Groq's qwen3-32b is capped at 1,000 RPD rather than 14,400, combined gives ~2,000/day → ~16 hours of heavy MSQ. The conclusion holds either way: **quota is not a practical constraint.**

### The real constraint is RPM during cutscenes

Cutscene dialogue advances every 3–5 seconds → 12–20 lines/minute, at or above Gemini's ~15 RPM alone.

**Required:**
- Token bucket set to **13 RPM** for Gemini (margin, since limits are not guaranteed)
- **Drop stale requests rather than queueing them.** If a line has waited >6 seconds, the NPC has moved on. Serving a backlog produces translations for dialogue already gone — worse than a gap.

### The realistic way to burn quota: a cache-key bug

If OCR reads `Y'shtola` one frame and `Y shtola` the next, those hash differently and you pay twice for identical text. Normalize **before** hashing, in this order:

1. Apply the OCR correction dictionary
2. Collapse whitespace, strip trailing punctuation
3. Lowercase
4. SHA-256

Log the hit rate. Below 10% after a few sessions indicates a normalization bug, not a content problem.

Expected hit rates: **10–25%** on a first playthrough (repeated NPC idle lines, quest text shown twice, turn-in re-reads). **70%+** on replayed content, alts, and Unending Journey rewatches.

---

## 6. Arabic rendering requirements

- `FlowDirection="RightToLeft"` + `Language="ar"` (or Avalonia equivalent) for contextual shaping and bidi.
- **Bundle the Noto Sans Arabic TTF.** Do not assume it is installed.
- **Test specifically** on lines that *end* in an English proper noun followed by punctuation — that is where bidi placement most commonly breaks.
- **Register:** FFXIV's English is deliberately archaic high-fantasy. Modern Standard Arabic fits the narrative voice. Egyptian Arabic reads as comedy for Elezen nobility but lands well for merchants and comic relief. **Default MSA, expose a toggle.**

### Glossary is the highest-value quality lever

Pin ~200 proper nouns: place names, Scion names, job names, aether terminology. Without it, the model invents a different transliteration of "Y'shtola" every third line.

**Inject only the glossary entries appearing in the current line**, to keep tokens minimal.

### OCR correction dictionary

Maintain a separate map for known OCR failure modes, applied before cache hashing and before translation:

```json
{
  "Y shtola": "Y'shtola",
  "Scions ot the Seventh Dawn": "Scions of the Seventh Dawn",
  "Limsa Lominsa.": "Limsa Lominsa"
}
```

---

## 7. The typewriter problem

FFXIV reveals dialogue character-by-character. A hotkey press mid-reveal captures truncated text.

**Fix in code:** on hotkey, OCR immediately, then re-OCR every 150 ms until two consecutive reads match (cap ~1.5 s), then translate **once**. Extra OCR is free; extra API calls are not.

**Fix in practice:** instruct the user to click once to complete the line, then press the hotkey. Zero engineering cost.

Use both.

---

## 8. Region profiles

- Store rectangles **relative to the FFXIV client rect**, not desktop coordinates, so they survive window moves.
- Key each profile to `(resolution, UI scale)`.
- Save **more than one region.** FFXIV renders text in at least three places: NPC dialogue box, cutscene subtitle bar, quest-accept window. Bind to separate keys or let one key cycle.

---

## 9. Development environment

**Constraint:** I code on macOS; testing happens on a borrowed Windows laptop, which is not always accessible.

### Project structure

```
GlassHudTranslator/
├── GlassHudTranslator.Core/          net9.0          → builds on Mac ✓
│   ├── Translation/               (providers, prompt construction)
│   ├── Caching/                   (SQLite, hashing, normalization)
│   ├── Glossary/
│   ├── TextNormalization/         (OCR correction dictionary)
│   └── Models/
├── GlassHudTranslator.Interop/       net9.0-windows  → builds on Mac ✓
│   └── (P/Invoke declarations — attributes only, compile anywhere)
├── GlassHudTranslator.Windows/       net9.0-windows
│   ├── ScreenCapture/
│   ├── Ocr/
│   └── Hotkeys/
└── GlassHudTranslator.Ui/            (WPF → Windows only ✗ / Avalonia → Mac ✓)
```

Roughly **50–70% of the codebase compiles on macOS** with this split, depending on the UI framework decision.

### Mockable seams — build these from the start

```csharp
public interface IFrameSource {
    Task<Image> GetFrameAsync(Rectangle region, CancellationToken ct);
}
// Win32FrameSource   — real BitBlt
// FolderFrameSource  — reads PNGs from test-frames/

public interface IOcrEngine { ... }
// TesseractNativeEngine   — nuget, Windows natives
// TesseractCliEngine      — shells out to `brew install tesseract` on Mac

public interface ITranslationProvider {
    Task<string> TranslateAsync(string text, string? speaker,
                                string? previousLine, CancellationToken ct);
}
// StubProvider     — fixed Arabic string + 400ms delay. USE THIS MOST.
// OllamaProvider   — local, dev only
// GeminiProvider
// GroqProvider
```

**The stub provider is the most-used implementation during development.** ~95% of debugging (hotkeys, capture, OCR, cache, overlay layout, stability loop) has nothing to do with translation quality. It catches RTL layout bugs just as well as a real model.

### First Windows session is for COLLECTING, not debugging

Capture **30–40 PNGs** of the dialogue region covering:
- Multiple UI scales
- Bright zones vs dark zones behind the text box
- Cutscene subtitle bar vs NPC dialogue box
- Names with apostrophes (Y'shtola, Y'mhitra)
- Long two-line text and short interjections

These frames turn the Mac into a real test environment. The entire pipeline runs deterministically via `FolderFrameSource`.

### What genuinely requires Windows

Only four things cannot be faked:
1. Real-time capture performance
2. Global hotkeys firing while FFXIV has focus
3. Click-through / topmost / `WDA_EXCLUDEFROMCAPTURE` behaviour
4. DPI scaling (125%, 150%)

### Remote development

**VS Code Remote-SSH** (or Rider remote debugging) into the Windows laptop. Enable OpenSSH Server via Settings → Optional Features. Edit on Mac, compile and run on Windows, breakpoints work, real game visible.

**Avoid RDP for capture testing** — session and DPI behaviour differ from console enough to produce phantom bugs.

A Windows ARM VM (Parallels/UTM) is useful for WPF/interop testing against static screenshots, but FFXIV won't run in it.

### CI as a compile safety net

GitHub Actions on `windows-latest` on every push. Two payoffs: compile errors surface in ~2 minutes without touching the laptop, and every push produces a downloadable `.exe` artifact — so "testing" becomes downloading a file rather than setting up a build environment.

```yaml
on: [push]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '9.0.x' }
      - run: dotnet publish -c Release -r win-x64 --self-contained true
             -p:PublishSingleFile=true
             -p:IncludeNativeLibrariesForSelfExtract=true -o out
      - uses: actions/upload-artifact@v4
        with: { name: Glass HUD Translator, path: out }
```

Windows runners bill at 2× multiplier; a 90-second build costs ~3 minutes of the 2,000/month private quota. ~600 pushes/month before exhaustion.

### Windows-only test checklist

Because sessions on the test machine are scarce, batch these rather than rediscovering them:

```
[ ] hotkey fires while FFXIV has focus
[ ] overlay stays above game, does not steal focus
[ ] click-through: mouse passes through to game
[ ] WDA_EXCLUDEFROMCAPTURE — overlay not self-OCR'd
[ ] Alt+Tab / minimise / restore
[ ] 125% and 150% display scaling
[ ] capture latency <50 ms at 3 fps
[ ] borderless-windowed detection and refusal message
[ ] SmartScreen prompt wording
```

---

## 10. Delivery

**Format:** plain unsigned `.exe`, zipped. No installer, no admin rights, no MSIX.

```
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```

`IncludeNativeLibrariesForSelfExtract` is required — Tesseract's native `leptonica`/`tesseract` DLLs will not survive single-file bundling without it.

| Approach | Size | User must install |
|---|---|---|
| Self-contained single file | ~90–140 MB | Nothing |
| Self-contained folder | ~110–160 MB | Nothing |
| Framework-dependent | ~5–15 MB | .NET Desktop Runtime |

**Choose self-contained.** Size is irrelevant for one user; "nothing to install" eliminates an entire support conversation.

**Ships alongside:** `eng.traineddata`, Noto Sans Arabic TTF, default region profiles JSON.

### First-run friction to warn about

**SmartScreen will block it.** Unsigned + no download reputation → "Windows protected your PC." User clicks *More info* → *Run anyway*, once. Tell him in advance.

Code signing is not worth it here — OV certificates now require hardware token storage and run ~€200–400/year, and reputation still takes weeks to build.

**Mild antivirus risk.** Screen capture + always-on-top + outbound HTTPS is a heuristic pattern. Mitigations: `RegisterHotKey` not `WH_KEYBOARD_LL`; if flagged, ship the non-single-file folder build instead.

**No elevation needed.** Ensure FFXIV is not running as administrator — if it is, run both at the same integrity level to avoid UIPI problems.

---

## 11. API key handling

**Bring-your-own-key. Do not embed my key.**

Three reasons:
1. A key in a distributed binary is extractable in seconds.
2. Google's Gemini API Additional Terms state that only Paid Services may be used when making API Clients available to users in the **EEA, Switzerland, or the UK**. Both of us are in Germany. BYO-key moves this question onto whoever holds the key.
3. Quota isolation — his usage never affects mine.

**Storage:** first-run settings dialog → `%APPDATA%\Glass HUD Translator\config.json`, key fields wrapped in `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)` (DPAPI). Tied to the Windows account, unreadable if the file is copied elsewhere.

**Data note:** free-tier prompts on all these providers may be used for training, and human reviewers may read API input and output. Irrelevant here — FFXIV dialogue is public game text. Do not use free tiers for anything sensitive.

---

## 12. Operational hardening

### Never hardcode model names

Free-model catalogues churn hard. Providers have been observed silently deleting free models, breaking client code without warning.

Put model names in `models.json` as a per-provider **ordered list**. On 404 / model-not-found, fall through to the next entry and **log loudly** rather than failing the translation.

### Visible quota counter

Small readout in the settings window (not the overlay):

```
Gemini  412 / 1000   ·   Groq  0 / 14400   ·   cache 23%
```

Persist counts in SQLite keyed to the Pacific-midnight day boundary so they survive restarts. At ~90% Gemini usage, switch silently to Groq and log it.

### Log everything for later analysis

Store every `(raw OCR → normalized → provider → translation → latency)` tuple in SQLite. This becomes simultaneously:
- The correction dataset
- The OCR error dictionary source
- Evidence for whether Gemini quality is actually sufficient
- Cache hit-rate diagnostics

`Ctrl+Shift+F` writes an override row that always wins on cache lookup, so corrections are permanent.

---

## 13. Build order

### Phase 1 — Prove the pipeline (1–2 days)

- Hardcoded region rectangle in a JSON config
- One hotkey
- Screenshot → Tesseract → StubProvider → plain window (no transparency)
- Then swap StubProvider for Gemini

No region picker, no transparency, no click-through. Goal is a working end-to-end path.

### Phase 2 — Usable overlay (3–7 days)

- Region picker UI with profile save
- SQLite cache with normalized SHA-256 keys
- Transparent, click-through, always-on-top overlay
- `WDA_EXCLUDEFROMCAPTURE` on the overlay window
- Auto-watch mode with frame hashing and self-expiry
- Token bucket + 429 fallback to Groq
- Loading state (`جارٍ الترجمة...`) painted on hotkey press

### Phase 3 — Quality (week 2+)

- Glossary with per-line entry injection
- Correction workflow (`Ctrl+Shift+F`) writing cache overrides
- Multiple region profiles (dialogue / subtitle / quest window)
- Provider abstraction finalised so Qwen-MT or others slot in cleanly
- OCR correction dictionary populated from real logs
- Quota counter UI

### Phase 4 — Only if warranted

- Native macOS frontend (ScreenCaptureKit) — nearly free if Avalonia was chosen
- Automatic dialogue-box detection
- Speaker-name separation
- Translation history browser

---

## 14. Expected latency

| Path | Time |
|---|---|
| Cache hit | <10 ms, effectively instant |
| Fresh line | ~120 ms OCR + 400–900 ms API ≈ **1 second** |

Paint `جارٍ الترجمة...` the instant the hotkey fires so the second does not feel dead.

---

## 15. Open questions

| # | Question | Blocks |
|---|---|---|
| 1 | **Avalonia vs WPF** — run the Arabic rendering test | All UI work |
| 2 | Does Groq's `qwen3-32b` actually get ~14,400 RPD, or a lower per-model cap? | Quota planning (conclusion holds either way) |
| 3 | Are Windows App SDK AI text-recognition APIs Copilot+ gated? | Confirms Tesseract choice |
| 4 | Actual Gemini limits for the specific project — check AI Studio | Token bucket tuning |
| 5 | Tesseract accuracy on real FFXIV frames — measure after Phase 1 collection | Possible PaddleOCR escalation |
| 6 | Does `BitBlt` suffice, or is `Windows.Graphics.Capture` needed? | Capture implementation |
| 7 | MSA vs Egyptian Arabic preference — ask the actual user | Prompt design |

---

## 16. Things NOT to do (explicit anti-patterns)

- Do **not** suggest Dalamud, XIVLauncher plugins, memory reading, or process injection.
- Do **not** use `Windows.Media.Ocr` (forces MSIX packaging).
- Do **not** use `WH_KEYBOARD_LL` low-level hooks (AV heuristics).
- Do **not** bind hotkeys to F1–F12 (FFXIV party targeting).
- Do **not** embed the developer's API key in the binary.
- Do **not** hardcode model name strings in code.
- Do **not** queue stale translation requests — drop them past 6 seconds.
- Do **not** use `localStorage`/browser storage patterns — this is a desktop app.
- Do **not** switch to classic MT APIs (Google Translate, Azure, DeepL) — strictly worse on quota, cost, and quality for this workload.
- Do **not** run a local LLM on the target machine.
- Do **not** hash the cache key before normalization.
