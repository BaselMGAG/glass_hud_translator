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

427 tests, all runnable on macOS and Linux.

```bash
dotnet run --project tools/Replay -- --no-cache
```

Replay is the main development loop and the fastest way to understand the system. It pushes
recorded PNGs through the exact `TranslationPipeline` the overlay uses and prints every stage.
Flags: `--provider stub|gemini|groq|ollama|all`, `--profile <id>`, `--frames <dir>`, `--no-skip`
to bypass change detection, `--generate-frames` to rewrite the sample corpus.

```bash
dotnet run --project src/GlassHudTranslator.App -f net10.0 -- --stub
dotnet run --project src/GlassHudTranslator.App -f net10.0 -- --render-test
```

Run `--render-test` after touching anything to do with fonts, text layout or the Avalonia version.
It renders the cases where Arabic layout usually breaks and tells you whether the bundled font
actually loaded rather than the OS quietly substituting one.

## Layout

```
src/GlassHudTranslator.Core/     net10.0                  all logic, all tests
src/GlassHudTranslator.Interop/  net10.0-windows          P/Invoke declarations, no logic
src/GlassHudTranslator.Windows/  net10.0-windows          Win32 impls (untested on hardware)
src/GlassHudTranslator.App/      net10.0;net10.0-windows  Avalonia UI
tools/Replay/                          net10.0                  headless harness
profiles/<game>/                       per-game data, no code
data/models.json                       provider and model config
```

CI is three workflows: `build.yml` (tests on ubuntu at 1x billing), `release.yml` (tag `v*` →
public zip), and `publish-windows.yml`, which both of the others call so that the artifact a
release ships comes from exactly the steps that have been running green on every commit.

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
rehearsal of the Windows build. `PlatformSeamTests` enforces this, along with Core never referencing
the Interop or Windows projects and the App keeping both TFMs — the multi-target is what makes
`[SupportedOSPlatform]` tell the truth, so dropping the neutral one would silence the analyzer and
let the seam erode with nothing complaining.

**The cache key is a frozen wire format.** Every installation has a `translations` table keyed by
`sha256("{register}\n" + lowercased body)`, and a key change makes every row unreachable —
silently, because a miss looks exactly like a line never seen before. `CacheKeyTests` pins it with
golden hex vectors; if one fails, the answer is almost never to update the expected value. Two
things make a future change survivable and both are now tested: `translations.source` holds
byte-for-byte what was hashed, so rows can be rehashed rather than discarded, and only two register
tokens are ever produced, neither containing a newline — the separator alone does *not* make the
encoding injective.

**Migrations are a ladder, and they are additive forever.** `AppDatabase` applies steps from
whatever version the file is at, bumping `user_version` per step so an interrupted upgrade resumes.
The previous shape — one conditional followed by every migration — works exactly once: a second
step would be skipped for everyone already at the current version, which is every existing user.
Never rename a column and never drop one: there is deliberately no self-updater, so re-unzipping an
older release is a supported recovery, and an older build opening a newer database proceeds without
complaint. That is only safe while nothing it knew about has moved.

**Normalise before hashing, and lowercase only for the cache key.** `TextNormalizer` returns
case-preserved text for the prompt; `CacheKey` lowercases on its way into SHA-256. Casing is real
signal to the model — "limsa lominsa" translates worse than "Limsa Lominsa". And the realistic way
to exhaust a daily API quota isn't long sessions, it's one line hashing two different ways.

**Context reaches the prompt and never the cache key, and that is a decision, not an oversight.**
The prompt already carries the game, the style hint, the glossary, the speaker and now three
previous lines; the key hashes the body alone. So the same English line in two games returns one
cached Arabic translation — a real limitation, accepted knowingly, because hit rate is the entire
quota argument and a context digest in the key would shred it. What keeps that tolerable is
`TranslationPipeline.ContextWindow` staying small: a cache hit replays with *no* context at all, so
the wider the window, the worse a hit is relative to a live translation. Three lines is the cap.
Widening it is not a tuning change — it is a change to how wrong cached lines are allowed to be,
and `translations.source` is what makes re-keying later a migration rather than data loss.

**The too-short guard belongs before the cache lookup, not after the translation.** It used to run
in `TranslationSession` on the returned outcome, which is to say after the line had been hashed,
looked up, sent to a provider, paid for, cached and pushed into context — every side effect the
guard exists to prevent had already happened, and only the display was suppressed. It is
`TranslationPipeline.MinimumBodyCharacters` now. Any future "don't translate this" rule goes in the
same place, ahead of `cache.TryGetAsync`, or it is decoration.

