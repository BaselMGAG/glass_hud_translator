using GamingTranslatorGlassHUD.Core.Capture;
using GamingTranslatorGlassHUD.Core.Config;
using GamingTranslatorGlassHUD.Core.Platform;
using GamingTranslatorGlassHUD.Core.Text;
using GamingTranslatorGlassHUD.Core.Regions;
using GamingTranslatorGlassHUD.Core.Storage;
using GamingTranslatorGlassHUD.Core.Translation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace GamingTranslatorGlassHUD.App.Views;

/// <summary>
/// The app's control panel. Keys, register, overlay appearance, and - importantly - the quota and
/// cache readouts, which are the diagnostics that answer whether anything is actually wrong
/// (brief 12). Deliberately not on the overlay: the overlay must stay unreadable-free while
/// the game is being played.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppServices _services;
    private readonly OverlayWindow _overlay;
    private readonly AppSettings _settings;
    private readonly TranslationSession _session;
    private readonly Dictionary<HotkeyAction, TextBox> _hotkeyBoxes = [];
    private readonly TextBlock _hotkeyStatus = Readout();
    private readonly TextBox _correction = new() { Watermark = "corrected Arabic", Width = 380 };

    private readonly TextBox _geminiKey = KeyBox();
    private readonly TextBox _groqKey = KeyBox();
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
        Height = 110,
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

        Title = "GamingTranslatorGlassHUD";
        Width = 720;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = new ScrollViewer { Content = BuildBody() };

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
        _ = RefreshAsync();
    }

    private static AvaloniaProperty RangeBase_ValueProperty => Slider.ValueProperty;

    private Control BuildBody()
    {
        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(24) };

        stack.Children.Add(Heading("GamingTranslatorGlassHUD"));
        stack.Children.Add(Note(PlatformServices.Description));
        if (!PlatformServices.IsWindows)
        {
            stack.Children.Add(Warning(
                "Development build. Capture replays recorded frames, hotkeys are inactive, and API "
                + "keys are stored in PLAINTEXT. Windows uses BitBlt, RegisterHotKey and DPAPI."));
        }

        stack.Children.Add(Section("API keys"));
        stack.Children.Add(Note(
            "Bring your own keys - neither needs a credit card. Gemini: aistudio.google.com. "
            + "Groq: console.groq.com."));
        stack.Children.Add(Row("Gemini", _geminiKey));
        stack.Children.Add(Row("Groq", _groqKey));
        stack.Children.Add(Button("Save keys", SaveSecrets));

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
            _status.Text = $"Profile set to '{id}'. Restart the app for it to take effect — the "
                         + "glossary and OCR language are loaded at startup.";
        };
        stack.Children.Add(Row("Profile", profiles));
        stack.Children.Add(Note(
            $"Active: {_services.Profile.DisplayName}. A game profile carries that game's glossary "
            + "and measures the capture region against the game window, so it survives the window "
            + "being moved. The 'general' profile has no window of its own — it measures against "
            + "the whole screen, which is what you want for a browser, a PDF or a video player."));

        stack.Children.Add(Section("Translation"));
        stack.Children.Add(Row("Register", _register));
        stack.Children.Add(Note(
            "Modern Standard suits FFXIV's archaic narrative voice. Egyptian lands well for "
            + "merchants and comic relief, and reads as comedy for Elezen nobility."));

        stack.Children.Add(Section("Overlay"));
        stack.Children.Add(Row("Font size", _fontSize));
        stack.Children.Add(Row("Panel opacity", _opacity));
        stack.Children.Add(Button("Preview overlay", () =>
            _overlay.ShowTranslation("Y'shtola", "تعال، فالأثير هنا يزداد اضطراباً.")));

        stack.Children.Add(Section("Regions"));
        stack.Children.Add(Note("FFXIV draws narrative text in three places; each gets its own rectangle."));
        var regionButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var name in RegionProfile.Names.All)
            regionButtons.Children.Add(Button($"Pick {name}", () => _ = PickRegionAsync(name)));
        stack.Children.Add(regionButtons);

        stack.Children.Add(Section("Hotkeys"));
        stack.Children.Add(Note(PlatformServices.IsWindows
            ? "Type a combination such as Ctrl+Shift+T. Modifiers: Ctrl, Shift, Alt, Win. Keys include "
              + "A-Z, 0-9, F1-F24, arrows, Insert/Delete/Home/End, numpad (Num0-Num9) and punctuation. "
              + "F13-F24 are the safest choices - games almost never bind them."
            : "Global hotkeys are Windows-only. On macOS use the buttons on this window instead."));

        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var box = new TextBox { Text = _settings.HotkeyFor(action).ToString(), Width = 200 };
            _hotkeyBoxes[action] = box;
            stack.Children.Add(Row(DefaultHotkeys.Describe(action), box));
        }

        var hotkeyButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        hotkeyButtons.Children.Add(Button("Apply hotkeys", ApplyHotkeys));
        hotkeyButtons.Children.Add(Button("Reset to defaults", ResetHotkeys));
        stack.Children.Add(hotkeyButtons);
        stack.Children.Add(_hotkeyStatus);

        stack.Children.Add(Section("Manual controls"));
        var manual = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        manual.Children.Add(Button("Translate now", () => _ = _session.TranslateNowAsync()));
        manual.Children.Add(Button("Toggle auto-watch", _session.ToggleAutoWatch));
        manual.Children.Add(Button("Show / hide overlay", () => _status.Text = _overlay.ToggleHidden()
            ? "Overlay shown." : "Overlay hidden. Translation carries on in the background."));
        stack.Children.Add(manual);

        stack.Children.Add(Note("Correct the line currently on the overlay. The correction is pinned "
                              + "and always wins over the model in future."));
        var correctRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        correctRow.Children.Add(_correction);
        correctRow.Children.Add(Button("Pin correction", () => _ = CorrectCurrentAsync()));
        stack.Children.Add(correctRow);

        stack.Children.Add(Section("Diagnostics"));
        stack.Children.Add(Note($"OCR: {_services.Ocr.Name} — {_services.Ocr.Diagnostics ?? "no detail"}"));
        stack.Children.Add(_quota);
        stack.Children.Add(_cache);
        stack.Children.Add(_status);
        stack.Children.Add(Note("Router log - a model disappearing upstream shows up here:"));
        stack.Children.Add(_routerLog);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button("Test translation", () => _ = TestTranslationAsync()));
        actions.Children.Add(Button("Refresh", () => _ = RefreshAsync()));
        stack.Children.Add(actions);

        return stack;
    }

    private void LoadSecrets()
    {
        _geminiKey.Text = _services.Secrets.Get(SecretNames.GeminiApiKey) ?? "";
        _groqKey.Text = _services.Secrets.Get(SecretNames.GroqApiKey) ?? "";
    }

    private void SaveSecrets()
    {
        Store(SecretNames.GeminiApiKey, _geminiKey.Text);
        Store(SecretNames.GroqApiKey, _groqKey.Text);
        _status.Text = "Keys saved.";

        void Store(string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) _services.Secrets.Delete(name);
            else _services.Secrets.Set(name, value.Trim());
        }
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
        await _services.Regions.SaveAsync(profile, CancellationToken.None);

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
            CacheKey.For(current.Source), current.Source, corrected, CancellationToken.None);

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
                _routerLog.Text = string.Join('\n', _services.RouterLog.TakeLast(30));
            });
        }
        catch (Exception e)
        {
            _status.Text = $"Could not read diagnostics: {e.Message}";
        }
    }

    // ── small helpers, kept local so the layout above reads as a list of rows ──────────────

    private static TextBox KeyBox() => new() { PasswordChar = '•', Width = 380, Watermark = "not set" };

    private static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 22, FontWeight = FontWeight.SemiBold,
    };

    private static TextBlock Section(string text) => new()
    {
        Text = text, FontSize = 14, FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 14, 0, 0),
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

    private static Control Row(string label, Control control) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        Children =
        {
            new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center },
            control,
        },
    };
}
