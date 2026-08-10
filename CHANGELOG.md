# Changelog

Notable changes. Started once the app was working end to end, so everything before the first entry
is "the thing being described in the README".

## Unreleased

The release about arriving. Everything here exists because of two support messages: "nothing opens
after Run anyway", and a player for whom every problem meant a trip into Settings.

### Added

- **A startup failure can no longer be invisible.** Errors during startup used to be written on the
  overlay — which is transparent, has no taskbar button and cannot be clicked, so a failed start
  and a successful invisible one looked identical from outside: "nothing opens." A failed start now
  shows an ordinary window, in Arabic and English at once, with the error selectable for copying.
  And from the very first moment, the app writes `startup.log` next to its exe (or into its data
  folder if that is read-only): if the file is missing the process never ran, if it stops early
  something killed it while loading, and if it holds an error, that is the answer. The log opens
  with a payload census — how many DLLs are present, whether the OCR files survived — because the
  most common cause of all of this is an antivirus quietly removing files after "Run anyway".
- **A health check.** Settings → Diagnostics → *Run health check*: one button that checks the game
  window, exclusive fullscreen, display scaling, every API key (with one real request each, the
  same way the Test button does), the text recognition, the capture region and its reading quality
  — and reports in plain words, worst news first, in the interface language. Its output is the bug
  report: run it before asking for help. One detail: if Windows itself is in Arabic but the
  interface is in English, the report says so — in Arabic, because that line is addressed to
  someone who reads Arabic.
- **The region picker now finds the text for you.** Open the picker and, a moment later, dashed
  green boxes appear around the places the app can see text, each labelled with what it thinks it
  is — dialogue, subtitles, a quest list — plus how many words and how confidently it read them.
  Click one to use it, or ignore them and drag your own exactly as before. Nothing is proposed when
  nothing was found: a wrong suggestion confidently drawn teaches you not to trust the right ones.
- **The picker's OCR test now reports its confidence.** Text that reads correctly at 45% will
  misread on the next frame; "did I pick right?" deserves the number, not just the words.

## v0.6.0 — 10 August 2026

You can see what the app is looking at, reach everything it does without memorising a key
combination, and translate one thing without disturbing the thing it was already watching. Two bugs
that had been sitting on the paths all of that uses are fixed underneath.

### The licence changes, from this release onward

**GNU AGPL 3.0** replaces Apache 2.0, starting here. Use it, study it, change it, pass it on — the
one condition is that anything built on it stays open the same way, including a modified version
someone runs as a service over a network.

**Everything up to and including v0.5.3 remains Apache 2.0 and always will.** That grant cannot be
withdrawn, and it isn't being: those tags are still there and still yours on the old terms.

The reason is not money. Nobody is charged for anything here and every provider key is the user's
own. It is that a translator written for readers who were left out of their own games should not be
something that can be taken closed, reskinned, and sold back to them. The AGPL is the version of
that which keeps the project open source rather than merely restricted.

The licence text now ships inside the download as `LICENSE.txt`, which it should have been doing
under Apache too, and Settings → Diagnostics says where the source is.

### Added

- **A floating toolbar.** Six buttons over the game — translate now, watch automatically, translate
  one thing, choose the region, hide the translation, settings — and one more that opens the rest.
  Drag it anywhere, shrink it to a single handle, and hover any button to see what it does **in
  Arabic and English at the same time**, whichever language the interface is set to. A toolbar has
  no words on it, only shapes, so a label in one language leaves somebody guessing: the friend
  helping with the setup, or the person the app was built for. Switch it off in Settings → Overlay
  if you would rather not have it.
- **A visible outline around what is being captured.** Switch it on and a thin border shows exactly
  which rectangle is being read — the answer to "is it even looking at the right place". Clicks pass
  straight through it, so it does not get in the way of the game. Press the toolbar's frame button
  again and you can drag it or pull a corner to resize; it saves the moment you let go, with no
  confirmation step, because the thing you are editing is already showing you the result. It can
  never end up inside the text it outlines: like the translation panel, it is invisible to every
  screen capture, including this app's own.
- **Translate one thing, once.** `Ctrl+Shift+X`, or the toolbar. Drag a box around anything on
  screen — a tooltip, an item name, a sign in the corner — and it is translated the moment you let
  go. Automatic mode keeps running throughout and comes straight back to what it was watching, and
  the one-off never joins the conversation: a menu tooltip must not steer the pronouns of the next
  line of dialogue, in either direction.

### Fixed