**`PipelineOutcome.Result` is null when nothing was attempted.** An empty region and a two-character
misread are not failed translations, and they used to be reported as one — a fabricated
`TranslationResult` carrying the `stale` outcome, which read to every consumer as "the provider let
us down". Null says the truth. Every consumer must branch on it; there is one in the App, one in
Settings and one in Replay.

**OCR word boxes are in frame coordinates, and the engine is what makes that true.** OCR runs on a
preprocessed copy that is upscaled — 2x by default, because Tesseract is markedly better on larger
text — so every box Tesseract reports is in doubled coordinates. `ParseTsv` takes the upscale factor
and divides back down; both engines pass their own. Forget it and the boxes are wrong by exactly
100% while still being perfectly plausible rectangles, pointing below and right of the words they
name. The unit test on the arithmetic cannot catch a caller that simply omits the argument, so
there is a second test that reads one frame at 1x and at 2x and requires the words to land in the
same place. It has been mutation-checked. Any new engine returning geometry owes the same mapping.

**A region proposal returns nothing rather than a weak guess.** `RegionFinder` is offered to
someone who cannot check the rectangle against the English, at the first moment they use the app.
One confident box drawn around a health bar teaches them the suggestions here are not to be
trusted, and nothing offered afterwards recovers that — so below three accepted words it returns an
empty list, and a block whose shape says nothing is `Unknown` rather than a guessed kind. The
integration test asserts that a dialogue *crop* classifies as `Unknown`, because a crop has no
bottom third: if that ever starts returning `Dialogue`, the classifier has learned to pattern-match
on nothing. Rejected words never contribute — a UI border read as `|~` at confidence 8 is exactly
what invents a text region where there is no text.

**The proposal heuristics have never seen a real game frame.** They are tested against layouts
written out by hand, deliberately: `test-frames/` is synthetic, so tuning the ranking against
rendered frames would measure `SyntheticFrames` rather than the ranking. Hand-built geometry at
least states the layout each rule claims to handle. This is the highest-value thing a real capture
corpus would fix, and until then treat any threshold in `RegionFinderOptions` as a guess with a
rationale, not as a measurement.

**Don't raise `MinWordConfidence` back to 40.** At 40 it silently deleted the word "linkpearl",
which Tesseract had actually read perfectly at confidence 39.2. Tesseract scores unusual proper
nouns down, and unusual proper nouns are most of what a game glossary contains. A dropped word
loses the sentence's meaning *and* changes its cache key; an uncertain word that survives is merely
visibly imperfect.

**Model names live in `data/models.json`, never in code.** Free model catalogues churn and
providers delete free models without warning. A 404 falls through to the next entry in the list and
logs `MODEL GONE` loudly.

**The router must never throw.** When every provider fails the user sees the OCR'd English with a
warning marker. Never blank, never crash. This has already been broken once: a provider that let
the four-second per-attempt cap surface as a bare `OperationCanceledException` escaped the router
entirely, because the only cancellation catch was guarded on the *outer* token, which is not the
one that fires on a timeout. `TryLaneAsync` now catches `OperationCanceledException` alongside
`ProviderException` and treats it as transient. Any new `catch` in that method needs the same care.

**Lane order in `data/models.json` is the cost policy.** The router walks it top to bottom, so the
free lanes must stay above the paid ones — a paid provider placed above a free one spends money on
lines the free tier would have answered for nothing. There is a test asserting this.

**A lane with no key must be skipped silently, not failed loudly.** `ITranslationProvider.
IsConfigured` is what makes shipping the paid lanes switched on safe: without it, a user with only
a Gemini key gets two "no API key" lines in the router log for every line translated, which buries
the failures that actually matter. It is read per request, so a key pasted into Settings takes
effect without a restart.

**Adding an OpenAI-shaped provider is a config edit, not a code change.** A new lane in
`data/models.json` gets a key field, a free/paid label and a "where to get one" link in Settings
automatically, because that screen is generated from the file. Only a provider with its own
protocol needs code — `AnthropicProvider` is the one example, and it exists as a separate class
precisely so that `OpenAiCompatibleProvider`, whose whole justification is having no
provider-specific branches, keeps having none.

