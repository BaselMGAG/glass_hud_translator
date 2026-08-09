# Glass HUD Translator — project brief

*Prepared as background material. Everything here is accurate as of 6 August 2026; the "What not
to claim" section near the end matters as much as the rest.*

**Download:** https://github.com/basel2000de/glass_hud_translator/releases

**Repository:** https://github.com/basel2000de/glass_hud_translator
**Licence:** Apache 2.0 (open source, free to use and modify, commercially too)
**Creator:** Basel — Frankfurt, Germany

---

## In one line

Glass HUD Translator puts live Arabic subtitles on top of games that were never translated into
Arabic, by reading the screen rather than modifying the game.

## In a paragraph

Most video games ship without Arabic, and the few that do rarely translate their story. Glass HUD
Translator sits on top of any game running in a window: it reads the on-screen text, translates it
with an AI model, and draws the Arabic back over the game in a transparent panel — about a second
per line, and free to run. It never touches the game itself, so there is no risk to anyone's
account. It also works on anything else on screen: a browser, a PDF, a video player's subtitles.

---

## Why it exists

Basel built it for someone who reads Arabic far more comfortably than English and plays Final
Fantasy XIV, a game with an enormous amount of story — and who was following maybe half of it. Not
the mechanics, the *story*: the reason to play that kind of game at all.

The obvious fixes don't work:

- **The game has no Arabic option.** Very few do.
- **Fan translation mods are risky.** Third-party plugins violate most games' terms of service and
  can get accounts banned. They also break with every patch.
- **Even if you could inject text, most game engines can't draw Arabic.** This is the technically
  interesting part, and it's a genuinely underappreciated problem — see below.

So the answer had to live outside the game entirely: read the pixels, translate, draw on top.

### The rendering problem, in plain terms

Arabic isn't just "English with different letters". Two things break naive implementations:

1. **Letters change shape depending on their neighbours.** The same letter has up to four different
   forms depending on what's next to it, and they join up like handwriting. A system that draws
   letters one at a time produces disconnected nonsense that a reader can technically decode but
   finds exhausting.
2. **Text runs right-to-left, but numbers and embedded English don't.** A sentence with a place name
   in it flows in two directions at once, and the full stop belongs at the *left* end.

Game engines mostly don't implement either. This is a real reason Arabic support is rare — it isn't
only that publishers don't prioritise the market, it's that adding it is more work than adding
another European language.

**A concrete example from this project:** an early version set a line-height value that was one
notch too tight. It silently cut off the marks that hang below the baseline — including the two dots
under the letter **ي**. Without those dots it becomes **ى**, a *different letter*, which changes
words. It looked fine at a glance. That class of bug is exactly why "just add Arabic" is harder than
it sounds, and it's a good illustration of the care the project takes.

---

## What it does

```
reads a rectangle of the screen
  → has anything changed since last time?   (most frames stop here)
  → optical character recognition
  → cleans up the text
  → seen this line before?                  (if yes: instant and free)
  → AI translation, with a glossary of the game's proper nouns
  → draws the Arabic over the game
```

Practical features:

- **The interface itself is in Arabic, if you want it.** English or Arabic, switchable in Settings,
  with the whole window mirroring right-to-left. English is the default.
- **Manual or automatic.** Press a key to translate the current line, or turn on auto-watch during
  a cutscene and let it follow along by itself.
- **Five configurable hotkeys** — translate, auto-watch, show/hide the overlay, re-pick the region,
  correct a translation.
- **Region picking on a frozen screenshot**, so the dialogue doesn't advance while you aim, with a
  button that shows exactly what the text recognition reads from your selection before you commit.
- **Corrections stick.** Fix a character's name once and that correction is used from then on.
- **A button that checks your API key**, beside the box you paste it into, so you find out before
  you are in a cutscene. It distinguishes "the key was refused" from "I could not check right now",
  which need opposite responses — only the first means you need a new key.
