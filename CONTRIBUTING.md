# Contributing

Thanks for looking. This is an early project and the most useful contributions are not code.

## The three things I actually need

**1. Review the Arabic.** Two places, and both were written without a native speaker:

- **`profiles/ffxiv/glossary.json`** — 86 proper nouns. If a spelling is wrong, or a name would
  obviously be transliterated differently by anyone who reads Arabic properly, that is the single
  most valuable thing you can tell me.
- **`src/GlassHudTranslator.Core/Config/UiText.cs`** — the app's own interface, now available in
  Arabic. Stiff phrasing, a wrong term, or something that just reads like a translation rather than
  like software: say so.

Open an issue or a PR — either is fine, and you do not need to justify it at length.

**2. Add a game profile.** No programming. A profile is a folder under `profiles/` with three
files, and `profiles/_template/` is there to be copied:

| File | What goes in it |
|---|---|
| `profile.json` | The window title to attach to, the source language, and one sentence describing the tone the translation should have |
| `glossary.json` | Proper nouns and how they should be spelled in Arabic |
| `ocr-corrections.json` | Text recognition mistakes that game's font causes, and what they should read |

Drop the folder in, restart, and pick it in Settings → Translating. If it works for you it will
work for everyone else playing that game, which is the whole point.

**3. Tell me what broke.** It has been tested against one game on one machine. A bug report that
says what you were playing, what you saw, and what the Diagnostics tab said is genuinely useful.

## If you do want to write code

```bash
dotnet build
dotnet test
```

Both work on macOS, Linux and Windows — that is deliberate, and worth keeping. Roughly all of the
code compiles and is tested off Windows; `PlatformServices.cs` is the only file in the app allowed
to contain `#if WINDOWS`.

To exercise the whole pipeline without a game, a key, or a network call:

```bash
dotnet run --project tools/Replay -- --no-cache
```

### Things that look like style and are actually correctness

`CLAUDE.md` is the long version. The short version:

- **Never set an explicit `LineHeight` on Arabic text.** Too tight and the marks below the baseline
  are clipped, which turns ي into ى — a different letter. Use `LineSpacing`.
- **Never reintroduce `PublishSingleFile`.** It breaks native OCR: TesseractOCR finds its own DLLs
  via `Assembly.Location`, which is empty inside a single-file bundle.
- **Model names live in `data/models.json`, never in code.** Free model catalogues get retired
  without warning; the ordered list is how the app survives that.
- **Don't raise the OCR confidence threshold back to 40.** Tesseract scores unusual proper nouns
  low, and those are exactly the words that matter — 40 silently deleted "linkpearl" at 39.2.
- **Don't add a low-level keyboard hook** (`WH_KEYBOARD_LL`). Antivirus heuristics flag it.
- **Nothing may touch the game process.** No injection, no memory reading, no modified files. That
  property is the reason the app is safe to use, and it is not negotiable for a feature.

### Adding or changing a user-facing string

Every string the user sees lives in `UiText`, in both languages. It is a class of `required`
properties rather than a key/value dictionary on purpose: adding a string without translating it is
a **compile error**, not a silent English leak in the Arabic interface.

A test asserts the `{0}`-style placeholders match between the two languages. That one matters — a
translation carrying a `{1}` the English doesn't have throws at runtime, and only ever for the
people this project exists for.

Platform error text (Win32 messages, "Global hotkeys are Windows-only") is deliberately left alone;
it comes from the OS and is English there too.

### Adding a provider

It is a config edit, not a code change, as long as the provider speaks the OpenAI
chat-completions shape — add a lane to `data/models.json` and it gets a key field in Settings
automatically. A provider with its own protocol needs a class implementing `ITranslationProvider`
and a branch in `ProviderFactory`; `AnthropicProvider` is the worked example.

Keep free lanes above paid ones in that file. The router walks the list top to bottom, so lane
order is the cost policy.

## Licence

Apache 2.0. By contributing you agree your contribution is licensed the same way.