**Every user-facing string goes in `UiText`, in both languages.** It is a class of `required`
properties rather than a key/value dictionary precisely so that adding a string without translating
it is a compile error, not a silent English leak in the Arabic interface. There is a test asserting
the `{0}`-style placeholders match between the two — a translation carrying a `{1}` the English
does not have throws `FormatException` at runtime, and only ever for the users this project exists
for. Platform error text (Win32 messages, "Global hotkeys are Windows-only") is deliberately left
untranslated; it comes from the OS.

**The primary monitor is not the screen.** Every "whole screen" call used to be
`GetSystemMetrics(SM_CXSCREEN)`, which is the primary display alone — so a game on a second monitor
was outside every captured frame. Worse, a monitor left of or above the primary starts at a
*negative* coordinate, and `CaptureRegion.FitsWithin` requires a non-negative origin, so such a
rectangle was actively refused three layers down in Core. Two questions, two methods: `FitsWithin`
asks "is this inside a pixel buffer" (origin must be ≥ 0, there is no pixel at −1); `Contains` asks
"is this on the desktop" against an origin that is wherever the monitors put it. `ClampTo` trims a
region the layout has moved under, because capturing the overhang BitBlts undefined pixels into OCR
and that reads as the model getting worse.

**But the client area a region is measured against must stay one monitor wide.** Regions are stored
as fractions of it, so widening it to the union of every display silently relocates every region
already saved — "22% from the left, 56% wide" becomes a band straddling the seam between two
screens, half of it reading the wrong display. `WholeScreen()` follows the monitor under the
foreground window for exactly this reason. `CaptureFullScreen()` is the opposite case and genuinely
wants the whole virtual desktop, because the picker has to be able to show you every screen.

**The overlay follows the game window.** It was pinned to the primary monitor, which was survivable
only while capture was primary-only — both halves were wrong together, so a player on a second
display got nothing and knew it. Once capture follows the game, an overlay left behind is worse: the
translation happens, the quota is spent, and the Arabic appears on a screen nobody is looking at.

**Anything the user creates goes in `AppPaths.UserProfiles`, never in `profiles/`.** The app folder
ships with the release and is replaced wholesale by an update — the release notes say so in both
languages. A profile written there is deleted the first time the user updates, taking their capture
regions, glossary and setup with it. `ProfileLibrary` merges the two roots, the user's copy always
wins, and the shipped one is left underneath so it keeps improving with each release.

**Deleting a bundled profile is a tombstone, not a delete.** Its files live in the app folder, so
removing them works exactly until the next update restores them. `_removed.json` in the user's
directory is what makes "delete Final Fantasy XIV because I don't play it" stay deleted. A corrupt
tombstone file must fail open — showing a profile the user deleted beats starting with an empty
list they cannot explain.

**`general` is read-only and undeletable; everything else, including `ffxiv`, is not.** It is the
screen-relative fallback that works on anything, and what the app falls back to when a game profile
is removed. Deleting the last profile would leave nothing to translate against.

**A display name becomes a folder name, so treat it as hostile.** `ProfileLibrary.SlugFor` strips
everything but ASCII letters and digits, because that string arrives from a text box and is then
joined onto a path. There are tests for `../`, absolute paths, drive letters and a leading
underscore — the last one because `Discover` skips `_`-prefixed folders, so a name slugging to
`_template` would create a profile that immediately vanished. Non-Latin names slug to nothing and
fall back to `game`; the display name keeps the original text.

**The update check notifies; it must never install.** `UpdateCheck` reads GitHub's public
`releases/latest` and puts a notice in Settings. Do not extend it into a self-updater. Windows will
not let a running process overwrite its own DLLs — and the natives under `x64/` are exactly the
files that would need replacing — the build is unsigned, so downloading and running an executable
is the same antivirus-heuristic problem that rules out `WH_KEYBOARD_LL`, and the whole path would
ship unverified because the Windows machine is borrowed. Its failure mode is an install that will
not start, in the hands of someone who cannot read the error.

**`UpdateCheck` never throws, and stays silent unless it has something to say.** Same contract as
the router, for the same reason: nobody asked for it, so it is not allowed to interrupt them with
its own failures. Two details are load-bearing. **`UpToDate` and `Unreachable` are distinct** — a
captive portal answering 200 with a login page must not be reported as "you have the latest
version", and only a check that actually reached GitHub resets the daily timer, or one launch spent
offline costs twenty hours. And **the request needs a `User-Agent`**: GitHub rejects API calls
without one with a 403, which would look exactly like a permanent rate limit.

