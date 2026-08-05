using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Platform;
using GlassHudTranslator.Core.Text;
using GlassHudTranslator.Core.Regions;
using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Translation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace GlassHudTranslator.App.Views;

/// <summary>
/// The app's control panel.
///
/// <para>
/// Organised as tabs rather than one long scroll, because the sections are used at completely
/// different times: keys and regions are first-run setup, hotkeys are set once, and Diagnostics is
/// where you go when something is wrong mid-session. Scrolling past the API keys to reach the
/// quota readout was the reason the readout went unnoticed (brief 12). The status line is docked
/// outside the tabs so that whichever tab is open, the answer to "did that work?" is on screen.
/// </para>
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppServices _services;
    private readonly OverlayWindow _overlay;
    private readonly AppSettings _settings;
    private readonly TranslationSession _session;
    private readonly Dictionary<HotkeyAction, TextBox> _hotkeyBoxes = [];

    /// <summary>Key box per secret name. Built from models.json, never hardcoded.</summary>
    private readonly Dictionary<string, TextBox> _keyBoxes = [];

    private readonly TextBlock _hotkeyStatus = Readout();
    private readonly TextBlock _laneSummary = Readout();
    private TextBlock _profileNote = Readout();
    private readonly TextBox _correction = new() { Watermark = "corrected Arabic", Width = 380 };

    private readonly ComboBox _register = new()
    {
        ItemsSource = new[] { "Modern Standard Arabic", "Egyptian Arabic" },
        SelectedIndex = 0,
        Width = 240,
    };

    private readonly Slider _fontSize = new() { Minimum = 16, Maximum = 48, Value = 26, Width = 240 };
    private readonly Slider _opacity = new() { Minimum = 0.3, Maximum = 1.0, Value = 0.82, Width = 240 };
    private readonly TextBlock _quota = Readout();
    private readonly TextBlock _cache = Readout();
    private readonly TextBlock _status = Readout();
    private readonly TextBox _routerLog = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        Height = 160,
        FontFamily = new FontFamily("monospace"),
        FontSize = 11,
        TextWrapping = TextWrapping.NoWrap,
    };

    public SettingsWindow(AppServices services, OverlayWindow overlay, AppSettings settings,
        TranslationSession session)
    {
        _services = services;
        _overlay = overlay;
        _settings = settings;
        _session = session;

        _register.SelectedIndex = settings.Register == ArabicRegister.Egyptian ? 1 : 0;
        _fontSize.Value = settings.OverlayFontSize;
        _opacity.Value = settings.OverlayOpacity;

        Title = "Glass HUD Translator";
        Width = 760;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = BuildShell();

        _fontSize.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase_ValueProperty) return;
            _overlay.BodyFontSize = _fontSize.Value;
            _settings.OverlayFontSize = _fontSize.Value;
            _settings.Save();
        };
        _opacity.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase_ValueProperty) return;
            _overlay.PanelOpacity = _opacity.Value;
            _settings.OverlayOpacity = _opacity.Value;
            _settings.Save();
        };

        _register.SelectionChanged += (_, _) =>
        {
            _settings.Register = _register.SelectedIndex == 1
                ? ArabicRegister.Egyptian
                : ArabicRegister.ModernStandard;
            _settings.Save();
            _services.Pipeline.Register = _settings.Register;
            _status.Text = $"Register set to {(_settings.Register == ArabicRegister.Egyptian ? "Egyptian" : "Modern Standard")} Arabic.";
        };

        LoadSecrets();
        UpdateLaneSummary();
        _ = RefreshAsync();
    }

    private static AvaloniaProperty RangeBase_ValueProperty => Slider.ValueProperty;

    // ── shell ─────────────────────────────────────────────────────────────────────────────

    private TabControl? _tabs;
    private Control? _shellRoot;

    /// <summary>Tab headers, in order. Used by the documentation screenshot pass.</summary>
    public IReadOnlyList<string> TabNames { get; private set; } = [];

    private Control BuildShell()
    {
        var tabs = _tabs = new TabControl { Margin = new Thickness(8, 8, 8, 0) };
        tabs.Items.Add(Tab("Providers", BuildProvidersTab()));
        tabs.Items.Add(Tab("Translating", BuildTranslatingTab()));
        tabs.Items.Add(Tab("Overlay", BuildOverlayTab()));
        tabs.Items.Add(Tab("Hotkeys", BuildHotkeysTab()));
        tabs.Items.Add(Tab("Diagnostics", BuildDiagnosticsTab()));
        TabNames = tabs.Items.OfType<TabItem>().Select(t => (string)t.Header!).ToList();

        // Docked, not scrolled with the tab body: every action on every tab reports here, and a
        // confirmation you have to scroll to find is a confirmation nobody reads.
        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1f2023")),
            Padding = new Thickness(16, 10),
            Child = _status,
        };
        DockPanel.SetDock(statusBar, Dock.Bottom);

        // Explicit rather than inherited from the window. Without it the root paints nothing, and
        // rendering it to a bitmap for the documentation screenshots produces a transparent
        // background - on which every default-coloured control label, which under the dark theme
        // is white, is invisible. Same colour the Fluent dark theme uses, so nothing looks different.
        var root = new DockPanel
        {
            LastChildFill = true,
            Background = new SolidColorBrush(Color.Parse("#1e1e1e")),
        };
        root.Children.Add(statusBar);
        root.Children.Add(tabs);
        return _shellRoot = root;
    }

    /// <summary>
    /// Renders one tab to a PNG. Drives the screenshots in the README, so that what is documented
    /// is the window as it actually renders rather than a photo of an older build - the settings
    /// screenshot went stale within a day of the tabs landing, which is the whole argument for
    /// generating it.
    /// </summary>
    public void SelectTab(int index)
    {
        if (_tabs is not null && index >= 0 && index < _tabs.ItemCount) _tabs.SelectedIndex = index;
    }

    public void SaveSnapshot(string path)
    {
        if (_shellRoot is null) return;

        var size = new PixelSize((int)Width, (int)Height);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(_shellRoot);
        bitmap.Save(path);
    }

    private static TabItem Tab(string header, Control body) => new()
    {
        Header = header,
        Content = new ScrollViewer
        {
            Content = new StackPanel { Spacing = 12, Margin = new Thickness(20, 16) }
                .With(panel => panel.Children.Add(body)),
        },
    };

    // ── tabs ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One key field per lane in models.json that needs one. Adding a provider is then a config
    /// edit rather than a UI change, which is the same reason model names live in that file.
    /// </summary>
    private Control BuildProvidersTab()
    {
        var stack = new StackPanel { Spacing = 12 };

        // Suppressed while generating documentation screenshots. The banner is true of the machine
        // the shots are rendered on and false of every machine that runs the app: leaving a
        // "keys are stored in PLAINTEXT" warning in the README would tell Windows users something
        // alarming and wrong about their own install.
        if (!PlatformServices.IsWindows && !Program.HasFlag("--ui-shots"))
        {
            stack.Children.Add(Warning(
                "Development build. Capture replays recorded frames, hotkeys are inactive, and API "
                + "keys are stored in PLAINTEXT. Windows uses BitBlt, RegisterHotKey and DPAPI."));
        }

        stack.Children.Add(Note(
            "Bring your own key. Nothing is embedded in this app, and lanes are tried top to "
            + "bottom - so the free tiers answer first and a paid provider only sees the lines they "
            + "could not. A lane with no key is switched off and costs nothing."));

        foreach (var problem in _services.Models.Problems())
            stack.Children.Add(Warning($"models.json: {problem}"));

        foreach (var provider in _services.Models.Providers.Where(p => p.Secret is not null))
        {
            stack.Children.Add(Section(provider.Label));
            stack.Children.Add(KeyRow(provider));
        }

        var free = _services.Models.Providers.Any(p => !p.IsPaid && p.Secret is not null);
        if (free)
        {
            stack.Children.Add(Note(
                "Gemini and Groq both issue a key without a credit card, and between them cover "
                + "roughly 15,000 lines a day - more than a full day of play."));
        }

        stack.Children.Add(Button("Save keys", SaveSecrets));
        stack.Children.Add(Section("Active lanes"));
        stack.Children.Add(_laneSummary);

        return stack;
    }

    private Control KeyRow(ProviderConfig provider)
    {
        var box = KeyBox();
        _keyBoxes[provider.Secret!] = box;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(box);
        row.Children.Add(TierBadge(provider));

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(row);

        if (!string.IsNullOrWhiteSpace(provider.KeyUrl))
            stack.Children.Add(Note($"Key from {provider.KeyUrl}"));

        stack.Children.Add(Note($"Models tried in order: {string.Join(" → ", provider.Models)}"));
        return stack;
    }

    /// <summary>
    /// The free/paid distinction is the whole reason the paid lanes are worth adding, so it is
    /// stated next to the box rather than buried in a paragraph someone has to read first.
    /// </summary>
    private static Control TierBadge(ProviderConfig provider)
    {
        var (text, colour) = provider.Tier.ToLowerInvariant() switch
        {
            ProviderTiers.Paid => ("PAID — billed per line", "#fdd663"),
            ProviderTiers.Local => ("LOCAL", "#9aa0a6"),
            _ => ("FREE TIER", "#81c995"),
        };

        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse(colour)),
        };
    }

    private Control BuildTranslatingTab()
    {
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(Section("What are you translating?"));
        var profiles = new ComboBox
        {
            ItemsSource = _services.AvailableProfiles,
            SelectedItem = _services.Profile.Id,
            Width = 240,
        };
        profiles.SelectionChanged += (_, _) =>
        {
            if (profiles.SelectedItem is not string id || id == _settings.ProfileId) return;

            _settings.ProfileId = id;
            _settings.Save();
            _services.SwitchProfile(id);
            UpdateProfileNote();

            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var picked = await _services.Regions.HasAsync(
                    id, _settings.LastRegionProfile, CancellationToken.None);

                _status.Text = picked
                    ? $"Switched to '{_services.Profile.DisplayName}'. Its saved capture region is back."
                    : $"Switched to '{_services.Profile.DisplayName}'. No region picked for it yet — "
                      + "press Ctrl+Shift+R.";
            });
        };
        stack.Children.Add(Row("Profile", profiles));
        _profileNote = Note("");
        UpdateProfileNote();
        stack.Children.Add(_profileNote);

        stack.Children.Add(Section("Arabic"));
        stack.Children.Add(Row("Register", _register));
        stack.Children.Add(Note(
            "Modern Standard suits FFXIV's archaic narrative voice. Egyptian lands well for "
            + "merchants and comic relief, and reads as comedy for Elezen nobility."));

        stack.Children.Add(Section("Capture regions"));
        stack.Children.Add(Note(
            "Games often draw narrative text in more than one place — a dialogue box, a subtitle bar, "
            + "a quest window — so each gets its own rectangle. Each profile keeps its own set, so "
            + "switching between a game and the desktop does not lose either."));
        var regionButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var name in RegionProfile.Names.All)
            regionButtons.Children.Add(Button($"Pick {name}", () => _ = PickRegionAsync(name)));
        stack.Children.Add(regionButtons);

        stack.Children.Add(Section("Corrections"));
        stack.Children.Add(Note("Correct the line currently on the overlay. The correction is pinned "
                              + "and always wins over the model in future."));
        var correctRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        correctRow.Children.Add(_correction);
        correctRow.Children.Add(Button("Pin correction", () => _ = CorrectCurrentAsync()));
        stack.Children.Add(correctRow);

        return stack;
    }

    private Control BuildOverlayTab()
    {
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(Row("Font size", _fontSize));
        stack.Children.Add(Row("Panel opacity", _opacity));
        stack.Children.Add(Note(
            "Both apply live. Never set a fixed line height on the overlay: too tight and the marks "
            + "that hang below the baseline are clipped, which turns ي into ى — a different letter."));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Button("Preview overlay", () =>
            _overlay.ShowTranslation("Y'shtola", "تعال، فالأثير هنا يزداد اضطراباً.")));
        buttons.Children.Add(Button("Show / hide overlay", () => _status.Text = _overlay.ToggleHidden()
            ? "Overlay shown." : "Overlay hidden. Translation carries on in the background."));
        stack.Children.Add(buttons);

        return stack;
    }

    private Control BuildHotkeysTab()
    {
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(Note(PlatformServices.IsWindows
            ? "Type a combination such as Ctrl+Shift+T. Modifiers: Ctrl, Shift, Alt, Win. Keys include "
              + "A-Z, 0-9, F1-F24, arrows, Insert/Delete/Home/End, numpad (Num0-Num9) and punctuation. "
              + "F13-F24 are the safest choices - games almost never bind them."
            : "Global hotkeys are Windows-only. On macOS use the manual buttons below instead."));

        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var box = new TextBox { Text = _settings.HotkeyFor(action).ToString(), Width = 200 };
            _hotkeyBoxes[action] = box;
            stack.Children.Add(Row(DefaultHotkeys.Describe(action), box, labelWidth: 150));
        }

        var hotkeyButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        hotkeyButtons.Children.Add(Button("Apply hotkeys", ApplyHotkeys));
        hotkeyButtons.Children.Add(Button("Reset to defaults", ResetHotkeys));
        stack.Children.Add(hotkeyButtons);
        stack.Children.Add(_hotkeyStatus);

        stack.Children.Add(Section("Manual controls"));
        stack.Children.Add(Note("The same five actions, for when a hotkey is unavailable or clashes."));
        var manual = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        manual.Children.Add(Button("Translate now", () => _ = _session.TranslateNowAsync()));
        manual.Children.Add(Button("Toggle auto-watch", _session.ToggleAutoWatch));
        stack.Children.Add(manual);

        return stack;
    }

    private Control BuildDiagnosticsTab()
    {
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(Note(PlatformServices.Description));
        stack.Children.Add(Note($"OCR: {_services.Ocr.Name} — {_services.Ocr.Diagnostics ?? "no detail"}"));
        stack.Children.Add(_quota);
        stack.Children.Add(_cache);

        stack.Children.Add(Section("Router log"));
        stack.Children.Add(Note("A model disappearing upstream shows up here, by name. So does a "
                              + "provider being rate limited, and a line that fell back to English."));
        stack.Children.Add(_routerLog);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button("Test translation", () => _ = TestTranslationAsync()));
        actions.Children.Add(Button("Refresh", () => _ = RefreshAsync()));
        stack.Children.Add(actions);

        return stack;
    }

    /// <summary>
    /// Rebuilt on every switch. Built once, it kept describing the previous profile while the
    /// dropdown showed the new one, which is worse than saying nothing.
    /// </summary>
    private void UpdateProfileNote()
    {
        var profile = _services.Profile;
        _profileNote.Text = profile.WindowTitles.Length > 0
            ? $"Active: {profile.DisplayName}. Carries its own glossary ({profile.Glossary.Count} "
              + "terms) and measures the capture region against that application's window, so the "
              + "region survives the window being moved."
            : $"Active: {profile.DisplayName}. No window of its own — the capture region is measured "
              + "against the whole screen. This is what you want for a browser, a PDF or a video "
              + "player. Move the window and you will need to pick the region again.";
    }

    /// <summary>
    /// Spells out which lanes will actually be tried, in order. Without this, "no key entered" and
    /// "key entered but wrong" look identical from the outside - the first is silent by design.
    /// </summary>
    private void UpdateLaneSummary()
    {
        var lanes = _services.Models.Enabled(includeDevOnly: !PlatformServices.IsWindows)
            .Select(p =>
            {
                var live = p.Secret is null || _services.Secrets.Has(p.Secret);
                var tier = p.IsPaid ? " (paid)" : "";
                return live ? $"{p.Label}{tier}" : $"{p.Label} — no key, skipped";
            })
            .ToList();

        _laneSummary.Text = lanes.Count == 0
            ? "No lanes configured. Translation will fall back to showing the English."
            : string.Join("\n", lanes.Select((lane, i) => $"{i + 1}.  {lane}"));
    }

    private void LoadSecrets()
    {
        foreach (var (name, box) in _keyBoxes)
            box.Text = _services.Secrets.Get(name) ?? "";
    }

    private void SaveSecrets()
    {
        var saved = 0;
        foreach (var (name, box) in _keyBoxes)
        {
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                _services.Secrets.Delete(name);
            }
            else
            {
                _services.Secrets.Set(name, box.Text.Trim());
                saved++;
            }
        }

        UpdateLaneSummary();
        _status.Text = saved == 0
            ? "All keys cleared. Nothing will be translated until one is entered."
            : $"{saved} key{(saved == 1 ? "" : "s")} saved. Lanes without one are skipped.";
    }

    public async Task PickRegionAsync(string profileName)
    {
        // Freeze the screen first so the dialogue does not advance while the user is aiming, and
        // hide our own overlay so it cannot end up inside the captured region.
        _overlay.Clear();
        await Task.Delay(120);
        var screenshot = PlatformServices.CaptureFullScreen();

        var picker = new RegionPickerWindow(profileName, screenshot, TestRegionAsync);
        await picker.ShowDialog(this);

        if (picker.Result is not { } region)
        {
            _status.Text = $"Region '{profileName}' unchanged.";
            return;
        }

        // Stored relative to the game's client area, not the screen, so the profile survives the
        // window being moved. Falls back to the screen when there is no game window to measure.
        var game = PlatformServices.FindGameWindow(_services.Profile.WindowTitles);
        var origin = game?.ClientArea ?? new CaptureRegion(0, 0,
            Screens.Primary?.Bounds.Width ?? 1920, Screens.Primary?.Bounds.Height ?? 1080);

        var relative = new CaptureRegion(
            region.X - origin.X, region.Y - origin.Y, region.Width, region.Height);

        var profile = RegionProfile.FromPixels(profileName, relative,
            origin.Width, origin.Height, game?.Scaling ?? 1.0);
        await _services.Regions.SaveAsync(_services.Profile.Id, profile, CancellationToken.None);

        _settings.LastRegionProfile = profileName;
        _settings.Save();

        _status.Text = $"Saved '{profileName}' as {profile.RelWidth:P0} x {profile.RelHeight:P0} " +
                       $"of the client rect.";
    }

    /// <summary>
    /// Reads whatever is inside a candidate rectangle, so the picker can show the user exactly what
    /// the OCR sees before they commit to it. Costs no API quota - OCR only, no translation.
    /// </summary>
    private async Task<string> TestRegionAsync(CaptureRegion region)
    {
        var screenshot = PlatformServices.CaptureFullScreen();
        if (screenshot is null) return "(screen capture is Windows-only)";

        if (!region.FitsWithin(screenshot.Width, screenshot.Height)) return "(selection is off-screen)";

        var result = await _services.Ocr.RecognizeAsync(screenshot.Crop(region), CancellationToken.None);
        return result.RawText;
    }

    /// <summary>
    /// Pins a manual correction for the line on the overlay. It is stored as an override row, so it
    /// always wins over whatever the model produces for that line from now on.
    /// </summary>
    public async Task CorrectCurrentAsync()
    {
        if (_session.Current is not { } current)
        {
            _status.Text = "Nothing on the overlay to correct yet.";
            return;
        }

        var corrected = _correction.Text?.Trim();
        if (string.IsNullOrWhiteSpace(corrected))
        {
            _correction.Text = current.Arabic;
            _status.Text = "Edit the text above, then press Pin correction.";
            return;
        }

        await _services.Cache.PutOverrideAsync(
            CacheKey.For(current.Source, _settings.Register), current.Source, corrected, CancellationToken.None);

        _overlay.ShowTranslation(null, corrected);
        _correction.Text = "";
        _status.Text = "Correction pinned. It will be used for this line from now on.";
        await RefreshAsync();
    }

    private void ApplyHotkeys()
    {
        foreach (var (action, box) in _hotkeyBoxes)
        {
            var parsed = Hotkey.TryParse(box.Text);
            if (parsed is null || !parsed.IsValid)
            {
                _hotkeyStatus.Text = $"'{box.Text}' is not a usable combination for " +
                                     $"{DefaultHotkeys.Describe(action)}. It needs at least one modifier and a known key.";
                return;
            }

            _settings.SetHotkey(action, parsed);
        }

        var conflicts = _settings.FindConflicts();
        if (conflicts.Count > 0)
        {
            _hotkeyStatus.Text = "Two actions share a combination: " +
                                 string.Join(", ", conflicts.Select(DefaultHotkeys.Describe)) +
                                 ". One of them would never fire.";
            return;
        }

        _settings.Save();
        ReportHotkeyRegistrations(_services.Hotkeys.Register(_settings.ResolvedHotkeys()));
    }

    private void ResetHotkeys()
    {
        foreach (var (action, hotkey) in DefaultHotkeys.All)
        {
            _settings.SetHotkey(action, hotkey);
            _hotkeyBoxes[action].Text = hotkey.ToString();
        }

        _settings.Save();
        ReportHotkeyRegistrations(_services.Hotkeys.Register(_settings.ResolvedHotkeys()));
    }

    /// <summary>A clash with another application fails one binding, so name which one.</summary>
    public void ReportHotkeyRegistrations(IReadOnlyList<HotkeyRegistration> results)
    {
        var failed = results.Where(r => !r.Succeeded).ToList();
        _hotkeyStatus.Text = failed.Count == 0
            ? $"All {results.Count} hotkeys registered."
            : string.Join("  ·  ", failed.Select(f => $"{DefaultHotkeys.Describe(f.Action)}: {f.Error}"));
    }

    public void ReportStatus(string message)
    {
        _status.Text = message;

        // Quota, cache and the router log were only read at startup, so they still showed zeroes
        // after a translation had plainly succeeded. Anything the session reports is a good moment
        // to re-read them.
        _ = RefreshAsync();
    }

    private async Task TestTranslationAsync()
    {
        _overlay.ShowLoading("Y'shtola");
        _status.Text = "Translating...";

        try
        {
            var frame = Core.Diagnostics.SyntheticFrames.Render(
                new Core.Diagnostics.SyntheticLine("Y'shtola", "Come, the aether here grows unstable."));

            var outcome = await _services.Pipeline.ProcessAsync(frame, CancellationToken.None);

            if (outcome.Result.IsFallbackEnglish)
                _overlay.ShowFallbackEnglish(outcome.Speaker, outcome.Result.Text);
            else
                _overlay.ShowTranslation(outcome.Speaker, outcome.Result.Text);

            _status.Text = $"OCR \"{outcome.Body}\" -> {outcome.Result.Provider}/{outcome.Result.Model} " +
                           $"in {outcome.Total.TotalMilliseconds:F0} ms ({outcome.Result.Outcome})";
        }
        catch (Exception e)
        {
            _status.Text = $"Test failed: {e.Message}";
        }

        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var limits = _services.Models.Enabled(includeDevOnly: true)
                .Select(p => (p.Name, p.Rpd)).ToList();
            var quota = await _services.Quota.SnapshotAsync(limits, CancellationToken.None);
            var stats = await _services.Cache.GetStatsAsync(CancellationToken.None);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _quota.Text = "Quota today:  " + string.Join("   ·   ", quota.Select(q => q.ToString()));
                _cache.Text = $"Cache:  {stats.Entries} entries ({stats.Overrides} corrected)   ·   " +
                              $"{stats.Hits}/{stats.Lookups} hits ({stats.HitRate:P0})";
                _routerLog.Text = string.Join('\n', _services.RouterLog.TakeLast(40));
            });
        }
        catch (Exception e)
        {
            _status.Text = $"Could not read diagnostics: {e.Message}";
        }
    }

    // ── small helpers, kept local so the layout above reads as a list of rows ──────────────

    private static TextBox KeyBox() => new() { PasswordChar = '•', Width = 380, Watermark = "not set" };

    private static TextBlock Section(string text) => new()
    {
        Text = text, FontSize = 14, FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 10, 0, 0),
        Foreground = new SolidColorBrush(Color.Parse("#8ab4f8")),
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.Parse("#9aa0a6")),
    };

    private static TextBlock Warning(string text) => new()
    {
        Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.Parse("#fdd663")),
    };

    private static TextBlock Readout() => new()
    {
        Text = "", FontSize = 12, TextWrapping = TextWrapping.Wrap,
    };

    private static Button Button(string text, Action onClick)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Control Row(string label, Control control, double labelWidth = 110) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        Children =
        {
            new TextBlock { Text = label, Width = labelWidth, VerticalAlignment = VerticalAlignment.Center },
            control,
        },
    };
}

internal static class ControlExtensions
{
    /// <summary>Lets a panel be configured inline, so tab bodies stay one expression.</summary>
    public static T With<T>(this T control, Action<T> configure)
    {
        configure(control);
        return control;
    }
}