- **The overlay goes where you want it.** Two sliders move the translation panel, and it moves while
  you drag. The position is held relative to the game's window, so it survives the game moving.
- **It remembers the last three lines**, so pronouns and gender agreement have context to work from.
- **Works on anything on screen**, not only games — a browser, a PDF, a video player.
- **It tells you when a new version is out**, with the filename and the steps, and never installs
  anything by itself. One request a day to GitHub's public releases page, nothing sent with it,
  switchable off.
- **Bring your own API key.** No key is embedded. Four providers are supported: two with free tiers
  that need no credit card (Google Gemini, Groq), and two paid ones for people who already have a
  key (OpenAI, Anthropic Claude). The free ones are always tried first, so the paid options cost
  nothing unless someone deliberately opts in.

---

## Current status — honest version

**It works.** Tested on Windows against Final Fantasy XIV: screen capture, text recognition, global
hotkeys, the overlay and the full translation round trip all run against the live game at roughly
**one second per line**. Also confirmed working on a browser.

**It is early.** Specifically:

- Click-through (clicking through the overlay to the game) hasn't been verified yet.
- Display scaling above 100% hasn't been tested, and neither has a game on a second monitor.
- The Final Fantasy XIV glossary is a first draft and needs review by a native Arabic speaker.
- Text-recognition accuracy against real game fonts hasn't been measured properly yet.
- Only one game has a profile so far.
- **Free AI models are withdrawn without warning** — both free providers retired every model this
  app shipped with during one week in August 2026. Model names live in a text file rather than in
  code precisely so that the fix is an edit rather than a release, but it does mean an installation
  left alone for months may need that file refreshed.

Anyone writing about this should present it as **a working early release that people can try and
contribute to**, not a finished product.

---

## Facts and numbers that can be quoted

| | |
|---|---|
| Translation speed | ~1 second per new line; instant for anything seen before |
| Running cost | Free — the two free-tier providers cover roughly 3,500 translations a day between them, which is more play than anyone manages, and roughly that again for each extra account you add a key for |
| Keys per provider | Up to 3, tried in order — a free allowance belongs to the account, so a second key only helps from a second account |
| Providers supported | 4 — two free (Google Gemini, Groq), two paid and opt-in (OpenAI, Anthropic Claude) |
| Interface languages | 2 — English and Arabic, English by default |
| Credit card needed | No |
| Automated tests | 573 |
| Glossary | 86 Final Fantasy XIV proper nouns pinned so far |
| Configurable hotkeys | 7 |
| Licence | Apache 2.0 |
| Platform | Windows 10/11 to use; developed on macOS |
| Install footprint | Download a folder, run it. No installer, no admin rights, nothing to configure |

**On efficiency, if a technical angle is wanted:** during dialogue, 85–90% of screen frames are
identical to the previous one. The app compares a tiny thumbnail first and skips the expensive work
entirely, which takes a typical cycle from about 120 milliseconds down to about 15. That's what
makes it viable on modest hardware alongside a running game.

---

## What makes it worth talking about

**1. It serves an audience that is genuinely underserved.** Arabic has hundreds of millions of
speakers and a large, active gaming community, and almost no narrative games are translated into it.
This is a real gap, not a manufactured one.

**2. It's safe in a way that mods aren't.** It never touches the game process — no injection, no
memory reading, no modified files. It reads pixels, exactly like a screenshot. Nothing about it can
put an account at risk, which is the first question any experienced player asks.

**3. Adding a new game needs no programming — and, now, no files either.** There is an **Add a
game** button: pick your game from the windows you have open, choose how the writing should sound
from a dropdown, drag a box over the text. That's the whole setup.

This was the same blind spot as the English-only interface, one layer down. "No programming
required" was true — a game profile is three text files — but *files* were still the interface, and
the person this app exists for has never opened a config file and does not read English comfortably.
"Anyone can add a game, they just have to write JSON" is not the same sentence as "anyone can add a
game."

