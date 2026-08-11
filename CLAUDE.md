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

611 tests, all runnable on macOS and Linux.

```bash
dotnet run --project tools/Replay -- --no-cache
```

Replay is the main development loop and the fastest way to understand the system. It pushes
recorded PNGs through the exact `TranslationPipeline` the overlay uses and prints every stage.
Flags: `--provider stub|gemini|groq|ollama|all`, `--profile <id>`, `--frames <dir>`, `--no-skip`
to bypass change detection, `--generate-frames` to rewrite the sample corpus.

```bash
dotnet run --project src/GlassHudTranslator.App -f net10.0 -- --stub
dotnet run --project src/GlassHudTranslator.App -f net10.0 -- --render-test --render-test-out /tmp/render.png --exit-after-render
dotnet run --project src/GlassHudTranslator.App -f net10.0 -- --toolbar-test --toolbar-test-out /tmp
```

Run `--render-test` after touching anything to do with fonts, text layout or the Avalonia version.
It renders the cases where Arabic layout usually breaks and tells you whether the bundled font
actually loaded rather than the OS quietly substituting one.

Run `--toolbar-test` after touching `Icons`. The icons are SVG path strings parsed at runtime, so a
typo is an exception thrown while the window is being built — on a user's machine, at startup, with
no compiler and no unit test able to see it. This draws the real strip, simple and expanded, to two
PNGs. If they render here they parse everywhere.

Run `--failure-test --failure-test-out <dir>` after touching `StartupFailureWindow` or anything on
the startup path — it renders the startup-failure window with a staged exception. Run
`--wizard-test --wizard-test-out <dir>` (optionally `--wizard-test-lang ar`) after touching
`FirstRunWizard` — it walks all four steps for the camera. Both flags exist for the same reason:
these are the windows that only ever appear on a stranger's machine at the worst possible moment,
and whose own failure to build is the most expensive crash the app can have.

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

**A retry must bypass the cache, and only a retry.** The line is in the cache *because* it was
translated once, so a retry that consulted it would hand back the same words instantly and forever —
the one behaviour a retry button cannot have. `TranslationPipeline.TranslateTextAsync` takes
`fresh`, and the retry path is the only caller that sets it. Editing the misread text is the same
method with `fresh` false, deliberately: a corrected line is a *different* string, so it is a
different key, and a user fixing a word to the spelling another line already uses should get that
line's answer for nothing. A retry also does not become the repeat reference — the next poll is
still looking at the original line and is still a repeat of it, which is the snip lesson in a new
place.

**The never-translate list is whole-line, and jitter-tolerant, and both halves are load-bearing.**
Whole-line because a substring rule lets one careless entry silence everything and cannot be
explained in a sentence to somebody typing into a settings box; it is affordable only because the
History tab hands over the exact text that was read, so there is nothing to guess at. Jitter-tolerant
because OCR is not repeatable, and an exact-match rule would let the next frame's «Press E ta
continue» through — leaving the user charged for a line they believe they switched off, which is
worse than having no rule. It reuses `TextSimilarity`, whose budget is proportional to the shorter
string, so a three-character phrase still needs an exact match. And it sits beside
`MinimumBodyCharacters`, ahead of the cache lookup, because that is where every "don't translate
this" rule goes or it is decoration.

**There are two nets before the cache now, and the second one exists because the first cannot see
text.** `FrameSettleGate` compares binarised thumbnails, so a subtitle burnt into moving footage
differs from the previous frame in whatever the picture is doing behind it — the words can be
pixel-identical while the signature says the screen changed. `TextSimilarity.LooksLikeARepeat` runs
after OCR and before the lookup, and drops a body within a few characters of the last one shown. It
costs one finished OCR and saves the metered half; the frame gate spends free polls and saves the
OCR. **Absolute edit distance, capped at a quarter of the shorter body** — an absolute budget of
three is right for a sentence and absurd for a word, because "yes" and "no" are three edits apart.
The reference is never advanced on a match, or a caption drifting three characters at a time would
never be translated at all.

**Suppressible and being-the-reference are different questions, and a test is what forced them
apart.** `ProcessOptions` carries `SuppressRepeats` and `RemembersLine` separately. A hotkey press
must never be dropped — it is a question and it gets an answer — but it does put a line on the
overlay, so the poll half a second later reading the same pixels with one comma misread is a repeat
*of it*, and the cache does not save that: a different string is a different key. A snip sets
neither, plus `UseContext = false`.

**A snip must leave no trace, and the list is longer than the feature.** It must never call
`FrameSettleGate.Offer` — calling a frame Ready is not an opinion, it records that frame as the one
on the overlay, so a snip offered to the gate would overwrite the watched region's signature and
every later poll of the real dialogue box would answer `Unchanged` forever. It must not become the
repeat reference, or the dialogue the player is reading gets suppressed as a repeat of a menu
tooltip. It must not touch `_consecutiveEmpty`, which is diagnosing a different rectangle, must not
repoint `LastRegionProfile`, and must not enter the cadence median that the adaptive settle deadline
is derived from — `WatchSession.CountedOutsideTheRhythm` counts it against the session cap and
against nothing else. And it deliberately ignores `_busy`: dropping a poll costs nothing because
another is half a second away, but the user dragged this box by hand.

**After a snip, forget the watched region — do not try to restore it.** `_settle.Reset()` and
`ResetRepeatGuard()` on the way out, so the next poll re-translates the main line. That looks like
waste and is not: the line was translated in this session, so it is already in the cache and a hit
is free. Without it the player sits looking at a menu tooltip until the dialogue advances.

**`PipelineOutcome.Result` is null when nothing was attempted.** An empty region and a two-character
misread are not failed translations, and they used to be reported as one — a fabricated
`TranslationResult` carrying the `stale` outcome, which read to every consumer as "the provider let
us down". Null says the truth. Every consumer must branch on it; there is one in the App, one in
Settings and one in Replay.

