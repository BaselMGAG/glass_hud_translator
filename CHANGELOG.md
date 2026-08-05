# Changelog

Notable changes. Started once the app was working end to end, so everything before the first entry
is "the thing being described in the README".

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
