# Changelog

Notable changes. Started once the app was working end to end, so everything before the first entry
is "the thing being described in the README".

## v0.5.0 — 8 August 2026

The first release verified against a real game since v0.1.0, and the first where the app can tell
you your key works before you are in a cutscene finding out that it does not.

### Added

- **You can move the overlay.** Settings → Overlay has two sliders, top-to-bottom and
  left-to-right, and the panel moves while you drag them. Its position was two constants tuned
  against one dialogue box in one game, and there was no way to change it — the overlay is
  click-through by design, so it cannot be dragged, and it has no Alt-Tab entry to grab. The
  position is measured inside the game's window, so it stays where you put it when the game moves
  or changes monitor, and the panel can no longer be pushed off the edge.
- **A button that tells you whether your API key works**, next to each key box. Three answers, and
  the difference between the last two matters: the key works, the key was refused, or it could not
  be checked right now — because telling someone their key is wrong when their wifi is down sends
  them to regenerate a key that was never the problem. A key that passes is saved on the spot.
- **The translator remembers the last three lines.** Pronouns and gender agreement have context to
  work from now instead of one line. The window clears itself after two minutes, so a conversation
  from earlier in the session cannot steer the one you are in.
- **Multi-monitor.** Capture follows the game to whichever screen it is on, and the overlay follows
  it there. A monitor placed to the left of or above the main one was previously not merely
  unsupported but actively refused, three layers down.
- **The translation history records which game and which capture region produced each line.**

### Fixed

- **Arabic error messages on the overlay were laid out as though they were English** — left to
  right, left aligned. The flag was correct while those messages were English literals and was
  never updated when they were translated.
- **The overlay could read its own output.** It is excluded from screen capture, but that feature
  is unavailable on Windows builds before version 2004, and the app knew when it had failed and
  said nothing. It now warns, in the Overlay tab, only when it actually happened.
- **Model catalogues.** Both free providers retired every model this app shipped with, within weeks
  of each other. The lists are current, and the app now names all of a provider's dead models
  instead of only the last one it tried.
- **Reasoning models returned nothing.** The replacement free models think before they answer, and
  the per-answer token budget was sized for models that do not — the thinking consumed it and every
  completion came back empty. The budget and the per-attempt timeout both suit a model that pauses
  to think, and a timed-out model is skipped rather than retried.
- **Region rectangles that no longer fit the screen are trimmed rather than captured whole**, which
  used to read undefined pixels into OCR and looked like the model getting worse.
- Settings labels no longer truncate themselves. «الموضع من أعلى إلى أسفل» was rendering as
  «ع من أعلى إلى أسفل».

### Changed

- The interface is unchanged in shape, but the Providers tab now lists the lanes that will actually
  be tried, in order, marking any that will be skipped for want of a key.

### Internal

- 427 automated tests, up from 368. The cache key is pinned by golden vectors; schema migrations
  are a ladder that can be extended safely; OCR now reports where on screen it saw each word, which
  is the groundwork for proposing a capture region for you rather than asking you to drag one.

## v0.4.2 — 8 August 2026

### Fixed

- **The profile list showed folder names instead of the names people gave their games.** A profile
  called "Baldur's Gate 3" was listed as `baldur-s-gate-3`. Tolerable while the only two shipped
  with the app and called `ffxiv` and `general`; not tolerable once anyone can add one. Same defect
  as building a button caption out of a stored key — an identifier where the user's own words
  belong. The list now shows display names, and the id stays the stored value.

### Added

- **An Egyptian Arabic readme** — [`README.masri.md`](README.masri.md), written for players rather
  than developers. The other two are the same document in two languages, both in the register I use
  writing to other developers: architecture, contracts, what is unverified. That is the wrong
  register for the person this exists for, who wants to know how to make the Arabic appear over the
  game. It opens with why the program exists, spells out getting a free API key click by click, and
  covers what actually goes wrong — fullscreen instead of borderless, running the game as
  administrator, a capture box that grabbed too much.

## v0.4.1 — 7 August 2026

### Fixed

- **Reworded the window-picking note in the Arabic Add a game screen**, on a native reader's
  correction. "اختر اللعبة من قائمة ما هو مفتوح الآن" parses but is not how anyone writes Arabic;
  "قائمة النوافذ المفتوحة حاليًا" is. The second half described the mechanism — that the region is
  measured against the window — where it should have said what that means for the reader: move the
  window and you will not have to pick the region again. The same sentence in `README.ar.md` was
  carrying the earlier wording and now matches.

## v0.4.0 — 7 August 2026

### Added

- **Add a game from inside the app.** Settings → Translating → **+ Add a game**, with **Edit** and
  **Delete** beside it. Adding a game used to mean copying a folder and writing three JSON files by
  hand — a reasonable ask for whoever wrote them and an impossible one for the person this app is
  built for, who does not read English comfortably and has never seen a config file.
- **Pick the game from the windows you have open**, rather than typing a "window title". The list
  shows each window with the program that owns it, so two windows called Settings are told apart.
- **Profiles now bind to the program name as well as the title.** Titles change while a program
  runs — a browser's is whatever page is open, and plenty of games append the zone or character
  name — whereas `ffxiv_dx11.exe` never does. Either match is accepted, so profiles written before
  this keep working on their title alone.
- **A dropdown of writing styles** instead of a free-text prompt field: plain, serious fantasy,
  modern and casual, funny, menus and numbers. This is the highest-value field in a profile and the
  one least likely to be filled in by someone who has never seen a system prompt, so the app writes
  the sentence. The free-text box is still there, folded away.
- Saving a new profile goes straight into picking the capture region, because a profile without one
  does nothing — and a new entry in a dropdown is not help.
- An optional two-column table for proper nouns and their Arabic spellings, explicitly marked
  optional: it is the part a non-technical user stalls on, and the correction hotkey fills it in
  over time anyway.

### Changed

- **User-created profiles are stored under your Windows account, not in the app folder.** The app
  folder is replaced wholesale by an update — the v0.3.0 release notes say so — so a profile written
  there would have been deleted the first time someone updated, taking their capture regions and
  glossary with it. Editing a bundled profile now saves your copy separately and leaves the shipped
  one underneath, so it keeps improving with each release and your changes survive.
- Deleting a bundled profile is recorded rather than performed, for the same reason: deleting the
  files would work exactly until the next update restored them.
- `Anything on screen` cannot be edited or deleted. It is the fallback that works on everything and
  what the app falls back to when a game profile is removed. Everything else, including the shipped
  Final Fantasy XIV profile, can be changed or removed.
- The profile list refreshes in place, so adding a game no longer needs a restart.
- Deleting a profile also forgets its capture regions, which live in the database rather than the
  folder and would otherwise have been inherited by any later profile with the same generated id.

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
