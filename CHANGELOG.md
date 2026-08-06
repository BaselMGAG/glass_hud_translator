# Changelog

Notable changes. Started once the app was working end to end, so everything before the first entry
is "the thing being described in the README".

## v0.3.0 — 6 August 2026

### Added

- **The app tells you when a new version is out.** Once a day it asks GitHub's public releases
  endpoint whether a newer release exists; if there is one, Settings opens with a notice naming the
  exact file to download, the three steps to install it, and what happens to the setup already on
  the machine. There is no update server and no account — GitHub already hosts the answer next to
  the download.
- It **does not download or install anything by itself**, and that is deliberate. Windows will not
  let a running process overwrite its own libraries; the build is unsigned, so an app that fetches
  and runs an executable is the exact pattern antivirus heuristics flag; and a self-updater that
  goes wrong leaves an install that no longer starts, belonging to someone who cannot read the
  English error it fails with. The same reasoning already rules out low-level keyboard hooks.
- Settings → **Diagnostics** → **Updates** has the off switch and a **Check now** button that
  ignores the daily throttle. Checking is on by default, because the person this was built for is
  not going to be watching a repository for tags — and it is the only request the app makes that is
  not a translation, so what it sends (nothing but the request) is stated in both READMEs.
- `--version` and `--check-updates` on the command line, so "which version are you on and can you
  reach GitHub?" can be answered without opening a window or reading a label in a second language.

### Changed

- The repository moved to `github.com/basel2000de/glass_hud_translator`.

## v0.2.1 — 6 August 2026

Everything here came out of one round of review of the Arabic interface by a native speaker. None
of it was findable by the person who wrote it, and all of it is the difference between an app that
looks finished and one that looks like a build someone left running.

### Fixed

- **Three buttons read `حدد dialogue`.** The capture region names are stored English keys, and the
  button captions were built by gluing one onto a translated verb — so the most prominent controls
  on the Translating tab were half in each language. They now read حدّد منطقة الحوار / الترجمة /
  المهمة, and the region picker's own title and instructions are translated the same way. A test
  asserts every stored region name has an Arabic display name.
- **The API key field said `غير محدَّد`** — "not set", which reads as though the setting's value is
  unknown rather than as though you have not pasted a key yet. It is now `الصق المفتاح هنا`, which
  is an instruction rather than a status, and "paste your key here" in English for the same reason.
- **The dialect selector was labelled `المستوى اللغوي`.** Accurate, and a linguist's term for what
  is a choice between two named dialects. Now `أسلوب العربية`; the English label is "Style" rather
  than "Register" on the same grounds.
- **The explanatory notes were styled as though nobody had to read them** — 11px in mid-grey, the
  conventional "secondary, skip this" treatment. They are what tell a first-time user which
  providers are free and what a capture region is, and that user may be neither technical nor an
  English reader. Now 13px at 10.3:1 contrast instead of 6.3:1, with the extra line spacing Arabic
  needs. The window is wider so the paragraphs wrap less, and no taller, so it still fits a laptop.
- **The quota readout showed the provider lanes in reverse.** Latin runs inside a mirrored
  paragraph reorder, so `gemini · groq · openai · anthropic · ollama` rendered back to front — and
  that order is the cost policy, so the Arabic interface was telling the user the paid lane is tried
  first. Model lists, key URLs and platform diagnostics had the same problem. They are now
  left-to-right explicitly.
- The region picker had no bundled font set, so on a Windows install without an Arabic font its
  instructions — the only thing on that full-screen window — would have rendered as empty boxes.

## v0.2.0 — 6 August 2026

### Added

- **The interface itself is available in Arabic.** The app translates games for people who read
  Arabic more comfortably than English, and until now its own interface was English-only — so the
  person who most needs it was the one least able to set it up. Settings → Providers →
  **Language · اللغة**, labelled in both scripts so it is findable either way. The whole window
  mirrors right-to-left and uses the bundled Arabic font rather than assuming Windows has one.
  English remains the default.
- **OpenAI and Anthropic as provider options.** Four lanes now ship: Gemini and Groq on their free
  tiers, then OpenAI and Anthropic for people who already pay for one of them. Lane order is the
  cost policy — the router walks `data/models.json` top to bottom, so the free tiers answer first
  and a paid provider only ever sees the lines they could not.
- A lane with no API key entered is now skipped in silence rather than failing once per translated
  line. That is what makes shipping the paid lanes switched on cost nothing until someone
  deliberately pastes a key.
- Settings is organised into tabs — Providers, Translating, Overlay, Hotkeys, Diagnostics — with
  the status line docked outside them so it is readable from every tab.
- API key fields are generated from `data/models.json`, so an OpenAI-shaped provider added to that
  file gets a key box, a free/paid label and a "where to get one" line without a code change.
- Settings reports mistakes in `data/models.json` instead of failing to start, and lists which
  lanes will actually be tried, in order.
- `maxOutputTokens` per provider. It was hardcoded at 300, which is fine for one subtitle and
  truncates any model that spends output tokens on reasoning before it answers.
- Tagged releases: pushing `v*` publishes a permanent, public zip. Build artifacts expire after
  ninety days and need a GitHub account to download, which is the wrong way to hand someone an app.
- `CONTRIBUTING.md`, issue templates and an `.editorconfig`.

### Fixed

- **The router could throw.** A provider that let the four-second per-attempt cap surface as a bare
  `OperationCanceledException` escaped the router entirely — out of a class whose contract, and
  whose callers, depend on it never throwing. A stalled provider crashed instead of falling through
  to the next lane and, ultimately, to English on the overlay.
- **Arabic tab labels rendered as empty boxes.** The interface had been leaning on a system Arabic
  font, which macOS has and a plain Windows install may not — the same build would have shown
  nothing but boxes to the users it was built for. It now uses the bundled font, as the overlay
  already did.
- **A first run with no keys explained nothing.** Skipping an unconfigured lane in silence is right
  per line, but with every lane unconfigured the log said only "all providers exhausted". It now
  names the lanes that have no key.
- **The shipped OpenAI model IDs were wrong** — none of them were current chat models. Corrected
  against the provider's own list.

### Changed

- `ProviderFactory` builds lanes for both the app and `tools/Replay`, so the headless harness
  exercises the same wiring the overlay does.
- Documentation screenshots are generated from the running app (`--ui-shots`) rather than taken by
  hand, in both languages. The previous hand-taken one was stale within a day of the tabs landing.

## v0.1.0 — 6 August 2026

First public release: the app as described in the README, verified against Final Fantasy XIV on
Windows. Screen capture, OCR, the five global hotkeys, the overlay and the full translation round
trip all working at roughly a second a line, with Gemini and Groq as free provider lanes.
