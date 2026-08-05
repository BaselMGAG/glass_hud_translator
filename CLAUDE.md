# Orientation for anyone changing this code

Arabic translation overlay for games that ship without Arabic support. Reads a rectangle of the
screen, OCRs it, translates it, draws the result back over the game in a separate window.

Start here, then read [`docs/BRIEF.md`](docs/BRIEF.md) for why the project is shaped this way and
[`PROJECT_PLAN.md`](PROJECT_PLAN.md) for the type contracts and schemas.

## The constraint behind most of the design

I develop on macOS. Live testing happens on a borrowed Windows laptop that is rarely available. So
the whole codebase is arranged to run as much as possible without Windows, and to keep the part
that genuinely needs it as small as I could make it.

```
runs on macOS/Linux   all logic, prompts, glossary, cache, OCR, the entire UI,
                      full pipeline replay against recorded screenshots
needs Windows         live screen capture, global hotkeys, click-through,
                      DPI handling, a real game
```

If you contribute, you can do almost everything without a Windows machine.

## Commands

```bash
dotnet build
```

Note it's plain `dotnet build`, not `dotnet build -f net10.0`. That flag is per-project, and at
solution level it tries to force `net10.0` onto the Windows-only projects and fails.

```bash
dotnet test
```

111 tests, all runnable on macOS and Linux.

```bash
dotnet run --project tools/Replay -- --no-cache
```

Replay is the main development loop and the fastest way to understand the system. It pushes
recorded PNGs through the exact `TranslationPipeline` the overlay uses and prints every stage.
Flags: `--provider stub|gemini|groq|ollama|all`, `--profile <id>`, `--frames <dir>`, `--no-skip`
to bypass change detection, `--generate-frames` to rewrite the sample corpus.

```bash
dotnet run --project src/GamingTranslatorGlassHUD.App -f net10.0 -- --stub
dotnet run --project src/GamingTranslatorGlassHUD.App -f net10.0 -- --render-test
```

Run `--render-test` after touching anything to do with fonts, text layout or the Avalonia version.
It renders the cases where Arabic layout usually breaks and tells you whether the bundled font
actually loaded rather than the OS quietly substituting one.

## Layout

```
src/GamingTranslatorGlassHUD.Core/     net10.0                  all logic, all tests
src/GamingTranslatorGlassHUD.Interop/  net10.0-windows          P/Invoke declarations, no logic
src/GamingTranslatorGlassHUD.Windows/  net10.0-windows          Win32 impls — currently stubs
src/GamingTranslatorGlassHUD.App/      net10.0;net10.0-windows  Avalonia UI
tools/Replay/                          net10.0                  headless harness
profiles/<game>/                       per-game data, no code
data/models.json                       provider and model config
```

A useful thing to know: `net10.0-windows` **compiles on macOS**. The TFM only applies
`[SupportedOSPlatform("windows")]`, which is an analyzer contract, not a build requirement. It's
`UseWPF`/`UseWindowsForms` that would make a Windows host mandatory, and this project uses neither.
That's why a typo in the Win32 layer gets caught locally instead of on the borrowed laptop.

## Rules that look like style but are correctness

**Never set an explicit `LineHeight` on Arabic text.** Arabic hangs marks below the baseline —
kasra, the dot under `ج`, the two dots under a final `ي`. A tight line box clips them silently, and
clipping those two dots turns `ي` into `ى`, which is a different letter and often a different word.
Measured at 26px with the bundled font: `LineHeight` 40 and 44 clip, 48 and above are fine, natural
is 54.9. Use `LineSpacing` instead — it adds to the natural height rather than replacing it, so it
stays correct when the user changes font size in Settings.

**`PlatformServices.cs` is the only file in the App allowed to contain `#if WINDOWS`.** If a second
one appears, the platform seam has leaked and the macOS build has stopped being a faithful
rehearsal of the Windows build.

**Normalise before hashing, and lowercase only for the cache key.** `TextNormalizer` returns
case-preserved text for the prompt; `CacheKey` lowercases on its way into SHA-256. Casing is real
signal to the model — "limsa lominsa" translates worse than "Limsa Lominsa". And the realistic way
to exhaust a daily API quota isn't long sessions, it's one line hashing two different ways.

**Don't raise `MinWordConfidence` back to 40.** At 40 it silently deleted the word "linkpearl",
which Tesseract had actually read perfectly at confidence 39.2. Tesseract scores unusual proper
nouns down, and unusual proper nouns are most of what a game glossary contains. A dropped word
loses the sentence's meaning *and* changes its cache key; an uncertain word that survives is merely
visibly imperfect.

**Model names live in `data/models.json`, never in code.** Free model catalogues churn and
providers delete free models without warning. A 404 falls through to the next entry in the list and
logs `MODEL GONE` loudly.

**The router must never throw.** When every provider fails the user sees the OCR'd English with a
warning marker. Never blank, never crash.

## Things deliberately not done

No game-process injection, no memory reading, no plugin frameworks — those risk accounts and break
on every patch. No `Windows.Media.Ocr`, which would force MSIX packaging and kill the "download a
zip and run it" delivery model. No `WH_KEYBOARD_LL` keyboard hooks, since that's the pattern
antivirus heuristics flag; `RegisterHotKey` already fires while a game has focus. No F1–F12
hotkeys, because games bind those. No embedded API key. No queued stale requests — anything older
than six seconds is dropped, because the dialogue has moved on. No classic machine-translation
APIs.

## Version choices that were deliberate

**Avalonia 11.3.x rather than 12.x.** 11.x is the API surface this was written against. Worth
revisiting once the UI settles.

**SkiaSharp pinned to 2.88.9**, because that's what Avalonia.Skia 11.3.18 depends on. Bumping it
independently causes a diamond conflict.

**`SQLitePCLRaw.lib.e_sqlite3` pinned to 2.1.12.** Microsoft.Data.Sqlite 10.0.5 pulls 2.1.11, which
carries GHSA-2m69-gcr7-jv3q. Drop the pin once the dependency moves past it on its own.

## Where things stand

The pipeline, UI, profiles, caching and provider routing all work and are tested. The Windows
project is compiling stubs — screen capture, global hotkeys and the click-through overlay are the
next milestone, and until they land you can't point this at a running game.

`test-frames/` currently holds **synthetic** frames drawn by `SyntheticFrames`. They exercise every
stage of the pipeline but say nothing about a real game's typeface, its translucency, or a moving
3D scene behind the text. Replacing them with real captures is the highest-value contribution
available.
