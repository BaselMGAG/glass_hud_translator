# Glass HUD Translator — Coding Sessions

Three coding sessions, plus one human-only prep session. Each coding prompt below is
**copy-paste ready** into a fresh Claude Code session in this directory.

Read alongside [`PROJECT_PLAN.md`](PROJECT_PLAN.md) and [`docs/BRIEF.md`](docs/BRIEF.md).

> **All three sessions are done.** This is kept as a record of how the work was actually broken up,
> not as a to-do list. Sessions 1 and 2 landed as planned; Session 3's "quality from real data" is
> the part still genuinely outstanding, because it needs the borrowed Windows laptop and a real
> frame corpus rather than the synthetic one in `test-frames/`.
>
> Work since then — the paid provider lanes, the tabbed settings screen, the Arabic interface, the
> release pipeline — is recorded in [`CHANGELOG.md`](CHANGELOG.md) instead.

```
Session 0   human, ~45 min    prereqs + frame corpus        no code
Session 1   Mac,   ~3–4 h     Core + UI + tests + CI        ~85% of the app
Session 2   Windows, ~2–3 h   Win32 + packaging             makes it real
Session 3   mixed, ~2–3 h     quality from real data        makes it good
```

---

## Session 0 — Prep (human, no coding)

### On the Mac

```bash
brew install tesseract ollama && ollama pull qwen3:8b
```

Ollama is dev-only (brief §2.7) but gives you an unlimited local provider for prompt iteration.
24 GB RAM handles `qwen3:8b` comfortably; `qwen3:14b` also fits if you want a stronger local check.

Fetch the font — bundled, never assumed installed (brief §6):

```bash
mkdir -p "src/GlassHudTranslator.App/Assets/Fonts" && curl -L -o "src/GlassHudTranslator.App/Assets/Fonts/NotoSansArabic-Regular.ttf" "https://github.com/notofonts/arabic/raw/main/fonts/NotoSansArabic/hinted/ttf/NotoSansArabic-Regular.ttf"
```

Get both keys — neither needs a credit card:

- **Gemini** — [aistudio.google.com](https://aistudio.google.com) → Get API key. While you're there,
  note the *actual* rate limits shown for your project; the published tables aren't guaranteed.
- **Groq** — [console.groq.com](https://console.groq.com) → API Keys. Check whether `qwen/qwen3-32b`
  is still listed and what its limits are (brief open question #2).

Create an empty GitHub repo and note the URL. Session 1 wires CI to it.

### On the Windows laptop — collect, don't debug

The single most valuable 20 minutes of this whole project. Capture **~40 PNGs** of the dialogue
region, covering:

- both UI scales he actually plays at, plus 125% and 150% Windows display scaling
- bright zone behind the box (Costa del Sol) *and* dark zone (a night dungeon)
- NPC dialogue box **and** cutscene subtitle bar **and** quest-accept window
- apostrophe names: Y'shtola, Y'mhitra, G'raha Tia
- long two-line text, and a one-word interjection
- one frame captured **mid-typewriter-reveal** (that's the case `StableOcrReader` has to survive)

Drop them in `test-frames/`. Write `test-frames/expected.json` by hand — the correct English for
each — which turns the corpus into an OCR accuracy benchmark for Session 3:

```json
[ { "file": "01-ysthola-night.png", "speaker": "Y'shtola", "body": "Come, the aether here grows unstable." } ]
```

While you're on the machine, do the one-time setup so later sessions cost nothing:

- **Settings → Optional Features → OpenSSH Server**, then `Start-Service sshd` and set it to
  Automatic. Gives you VS Code Remote-SSH from the Mac. (Don't use RDP for capture testing —
  session and DPI behaviour differ enough from console to produce phantom bugs.)
- Set FFXIV to **borderless windowed**; write down the resolution and UI scale.
- Confirm FFXIV is **not** running as administrator (UIPI would block the overlay).

**If the laptop isn't available, skip this and start Session 1 anyway** — its first task generates
synthetic frames. Swap in real ones later; no code changes.

---

## Session 1 — Mac · Core, UI, tests, CI

Everything that doesn't need Win32. Roughly 85% of the app.

> **Do not start UI work before task 2 passes.** It decides the UI framework.

<details open>
<summary><b>Copy-paste prompt</b></summary>

```
Read PROJECT_PLAN.md and docs/BRIEF.md fully before writing code. PROJECT_PLAN.md §1
lists five deltas that override the brief — apply those. Build only what runs without
Windows; Session 2 handles Win32.

1. SCAFFOLD
   Create the solution exactly as laid out in PROJECT_PLAN.md §3. TFMs: Core/tests/Replay
   net10.0, Interop and Windows net10.0-windows, App multi-targets net10.0;net10.0-windows
   with the conditional ProjectReference from §1.2. Add Avalonia 11, SkiaSharp,
   Microsoft.Data.Sqlite, xunit. Create the Windows/Interop projects with the file stubs
   named in §3 but leave their bodies as NotImplementedException — they must compile on
   macOS, which they will, since nothing uses UseWPF/UseWindowsForms.
   Gate: `dotnet build` succeeds. (Note: `-f net10.0` is a per-project flag — at solution level
   it tries to force that TFM onto the Windows-only projects and fails. Use plain `dotnet build`.)

2. THE GO/NO-GO TEST — do this before any other UI work
   Minimal Avalonia window, bundled NotoSansArabic-Regular.ttf, FlowDirection=RightToLeft,
   rendering the two strings in PROJECT_PLAN.md §7. Run it, screenshot it, show me the
   screenshot, and state whether letter joining, bidi placement of the Latin names, and
   final-period position are all correct. STOP and ask me before continuing if any fail.

3. TEST FRAMES
   If test-frames/ is empty, write tools/Replay/SyntheticFrames.cs to generate 12 SkiaSharp
   PNGs imitating the FFXIV dialogue box — dark rounded rect ~60% opacity, white ~22px text,
   speaker name on its own first line, over both light and dark backgrounds. Include one
   mid-typewriter truncated frame. Write the matching expected.json.

4. CORE — implement PROJECT_PLAN.md §4 exactly as signed, with xunit tests for each:
   • Capture: Frame, FolderFrameSource, FrameHasher (64×24 greyscale → 64-bit, Hamming ≤3)
   • Ocr: OcrPreprocessor (greyscale → contrast stretch → 2× upscale → threshold, SkiaSharp),
     TesseractCliEngine shelling out to `tesseract <in> stdout --psm 6 -l eng`,
     StableOcrReader (re-OCR every 150ms until two consecutive reads match, cap 1.5s)
   • Text: TextNormalizer, DialogueParser, CacheKey — implement the SPLIT in §1.5:
     case-preserved Body for the prompt, lowercased-then-sha256 for the cache key.
     Test that "Y shtola" and "Y'shtola" collapse to the SAME cache key via
     ocr-corrections.json. This test is the quota guard — brief §5.
   • Glossary: longest-match-first, word-boundary, case-insensitive, cap 12 terms
   • Translation: OpenAiCompatibleProvider (one class, base URL swapped), StubProvider
     (400ms delay + fixed Arabic), PromptBuilder per brief §6 with an ArabicRegister switch,
     TokenBucket, QuotaLedger, ProviderRouter implementing PROJECT_PLAN.md §5 EXACTLY —
     including the >6s staleness drop, the 4s hard timeout, 404-model fallthrough with a
     loud log, and English-plus-warning as the terminal fallback. Never blank, never throw
     out of the router.
   • Storage: AppDatabase with the four tables in §6, WAL, migrations-on-open.
     Override rows always win on lookup.
   • ISecretStore + DevPlainFileSecretStore, with the visible dev warning from §1.3.
   Gate: `dotnet test` green, ≥40 tests.

5. DATA FILES
   data/models.json per §6. profiles/<game>/glossary.json seeded with ~60 terms — Scions, the main
   city-states, job names, core aether vocabulary. profiles/<game>/ocr-corrections.json seeded with the
   brief §6 examples. Everything loaded from disk, nothing hardcoded.

6. tools/Replay
   Headless: runs FolderFrameSource → the full pipeline → prints a table of
   file | raw OCR | normalized | cache hit? | provider | Arabic | latency.
   Flags: --provider stub|ollama|gemini|groq, --frames <dir>, --no-cache.
   This is the main dev loop — it must run entirely on macOS.

7. AVALONIA UI (Core wired in, platform calls behind PlatformServices only)
   • OverlayWindow — transparent, borderless, Topmost, RTL, bundled font, configurable
     opacity/size, paints "جارٍ الترجمة..." immediately on trigger (brief §14)
   • RegionPickerWindow — full-screen drag-a-rectangle, saves as FRACTIONS of the game
     client rect per brief §8, named profile (dialogue/subtitle/quest)
   • SettingsWindow — API keys via ISecretStore, register MSA/Egyptian, font size, opacity,
     hotkey display, the quota readout from brief §12, cache hit rate, and a "Test
     translation" button
   • PlatformServices.cs — the ONLY #if WINDOWS in the App. Non-Windows returns
     FolderFrameSource, TesseractCliEngine, DevPlainFileSecretStore, and a no-op hotkey
     service.

8. CI — .github/workflows/build.yml, windows-latest, setup-dotnet 10.0.x, publish
   -f net10.0-windows -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
   -p:IncludeNativeLibrariesForSelfExtract=true, upload the artifact. Also run
   `dotnet test` on ubuntu-latest.

9. CLAUDE.md — architecture summary, the platform seam rule, how to run Replay, and the
   brief §16 anti-pattern list so future sessions don't regress.

Report at the end: test count, Replay output for all frames, the §7 screenshot, and
anything you had to deviate from.
```

</details>

**Done when** `dotnet test` is green · Replay prints Arabic for every frame using `--provider stub`
*and* `--provider gemini` · the overlay renders §7 correctly · CI is green and has produced a
downloadable `.exe`.

---

## Session 2 — Windows · make it real

Run this over VS Code Remote-SSH into the laptop, or directly on it. FFXIV should be running in
borderless windowed the whole time.

<details open>
<summary><b>Copy-paste prompt</b></summary>

```
Read PROJECT_PLAN.md and CLAUDE.md. Session 1 left GlassHudTranslator.Windows and
GlassHudTranslator.Interop as compiling stubs. Implement them and ship a working .exe.

1. INTEROP — P/Invoke declarations only, no logic:
   BitBlt/CreateCompatibleDC/GetDC/SelectObject/DeleteObject, GetWindowRect/GetClientRect/
   ClientToScreen, FindWindow/GetForegroundWindow, RegisterHotKey/UnregisterHotKey,
   GetWindowLong/SetWindowLong + WS_EX_TRANSPARENT|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE,
   SetWindowPos + HWND_TOPMOST, SetWindowDisplayAffinity + WDA_EXCLUDEFROMCAPTURE,
   SetProcessDpiAwarenessContext (per-monitor v2).

2. Win32FrameSource — BitBlt from the desktop DC (brief §2.4, NOT Windows.Graphics.Capture),
   convert HBITMAP → BGRA byte[] once at the boundary so nothing downstream sees Win32.
   Reuse the DC and bitmap across calls; measure and log per-capture ms.

3. GameWindowLocator — find the FFXIV window, expose its client rect, resolve stored
   fractional region profiles against it (brief §8). DisplayModeGuard — detect exclusive
   fullscreen at startup and refuse with a clear message rather than yielding black frames
   (brief §2.2).

4. GlobalHotkeyService — RegisterHotKey on a message-only window. NOT WH_KEYBOARD_LL
   (brief §2.6, AV heuristics). Bind exactly: Ctrl+Shift+R region picker, Ctrl+Shift+T
   translate now, Ctrl+Shift+A toggle auto-watch, Ctrl+Shift+F flag/correct. Never F1–F12.

5. OverlayWindowStyles — applied to the Avalonia overlay handle: WS_EX_TRANSPARENT
   click-through, WS_EX_NOACTIVATE so it never steals focus from the game, HWND_TOPMOST,
   and WDA_EXCLUDEFROMCAPTURE so the overlay can't OCR itself.

6. TesseractNativeEngine — the nuget natives, so the user installs nothing. Ship
   eng.traineddata (start with `fast`). Same IOcrEngine contract as the CLI engine; both
   must produce identical results on test-frames/.

7. DpapiSecretStore — ProtectedData.Protect, DataProtectionScope.CurrentUser, into
   %APPDATA%\Glass HUD Translator\config.json. Wire PlatformServices to it under #if WINDOWS.

8. FirstRunWindow — key entry, a "Set your region" step that launches the picker, the
   borderless-windowed check, and an explicit SmartScreen warning ("More info → Run anyway").

9. Auto-watch, Windows side — 2 fps, BelowNormal worker thread priority, FrameHasher gate
   before OCR (brief §3), 90-second self-expiry. Verify the hash gate actually skips: log
   the skip percentage; it should be 85%+ during dialogue.

10. Publish and verify the artifact from CI runs on a clean profile.

Then work the brief §9 checklist with FFXIV running and report each line pass/fail:
   [ ] hotkey fires while FFXIV has focus
   [ ] overlay stays above game, does not steal focus
   [ ] click-through: mouse passes to the game
   [ ] WDA_EXCLUDEFROMCAPTURE — overlay not self-OCR'd
   [ ] Alt+Tab / minimise / restore
   [ ] 125% and 150% display scaling
   [ ] capture latency <50 ms
   [ ] borderless-windowed detection and refusal message
   [ ] SmartScreen prompt wording

Before you finish: capture 40 more real frames into test-frames/ if Session 0 didn't, and
export translation_log to docs/session2-log.csv for Session 3.
```

</details>

**Done when** the checklist is all green and he can press `Ctrl+Shift+T` during MSQ and read Arabic.

---

## Session 3 — Quality from real data

Only meaningful **after** real play. Its whole input is `translation_log`.

<details open>
<summary><b>Copy-paste prompt</b></summary>

```
Read PROJECT_PLAN.md, CLAUDE.md, and docs/session2-log.csv (real play data from Session 2).
This session is measurement-driven — do not guess where the problems are, read the log.

1. MEASURE FIRST, report before changing anything:
   • OCR accuracy — Replay over test-frames/ vs expected.json, character error rate,
     and the 10 most frequent misrecognitions
   • cache hit rate from the log, and how many near-miss pairs differ only by
     normalization (each one is a wasted API call — brief §5)
   • actual RPD/RPM reached per provider from the quota table → answers brief open
     questions #2 and #4; write the real numbers back into models.json
   • p50/p95 latency, split cache-hit vs fresh
   • every `outcome != ok` row, grouped

2. ocr-corrections.json — populate from the real misrecognitions found above, not from
   imagination. Re-run Replay and report the hit-rate delta.

3. glossary.json to ~200 terms: all Scions, city-states and their districts, every job and
   role name, aether/primal/Echo vocabulary, the beast tribes, and the proper nouns that
   actually appear in the log. Verify the matcher still caps injection at 12 terms/request.

4. Correction workflow — Ctrl+Shift+F opens a small edit box on the current translation and
   writes an is_override=1 row that always wins on lookup (brief §12). Verify it survives
   restart.

5. Quota readout in Settings, live: "Gemini 412/1000 · Groq 0/14400 · cache 23%", persisted
   across the Pacific-midnight boundary. At ~90% Gemini, switch silently to Groq and log it.

6. Multi-region profiles — cycle dialogue → subtitle → quest on repeated Ctrl+Shift+R, keyed
   to (resolution, UI scale) per brief §8.

7. Register toggle — ask which he prefers, then make MSA/Egyptian a real setting that changes
   one line of the system prompt (brief §6).

8. If OCR character error rate is >5% on the real corpus, evaluate PaddleOCR-via-ONNX behind
   the existing IOcrEngine and report the comparison. Do not swap without showing me numbers.

Report: before/after on hit rate, OCR error rate, and latency.
```

</details>

**Done when** a 4-hour session runs without a crash, hit rate ≥15%, and the measured quota numbers
are written back into `models.json`.

---

## Compressing to fewer sessions

If you want this in **one** sitting, run Session 1 and cut: the region picker (hardcode a rectangle
in JSON), the settings window (edit the JSON by hand), auto-watch (manual `Ctrl+Shift+T` only), and
the quota ledger. That is brief §13 Phase 1, and it still gets you English → Arabic on screen.
Everything cut is additive later — none of it changes the contracts in `PROJECT_PLAN.md` §4.

**Two** sessions works cleanly as Session 1 + Session 2; Session 3 is a quality pass that genuinely
cannot happen before real play data exists, so it isn't really compressible — it's just later.

## Between sessions

Push after every session. CI compiles Windows in ~2 minutes and emits a runnable `.exe`, so his
"testing" is downloading an artifact rather than you setting up a build environment on his machine.
Keep a `NOTES.md` of anything you couldn't verify without the laptop — scarce sessions get burned
rediscovering what you already knew was untested.