**OCR word boxes are in frame coordinates, and the engine is what makes that true.** OCR runs on a
preprocessed copy that is upscaled — 2x by default, because Tesseract is markedly better on larger
text — and then **padded**, so every box Tesseract reports is in doubled *and* offset coordinates.
`ParseTsv` takes both numbers, subtracts the padding and divides by the upscale, **in that order**:
the margin is added after the enlargement, so it lives in enlarged units and is not itself scaled.
Both engines pass their own. Forget the upscale and the boxes are wrong by exactly 100% while still
being perfectly plausible rectangles; get the padding order wrong and they are quietly out by half
the margin. The unit test on the arithmetic cannot catch a caller that simply omits an argument, so
there is a second test that reads one frame at 1x and at 2x and requires the words to land in the
same place — which covers both transforms at once, because the pad is 8 at 1x and 16 at 2x. It has
been mutation-checked. Any new engine returning geometry owes the same mapping.

**Pad the crop, and pad it last.** A glyph touching the edge of the image has no background left for
layout analysis to measure it against, and a hand-drawn capture region guarantees exactly that — it
is why a box drawn tightly around the words reads worse than one drawn a little wide, which is not
advice anyone should have to be given. The margin goes on **after** the auto-invert, when the image
is dark-on-light whichever way it started, so white is continuous with the background. Padding
before the invert draws a bright frame around the text and is worse than not padding at all.

**Contrast is stretched on percentiles, never on min and max.** One specular highlight, or one
sliver of a white UI border clipped into a corner, pins an end of the range at its limit and the
rescale becomes a near no-op on the text — which is the part that needed it. Real frames nearly
always contain such a pixel and `test-frames/` almost never does, which is exactly why this survived
so long. Two percent trimmed each end, and the result is **clamped**: the trimmed outliers are
outside the chosen range by construction, so without the clamp the brightest pixel in the frame
overflows to black.

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

**Free providers meter per model, so a 429 means "next model", never "next lane".** Gemini allows
`gemini-3.1-flash-lite` 500 requests a day and the flash models twenty *each*; Groq gives every one
of its three models its own thousand. The router used to put a whole provider in cooldown on the
first 429 from any single model, which reported "all providers exhausted" to a user who still had
498 Gemini requests and two completely untouched Groq models. Only `Fatal` — 401/403, a bad key —
ends a lane immediately, because that is the one failure that really is about the provider rather
than the model. A lane cools down only when *every* model it tried was rate limited. The same logic
puts the biggest daily allowance first in each lane: **order within a lane is the quota policy**,
and it is not the same as quality order.

**`maxOutputTokens` is what a provider *reserves*, and on Groq that is a throughput setting.** Groq
admits a request against `prompt_tokens + max_tokens` — not against what the answer costs — against
a ceiling of **8,000 tokens a minute** for the gpt-oss models and 12,000 for llama. So the lane-wide
4096, set in good faith to stop reasoning models returning empty completions, reserved more than
half of one minute's allowance on every line: the *second* line inside any minute was refused, all
three models were refused in turn, the lane went into a sixty-second cooldown, and the log said
`groq in cooldown, skipping` for the rest of the session. Groq was never slow — 0.09 to 0.74 s
measured — and never refusing on principle. The comment in `models.json` that said "tokens cost
nothing that matters here, the workload is request-limited" was true of Gemini and false of Groq,
and nobody checked. Budgets are per model now, and `reasoningEffort: "low"` is what makes a small
one safe: gpt-oss spends ~360 tokens thinking about a subtitle at default effort and 5 at low.
**Those two numbers have to move together**, which is why they live on the same entry.

**A models.json entry is a string or an object, and both are read forever.** The string form is what
every installation already has. `ProviderConfig.Models` stays a computed `string[]` so every reader
is unchanged; `ModelFor(id)` is how a provider finds the overrides. A malformed entry degrades to a
reported problem rather than refusing to load — this file is meant to be hand-edited, and an
unstartable app is a poor answer to a stray comma.

**`reasoning_effort` is per model because one lane needs two request shapes.**
`llama-3.3-70b-versatile` answers 400 to the very parameter its two gpt-oss lane-mates require. The
parameter is omitted from the JSON entirely when unset — a `null` is still the parameter.

**A rate-limit cooldown honours the provider's own `retry-after`, clamped.** Groq asks for about
four seconds when it is a per-minute limit and over an hour when it is a daily one; a fixed minute
was wrong in both directions. Clamped into `[MinimumCooldown, RateLimitCooldown]`: the floor stops a
provider answering "0" from turning the fallback walk into a spin, and the ceiling stops an
unverified header taking a lane out of the whole session. When several models refuse, the *soonest*
of them decides — one model out of tokens for the day beside one out for four seconds is the normal
Groq mixture.

**One provider is one lane per key, and the expansion happens in `ProviderFactory`, not the
router.** Slot 1 keeps the plain secret name — `GeminiApiKey`, not `GeminiApiKey#1` — because every
existing installation has a key filed under it and a rename would silently log all of them out with
the English fallback as the only symptom. Every slot is built whether or not it holds a key, so a
key pasted into Settings works without a restart; `AnnouncesMissingKey` is false for slots 2 and 3
so their emptiness, which is the normal state, stays out of the router's "No API key for:" line.
Lane names reach the quota ledger and `translations.provider` as ordinary data — the cache key does
not include the provider, so nothing becomes unreachable.

**`RouterOptions.TotalBudget` exists because everything falls through now.** With seven models
across two free lanes, a run of timeouts could otherwise spend a minute before the overlay said
anything. Most failures are instant — a 404 or a 429 costs milliseconds — so the cap only bites on
a genuinely slow provider. Any new failure kind that continues the walk has to stay inside it.

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

**The app must never mistake its own windows for the thing it is watching.** Every fallback for
"which window is the user looking at" went through `GetForegroundWindow`, and this app has windows
of its own — Settings, the wizard, the picker, and the toolbar the user *clicks to change modes*.
Bring any of them forward and the capture region was resolved against it: a few hundred pixels of
our own interface, on whichever monitor that window happened to be. For the `general` profile, whose
region is stored against the whole screen, that is the entire frame landing on the wrong display.
One cause, three symptoms that look unrelated — a region that reads nothing, a detector fed two
different pictures alternately, and «رُسمت منطقة الالتقاط هذه على نافذة بمقاس مختلف» repeating
forever because the client size flipped between two values. `GameWindowLocator.ForegroundNotOurs`
filters on the process id and falls back to the topmost window that is not ours, never to null:
null means "no game window", which asks "is the game running?" of somebody whose game is plainly
running and merely behind our Settings window.

