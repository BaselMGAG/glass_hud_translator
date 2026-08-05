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
///
/// <para>
/// Every control here is built rather than declared, so switching the interface language rebuilds
/// the whole tree. That is also why nothing is created in a field initialiser: Avalonia will not
/// re-parent a control that already belongs to a discarded tree, so the second build would throw.
/// </para>
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppServices _services;
    private readonly OverlayWindow _overlay;
    private readonly AppSettings _settings;
    private readonly TranslationSession _session;

    private UiText _text;

    private readonly Dictionary<HotkeyAction, TextBox> _hotkeyBoxes = [];

    /// <summary>Key box per secret name. Built from models.json, never hardcoded.</summary>
    private readonly Dictionary<string, TextBox> _keyBoxes = [];

    private TextBlock _hotkeyStatus = null!;
    private TextBlock _laneSummary = null!;
    private TextBlock _profileNote = null!;
    private TextBox _correction = null!;
    private ComboBox _register = null!;
    private Slider _fontSize = null!;
    private Slider _opacity = null!;
    private TextBlock _quota = null!;
    private TextBlock _cache = null!;
    private TextBlock _status = null!;
    private TextBox _routerLog = null!;
    private TabControl? _tabs;
    private Control? _shellRoot;

    public SettingsWindow(AppServices services, OverlayWindow overlay, AppSettings settings,
        TranslationSession session)
    {
        _services = services;
        _overlay = overlay;
        _settings = settings;
        _session = session;
        _text = UiText.For(settings.Language);

        Width = 760;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Build();

        LoadSecrets();
        UpdateLaneSummary();
        _ = RefreshAsync();
    }

    /// <summary>Tab headers, in order. Used by the documentation screenshot pass.</summary>
    public IReadOnlyList<string> TabNames { get; private set; } = [];

    private static AvaloniaProperty RangeBase_ValueProperty => Slider.ValueProperty;

    // ── shell ─────────────────────────────────────────────────────────────────────────────

    private void Build(int selectedTab = 0)
    {
        Title = _text.WindowTitle;

        // The whole interface mirrors, not only the text inside it: a right-to-left reader expects
        // the label before its field and the tab strip to start on the right.
        FlowDirection = _text.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        // The bundled font, for the same reason the overlay uses it: a Windows machine with no
        // Arabic font installed draws every Arabic string as empty boxes. Set at the window so it
        // inherits into tab headers and button captions, which is where it was first noticed
        // missing - the body text was falling back to a system font that Windows may not have.
        FontFamily = _text.IsRightToLeft ? Fonts.Arabic : FontFamily.Default;

        _hotkeyBoxes.Clear();
        _keyBoxes.Clear();

        _hotkeyStatus = Readout();
        _laneSummary = Readout();
        _profileNote = Note("");
        _status = Readout();
        _quota = Readout();
        _cache = Readout();
        _correction = new TextBox { Watermark = _text.CorrectedArabic, Width = 380 };
        _routerLog = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            Height = 160,
            FontFamily = new FontFamily("monospace"),
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap,

            // The log is machine output - provider names, model ids, HTTP codes - and stays
            // left-to-right even when the interface around it is mirrored.
            FlowDirection = FlowDirection.LeftToRight,
        };

        Content = BuildShell();
        if (_tabs is not null && selectedTab < _tabs.ItemCount) _tabs.SelectedIndex = selectedTab;
    }

    private Control BuildShell()
    {
        var tabs = _tabs = new TabControl { Margin = new Thickness(8, 8, 8, 0) };
        tabs.Items.Add(Tab(_text.TabProviders, BuildProvidersTab()));
        tabs.Items.Add(Tab(_text.TabTranslating, BuildTranslatingTab()));
        tabs.Items.Add(Tab(_text.TabOverlay, BuildOverlayTab()));
        tabs.Items.Add(Tab(_text.TabHotkeys, BuildHotkeysTab()));
        tabs.Items.Add(Tab(_text.TabDiagnostics, BuildDiagnosticsTab()));
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
            FlowDirection = _text.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };
        root.Children.Add(statusBar);
        root.Children.Add(tabs);
        return _shellRoot = root;
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

        if (!_text.IsRightToLeft)
        {
            bitmap.Save(path);
            return;
        }

        // Rendering a right-to-left subtree on its own loses the compensating transform the window
        // applies around it, so the bitmap comes out mirrored - letters and all - even though the
        // window on screen is correct. Flipping it back is exact, because what was applied was a
        // single flip of the whole surface. Documentation-only: nothing here affects the live UI.
        using var buffer = new MemoryStream();
        bitmap.Save(buffer);
        buffer.Position = 0;

        using var rendered = SkiaSharp.SKBitmap.Decode(buffer);
        using var flipped = new SkiaSharp.SKBitmap(rendered.Width, rendered.Height);
        using (var canvas = new SkiaSharp.SKCanvas(flipped))
        {
            canvas.Scale(-1, 1, rendered.Width / 2f, 0);
            canvas.DrawBitmap(rendered, 0, 0);
        }

        using var image = SkiaSharp.SKImage.FromBitmap(flipped);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        encoded.SaveTo(file);
    }

    // ── tabs ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One key field per lane in models.json that needs one. Adding a provider is then a config
    /// edit rather than a UI change, which is the same reason model names live in that file.
    /// </summary>
    private Control BuildProvidersTab()
    {
        var stack = new StackPanel { Spacing = 12 };

        // First control on the first tab, and the only label written in both languages at once:
        // someone who cannot read the interface has to be able to find the switch that fixes it.
        var language = new ComboBox
        {
            ItemsSource = UiText.Choices.Select(c => c.Name).ToList(),
            SelectedIndex = UiText.Choices.ToList().FindIndex(c => c.Language == _text.Language),
            Width = 240,
        };
        language.SelectionChanged += (_, _) =>
        {
            if (language.SelectedIndex < 0) return;
            var chosen = UiText.Choices[language.SelectedIndex].Language;
            if (chosen == _settings.Language) return;

            _settings.Language = chosen;
            _settings.Save();
            _text = UiText.For(chosen);

            Build();
            LoadSecrets();
            UpdateLaneSummary();
            _status.Text = _text.LanguageChanged;
            _ = RefreshAsync();
        };
        stack.Children.Add(Row("Language · اللغة", language, labelWidth: 130));

        // Suppressed while generating documentation screenshots. The banner is true of the machine
        // the shots are rendered on and false of every machine that runs the app: leaving a
        // "keys are stored in PLAINTEXT" warning in the README would tell Windows users something
        // alarming and wrong about their own install.
        if (!PlatformServices.IsWindows && !Program.HasFlag("--ui-shots"))
            stack.Children.Add(Warning(_text.DevBuildWarning));

        stack.Children.Add(Note(_text.ProvidersIntro));

        foreach (var problem in _services.Models.Problems())
            stack.Children.Add(Warning($"models.json: {problem}"));

        foreach (var provider in _services.Models.Providers.Where(p => p.Secret is not null))
        {
            stack.Children.Add(Section(provider.Label));
            stack.Children.Add(KeyRow(provider));
        }

        if (_services.Models.Providers.Any(p => !p.IsPaid && p.Secret is not null))
            stack.Children.Add(Note(_text.FreeProvidersNote));

        stack.Children.Add(Button(_text.SaveKeys, SaveSecrets));
        stack.Children.Add(Section(_text.ActiveLanes));
        stack.Children.Add(_laneSummary);

        return stack;
    }

    private Control KeyRow(ProviderConfig provider)
    {
        var box = KeyBox(_text.NotSet);
        _keyBoxes[provider.Secret!] = box;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(box);
        row.Children.Add(TierBadge(provider, _text));

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(row);

        if (!string.IsNullOrWhiteSpace(provider.KeyUrl))
            stack.Children.Add(Note($"{_text.KeyFrom} {provider.KeyUrl}"));

        stack.Children.Add(Note($"{_text.ModelsInOrder} {string.Join(" → ", provider.Models)}"));
        return stack;
    }

    /// <summary>
    /// The free/paid distinction is the whole reason the paid lanes are worth adding, so it is
    /// stated next to the box rather than buried in a paragraph someone has to read first.
    /// </summary>
    private static Control TierBadge(ProviderConfig provider, UiText text)
    {
        var (label, colour) = provider.Tier.ToLowerInvariant() switch
        {
            ProviderTiers.Paid => (text.TierPaid, "#fdd663"),
            ProviderTiers.Local => (text.TierLocal, "#9aa0a6"),
            _ => (text.TierFree, "#81c995"),
        };

        return new TextBlock
        {
            Text = label,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse(colour)),
        };
    }

    private Control BuildTranslatingTab()
    {
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(Section(_text.WhatAreYouTranslating));
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

                _status.Text = string.Format(
                    picked ? _text.ProfileSwitchedRegionRestored : _text.ProfileSwitchedNoRegion,
                    _services.Profile.DisplayName);
            });
        };
        stack.Children.Add(Row(_text.Profile, profiles));
        UpdateProfileNote();
        stack.Children.Add(_profileNote);

        stack.Children.Add(Section(_text.Arabic));
        _register = new ComboBox
        {
            ItemsSource = new[] { _text.RegisterMsa, _text.RegisterEgyptian },
            SelectedIndex = _settings.Register == ArabicRegister.Egyptian ? 1 : 0,
            Width = 240,
        };
        _register.SelectionChanged += (_, _) =>
        {
            _settings.Register = _register.SelectedIndex == 1
                ? ArabicRegister.Egyptian
                : ArabicRegister.ModernStandard;
            _settings.Save();
            _services.Pipeline.Register = _settings.Register;
            _status.Text = string.Format(_text.RegisterSetTo,
                _settings.Register == ArabicRegister.Egyptian
                    ? _text.RegisterEgyptian
                    : _text.RegisterMsa);
        };
        stack.Children.Add(Row(_text.Register, _register));
        stack.Children.Add(Note(_text.RegisterNote));

        stack.Children.Add(Section(_text.CaptureRegions));
        stack.Children.Add(Note(_text.RegionsNote));
        var regionButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var name in RegionProfile.Names.All)
            regionButtons.Children.Add(Button($"{_text.Pick} {name}", () => _ = PickRegionAsync(name)));
        stack.Children.Add(regionButtons);

        stack.Children.Add(Section(_text.Corrections));
        stack.Children.Add(Note(_text.CorrectionsNote));
        var correctRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        correctRow.Children.Add(_correction);
        correctRow.Children.Add(Button(_text.PinCorrection, () => _ = CorrectCurrentAsync()));
        stack.Children.Add(correctRow);

        return stack;
    }

    private Control BuildOverlayTab()
    {
        var stack = new StackPanel { Spacing = 12 };

        _fontSize = new Slider
        {
            Minimum = 16, Maximum = 48, Value = _settings.OverlayFontSize, Width = 240,
        };
        _opacity = new Slider
        {
            Minimum = 0.3, Maximum = 1.0, Value = _settings.OverlayOpacity, Width = 240,
        };

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

        stack.Children.Add(Row(_text.FontSize, _fontSize));
        stack.Children.Add(Row(_text.PanelOpacity, _opacity));
        stack.Children.Add(Note(_text.OverlayNote));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Button(_text.PreviewOverlay, () =>
            _overlay.ShowTranslation("Y'shtola", "تعال، فالأثير هنا يزداد اضطراباً.")));
        buttons.Children.Add(Button(_text.ShowHideOverlay, () => _status.Text = _overlay.ToggleHidden()
            ? _text.OverlayShown : _text.OverlayHidden));
        stack.Children.Add(buttons);

        return stack;
    }

    private Control BuildHotkeysTab()
    {
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(Note(PlatformServices.IsWindows
            ? _text.HotkeysNoteWindows
            : _text.HotkeysNoteOther));

        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var box = new TextBox
            {
                Text = _settings.HotkeyFor(action).ToString(),
                Width = 200,
                FlowDirection = FlowDirection.LeftToRight,
            };
            _hotkeyBoxes[action] = box;
            stack.Children.Add(Row(_text.HotkeyDescription(action), box, labelWidth: 190));
        }

        var hotkeyButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        hotkeyButtons.Children.Add(Button(_text.ApplyHotkeys, ApplyHotkeys));
        hotkeyButtons.Children.Add(Button(_text.ResetToDefaults, ResetHotkeys));
        stack.Children.Add(hotkeyButtons);
        stack.Children.Add(_hotkeyStatus);

        stack.Children.Add(Section(_text.ManualControls));
        stack.Children.Add(Note(_text.ManualControlsNote));
        var manual = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        manual.Children.Add(Button(_text.TranslateNow, () => _ = _session.TranslateNowAsync()));
        manual.Children.Add(Button(_text.ToggleAutoWatch, _session.ToggleAutoWatch));
        stack.Children.Add(manual);

        return stack;
    }

    private Control BuildDiagnosticsTab()
    {
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(Note(PlatformServices.Description));
        stack.Children.Add(Note($"OCR: {_services.Ocr.Name} — {_services.Ocr.Diagnostics ?? "-"}"));
        stack.Children.Add(_quota);
        stack.Children.Add(_cache);

        stack.Children.Add(Section(_text.RouterLog));
        stack.Children.Add(Note(_text.RouterLogNote));
        stack.Children.Add(_routerLog);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button(_text.TestTranslation, () => _ = TestTranslationAsync()));
        actions.Children.Add(Button(_text.Refresh, () => _ = RefreshAsync()));
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
            ? string.Format(_text.ProfileNoteWindowed, profile.DisplayName, profile.Glossary.Count)
            : string.Format(_text.ProfileNoteScreen, profile.DisplayName);
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
                var tier = p.IsPaid ? $" ({_text.TierPaid})" : "";
                return live ? $"{p.Label}{tier}" : $"{p.Label} — {_text.NoKeySkipped}";
            })
            .ToList();

        _laneSummary.Text = lanes.Count == 0
            ? _text.NoLanes
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
        _status.Text = saved == 0 ? _text.KeysCleared : string.Format(_text.KeysSaved, saved);
    }

    public async Task PickRegionAsync(string profileName)
    {
        // Freeze the screen first so the dialogue does not advance while the user is aiming, and
        // hide our own overlay so it cannot end up inside the captured region.
        _overlay.Clear();
        await Task.Delay(120);
        var screenshot = PlatformServices.CaptureFullScreen();

        var picker = new RegionPickerWindow(profileName, screenshot, TestRegionAsync, _text);
        await picker.ShowDialog(this);

        if (picker.Result is not { } region)
        {
            _status.Text = string.Format(_text.RegionUnchanged, profileName);
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

        _status.Text = string.Format(_text.RegionSaved, profileName,
            profile.RelWidth.ToString("P0"), profile.RelHeight.ToString("P0"));
    }

    /// <summary>
    /// Reads whatever is inside a candidate rectangle, so the picker can show the user exactly what
    /// the OCR sees before they commit to it. Costs no API quota - OCR only, no translation.
    /// </summary>
    private async Task<string> TestRegionAsync(CaptureRegion region)
    {
        var screenshot = PlatformServices.CaptureFullScreen();
        if (screenshot is null) return _text.CaptureWindowsOnly;

        if (!region.FitsWithin(screenshot.Width, screenshot.Height)) return _text.SelectionOffScreen;

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
            _status.Text = _text.NothingToCorrect;
            return;
        }

        var corrected = _correction.Text?.Trim();
        if (string.IsNullOrWhiteSpace(corrected))
        {
            _correction.Text = current.Arabic;
            _status.Text = _text.EditThenPin;
            return;
        }

        await _services.Cache.PutOverrideAsync(
            CacheKey.For(current.Source, _settings.Register), current.Source, corrected, CancellationToken.None);

        _overlay.ShowTranslation(null, corrected);
        _correction.Text = "";
        _status.Text = _text.CorrectionPinned;
        await RefreshAsync();
    }

    private void ApplyHotkeys()
    {
        foreach (var (action, box) in _hotkeyBoxes)
        {
            var parsed = Hotkey.TryParse(box.Text);
            if (parsed is null || !parsed.IsValid)
            {
                _hotkeyStatus.Text = string.Format(
                    _text.HotkeyInvalid, box.Text, _text.HotkeyDescription(action));
                return;
            }

            _settings.SetHotkey(action, parsed);
        }

        var conflicts = _settings.FindConflicts();
        if (conflicts.Count > 0)
        {
            _hotkeyStatus.Text = string.Format(_text.HotkeyConflict,
                string.Join(", ", conflicts.Select(_text.HotkeyDescription)));
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
            ? string.Format(_text.AllHotkeysRegistered, results.Count)
            : string.Join("  ·  ",
                failed.Select(f => $"{_text.HotkeyDescription(f.Action)}: {f.Error}"));
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
        _status.Text = _text.Translating;

        try
        {
            var frame = Core.Diagnostics.SyntheticFrames.Render(
                new Core.Diagnostics.SyntheticLine("Y'shtola", "Come, the aether here grows unstable."));

            var outcome = await _services.Pipeline.ProcessAsync(frame, CancellationToken.None);

            if (outcome.Result.IsFallbackEnglish)
                _overlay.ShowFallbackEnglish(outcome.Speaker, outcome.Result.Text);
            else
                _overlay.ShowTranslation(outcome.Speaker, outcome.Result.Text);

            _status.Text = string.Format(_text.TestResult,
                outcome.Result.Provider, outcome.Result.Model,
                outcome.Total.TotalMilliseconds.ToString("F0"), outcome.Result.Outcome);
        }
        catch (Exception e)
        {
            _status.Text = $"{_text.TestFailed} {e.Message}";
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
                _quota.Text = $"{_text.QuotaToday}  " +
                              string.Join("   ·   ", quota.Select(q => q.ToString()));
                _cache.Text = $"{_text.Cache}  {stats.Entries} {_text.Entries} " +
                              $"({stats.Overrides} {_text.Corrected})   ·   " +
                              $"{stats.Hits}/{stats.Lookups} {_text.Hits} ({stats.HitRate:P0})";
                _routerLog.Text = string.Join('\n', _services.RouterLog.TakeLast(40));
            });
        }
        catch (Exception e)
        {
            _status.Text = $"{_text.DiagnosticsFailed} {e.Message}";
        }
    }

    // ── small helpers, kept local so the layout above reads as a list of rows ──────────────

    private static TextBox KeyBox(string watermark) => new()
    {
        PasswordChar = '•', Width = 380, Watermark = watermark,
        FlowDirection = FlowDirection.LeftToRight,
    };

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