What it produces is still a folder anyone can share, which is the part that matters for reach: one
person setting up a game properly is enough for everyone else playing it.

**4. It's not only for games.** The same overlay reads a browser, a PDF, a subtitle bar. The
"gaming" framing is where it started, not the limit of what it does.

**5. There's a good build story.** The whole Windows layer — screen capture, global hotkeys, the
transparent overlay — was written on a Mac, without ever running it, and worked on the first attempt
on Windows. Architecture, technical decisions and debugging were Basel's; the code was written with
AI assistance working to that direction. That's a genuinely current story about how software gets
made now, and it's told honestly rather than as a "look what AI built" claim.

**6. One nice incident.** Mid-testing, Google retired one of the AI models the app was using. The
app noticed, logged it clearly, automatically switched to a different model, and carried on
translating without the user seeing anything. It was designed in advance for exactly that, because
free AI model catalogues change without warning. Small, but it's a concrete example of the
engineering being thought through rather than thrown together.

**7. The blind spot, caught and fixed — a good story if an honest one is wanted.** The app exists so
people don't have to read English. Its own interface was English-only. The person who most needed
the tool was the one least able to set it up, and it took weeks to notice, because everyone building
and testing it read English fine.

The whole interface now switches to Arabic and mirrors right-to-left. The language control is
labelled **Language · اللغة** in both scripts, because a language switch you can only find if you
already read the current language is no use to anyone.

Two things broke on the way, both worth mentioning if a technical audience is listening: the Arabic
tab labels came out as empty boxes, because the app had been leaning on a system font that macOS
has and a plain Windows install may not — the same build would have shown nothing but boxes to the
actual users. And the first screenshots of the Arabic interface came out mirrored, letters and all,
which turned out to be a quirk of how the screenshots were being generated rather than a bug in the
app. Both were found by looking at the result instead of trusting that it worked.

**8. The part a test cannot catch.** Translating the interface was not the end of it. A native
speaker went through the Arabic and found four things nobody who built it would have: three buttons
that read `حدد dialogue`, because the capture region names are stored English keys and had been
glued onto a translated verb; a key field labelled "not set", which sounds like the setting is
broken rather than like you haven't pasted a key yet; a linguist's term where a plain word belonged;
and the grey explanatory notes styled as though nobody had to read them — when they are precisely
what a non-technical user has to read to finish setup. One more surfaced while fixing those: the
per-provider quota line was listing the providers in reverse, because Latin text inside a mirrored
paragraph reorders — and that order is which provider gets tried first, so the Arabic interface was
quietly reporting the paid one as the default.

The takeaway is the useful part: a translated interface that passes every test can still be wrong in
ways only a reader of that language sees, and "we translated it" is not the same as "someone who
reads it has looked at it."

---

## Images available

All in the repository under `docs/images/`:

| File | What it shows |
|---|---|
| `in-game.jpeg` | Final Fantasy XIV with Arabic drawn over the game's English dialogue box. **The best single image.** |
| `on-desktop.jpeg` | A YouTube page being translated — proves it isn't only for games |
| `settings-providers.png` | The Providers tab: a key field per provider, each labelled free or paid. **Good for the "free to run" angle.** |
| `settings-providers-ar.png` | The same tab with the interface in Arabic, mirrored right-to-left. **Pairs with the one above as a before/after — the strongest image for the localisation angle.** |
| `settings-translating.png` | The Translating tab: which game, which Arabic dialect, where the text sits |
| `settings-translating-ar.png` | The same, in Arabic |
| `settings-overlay.png` | The Overlay tab: font size, opacity, and the two sliders that move the panel |
| `settings-overlay-ar.png` | The same, in Arabic |
| `settings-hotkeys.png` / `-ar.png` | The five rebindable hotkeys |
| `add-game.png` / `-ar.png` | Adding a game that has no profile yet, without touching a file |
| `diagnostics.png` | The diagnostics panel, including the moment it caught a model being retired |
| `overlay.png` | The overlay's three states, cleanly rendered — good for a graphic |