**A "warn once per X" guard needs a set, not a slot.** `_layoutWarnedFor` held the last key only, so
a value alternating A, B, A, B missed on every comparison and warned on every poll — "once per
layout" that fires 120 times a minute. Anything alternating produces this, and something always
alternates.

**Every mode cycle goes through `WatchModes.After`.** The toolbar flipped between two modes while
Settings offered three, so `Auto` was unreachable from the toolbar — which also drew an `Auto` icon
for a state it could not produce, advertising a mode and then refusing to select it. Both surfaces
now read one list, and a test asserts that list covers every value of the enum, because adding a
fourth mode and forgetting the toolbar is the same defect wearing a new coat.

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

**Tashkeel is stripped on the way OUT, and the cache keeps what the provider actually said.**
`TranslationPipeline.Present` runs on both return sites — the live one and the cache hit — and
nowhere else. That placement is the whole design: a display preference must not be baked into a row
that outlives it, and stripping before the cache write would mean turning the switch on only
affected sentences the player had not reached yet. It also has to stay *above* the overlay:
`OverlayWindow.Render` is shared by «جارٍ الترجمة» and «تعذّرت الترجمة», both of which carry
deliberate diacritics, and `ArabicRenderTestWindow` has a case that exists to prove they render.
The prompt asks for plain Arabic too — that is not redundancy, it stops us paying output tokens for
marks we then delete, and on Groq output tokens are the metered resource.

**Auto-watch waits for the frame to stop changing before it translates.** `FrameSettleGate` is the
gate; FFXIV reveals dialogue a character at a time and every partial line used to count as a new
one, so one sentence cost four requests to show four progressively less wrong versions of itself.
The asymmetry that makes it safe is the one `StableOcrReader` was written around: another poll is a
BitBlt and a 64×24 thumbnail, another translation is a metered request, so the gate spends polls to
avoid requests and never the reverse. `SettleOptions.Cap` is not optional — without it a game whose
subtitles animate continuously settles never and translates never. It compares *signatures*, not
OCR text, which is what keeps deciding-to-wait free. `tools/Replay` deliberately does **not** apply
it: its corpus is a set of distinct frames rather than a time series off one screen, so a settle
gate there would skip most of the frames the harness exists to push through.

**Auto-watch pacing is per mode, and the two modes disagree about every number.** `WatchPacing.For`
is the only place they are written down. Dialogue waits for text to finish appearing because it
types itself out and then stays; video cannot afford to, because a subtitle lives three seconds and
leaves whether or not anything was translated. Measured on a moving picture, the old settings put
the Arabic on screen **4.6 seconds** after the line appeared. The cause was not the poll rate —
raising it from 2 fps to 6 buys 334 ms of that 4,625 and triples the CPU. It is that
`MaxDifferingCells = 2` out of 1536 can never be satisfied by full-motion video, so the gate never
settles and every release comes from the cap. **Tune the cap, not the poll rate.**

**That cap is now at its floor, so the advice above has expired — read the arithmetic before tuning
again.** Over video the cap is not a deadline, it is a pure wait: the stillness test cannot pass, so
nothing is bought by it except not catching a caption mid fade-in, and a subtitle fade is one to
three frames. It is `MinimumSettleCap` (400 ms) — the value this file already calls the point below
which a cap "stops being a deadline and starts being a guarantee of translating mid-change".
`MinimumInterval` came down with it, 1500 ms to 1000 ms, because 1500 was longer than a subtitle is
allowed to be *short*: Netflix's floor is 20 frames, five sixths of a second, so a conformant track
could legally change faster than we were willing to look — and a caption arriving inside the floor
was not delayed, it was **dropped**, because the poll is skipped and the line is gone before the
next one asks. What remains in the budget is about 125 ms of average detection lag, the OCR, and the
provider round trip, which is the largest item by far. The next honest latency win is the poll rate
after all, or a faster lane. It is no longer the cap.

**A session cap must be measured from switch-on.** The 90-second idle expiry cannot do this job and
never could: `lastChange` resets on any movement, so on a video — or in a game with animation
anywhere in the capture region — it never fires at all. The guard the readmes promised for three
releases was already inoperative in the workload it was written for. Both caps are counted, time
*and* requests, because four minutes of cutscene is a dozen translations and four minutes of film is
eighty; neither unit alone means anything.

**A per-poll failure must not end the run.** The try/catch used to wrap the entire `while`, so one
OCR throw on an unfamiliar font killed auto-watch permanently — and `StopAutoWatch` reported to the
Settings status line, which a player in a fullscreen game never sees. It stopped, silently, and the
only way to learn why was to open Settings. Anything auto-watch does that the user did not ask for
goes to the **overlay**: `StopAutoWatch(..., onOverlay: true)`, and `OverlayWindow.Notice` for
anything that has to survive the translations arriving after it.

**Silence is the right answer to a poll that found nothing.** A hotkey press is a question and gets
an answer; a poll is one of dozens a minute, and the gap between two subtitles is an empty region by
definition. Answering those with «لا نصّ في منطقة الالتقاط. هل يظهر صندوق حوار على الشاشة فعلاً؟»
flashed an error, asking about a dialogue box, over a film, between every line. `ProcessAsync` takes
`fromAutoWatch` for exactly this, and a long *run* of empty reads is the only case worth reporting —
that one means the region, not the moment.

**The adaptive pacing may only tighten.** `WatchSession` times the gaps between lines and cuts the
settle deadline to a third of the observed cadence, floored at `MinimumSettleCap`. It can never
exceed the mode's own cap: a human chose that as the longest defensible wait, and no measurement
should be able to argue the app into being lazier than a number somebody reasoned about. It is a
median over eight samples, kept in memory, never persisted and never sent — and it is surfaced in
Diagnostics because an adaptation nobody can see is indistinguishable from a bug.