- **The same line is no longer paid for twice because one comma was misread.** Text recognition is
  not perfectly repeatable: the same pixels a moment later come back with a comma turned into a full
  stop or an `l` read as an `I`, and every one of those is a different line as far as the cache is
  concerned — so it was translated again, and paid for again. This matters most on video, where the
  picture behind a subtitle is always moving and no amount of comparing frames can tell you the
  words have not changed. The app now compares the text as well, and a line within a few characters
  of the one already on screen costs nothing. Short labels still have to match exactly, because
  "yes" and "no" are three characters apart and are not the same word.
- **"Test what the OCR reads here" was reading the wrong pixels.** The region picker re-photographed
  the screen while it was itself covering it, so the preview included its own instruction panel and
  the blue selection box drawn across the very text being tested. On one monitor the sizes happened
  to line up and it looked right; on two screens it reported on an entirely different part of the
  desktop. It now reads the frozen image you are actually looking at.
- **The translation panel hung off the edge at 125% and 150% display scaling.** Its size was being
  measured in one unit and its position in another. At 100% those are the same number, which is why
  this was invisible — and it stops being invisible the moment something has to line up exactly with
  the captured rectangle, which the new outline does.
- **Text touching the edge of the capture region reads better.** A blank margin is added around the
  crop before recognition, which is what layout analysis needs in order to find a block of text at
  all. It is why a box drawn tightly around the words used to read worse than one drawn a little
  wide — advice nobody should have had to be given.
- **One bright pixel no longer cancels the contrast correction.** A glint on a sword, or a sliver of
  a white interface border clipped into the corner of the region, was enough to make the brightness
  adjustment do nothing at all — it took the single brightest and single darkest pixel as the range.
  It now ignores the outermost two percent at each end, which is where those live.
- **An answer that arrived as you switched something off is kept.** If a provider replied at the
  moment automatic mode stopped, or the app closed, the reply was thrown away — after being sent,
  counted against the day's allowance, and paid for. It is stored now.

## v0.5.3 — 9 August 2026

Almost all of this came from one player's feedback after an evening with Wuthering Waves, and from
finally measuring something that had only ever been assumed.

### Fixed

- **Automatic mode stopped dead on text it could not read, and did not say so.** One bad frame —
  an unusual font, a moment of bad luck in the text recognition — ended the whole session, because
  the error handling wrapped the entire loop rather than one frame of it. Worse, it announced this
  only on the Settings status line, which nobody playing a fullscreen game is looking at. So it
  simply stopped, and the only way to find out why was to go into Settings. It now skips the bad
  frame and carries on, gives up only after five failures in a row, and says so **on the overlay**.
- **The gap between two subtitles was reported as an error.** Between one line and the next there is
  no text, which is normal — and the app answered every one of those with "no text in the capture
  region, is a dialogue box actually on screen?" flashed over the film. Automatic mode now clears
  the overlay and says nothing, which is the correct answer to a question nobody asked.
- **A region that has stopped working now says so.** If nothing has been readable for a while, the
  app tells you once, and names the key to draw the box again, instead of leaving you to guess why
  it went quiet.
- **The 90-second idle stop could never fire on anything that moves.** It counted time with *no new
  text at all*, and any movement in the captured area reset it — so on a video, or in a game with
  animation behind the dialogue, it never fired once. It is still there for a genuinely still
  screen, and a real limit now sits alongside it.

### Added

- **A limit on automatic mode, measured from when you switch it on.** It tells you on the overlay
  after two minutes that it is still running and what it has spent, and switches itself off after
  four — or sooner if it has spent more than expected, because four minutes of cutscene is a dozen
  translations and four minutes of film is eighty. There is a switch to let it run without a limit.
- **A mode for watching video.** Subtitles appear whole and leave after a few seconds, so waiting
  for the text to "settle" — right for a game that types dialogue out character by character — meant
  the Arabic arrived after the line it translated had already gone. Measured: **4.6 seconds** on a
  moving picture, against a subtitle that lives three. Video mode checks more often, waits far less,
  keeps a minimum gap between translations, and is honest that a film costs a large part of a day's
  free allowance.
- **Control over the pace.** Settings → Hotkeys now has both the mode and the seconds between
  translations, neither of which was adjustable outside a JSON file before. Asked for directly.
- **It works out the rhythm for itself.** The app times the gaps between lines and tightens its own
  deadline to match, so a dialogue box that advances every eight seconds and subtitles that change
  every three get different timings without anyone choosing. What it has worked out is shown in
  Diagnostics, and when the text is genuinely arriving faster than it can be read, it says that
  rather than quietly skipping lines. It is measurement, not a model — nothing is stored, nothing is
  sent, and every decision is a number you can read off the screen.