The in-game photo is a phone photo of a screen, so it's usable but not pristine. A clean screen
capture would be worth taking if a polished asset is needed.

The settings screenshots are generated from the running app by a build flag rather than taken by
hand, so they never drift out of date with the interface. That is why there is an Arabic pair of
each at no extra effort.

---

## Audiences

- **Arabic-speaking gamers**, especially MENA gaming communities — the primary audience
- **Final Fantasy XIV players** who read Arabic — the most immediately reachable group
- **Open source and .NET developers** — the technical story is genuinely interesting
- **Localisation and accessibility people** — this is an accessibility tool in everything but name
- **People interested in how AI-assisted development actually works** in practice

## Useful calls to action

1. **Try it** — one download, no installation, works in a few minutes
2. **Contribute a game profile** — no programming needed, three text files
3. **Review the Arabic** — a native speaker's eye on the glossary is the single most valuable
   contribution available right now
4. **Report bugs** — it's early and that's genuinely useful

---

## What NOT to claim

Please treat this list as firm. Overclaiming on an early project costs more than it gains, and some
of these are also accuracy or safety issues.

- ❌ **Don't call it finished, polished, or production-ready.** It's a working early release.
- ❌ **Don't say it works with every game.** It's been tested with one. The approach should work with
  any game in borderless-windowed mode, but "should" is doing real work in that sentence.
- ❌ **Don't call it a mod, plugin, patch or hack.** It's none of those, and that distinction is the
  whole reason it's safe. "Overlay" or "companion app" is right.
- ❌ **Don't claim the translation is perfect or professional-grade.** It's good, and the glossary
  keeps names consistent, but it's machine translation and hasn't had a native-speaker review pass.
- ❌ **Don't promise it will always be free.** It's free today because the AI providers offer free
  tiers. Those could change. The app is free and open source permanently; the AI service it calls is
  not under our control.
- ❌ **Don't present the AI-assisted development as "AI built an app".** The framing is: Basel owns
  the architecture, the technical decisions, the debugging and the calls on what to fix; AI
  assistance wrote implementation code to that direction. That's both accurate and more interesting.
- ❌ **Don't imply any affiliation with Square Enix or Final Fantasy XIV.** It's an independent tool
  that happens to have been tested against that game.

---

## Frequently-asked questions, with answers

**Will it get me banned?**
No. It doesn't touch the game at all — no injection, no memory reading, no modified files. It reads
the screen the same way a screenshot does.

**How much does it cost?**
Nothing. It uses free tiers from AI providers that don't require a credit card. You'd need to play
for more than a day straight to exhaust a daily allowance. There are also paid options (OpenAI,
Anthropic) for people who already pay for one and would rather use it — those are tried only after
the free ones, and do nothing at all unless you deliberately enter a key.

**Is the app itself in Arabic?**
Yes, optionally. English or Arabic, switchable in Settings, with the whole window mirroring
right-to-left. English is the default. This was a genuine oversight for the first few weeks — see
point 7 above.

**How good is the translation?**
Good, and it's consistent about character and place names because those are pinned in a glossary.
It's machine translation, so it won't match a professional localisation, but it's the difference
between following a story and not.

**Does it slow the game down?**
It's designed not to. Most of the time it does almost nothing — it only does real work when the text
on screen actually changes, and it runs at a lower priority than the game.

**Does it work on other games?**
The approach works on any game running in a window. Only Final Fantasy XIV has a prepared profile so
far, but adding one takes no programming.

**Does it only do Arabic?**
Arabic is what it was built for, and the display side is specifically engineered for Arabic's
rendering requirements. The translation side isn't inherently limited to it.

**What if my game moves or I change resolution?**
Capture regions are stored relative to the game's window, so moving it is fine. Changing resolution
or the game's interface scale means re-selecting the area, which takes a few seconds.