**`ContentRhythm` measures persistence in SECONDS, against a threshold taken from what subtitles
are allowed to do.** It was four consecutive polls, justified in a comment as "longer than any
caption holds still" — a claim about the world that nobody had checked against the world. The
subtitling houses publish it: the ESIST Code and Netflix both cap a caption at **seven seconds**,
Karamitroglou at six for two lines, against a floor near one. `LongerThanAnyCaption` is eight, and
that margin is the entire justification — a line still up after it is not a caption, because nothing
that leaves of its own accord stays that long. The unit was the deeper error. A poll is not a fixed
quantity of evidence: over a *static* box the gate answers `Unchanged` and a run grows once per
poll, while over *moving picture* it answers `Settling`, which must not vote, so the run can only
grow on a read — once per settle cap, six times slower. Two quantities that agree on the machine you
tested and disagree on the user's, which is this project's recurring defect and always has the same
fix: state the unit. Seconds also make the signal recency-correct for free, because an empty read
resets the clock — "still for eight seconds" already means "has not left in eight seconds", which is
what lets persistence answer first without a stale verdict outliving its evidence.

**Any gate counting OCR reads has to fit in the read budget, and the budget is small.**
`RhythmSample.HasText` is filled in on one verdict only — `Ready` — so a read costs a whole settle
cap and a window holds `Window / (PollsPerSecond × SettleCap)` of them: **five** at the dialogue
timings, nine at the video ones. `MinimumReads` was six. Since `Auto` starts on the dialogue timings
(Unknown resolves to Dialogue), the gate gating every route out of them could never open, and over a
film the classifier sat at `Unknown` with every signal it had screaming video — the feature
inoperative in exactly the workload it exists for. `TheReadBudgetAtDialoguePacingIsEnoughToDecideWith`
asserts the arithmetic directly, because no behavioural test can catch this: the tests all fed a
read on every poll, which the poll loop does on no verdict at all. **When a test's input stream is
richer than production's, every threshold tuned against it is tuned against fiction.**

**The weakest signal needs the most evidence, or it wins by being fastest.** `MotionFraction` is the
tie-breaker of last resort and it accumulates on *every* poll, while persistence needs seconds to
mature — so left ungated it reaches its threshold first and decides everything. An animated
background behind a static dialogue box hits "moving on every poll" in about two seconds, four
seconds before the line behind it is old enough to speak for itself. It therefore waits for a full
window, which at the dialogue rate is fifteen seconds and comfortably longer than
`LongerThanAnyCaption`. Ordering the checks in the source is not enough when the signals mature at
different rates; the gate has to say so.

**Set the bundled font whenever Arabic is on screen.** `Fonts.Arabic` is bundled for the reason
`NOTICE` gives: a Windows machine with no Arabic font installed renders every Arabic string as
empty boxes. Relying on OS fallback works on macOS and hides the problem — the Arabic tab headers
were tofu the first time the interface was switched, and that was on a Mac that *does* have Arabic
fonts.

**Icons are geometry, never glyphs.** `Icons` holds SVG path data compiled into the assembly. The
bundled Arabic font has no Latin and no symbols, so any character-based icon already depends on OS
fallback — the exact dependency the font was bundled to remove — and one unresolvable codepoint has
already poisoned fallback for a whole window once. A toolbar has no text on it, so if its glyphs
fail there is nothing left. Nothing on the machine can change what a `Path` looks like.
`--toolbar-test` renders the real strip to a PNG, because a typo in a path string is an exception
thrown while the window is built, on a user's machine, and no compiler or unit test here can catch
it: the parser is Avalonia's and the test project does not reference Avalonia.

**A bilingual tooltip is two TextBlocks, not one string with a separator.** `BilingualTip` shows
Arabic and English at once, whichever language the interface is in, for the reason the
`Language · اللغة` control exists — and more strongly, because a toolbar button is a shape and
nothing else. One control holding "Translate now · ترجم الآن" would have to resolve half its
characters through OS fallback; two controls let the Arabic line use the bundled font and the Latin
line the system default, and no separator glyph is needed because they are on different lines. The
interface language leads; both are always present.

**Every floating window is a `FloatingWindow`, and they differ only in which bits they want.**
`OverlayStyleOptions` names the four: click-through, no-activate, exclude-from-capture, and topmost
which is not optional. **The bits are set AND cleared** — the old code OR'd four constants in and
returned, which is idempotent only while nobody ever wants one back off, and the capture frame wants
`WS_EX_TRANSPARENT` off while it is being dragged and on the instant that finishes.

**A fully transparent window is invisible to the mouse, not just to the eye.** The compositor
hit-tests against alpha, so a zero-alpha region passes clicks through whatever the extended styles
say. That is *useful* — it is how the capture frame stays out of the way — but anything that has to
be grabbed paints `FloatingWindow.BarelyThere`, one part in 255 of black.

**Nothing drags itself with `BeginMoveDrag`.** That helper asks the window manager to run a modal
move loop, which is the kind of thing a `WS_EX_NOACTIVATE` window is least likely to be granted, and
it cannot be rehearsed off Windows. The toolbar and the frame both do the arithmetic themselves:
`PointToScreen` at grab, `PointToScreen` on move, anchor to the grab rather than to the previous
frame so rounding cannot accumulate. Exact at any DPI and identical on both platforms.

**The visible capture frame is only possible because of the exclusion flag.** A border drawn around
the rectangle being OCR'd is normally a border inside the pixels about to be OCR'd, and every
general fix is unpleasant. `WDA_EXCLUDEFROMCAPTURE` means it physically cannot appear in our BitBlt,
and the border is drawn *outside* the rect as well — window inflated, stored region deflated back —
so it would be on the wrong side of the boundary even without it. Both, deliberately.

**The frame saves on release, with no confirm step.** Dragging it *is* the edit, and there is
nothing to confirm because the thing being edited is showing its own result. That is also what keeps
it off the keyboard, which is the one behaviour on this platform nobody here can test.

**Sizes come out of a UI toolkit in device-independent pixels; screen coordinates are physical.**
`OverlayPlacement.Within` has an overload that takes the scale and does the conversion, and it lives
in Core because that is where it can be tested — it used to live in `App.axaml.cs` where nothing
could, and it was wrong. At 100% the two units are the same number, which is the machine this gets
written on; at 125% a 900-DIP panel is 1125 pixels and "flush right" hangs a quarter of it off the
screen. Take the scale from the monitor the window is going **to**, not from `RenderScaling`, which
is the monitor it currently happens to be on.