**A local build is version 0.0.0, and that means "say nothing".** `Directory.Build.props` sets
`<Version>0.0.0-dev</Version>`; CI overrides it from the tag with `-p:Version=`. Without that
default a build from source reports 1.0.0, which is a version this project could plausibly reach —
so every release would look like a downgrade-to-update to whoever is developing it. Check with
`dotnet run --project src/GlassHudTranslator.App -f net10.0 -- --version`.

**Never build a label by concatenating a stored identifier onto a translated word.** The capture
regions are stored under English keys — `dialogue`, `subtitle`, `quest` — because they are lookup
keys in the region store and in every saved profile. Gluing one onto a translated verb produced
`حدد dialogue` on three buttons, which is what a first-time Arabic user saw before they had done
anything: half an interface. `UiText.RegionName` maps the key to a display name, and `PickRegion` is
a format string rather than a prefix. A test asserts every stored region name has an Arabic one.

**Machine output stays left-to-right, by `FlowDirection` on the control.** Model ids, provider
names, URLs and quota counts are not words. Left in a mirrored paragraph they reorder, so
`gemini → gemini-2.0-flash → gemini-2.5-flash-lite` renders back to front — and the order of the
lanes *is* the cost policy, so a reversed quota line tells the user the paid provider is tried
first. `SettingsWindow.Note`/`Warning`/`Readout` take a `machine: true` flag for this. **Do not use
Unicode isolates (U+2066…U+2069) instead.** That was tried: neither character exists in the bundled
Arabic font, and one unresolvable codepoint poisoned glyph fallback for the entire window, so every
Latin word in the interface rendered as an empty box. It reproduced on every run.

**The grey explanatory notes are not decoration — size them accordingly.** They were 11px in
`#9aa0a6`, the conventional "secondary, skip this" styling. Nothing in Settings is secondary to
someone opening it for the first time: those paragraphs are what say which providers are free and
what a capture region is, and the intended reader is not technical and may not read English. They
are 13px at `#c8ccd0` (10.3:1 against the window, up from 6.3:1), with `LineSpacing` — never
`LineHeight` — and more of it in Arabic, where the wrapped paragraphs are denser.

**Set the bundled font whenever Arabic is on screen.** `Fonts.Arabic` is bundled for the reason
`NOTICE` gives: a Windows machine with no Arabic font installed renders every Arabic string as
empty boxes. Relying on OS fallback works on macOS and hides the problem — the Arabic tab headers
were tofu the first time the interface was switched, and that was on a Mac that *does* have Arabic
fonts.

## Things deliberately not done

No game-process injection, no memory reading, no plugin frameworks — those risk accounts and break
on every patch. No `Windows.Media.Ocr`, which would force MSIX packaging and kill the "download a
zip and run it" delivery model. No `WH_KEYBOARD_LL` keyboard hooks, since that's the pattern
antivirus heuristics flag; `RegisterHotKey` already fires while a game has focus. No F1–F12
hotkeys, because games bind those. No embedded API key. No queued stale requests — anything older
than six seconds is dropped, because the dialogue has moved on. No classic machine-translation
APIs.

## Version choices that were deliberate

**Avalonia 11.3.x rather than 12.x — a deliberate hold, not a pending question.** 11.x is the API
surface this was written against, and the UI has now settled: tabs, the profile editor, two
languages, a mirrored layout, a bundled font. That is precisely why the hold is deliberate rather
than temporary. There are live users on a build that works, the Arabic path depends on shaping,
bidi and font-fallback behaviour that a major version could change silently, and `--render-test`
is the only thing that would catch it. Revisit when there is a reason — a bug fixed upstream, a
feature 11.x cannot do — not on a schedule.

**SkiaSharp pinned to 2.88.9**, because that's what Avalonia.Skia 11.3.18 depends on. Bumping it
independently causes a diamond conflict.

**`SQLitePCLRaw.lib.e_sqlite3` pinned to 2.1.12.** Microsoft.Data.Sqlite 10.0.5 pulls 2.1.11, which
carries GHSA-2m69-gcr7-jv3q. Drop the pin once the dependency moves past it on its own.

## Where things stand

The pipeline, UI, profiles, caching and provider routing all work and are tested.

**The Windows layer works.** Verified against Final Fantasy XIV: BitBlt capture, native Tesseract,
all five global hotkeys registering and firing under game focus, the overlay drawing over the game,
and the full translation round trip at roughly 940 ms a line with OCR confidence around 95.

Every part of it was written on macOS without ever running it, so that is worth recording: the two
things expected to break — the `BITMAPINFOHEADER` layout in `GetDIBits` and Avalonia's window-handle
timing for the overlay styles — both turned out fine.

