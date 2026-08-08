using System.Text.Json;
using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Platform;
using GlassHudTranslator.Core.Profiles;
using GlassHudTranslator.Core.Text;
using GlassHudTranslator.Core.Regions;
using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Translation;
using GlassHudTranslator.Core.Update;
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
    private TextBlock _updateStatus = null!;
    private TabControl? _tabs;
    private Control? _shellRoot;

    /// <summary>
    /// The newest release seen, or null. Held on the window rather than re-fetched, so switching
    /// language rebuilds the banner from what is already known instead of asking GitHub again.
    /// </summary>
    private AvailableUpdate? _update;

    private bool _updateDismissed;

    public SettingsWindow(AppServices services, OverlayWindow overlay, AppSettings settings,
        TranslationSession session)
    {
        _services = services;
        _overlay = overlay;
        _settings = settings;
        _session = session;
        _text = UiText.For(settings.Language);

        // Widened with the note text so the explanatory paragraphs wrap less. Height stays inside
        // 768 - the shortest screen this is likely to run on is a laptop, and a settings window
        // whose Save button is below the bottom of the display is worse than one that scrolls.
        Width = 800;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // Rebuilt from what the last check found, before the window is laid out, so someone who has
        // been putting off an update sees it as the window opens rather than a second later.
        _update = UpdateCheck.FromRememberedTag(settings.LastSeenRelease, UpdateCheck.RunningVersion);

        Build();

        LoadSecrets();
        UpdateLaneSummary();
        _ = RefreshAsync();

        // Fire and forget, and silent unless it finds something. Suppressed for the documentation
        // screenshots: whether a banner is in them would otherwise depend on the day they were run.
        if (!Program.HasFlag("--ui-shots")) _ = CheckForUpdateAsync(manual: false);
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
        _updateStatus = Readout();

        // Both are lists of provider names and counts separated by dots. Left in the interface
        // direction they read back to front, and the quota line's order is the lane order.
        _quota = Readout(machine: true);
        _cache = Readout(machine: true);
        _correction = new TextBox { Watermark = _text.CorrectedArabic, Width = 380 };
        _routerLog = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            Height = 160,
            FontFamily = new FontFamily("monospace"),
            FontSize = 12,
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

        // Above the tabs rather than inside one: an update notice that only appears on the tab you
        // happen to open is one most people never see. It is only built when there is something to
        // say, so the window is unchanged for a user who is already current.
        if (BuildUpdateBanner() is { } banner)
        {
            DockPanel.SetDock(banner, Dock.Top);
            root.Children.Add(banner);
        }

        root.Children.Add(statusBar);
        root.Children.Add(tabs);
        return _shellRoot = root;
    }

    /// <summary>
    /// The update notice, or null when there is nothing to say.
    ///
    /// <para>
    /// It spells the whole thing out - which file to download, what to do with it, and what happens
    /// to the setup already on the machine - rather than only saying a version number. The reader
    /// installed this once, possibly weeks ago, possibly with someone else's help, and "an update
    /// is available" on its own leaves them exactly where they were.
    /// </para>
    /// </summary>
    private Control? BuildUpdateBanner()
    {
        if (_update is not { } update || _updateDismissed) return null;

        var body = new StackPanel { Spacing = 6 };

        body.Children.Add(new TextBlock
        {
            Text = string.Format(_text.UpdateAvailable, update.Tag, Running()),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#81c995")),
        });

        body.Children.Add(Note(_text.UpdateDownloadFile));

        // The asset name comes from the release itself, so this names a file that exists rather
        // than one assembled from a pattern. Machine text, and long - it must not be mirrored or
        // wrapped mid-name.
        body.Children.Add(new TextBlock
        {
            Text = update.AssetName,
            FontSize = 14,
            FontFamily = new FontFamily("monospace"),
            TextWrapping = TextWrapping.Wrap,
            FlowDirection = FlowDirection.LeftToRight,
            Foreground = new SolidColorBrush(Color.Parse("#e8eaed")),
        });

        body.Children.Add(Note(_text.UpdateSteps));
        body.Children.Add(Note(_text.UpdateKeepsYourSetup));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0),
        };
        buttons.Children.Add(Button(_text.OpenDownloadPage, () => OpenUrl(update.ReleaseUrl)));
        buttons.Children.Add(Button(_text.DismissUpdate, () =>
        {
            // For this session only. Nothing is written to settings: a user who dismisses a notice
            // has not decided never to update, and the next launch is a fair time to mention it
            // again. Turning the check off entirely is a separate, deliberate switch.
            _updateDismissed = true;
            Build(_tabs?.SelectedIndex ?? 0);
        }));
        body.Children.Add(buttons);

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#26312a")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3d5245")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(20, 14),
            Child = body,
        };
    }

    private static string Running() =>
        UpdateCheck.RunningVersion is { } v ? $"v{v.Major}.{v.Minor}.{v.Build}" : "-";

    /// <summary>
    /// Hands a URL to the OS browser. Avalonia's launcher rather than Process.Start, which needs
    /// UseShellExecute and a platform branch - and this file is not allowed a platform branch.
    /// </summary>
    private void OpenUrl(string url)
    {
        try
        {
            _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception e) when (e is UriFormatException or InvalidOperationException)
        {
            // No browser, or a URL the platform refuses. The address is on screen either way.
            _status.Text = url;
        }
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

        WindowSnapshot.Save(_shellRoot, Width, Height, _text.IsRightToLeft, path);
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
            stack.Children.Add(Warning($"models.json: {problem}", machine: true));

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
        var box = KeyBox(_text.PasteKeyHere);
        _keyBoxes[provider.Secret!] = box;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(box);
        row.Children.Add(TierBadge(provider, _text));

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(row);

        if (!string.IsNullOrWhiteSpace(provider.KeyUrl))
            stack.Children.Add(Note($"{_text.KeyFrom} {provider.KeyUrl}", machine: true));

        stack.Children.Add(Note(
            $"{_text.ModelsInOrder} {string.Join(" → ", provider.Models)}", machine: true));
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
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse(colour)),
        };
    }

    private Control BuildTranslatingTab()
    {
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(Section(_text.WhatAreYouTranslating));
        // Display names, not ids. The list used to show the folder name - "ffxiv", "general" - which
        // was tolerable while both were shipped by us and is not now that a user-created profile
        // shows up as "baldur-s-gate-3". Same defect as building a button caption out of a stored
        // key: the identifier is not what the user called it.
        var listed = _services.Profiles.List();
        var profiles = new ComboBox
        {
            ItemsSource = listed.Select(p => p.DisplayName).ToList(),
            SelectedIndex = Math.Max(0, listed.ToList().FindIndex(p => p.Id == _services.Profile.Id)),
            Width = 240,
        };
        profiles.SelectionChanged += (_, _) =>
        {
            if (profiles.SelectedIndex < 0 || profiles.SelectedIndex >= listed.Count) return;

            var id = listed[profiles.SelectedIndex].Id;
            if (id == _settings.ProfileId) return;

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

        // Beside the list they add to, so they are found by someone already looking at the profile
        // list and wondering why their game is not in it.
        var profileActions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 4),
        };
        profileActions.Children.Add(Button(_text.AddGame, () => _ = EditProfileAsync(null)));
        profileActions.Children.Add(Button(_text.EditProfile,
            () => _ = EditProfileAsync(_services.Profile.Id)));
        profileActions.Children.Add(Button(_text.DeleteProfile,
            () => _ = DeleteProfileAsync(_services.Profile.Id)));
        stack.Children.Add(profileActions);

        if (ProfileLibrary.IsReadOnly(_services.Profile.Id))
            stack.Children.Add(Note(_text.ProfileReadOnly));

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
        {
            // Formatted, not concatenated: the region name is a stored English key, and gluing it
            // onto a translated verb produced "حدد dialogue" - a button half in each language.
            regionButtons.Children.Add(Button(
                string.Format(_text.PickRegion, _text.RegionName(name)),
                () => _ = PickRegionAsync(name)));
        }
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

        // Both lines are English platform detail rather than interface text - Win32 names, binary
        // paths - so they are isolated whole rather than translated.
        stack.Children.Add(Note(PlatformServices.Description, machine: true));
        stack.Children.Add(Note(
            $"OCR: {_services.Ocr.Name} — {_services.Ocr.Diagnostics ?? "-"}", machine: true));
        stack.Children.Add(_quota);
        stack.Children.Add(_cache);

        // Above the router log, which is a tall box that would otherwise push this below the fold.
        // The off switch for the only request the app makes that is not a translation should not
        // need scrolling to find.
        stack.Children.Add(Section(_text.Updates));
        stack.Children.Add(Note(_text.CheckForUpdatesNote));

        var enabled = new CheckBox
        {
            Content = _text.CheckForUpdatesLabel,
            IsChecked = _settings.CheckForUpdates,
        };
        enabled.IsCheckedChanged += (_, _) =>
        {
            _settings.CheckForUpdates = enabled.IsChecked == true;
            _settings.Save();
            if (!_settings.CheckForUpdates) _updateStatus.Text = _text.UpdateCheckDisabled;
        };
        stack.Children.Add(enabled);

        var updateActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        // Works whether or not the daily check is on, and ignores the once-a-day throttle: someone
        // pressing this has asked, and answering "not yet, come back tomorrow" would be absurd.
        updateActions.Children.Add(Button(_text.CheckNow, () => _ = CheckForUpdateAsync(manual: true)));
        stack.Children.Add(updateActions);
        stack.Children.Add(_updateStatus);
        DescribeUpdateState();

        stack.Children.Add(Section(_text.RouterLog));
        stack.Children.Add(Note(_text.RouterLogNote));
        stack.Children.Add(_routerLog);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button(_text.TestTranslation, () => _ = TestTranslationAsync()));
        actions.Children.Add(Button(_text.Refresh, () => _ = RefreshAsync()));
        stack.Children.Add(actions);

        return stack;
    }

    /// <summary>What the update line says before any check has run this session.</summary>
    private void DescribeUpdateState()
    {
        _updateStatus.Text = UpdateCheck.IsDevelopmentBuild(UpdateCheck.RunningVersion)
            ? _text.DevelopmentBuildNoUpdates
            : !_settings.CheckForUpdates
                ? _text.UpdateCheckDisabled
                : _settings.LastUpdateCheckUtc is { } last
                    ? string.Format(_text.UpdateCheckOffline, last.ToLocalTime().ToString("d MMM HH:mm"))
                    : "";
    }

    /// <summary>
    /// Asks GitHub whether there is a newer release. Fire-and-forget from the constructor, and
    /// awaited from the Check now button.
    ///
    /// <para>
    /// Never throws and never blocks: <see cref="UpdateCheck.FetchAsync"/> swallows every failure
    /// into null, and this only distinguishes them for the manual case, where silence would look
    /// like a broken button. The automatic case says nothing at all when there is nothing to say.
    /// </para>
    /// </summary>
    public async Task CheckForUpdateAsync(bool manual)
    {
        if (UpdateCheck.IsDevelopmentBuild(UpdateCheck.RunningVersion))
        {
            if (manual) _updateStatus.Text = _text.DevelopmentBuildNoUpdates;
            return;
        }

        if (!manual && !UpdateCheck.IsDue(_settings, DateTime.UtcNow)) return;

        if (manual) _updateStatus.Text = _text.CheckingForUpdates;

        var result = await UpdateCheck.FetchAsync(
            _services.Http, UpdateCheck.RunningVersion, CancellationToken.None);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Only a check that actually reached GitHub resets the daily timer. Stamping a failed
            // one would mean a user who happened to be offline at launch waits another twenty hours
            // before anything tries again.
            if (result.Reached)
            {
                _settings.LastUpdateCheckUtc = DateTime.UtcNow;
                _settings.LastSeenRelease = result.Update?.Tag;
                _settings.Save();
            }

            switch (result.Outcome)
            {
                case UpdateOutcome.UpdateAvailable when result.Update is { } update:
                    _update = update;
                    _updateDismissed = false;
                    _updateStatus.Text = string.Format(_text.UpdateAvailable, update.Tag, Running());
                    Build(_tabs?.SelectedIndex ?? 0);
                    break;

                case UpdateOutcome.UpToDate:
                    // Clears a notice left over from a remembered tag - a release can be deleted,
                    // or the user may have updated by hand since it was recorded.
                    var wasShowing = _update is not null;
                    _update = null;
                    if (manual) _updateStatus.Text = string.Format(_text.UpToDate, Running());
                    if (wasShowing) Build(_tabs?.SelectedIndex ?? 0);
                    break;

                case UpdateOutcome.Unreachable when manual:
                    _updateStatus.Text = _text.UpdateCheckUnavailable;
                    break;
            }
        });
    }

    /// <summary>
    /// Opens the profile editor. A null id creates; anything else edits that profile.
    ///
    /// <para>
    /// Creating chains straight into picking a capture region, because a profile without one does
    /// nothing — a user who is left looking at a new entry in a dropdown has not been helped.
    /// </para>
    /// </summary>
    private async Task EditProfileAsync(string? id)
    {
        if (id is not null && ProfileLibrary.IsReadOnly(id))
        {
            _status.Text = _text.ProfileReadOnly;
            return;
        }

        GameProfile? existing = null;
        if (id is not null)
        {
            try
            {
                existing = _services.Profiles.Load(id);
            }
            catch (Exception e) when (e is IOException or FileNotFoundException
                                          or InvalidDataException or JsonException)
            {
                _status.Text = $"{_text.ProfileSaveFailed} {e.Message}";
                return;
            }
        }

        var editor = new ProfileEditorWindow(_text, _services.Profiles, existing);
        await editor.ShowDialog(this);

        if (editor.SavedId is not { } savedId) return;

        _services.RefreshProfiles();
        _settings.ProfileId = savedId;
        _settings.Save();

        // Reload rather than switch: when editing, the id has not changed but the name, voice and
        // glossary all have, and SwitchProfile short-circuits on an unchanged id.
        _services.ReloadProfile(savedId);

        Build(selectedTab: 1);

        _status.Text = string.Format(
            editor.WasCreated ? _text.ProfileCreated : _text.ProfileUpdated,
            _services.Profile.DisplayName);

        if (editor.WasCreated) await PickRegionAsync(RegionProfile.Names.Dialogue);
    }

    /// <summary>
    /// Deletes a profile, after asking. Also forgets its capture regions, which live in the
    /// database rather than the folder and would otherwise be inherited by any later profile whose
    /// name happened to produce the same id.
    /// </summary>
    private async Task DeleteProfileAsync(string id)
    {
        if (!ProfileLibrary.CanDelete(id))
        {
            _status.Text = _text.ProfileReadOnly;
            return;
        }

        var name = _services.Profile.DisplayName;
        if (!await ConfirmAsync(string.Format(_text.ConfirmDeleteProfile, name),
                _text.ConfirmDelete, _text.KeepProfile))
            return;

        try
        {
            _services.Profiles.Delete(id);
            await _services.Regions.DeleteAllAsync(id, CancellationToken.None);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or InvalidOperationException)
        {
            _status.Text = $"{_text.ProfileSaveFailed} {e.Message}";
            return;
        }

        var remaining = _services.RefreshProfiles();
        var next = remaining.FirstOrDefault() ?? ProfileLibrary.GeneralProfileId;

        _settings.ProfileId = next;
        _settings.Save();
        _services.SwitchProfile(next);

        Build(selectedTab: 1);
        _status.Text = string.Format(_text.ProfileDeleted, name);
    }

    /// <summary>
    /// A yes/no dialog. Deleting a profile takes the user's capture regions and glossary with it,
    /// which is not something a stray click should be able to do.
    /// </summary>
    private async Task<bool> ConfirmAsync(string question, string confirm, string cancel)
    {
        var answer = false;

        var dialog = new Window
        {
            Title = _text.WindowTitle,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            FlowDirection = _text.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            FontFamily = _text.IsRightToLeft ? Fonts.Arabic : FontFamily.Default,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };

        var yes = new Button { Content = confirm };
        yes.Click += (_, _) => { answer = true; dialog.Close(); };
        var no = new Button { Content = cancel, IsDefault = true };
        no.Click += (_, _) => dialog.Close();

        // Cancel first in reading order: the destructive choice should not be the one under the
        // cursor, and Enter closes without deleting.
        buttons.Children.Add(no);
        buttons.Children.Add(yes);

        dialog.Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1e1e1e")),
            Padding = new Thickness(24, 20),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = question,
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap,
                        LineSpacing = _text.IsRightToLeft ? 5 : 3,
                    },
                    buttons,
                },
            },
        };

        await dialog.ShowDialog(this);
        return answer;
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

        // The frozen still now covers every monitor, so its top-left is the virtual desktop's
        // origin rather than (0,0) - negative when a screen sits left of or above the primary.
        var desktop = PlatformServices.VirtualDesktop();
        var screenshot = PlatformServices.CaptureFullScreen();

        var picker = new RegionPickerWindow(profileName, screenshot, TestRegionAsync, _text);
        await picker.ShowDialog(this);

        if (picker.Result is not { } picked)
        {
            _status.Text = string.Format(_text.RegionUnchanged, _text.RegionName(profileName));
            return;
        }

        // The picker works in the still's pixels; everything below works in desktop coordinates.
        // Converting here rather than inside the picker keeps the picker ignorant of monitors.
        //
        // Only when there IS a still. With no screenshot the picker falls back to scaling its own
        // window coordinates, which are already desktop coordinates for the monitor it opened on -
        // translating those by the desktop origin would move them by a screen's width on any layout
        // with a monitor left of the primary. The offset belongs to the frame, so it may only be
        // applied to a rectangle picked on that frame.
        var region = screenshot is null ? picked : picked.Translate(desktop.X, desktop.Y);

        // Stored relative to the game's client area, not the screen, so the profile survives the
        // window being moved. Falls back to the whole desktop when there is no game window.
        var game = PlatformServices.FindGameWindow(_services.Profile.WindowTitles, _services.Profile.ProcessNames);
        var origin = game?.ClientArea
                     ?? (desktop.IsEmpty
                         ? new CaptureRegion(0, 0,
                             Screens.Primary?.Bounds.Width ?? 1920,
                             Screens.Primary?.Bounds.Height ?? 1080)
                         : desktop);

        var relative = region.RelativeTo(origin);

        var profile = RegionProfile.FromPixels(profileName, relative,
            origin.Width, origin.Height, game?.Scaling ?? 1.0);
        await _services.Regions.SaveAsync(_services.Profile.Id, profile, CancellationToken.None);

        _settings.LastRegionProfile = profileName;
        _settings.Save();

        _status.Text = string.Format(_text.RegionSaved, _text.RegionName(profileName),
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
        Text = text, FontSize = 15, FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 10, 0, 0),
        Foreground = new SolidColorBrush(Color.Parse("#8ab4f8")),
    };

    /// <summary>
    /// The grey explanatory paragraphs.
    ///
    /// <para>
    /// These were 11px in #9aa0a6, which is the conventional "this is secondary, skip it" styling -
    /// and exactly wrong here. Nothing in this window is secondary to someone setting it up for the
    /// first time: these paragraphs are what tell them which providers are free, why the lanes are
    /// ordered the way they are, and what a capture region is. The audience is not technical, and
    /// the setting they are being talked through is the whole first-run experience.
    /// </para>
    ///
    /// <para>
    /// So: 13px at #c8ccd0, which is 10.3:1 against this window's background rather than 6.3:1.
    /// The line spacing is extra rather than a fixed line height, per the rule the overlay follows -
    /// Arabic hangs marks below the baseline, and a fixed box clips them. Arabic gets more of it,
    /// because these are dense wrapped paragraphs and it is the script that needs the room.
    /// </para>
    /// </summary>
    private TextBlock Note(string text, bool machine = false) => new()
    {
        Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap,
        LineSpacing = _text.IsRightToLeft ? 5 : 3,
        FlowDirection = Direction(machine),
        Foreground = new SolidColorBrush(Color.Parse("#c8ccd0")),
    };

    private TextBlock Warning(string text, bool machine = false) => new()
    {
        Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap,
        LineSpacing = _text.IsRightToLeft ? 5 : 3,
        FlowDirection = Direction(machine),
        Foreground = new SolidColorBrush(Color.Parse("#fdd663")),
    };

    private TextBlock Readout(bool machine = false) => new()
    {
        Text = "", FontSize = 13, TextWrapping = TextWrapping.Wrap,
        LineSpacing = _text.IsRightToLeft ? 5 : 3,
        FlowDirection = Direction(machine),
    };

    /// <summary>
    /// Machine output - model ids, provider names, URLs, quota counts - stays left-to-right even
    /// when the interface is mirrored.
    ///
    /// <para>
    /// A mirrored paragraph reorders the Latin runs inside it, so
    /// <c>gemini → gemini-2.0-flash → …</c> renders back to front. That is not cosmetic: the order
    /// of the lanes <em>is</em> the cost policy, and a quota readout showing it reversed tells the
    /// user the paid provider is tried first. The obvious fix - wrapping each run in Unicode
    /// isolates, U+2066…U+2069 - was tried and is wrong here: neither character exists in the
    /// bundled Arabic font, and one unresolvable codepoint poisoned glyph fallback for the whole
    /// window, so every Latin word in the interface rendered as an empty box.
    /// </para>
    /// </summary>
    private FlowDirection Direction(bool machine) =>
        !machine && _text.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

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