**A startup failure goes on a normal window, and the black box opens before anything else.**
Startup errors used to be shown on the overlay, whose defining properties — transparent,
click-through, no taskbar entry, no Alt-Tab — turn an error shown there into an error shown to
nobody. "Nothing opens" from a real user was the app running with its explanation on screen.
`StartupFailureWindow` is everything the overlay refuses to be, in both languages at once, with the
exception selectable. Underneath it, `StartupLog` writes from the first line of `Main`: absent file
means the process never ran, a truncated file means something killed it while loading, and the
opening census (assembly count, OCR natives present or MISSING) turns the commonest cause — an
antivirus quietly unpacking the folder — into a one-line diagnosis. Two details are load-bearing:
console-only flags (`--version`, `--licence`) return **before** `Begin`, or a support run would
overwrite the failing run's evidence with a boring success; and the Avalonia bootstrap lives in a
separate `[MethodImpl(NoInlining)]` method, because assemblies load when the method referencing
them is JIT-compiled — inlined, a quarantined Avalonia DLL would throw outside Main's own try.

**The health check is split gather/judge, and probes prove things by doing them.** `HealthCheck.Run`
in Core takes a record of plain facts and returns sentences — every rule testable with no Windows
and no game. The App gathers: keys are verified with one real translation each (`KeyProbe`, same as
the Test button), and OCR is verified by OCR'ing a rendered frame, because the only thing that
proves the natives loaded is the natives running. Severity is a coloured **word**, never a ✓/✗
glyph — the bundled Arabic font has no symbols, and one unresolvable codepoint has already poisoned
fallback for a whole window. The Arabic-Windows-English-interface advice is written in Arabic
inside an otherwise-English report, deliberately: it is addressed to someone who reads Arabic, and
delivering it in English is the bug it reports. Findings sort worst-first, and lane lists are
`Machine` so the mirrored layout cannot reverse the cost policy.

**Region proposals are a bonus layer the picker must never depend on.** The full-frame OCR pass
runs at 1x with PSM 3 (automatic layout) — the per-line engine's PSM 6 ("one uniform block") merges
the hotbar into the dialogue when shown a whole screen — cropped to the game window, because the
classifier's "bottom third" means the bottom third of the *game*, not of a two-monitor desktop.
Candidates arrive asynchronously; the picker is complete without them. The outlines are
`IsHitTestVisible = false` and adoption is a geometric test on a too-small release, so drawing a
fresh box straight through a suggestion works untouched — the suggestion catches exactly the click
that was previously an error ("too small"). `FromScreenPixels` must stay the exact inverse of
`ToScreenPixels`: a proposal drawn with different letterbox arithmetic than the save uses would
highlight one rectangle and store another.

**Nothing lives on one surface alone.** Any *action* or *two-state toggle* must exist on both the
toolbar and in Settings — the toolbar for someone inside a fullscreen game, Settings for someone
who never switched the toolbar on. Snip was toolbar-and-hotkey only, which made it the one feature
findable solely by hovering an unlabelled shape; the dialect switch and the recording toggle were
Settings-only. All three are on both now. The exception is deliberate and narrow: controls with
many values or free text — the profile list, the sliders, the key boxes, hotkey bindings — stay in
Settings, because a toolbar cannot hold them and pretending otherwise would mean a worse version of
both.

**Move mode is one toggle for two windows, and it is the only state where either takes a click.**
The capture outline and the translation panel are separate windows but a single intention — "let me
move the thing that is in my way" — so one control unlocks both, borders both so the state is
visible, and re-pins both. Turning it off must restore click-through on both: a mode the app can be
left in by accident is a mode that silently eats every click aimed at the game. The outline is
forced visible while unlocked, because being asked to drag something invisible is not an
interaction, and it returns to hidden afterwards so the mode leaves no trace.

**A dragged panel is stored as fractions, and the round trip must not creep.** `OverlayPlacement`
has `Within` and `FractionsWithin` as inverses; a drop is converted straight back into the same two
fractions the sliders write, so dragging and the sliders are one setting seen twice. A fraction
cannot survive a trip through an integer pixel unchanged — freeX is about a thousand pixels, so the
recovered value differs in the fourth decimal — and that is fine. What is tested is **idempotence in
pixels**: place, read back, place again lands on the same pixel. Without that, a panel dragged to a
corner walks across the screen over a few dozen sessions.

**`ContentRhythm` weighs text signals above pixel signals, and that ordering is the whole design.**
Motion is the obvious way to tell a film from a dialogue box and it is the one that lies: weather,
an idling character or a scrolling sky behind a text panel changes every frame while the words sit
still, so motion alone calls FFXIV a film. Persistence (how many consecutive polls one line
survives) and emptiness (whether the region goes blank between lines) are both measured on the OCR
text, are already paid for by the pipeline, and see through exactly that animation. Two rules keep
it honest: a run is extended by "frame changed but the TEXT came back the same" — which is what the
repeat gate reports and what defeats the animated-background case — and persistence is measured
over the recent **half** of the window, because a stale run left by a dialogue box that has just
ended would otherwise outvote every caption observed since. Switching costs a 12-second dwell, so
alternating content settles on whichever it mostly is rather than chasing every transition.

**Safe mode neither reads nor writes, and the second half is the one that would be forgotten.**
`AppSettings.SafeMode` short-circuits `Load` to defaults and `Save` to a no-op. Two dozen call
sites save on every checkbox click; any one of them would otherwise overwrite the user's real
configuration with the defaults they were only borrowing. Keys are unaffected — they live in the
secret store, so safe mode still translates. The flag is set in `Program.Main` before ANY settings
read, and the wizard is suppressed in safe mode because `HasCompletedFirstRun` could not stick.

**The tray is the exit of last resort, and its icon is rendered, not shipped.** Every window this
app floats is deliberately hard to reach, so the tray carries the way back in and the way out —
which is what retired `0-force-stop.bat`, a `taskkill` script beside an unsigned exe. The icon is
drawn at runtime from `Icons` geometry into a `WindowIcon`: no asset to quarantine, no font to
substitute, nothing on the machine that can change what it looks like.