- **`Ctrl+Shift+S` opens Settings** without leaving the game. Everything that goes wrong sends you
  there, and until now getting back to it meant hunting for a window with no taskbar entry.
- **An option to let screen recorders see the overlay.** It is hidden from capture by default so the
  app cannot read its own Arabic back and translate that instead — which is why the translation was
  missing from recordings and from the Nvidia app. That is now your choice, with the reason stated.

## v0.5.2 — 9 August 2026

Three things that were quietly wasting your free tier, and one you asked for.

### Fixed

- **Groq was being throttled by this app, not by Groq.** It kept dropping into a one-minute
  cooldown, and the reason was ours: Groq admits a request against the tokens you *reserve*, not
  the ones the answer uses, and we were reserving 4,096 of an 8,000-a-minute allowance on every
  single line. The second line inside any minute was refused, all three models were refused in
  turn, and the whole provider was set aside for sixty seconds while the log said it was rate
  limited. It never was slow either — measured against a live key it answers in **0.09 to 0.74
  seconds**. Each model now reserves what it actually needs, and the two that think before
  answering are told to think less. Verified live: thirteen lines in eight seconds, one 429 in the
  middle, fell through to the next model and translated every one.
- **A provider that asks to be tried again in four seconds now waits four seconds, not sixty.**
  Groq's per-minute limit clears almost immediately and its daily one does not; the app used to
  treat both the same and take the provider off the board either way.
- **Automatic mode translated the same sentence four or five times while it appeared on screen.**
  Final Fantasy XIV reveals dialogue one character at a time, and every partial line counted as a
  new one — so a single sentence cost four requests to show you four progressively less wrong
  versions of itself. It now waits for the text to stop moving. A screen that never stops moving is
  still translated, after three seconds, so this cannot leave you looking at nothing.

### Added

- **More than one key per provider.** Up to three each, tried in order before moving on to the next
  provider — so all your Google keys are used before Groq is touched. The Settings screen says the
  part that decides whether it is worth doing: a free allowance belongs to the **account**, not the
  key, so a second key from the same account shares one allowance and buys you nothing. Your
  existing key is untouched and stays exactly where it was.
- **Diacritics (تشكيل) are now a switch, and off by default.** The models were adding the
  short-vowel marks unevenly — the same conversation coming back half vowelled and half not,
  depending on which model answered which line — and fully vowelled text reads as scripture or a
  school book rather than a subtitle. The switch changes what is on screen straight away, including
  lines already translated, because what the provider sent is kept as-is and the marks are removed
  on the way to the overlay.

### Verified

On Windows against a real game, and against live keys with Groq's per-minute ceiling genuinely
reached: ten translations in a row on one model, a real refusal, a fall-through to the next model,
and every line translated. Multiple keys work; how far they scale has not been stress-tested yet.

### Still true, and worth repeating

Automatic mode has no time limit yet. It stops after 90 seconds with **no new text at all**, so
leaving it on during a conversation keeps it running and keeps spending your daily allowance. A cap
with a warning is the next thing being built.

## v0.5.1 — 8 August 2026

One fix, and it is the difference between the free tier lasting an evening and lasting all day.

### Fixed

- **The app gave up on a provider that still had most of its budget left.** Free providers meter
  **per model**, not per provider — Google allows one of its models 20 requests a day and another
  **500**; Groq gives each of three models its own thousand. The router treated the first "too many
  requests" from any single model as the end of that whole provider, so it announced that
  everything was exhausted while, in one measured evening, 498 unused Google requests and two
  entirely untouched Groq models sat there. It now moves to the next model, and only sets a
  provider aside when every one of its models says no.
- **The model with the biggest daily allowance now goes first.** It was third. The two ahead of it
  had 20 requests a day between them, which a single conversation spends.
- **The daily quota shown in Diagnostics was invented.** It claimed 1,000 requests for Google and
  14,400 for Groq. The real figures, read off each provider's own dashboard, are about 540 and
  3,000. Both are now stated per model in `data/models.json`.
- A model refusing one particular request — a token ceiling lower than asked for, a setting a
  sibling model accepts — no longer condemns the whole provider. Only a rejected API key does that.

Verified against live keys with the Google allowance genuinely exhausted: the app walked three
Google models, moved to Groq, walked all three of those as each filled up, and translated every
line. Fourteen out of fourteen, no English fallback.

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