**That verification covers v0.4.2 and nothing after it.** Everything since — the multi-monitor
coordinate work, the overlay following the game, the DC rebuild on display change, and the schema
migration to version 3 — was written on macOS and has not been run on Windows. The migration is the
one to check first, against a real populated database rather than a fresh one, because it is the
only change in that batch that can cost a user something they cannot get back. It is also the one
with a safety net: migrations are additive, so an older release re-unzipped over a version 3
database opens it without complaint.

Still unverified: click-through, display scaling above 100%, auto-watch under sustained load,
cache hit rate over a real session, and multi-monitor behaviour of any kind.

### What has actually gone wrong

The most useful section of this file, because every entry is a mistake that shipped or nearly did.
Kept current with the changelog.

**From the first Windows run (v0.1.0), all from one screenshot:**

- The app never exited. Avalonia shuts down on last-window-close and the overlay is a second
  top-level window, so closing Settings left an orphaned overlay and a live process.
- Failures reported only to the Settings status line, leaving the overlay stuck on
  "جارٍ الترجمة" — which reads as a hang. Every exit path now leaves the overlay defined.
- Tesseract discovery only knew Unix paths, so on Windows it suggested `brew install`.
- **`PublishSingleFile` broke native OCR.** TesseractOCR ships its natives as plain
  copy-to-output content under `x64/` and resolves them from `Assembly.Location`, which is an
  empty string inside a single-file bundle. Publishing as a folder fixes it, and CI now fails if
  `x64/tesseract55.dll` is missing. Do not reintroduce single-file publishing — it buys nothing
  here, because tessdata, profiles and data ship alongside regardless.

**Found by adding providers and the Arabic interface (v0.2.0):**

- **The router threw.** A provider that let the four-second per-attempt cap surface as a bare
  `OperationCanceledException` escaped the class whose entire contract is never throwing, because
  the only cancellation catch was guarded on the *outer* token rather than the linked timeout one.
- Arabic tab labels rendered as empty boxes: the interface was leaning on a system Arabic font,
  which macOS has and a plain Windows install may not. The same build would have shown nothing but
  boxes to the users it was built for.
- A first run with no keys explained nothing — skipping an unconfigured lane silently is right per
  line, but with every lane unconfigured the log said only "all providers exhausted".
- The shipped OpenAI model IDs were all wrong; none were current chat models.

**Found by a native Arabic reader, none of them catchable by a test (v0.2.1):**

- Three buttons read `حدد dialogue`. Region names are stored English keys, and the caption was
  built by gluing one onto a translated verb — half an interface, on the most prominent controls.
- The API key field said `غير محدَّد` — "not set", which describes a setting whose value is unknown
  rather than one you have not filled in.
- The dialect selector was labelled `المستوى اللغوي`, a linguist's term for a choice between two
  named dialects.
- The explanatory notes were 11px in mid-grey — the standard "secondary, skip this" styling, and
  exactly wrong for the paragraphs that tell a first-time user which providers are free.
- **The quota readout listed the provider lanes in reverse.** Latin runs inside a mirrored
  paragraph reorder, and that order *is* the cost policy — so the Arabic interface was reporting
  the paid lane as the one tried first.

**Found while writing the player-facing readme (v0.4.2):**

- The profile list showed folder names, not the names people gave their games: "Baldur's Gate 3"
  was listed as `baldur-s-gate-3`. Tolerable while the only two shipped with the app; not once
  anyone could add one. Same defect as building a button caption out of a stored key.

**Latent, found by inspection and not yet hit in the wild:** the bundled `NotoSansArabic-Regular.ttf`
contains **no Latin at all** — not `A`, not `%`, and none of `✓ ✗ ⚠ → · ⏎`. Every Latin word in the
Arabic interface is already resolved by OS fallback. That works today, but the whole reason the font
is bundled is to not depend on fallback, and the Unicode-isolate incident proved a single
unresolvable codepoint can poison fallback for an entire window. Before adding any new non-Arabic
codepoint to an Arabic string, check it against the font.

### Still unverified

Click-through, display scaling above 100%, auto-watch under sustained load, cache hit rate over a
real session, and multi-monitor behaviour of any kind.

`test-frames/` holds **synthetic** frames drawn by `SyntheticFrames`. They exercise every stage of
the pipeline but say nothing about a real game's typeface, its translucency, or a moving 3D scene
behind the text. Replacing them with real captures is the highest-value contribution available —
see `CONTRIBUTING.md`, which asks for the same thing.