**The wizard consumes detections; it never asks what the app can find out.** Language preselected
from the Windows locale, the running game named from the open-window list, exclusive fullscreen
flagged at the moment of choosing rather than diagnosed after the first black frame — and the key
step tests with one real request and **saves on success**, because a Test button that validates
without persisting is the exact lie behind the v0.5.0 first-day failure. Everything is skippable,
`HasCompletedFirstRun` is written once, and nothing ever nags. `--wizard-test` renders all four
steps in both languages; the wizard is the one screen every new user sees and no developer does.

**The Advanced expander in Settings and the toolbar's expander are ONE concept.** Whichever grew
first owns it — the toolbar did — and Settings consumes it. Behind it go controls that exist for
exactly one rare situation (the no-limit switch, the toolbar focus escape hatch), never controls
the readmes give paths to. Collapsed on every open: a sticky-open advanced section stops being
advanced.

**The diagnostic report goes to the clipboard first and a Desktop file second.** Support for this
app happens in Facebook comments, not issue trackers, so paste-into-a-message is the export that
actually gets delivered; the file is the bonus. Severity markers in the report are ASCII (`[OK]`
`[!]` `[X]`) because the text lives on clipboards and in chat apps, outside every font decision
this project controls.

**The picker tests the still it is already holding, never a fresh capture.** `RegionPickerWindow` is
full-screen, topmost and has no capture exclusion, so re-photographing the screen to answer "what
does the OCR read here" photographs the picker: its instruction panel, and the blue selection box
drawn across the very text being tested. On one monitor the scale works out and it looks right. On
two, the still spans the desktop while the window covers one screen, and the preview reported on
entirely different pixels. The callback takes a `Frame`, not a rectangle, so this cannot come back.

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

**v0.5.0 was verified on Windows too**, against a real populated database: the v2→v3 migration ran,
the cache survived, translation worked, keys persisted across a restart, and the overlay position
sliders moved the panel over a running game.

**Multi-monitor and display scaling are verified now**, by a long session across several games on a
two-screen setup at different DPIs. That retires the two oldest entries on this list.

**v0.5.2's provider work was verified against live keys**, not reasoned about: thirteen frames
through `tools/Replay --provider groq`, one real 429 on `gpt-oss-120b` mid-run, fall-through to
`gpt-oss-20b`, thirteen translations, no cooldown and no English fallback. The refusal that used to
kill the lane now costs one model.

Still unverified: click-through, cache hit rate over a real session, and the settle gate against a
real game — it is tested against synthetic reveals, and FFXIV's actual reveal speed against a 2 fps
poll is arithmetic nobody has measured.

### What a day of real use cost, and what it taught

v0.5.0 shipped after four rounds of chasing the wrong cause, and the sequence is worth keeping
because every step was a reasonable-looking mistake.

The symptom was always the same sentence on the overlay: «تعذّرت الترجمة». The first cause was real
— Gemini's whole model list had died. So was the second: Groq's had too, in the same week. The
third was mine, introduced by fixing the first two, because the replacement models reason before
answering and the token budget and timeout were both sized for models that do not. **The fourth was
the actual one, and it had been in the very first log line I was sent**: `No API key for: gemini,
groq` — the app had no key at all, because the Test button next to the key box validated the text
without saving it while the Save button sat off-screen below four provider sections.

Three lessons, in order of how much they cost:

- **Read the log literally before theorising.** "No API key" meant no API key. I read it as a
  symptom of the provider problem I had just been fixing, twice.
- **A positive confirmation next to a control is a promise about that control.** A Test button that
  says «يعمل» and does not persist is worse than no button, because it converts a user's uncertainty
  into false confidence. Any button that validates something should also commit it.
- **Verify against the real thing before changing config in response to a report.** Removing
  `llama-3.3-70b-versatile` on the strength of a deprecation notice — against the evidence of a
  probe that had just passed on it — broke a working lane. `tools/Replay` with real keys settles in
  one minute what documentation gets wrong; both providers' own docs were wrong on the same day.

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

**Found by a day of real play, and the most expensive assumption in the file (v0.5.2):**

- **"Tokens cost nothing that matters here, the workload is request-limited."** That sentence was
  written in `models.json` while fixing the empty-completion disaster, it was true of Gemini, and it
  was never checked against Groq. Groq admits a request against `prompt_tokens + max_tokens` — what
  you reserve, not what you spend — against 8,000 tokens a *minute*. So `maxOutputTokens: 4096`
  meant one line a minute, and the second one 429'd. Three models refused in turn, the lane went
  into a sixty-second cooldown, and every line after that logged `groq in cooldown, skipping`. The
  report was "groq seems not to be utilized" and the obvious readings were all wrong: not slow (it
  answers in under a second), not refusing, not deprecated. **A rate limit names its own unit, and
  the unit was in the error text the whole time** — `on tokens per minute (TPM): Limit 8000, Used
  7629, Requested 508`. Read the refusal before theorising about the provider.
- The same 4096 was doing this on every model of the lane at once, including the two that needed
  perhaps 200 tokens. Per-lane was the wrong granularity and there was no evidence it was the right
  one; it was simply where the field already existed.
- **Automatic mode was paying four times over for one sentence.** FFXIV types dialogue out a
  character at a time, and every intermediate state is a different frame, a different OCR string, a
  different cache key and a new request. The user saw it as "it translates the same frame more than
  once till it adjusts". `StableOcrReader` had been written for exactly this in the first week and
  was never wired to anything, so the codebase had a fix for a bug it was still shipping.
- **The models were adding tashkeel unevenly** — same conversation, half vowelled, half not,
  depending which model in the fallback chain answered which line. Nobody had asked for it either
  way; it was simply never specified, and unspecified means the model decides differently each time.

**Caught in review before v0.5.2 shipped, and worth keeping because each was introduced by a fix:**

- **The settle gate committed a frame as translated before the caller could refuse it.** `Offer`
  returning `Ready` is not an opinion, it is a side effect — it records that frame as the one now on
  the overlay. The `_busy` check sat *after* it, so a frame arriving while the manual hotkey was
  mid-translation was dropped and simultaneously remembered as done, and that line was then never
  translated at all. The old code had the same ordering and got away with it: a typewriter reveal
  produced four or five candidate frames, so losing one cost nothing. Making it exactly one per line
  is precisely what turned a harmless drop into a lost line.
- **The total-time ceiling stopped being a ceiling.** Every lane is guaranteed one attempt even out
  of budget — added in v0.5.1 so a slow Gemini could not starve Groq. With one provider becoming
  three lanes, six configured lanes against a stalled provider measured **35 seconds for one line**
  against a documented ceiling of twenty. The fix is the distinction the guarantee was always really
  about: a different *provider* may answer, a different *key on the same endpoint* will not.
- **The new config parser threw on the exact edit its own comment invited.** `"maxOutputTokens":
  "700"` — a quoted number, beside the quoted `"low"` that belongs next to it — took `GetInt32` on a
  string token, out through a `Load` that catches nothing, and the app came up as a bare overlay
  reading "Startup failed" with no Settings window and so no way to reach the `Problems()` list that
  would have named the field. A `null` in `models[]` did worse: it bypassed the converter entirely
  and made `Problems()` itself throw. **When a file is documented as hand-editable, every reader of
  it has to degrade, not just the ones you thought of.**
- **A truncated answer was cached forever.** `finish_reason: "length"` was only consulted when the
  answer was *empty*; a sentence cut off mid-word was returned as a success and written to the
  cache, where every later capture of that line replayed the fragment permanently. Unreachable while
  the lane reserved 4096 tokens — cutting the reservation is what made it live, which is the general
  shape worth remembering: a change that makes something cheaper usually makes some other failure
  reachable for the first time.
- **The key-slot count hid a live key.** Settings counted how many slots held a key instead of
  taking the highest, so clearing box 1 of a two-key setup left key 2 authenticating every line with
  no box to see or clear it — while the lane summary on the same screen listed it. Emptying the
  visible boxes then reported "All keys cleared. Nothing will be translated", which was false.

**Found by one player, one evening, one game nobody here had tried (v0.5.3):**

Wuthering Waves, and a message that was mostly praise with five defects buried in it. Every one had
been in the code for weeks; none had been found by testing, because they only appear to someone
using the app the way it was meant to be used rather than the way it was written.

- **«كلام مش عارف يترجمه فا يقف خالص» — it stops dead on text it cannot translate.** One frame
  throwing ended the whole session, because the try/catch wrapped the loop rather than the frame.
  Exactly the kind of bug that survives every test: no test ever throws in the middle of a poll.
- **«كل ما يحصل مشكله اخش علي الاعدادات نفسها» — every problem means going into Settings.** Not a
  feature request. It is the *consequence* of the one above: the app had one reporting channel for
  auto-watch and it was the Settings status line. The user was describing our error handling from
  the outside without knowing it.
- **«البرنامج بيمنع تصوير اي برنامج زي Nvidia app» — the overlay blocks screen recording.** True,
  deliberate, correct, and never explained anywhere. `WDA_EXCLUDEFROMCAPTURE` is there so the app
  cannot read its own Arabic back — but wanting to record what you are playing, with the translation
  in it, is an obvious thing to want and the app simply refused without saying why.
- **«ممكن ما يشوفش الكلام لازم اعيد تحديد المكان» — sometimes it cannot see the text and the region
  has to be redrawn.** The app knew: it was getting empty reads poll after poll. It said "no text in
  the capture region" once per poll and never once said "the region is probably wrong now".
- **«يتحكم في عدد ثواني الترجمه بدل انو تلقائي» — let the user set the seconds.** Both auto-watch
  numbers existed and neither had any UI at all; they were hand-edited JSON. Nobody had noticed
  because nobody who knew they existed needed the control.

The pattern worth keeping: **four of the five were failures of REPORTING, not of behaviour.** The
app already knew it had stopped, knew the region was dead, knew it was hiding from the recorder. It
had nowhere to say any of it that the person in a fullscreen game would ever look.

**Found while building the window features (v0.6.0), all of them latent for months:**

- **"Test what the OCR reads here" read the picker's own rendering.** `TestRegionAsync` re-captured
  the screen live while the full-screen picker was on top of it, so the preview contained the HUD
  panel and the blue selection rectangle sitting over the text under test. On one monitor the scale
  works out 1:1 and the answer looks plausible, which is why it shipped; on two it reported on a
  different screen entirely. The fix is a signature change — the callback takes the crop rather than
  the rectangle — because a function handed pixels cannot go and fetch different ones.
- **DIPs and physical pixels were mixed in overlay placement.** Harmless at 100% and only at 100%.
  Worth recording as a category rather than a bug: two units that are equal on the development
  machine and unequal on the user's is the shape of defect this project keeps producing, and the fix
  is always to state the unit in the signature and move the conversion somewhere testable.
- **A manual press left no repeat reference, so the poll after it paid for OCR jitter.** Caught by a
  test that was asserting the wrong thing, which turned out to be the design being wrong: I had
  folded "may be suppressed" and "becomes the reference" into one flag because the three callers
  happened to line up. They do not.
- **Padding changed what Tesseract segments at 1x**, enough to produce the apostrophe in "Y'shtola"
  as two separate word tokens — which made a `ToDictionary` in an existing test throw on the
  duplicate key. The test's invariant was fine; keying by word text was the assumption.

**Caught before v0.7.0 shipped, and the Auto detector did not work at all. Three defects, none
findable by the tests that were written for it:**

- **`MinimumReads = 6` was one more read than the dialogue timings can produce, so Auto could never
  reach Video.** Pure arithmetic: `Window / (PollsPerSecond × SettleCap)` is 5 at those timings, and
  `Auto` starts there because `Unknown` resolves to `Dialogue`. Over a film the classifier sat at
  `Unknown` with `EmptyFraction` and `MotionFraction` both saturated, running patient timings on
  moving picture — the precise 4.6-second lateness the feature exists to delete, in the feature
  built to delete it. Two constants chosen independently in different files, neither wrong alone.
- **Persistence was counted in polls, and a poll is not a fixed amount of evidence.** Over a static
  box the gate answers `Unchanged` and the run grows per poll; over video it answers `Settling`,
  which correctly does not vote, so the run grows only per read — six times slower. The same
  dialogue box therefore scored differently depending on what was happening behind it. Same shape as
  the DIP-versus-pixel bug, and the same fix: state the unit. It is seconds now.
- **The threshold's justification was invented.** The comment said four polls was "longer than any
  caption holds still". The subtitling houses publish the answer and it is **seven seconds** — so
  the threshold was inside the life of an ordinary two-line caption rather than beyond it, and one
  long subtitle was enough to call a film dialogue.
- **And fixing the first one exposed a fourth**: with the read gate finally passable, `MotionFraction`
  — the weakest signal, the tie-breaker of last resort — started deciding everything, because it
  accumulates every poll while persistence needs seconds. It reached its threshold four seconds
  before the line behind it could speak, so an animated background behind a static dialogue box got
  called a film. Ordering checks in the source is not enough when signals mature at different rates.

**What made all four invisible: the tests fed a richer stream than production ever produces.** Every
helper handed the classifier an OCR read on *every poll*; the poll loop populates `HasText` on one
verdict in three. So the suite was exercising a machine with six times the evidence, in which the
read budget is ample, persistence matures instantly, and motion never gets a head start. Sixteen
passing tests, a design pass and a competitive review all ran on top of a defect that a single line
of arithmetic exposes. **A test whose input is more generous than production is not a weak test, it
is a test of a different program** — and the fix that generalises is the one now in
`TheReadBudgetAtDialoguePacingIsEnoughToDecideWith`: assert the relationship between the constants,
not just the behaviour they produce.

**Found by adding the first tests that touch settings statics (v0.7.1):** `AppSettings.SafeMode`
is a static — it has to be, it is set in `Program.Main` before any settings read — and xUnit runs
test *classes* in parallel, so while `SafeModeTests` held it true any other class calling
`AppSettings.Load` concurrently was short-circuited to defaults and saw a file it had just written
come back empty. Latent since safe mode shipped; it surfaced the moment new test classes changed the
scheduling enough to overlap the two, which is the worst way for it to appear because it looks
exactly like whatever change happens to be in flight. `SettingsStaticCollection` serialises every
class that touches it. **A static in production code is a shared fixture in the test suite whether
or not anybody wrote one.**

**Found by one evening of real use of v0.7.0, and the first one is the most embarrassing kind:**

- **The app could not tell itself apart from the thing it was watching.** Auto mode "does not work
  at all and keeps giving error that frame moved" turned out to be one bug with three faces. Every
  fallback for "which window is in front" used `GetForegroundWindow` with nothing excluding our own
  process — and the toolbar is *how you change modes*, so testing the feature was itself the thing
  that broke it. The region resolved against our own window: wrong pixels (the detector saw two
  alternating pictures and could conclude nothing), wrong size (the layout warning fired again on
  every new size), and on two monitors, the wrong screen entirely. Every individual piece was
  written correctly; nothing had ever asked whether the window in front might be ours.
- **The mode button offered a mode it could not select.** Three modes in Settings, a two-way flip on
  the toolbar, and an `Auto` icon in `ToolbarWindow` that no state could reach — the icon mapping had
  been written for three from the start. Worse, `--toolbar-test` only ever rendered the default
  mode, so the one icon reachable solely through the least-used branch was also the one the
  rehearsal never drew. It renders all three now.
- **`_layoutWarnedFor` was a single slot, so "once per layout" fired forever.** A value alternating
  A, B, A, B misses on every comparison. A set costs nothing and means what the comment said.
- **The video settle cap was the biggest item in the latency budget and bought nothing.** Over
  moving picture the stillness test cannot pass, so the cap is a pure wait — and at 800 ms it was
  larger than the OCR and comparable to the whole network round trip. It is 400 ms now, the
  documented floor. `MinimumInterval` was worse than slow: at 1500 ms it silently **dropped** any
  caption arriving sooner, and the published minimum subtitle duration is 833 ms.

**Latent, found by inspection and not yet hit in the wild:** the bundled `NotoSansArabic-Regular.ttf`
contains **no Latin at all** — not `A`, not `%`, and none of `✓ ✗ ⚠ → · ⏎`. Every Latin word in the
Arabic interface is already resolved by OS fallback. That works today, but the whole reason the font
is bundled is to not depend on fallback, and the Unicode-isolate incident proved a single
unresolvable codepoint can poison fallback for an entire window. Before adding any new non-Arabic
codepoint to an Arabic string, check it against the font.

### Still unverified

Click-through, and cache hit rate over a real session. Multi-monitor and display scaling above 100%
came off this list in v0.5.2, verified by a long session across several games on two screens.

**And, new in v0.6.0, one specific question worth budgeting the Windows laptop for: does a
`WS_EX_NOACTIVATE` Avalonia window receive pointer input?** At the Win32 level it should — it is
`WS_EX_TRANSPARENT`, not this, that makes a window invisible to the mouse — but whether Avalonia's
input stack delivers events to a window it never activates has not been tested on hardware, and the
toolbar and the adjustable capture frame both depend on it. Everything that *would* have depended on
activation is avoided deliberately: manual dragging instead of `BeginMoveDrag`, save-on-release
instead of Enter, no keyboard input anywhere on either window. If it does turn out to be dead to the
mouse, `AppSettings.ToolbarCanTakeFocus` switches `NOACTIVATE` off without a new build — which is
why that checkbox is worded as a symptom ("turn it on if the toolbar does not respond to clicks")
rather than as a mechanism.

The second unverified thing is smaller and has the same shape: whether a zero-alpha region of a
layered window really does pass clicks through on every Windows compositor configuration. It is
standard behaviour and the capture frame leans on it in its non-adjustable state, but the
belt-and-braces is that the extended style is set to match, so the two would have to fail together.

The settle gate is the newest thing here and the least measured. It is tested against synthetic
reveals, but whether FFXIV's actual reveal — against a 2 fps poll and `FrameSignature`'s six-cell
threshold, which the file itself calls provisional — produces two identical consecutive polls is
arithmetic nobody has run against a real capture. If it settles too eagerly the symptom is the old
one, a half-typed line translated; if too slowly, a poll of extra latency. Both are recoverable,
which is why it shipped, but neither is measured.

`test-frames/` holds **synthetic** frames drawn by `SyntheticFrames`. They exercise every stage of
the pipeline but say nothing about a real game's typeface, its translucency, or a moving 3D scene
behind the text. Replacing them with real captures is the highest-value contribution available —
see `CONTRIBUTING.md`, which asks for the same thing.
