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

    /// <summary>
    /// How many key slots to show per provider. Deliberately NOT cleared by <see cref="Build"/>:
    /// revealing a slot rebuilds the window, and forgetting it there would close the box the user
    /// had just asked for. Grows on request and from whatever is already saved; never shrinks
    /// within a session, so clearing a key does not make its box vanish under the cursor.
    /// </summary>
    private readonly Dictionary<string, int> _keySlotsShown = [];

    private TextBlock _hotkeyStatus = null!;
    private TextBlock _laneSummary = null!;
    private TextBlock _profileNote = null!;
    private TextBox _correction = null!;
    private ComboBox _register = null!;
    private Slider _fontSize = null!;
    private Slider _opacity = null!;
    private TextBlock _quota = null!;
    private TextBlock _cache = null!;
    private TextBlock _pace = null!;
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

        // Not machine text: it is a sentence about what the app has worked out, and it is the
        // only window onto the adaptive pacing. In Arabic it should mirror like any other.
        _pace = Readout();
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

            var shown = SlotsShownFor(provider);
            for (var slot = 1; slot <= shown; slot++)
                stack.Children.Add(KeyRow(provider, slot));

            // Offered only on the free lanes. A second paid key is the same bill twice over, so
            // the button would be an invitation to do nothing useful.
            if (shown < ProviderConfig.MaxKeys && !provider.IsPaid)
                stack.Children.Add(Button(_text.AddAnotherKey, () => RevealAnotherKeySlot(provider)));
        }

        if (_services.Models.Providers.Any(p => !p.IsPaid && p.Secret is not null))
        {
            stack.Children.Add(Note(_text.FreeProvidersNote));
            stack.Children.Add(Note(_text.ExtraKeysNote));
        }

        stack.Children.Add(Note(_text.TestKeysNote));

        // Kept, but no longer load-bearing: keys now persist when a box loses focus and when a
        // test passes. It sits below four provider blocks and two paragraphs, so relying on
        // someone scrolling to it - past a Test button that had just told them the key works -
        // was the reason a fully configured-looking app had no keys at all.
        stack.Children.Add(Button(_text.SaveKeys, SaveSecrets));

        // Directly beneath, because this is the only readout on the screen that reports what the
        // ROUTER will see rather than what a text box contains. Those two disagreeing silently is
        // the entire bug this section now exists to make visible.
        stack.Children.Add(Section(_text.ActiveLanes));
        stack.Children.Add(_laneSummary);

        return stack;
    }

    /// <summary>
    /// One box for the first key, plus one for every extra key already saved, plus any the user has
    /// asked for this session. Never fewer than one and never more than the router can use.
    /// </summary>
    private int SlotsShownFor(ProviderConfig provider)
    {
        // The HIGHEST slot holding a key, not how many hold one. Counting them hides a key
        // whenever the occupied slots have a gap - clear box 1 of a two-key setup and slot 2's
        // key still authenticates every line, still shows up in the lane summary on this same
        // screen, and has no box to see it in, edit it or clear it. Emptying the boxes that ARE
        // shown then reports "All keys cleared. Nothing will be translated until one is entered."
        // while translation carries on, which is the one sentence this screen must never get wrong.
        var highestSaved = provider.KeySlots()
            .Where(slot => _services.Secrets.Has(provider.SecretSlot(slot)))
            .DefaultIfEmpty(1)
            .Max();

        var asked = _keySlotsShown.GetValueOrDefault(provider.Name, 1);

        return Math.Clamp(Math.Max(highestSaved, asked), 1, ProviderConfig.MaxKeys);
    }

    /// <summary>
    /// Rebuilds the window with one more key box for this provider. Safe to lose the typed text:
    /// clicking the button moves focus off whichever box had it, and that fires the LostFocus that
    /// persists it - so <see cref="LoadSecrets"/> puts it straight back.
    /// </summary>
    private void RevealAnotherKeySlot(ProviderConfig provider)
    {
        _keySlotsShown[provider.Name] = SlotsShownFor(provider) + 1;

        Build(_tabs?.SelectedIndex ?? 0);
        LoadSecrets();
        UpdateLaneSummary();
    }

    private Control KeyRow(ProviderConfig provider, int slot)
    {
        var secretName = provider.SecretSlot(slot);
        var box = KeyBox(_text.PasteKeyHere);
        _keyBoxes[secretName] = box;

        // Persist as soon as the user leaves the box. The explicit Save button remains, but it can
        // no longer be the ONLY way a key reaches the store: it sits at the bottom of this tab
        // below four provider blocks, while the Test button is right here saying the key works.
        // A user who pastes a key, tests it, sees «يعمل» and starts a game has done everything the
        // interface asked of them - and got "no API key for gemini, groq" in the router log,
        // because a positive verdict from an adjacent button reads as "you are set up".
        box.LostFocus += (_, _) => PersistKey(secretName, box.Text);

        // The verdict goes beside the box it is about, not on the shared status line. A user
        // testing four keys in a row needs to see which one answered what, and a single status line
        // only ever remembers the last.
        var verdict = new TextBlock
        {
            Text = "",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var test = Button(_text.TestKey, () => _ = TestKeyAsync(provider, slot, verdict));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(box);
        row.Children.Add(test);
        row.Children.Add(TierBadge(provider, _text));
        row.Children.Add(verdict);

        var stack = new StackPanel { Spacing = 4 };

        // Numbered only from the second one. A single key does not need to be told it is the first.
        if (slot > 1) stack.Children.Add(Note(string.Format(_text.KeySlot, slot)));

        stack.Children.Add(row);

        // Said once per provider rather than once per box: three copies of the same URL under
        // three boxes is noise, and the model list is the same list for every key.
        if (slot == 1)
        {
            if (!string.IsNullOrWhiteSpace(provider.KeyUrl))
                stack.Children.Add(Note($"{_text.KeyFrom} {provider.KeyUrl}", machine: true));

            stack.Children.Add(Note(
                $"{_text.ModelsInOrder} {string.Join(" → ", provider.Models)}", machine: true));
        }

        return stack;
    }

    /// <summary>
    /// Answers "does this key work?" before the user is in a game.
    ///
    /// <para>
    /// A mistyped or expired key was previously indistinguishable from a correct one right up to
    /// the first translation, where the symptom is English on the overlay and the real reason in an
    /// English router log — not a diagnosable failure for the person this app is for. The key in
    /// the box is used rather than the saved one, so testing works before saving, which is the
    /// order people actually do it in.
    /// </para>
    /// </summary>
    private async Task TestKeyAsync(ProviderConfig provider, int slot, TextBlock verdict)
    {
        var secretName = provider.SecretSlot(slot);
        var typed = _keyBoxes.TryGetValue(secretName, out var box) ? box.Text?.Trim() : null;

        if (string.IsNullOrWhiteSpace(typed))
        {
            Paint(verdict, _text.PasteKeyHere, "#9aa0a6");
            return;
        }

        Paint(verdict, _text.TestingKey, "#9aa0a6");

        // Probed as the slot it actually is, so a failure names "gemini#2" rather than blaming the
        // key in the first box.
        var result = await Task.Run(() => KeyProbe.TestAsync(
            ProviderFactory.Create(provider, _services.Http, new SingleKeyStore(secretName, typed), slot),
            TimeSpan.FromSeconds(20),
            CancellationToken.None));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // A key that has just proved it works is a key worth keeping. Not saving here was the
            // whole defect: the verdict said «يعمل» while the store stayed empty, so the app the
            // user then went and played with had no key at all. A key that FAILED is still not
            // persisted - there is no value in storing something known to be refused.
            if (result.Status == KeyStatus.Working) PersistKey(secretName, typed);

            var (message, colour) = result.Status switch
            {
                KeyStatus.Working => (_text.KeyWorksSaved, "#81c995"),
                KeyStatus.Rejected => (_text.KeyRejected, "#f28b82"),
                KeyStatus.NotSet => (_text.PasteKeyHere, "#9aa0a6"),
                _ => (_text.KeyUnknown, "#fdd663"),
            };

            Paint(verdict, message, colour);

            // The provider's own words go to the status line, not into the badge: they are English,
            // machine-shaped, and often long. The badge stays a word the user can read at a glance.
            if (result.Detail is { Length: > 0 } detail && result.Status != KeyStatus.Working)
                _status.Text = $"{provider.Label}: {detail}";
        });
    }

    private static void Paint(TextBlock target, string text, string colour)
    {
        target.Text = text;
        target.Foreground = new SolidColorBrush(Color.Parse(colour));
    }

    /// <summary>
    /// Presents one just-typed key to the provider factory without saving it. Testing before saving
    /// is the order people actually work in, and a key that fails should not have been persisted.
    /// </summary>
    private sealed class SingleKeyStore(string name, string value) : ISecretStore
    {
        public string? Get(string secretName) => secretName == name ? value : null;

        public bool Has(string secretName) => secretName == name;

        public void Set(string secretName, string secretValue) { }

        public void Delete(string secretName) { }
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

        var diacritics = new CheckBox
        {
            Content = _text.Diacritics,
            IsChecked = _settings.Diacritics,
        };
        diacritics.IsCheckedChanged += (_, _) =>
        {
            _settings.Diacritics = diacritics.IsChecked == true;
            _settings.Save();

            // Straight into the live pipeline, not only into the file. The strip runs on the way
            // out, so this re-presents lines already cached rather than only affecting sentences
            // the player has not reached yet - which is what makes it worth being a switch at all.
            _services.Pipeline.Diacritics = _settings.Diacritics;
            _status.Text = _settings.Diacritics ? _text.DiacriticsShown : _text.DiacriticsHidden;
        };
        stack.Children.Add(diacritics);
        stack.Children.Add(Note(_text.DiacriticsNote));

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

        // Snip lived on the toolbar and a hotkey and nowhere else, which made it the one action a
        // user could only find by hovering an unlabelled shape or reading the readme. Nothing is
        // allowed to exist on one surface alone.
        var snipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        snipRow.Children.Add(Button(_text.ToolbarSnip, () => _ = SnipAsync()));
        snipRow.Children.Add(Button(_text.MoveMode, () => MoveModeToggled?.Invoke()));
        stack.Children.Add(snipRow);
        stack.Children.Add(Note(_text.MoveModeNote));

        // What is on screen, and how fast it should be read. Here rather than on the Hotkeys tab,
        // where these three first landed: what is being translated is this tab's whole subject, and
        // nobody looking for it goes to a tab named after key bindings.
        AddWatchPacing(stack);

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

        var vertical = _overlayVertical = new Slider
        {
            Minimum = 0, Maximum = 1, Value = _settings.OverlayVertical, Width = 240,
        };
        var horizontal = _overlayHorizontal = new Slider
        {
            Minimum = 0, Maximum = 1, Value = _settings.OverlayHorizontal, Width = 240,
        };

        vertical.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase_ValueProperty) return;
            _settings.OverlayVertical = vertical.Value;
            _settings.Save();
            OverlayPlacementChanged?.Invoke();
        };
        horizontal.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase_ValueProperty) return;
            _settings.OverlayHorizontal = horizontal.Value;
            _settings.Save();
            OverlayPlacementChanged?.Invoke();
        };

        // A wider label column here, so the four sliders still line up under each other now that
        // two of the labels are sentences rather than words.
        const double OverlayLabels = 180;

        stack.Children.Add(Row(_text.FontSize, _fontSize, OverlayLabels));
        stack.Children.Add(Row(_text.PanelOpacity, _opacity, OverlayLabels));
        stack.Children.Add(Row(_text.OverlayVerticalPosition, vertical, OverlayLabels));
        stack.Children.Add(Row(_text.OverlayHorizontalPosition, horizontal, OverlayLabels));
        stack.Children.Add(Note(_text.OverlayPositionNote));
        stack.Children.Add(Note(_text.OverlayNote));

        // Only when it actually failed. The overlay reading its own Arabic back is the failure
        // this warns about, and it is invisible from the inside - the translations simply get
        // stranger. Silence when the exclusion worked, which is every supported Windows build.
        if (_overlay.CaptureExclusionWarning is not null)
            stack.Children.Add(Warning(_text.OverlayCaptureWarning));

        // The answer to "why is the translation missing from my recording". It is deliberate, and
        // until now there was no way to say so or to change it.
        var recordable = new CheckBox
        {
            Content = _text.AllowRecording,
            IsChecked = !_settings.HideOverlayFromCapture,
        };
        recordable.IsCheckedChanged += (_, _) =>
        {
            _settings.HideOverlayFromCapture = recordable.IsChecked != true;
            _settings.Save();

            // Applied to the live window, not just stored. A setting whose effect needs a restart
            // is a setting people conclude does not work.
            _overlay.HideFromCapture = _settings.HideOverlayFromCapture;
        };
        stack.Children.Add(recordable);
        stack.Children.Add(Note(_text.AllowRecordingNote));

        var toolbar = new CheckBox
        {
            Content = _text.ShowToolbar,
            IsChecked = _settings.ShowToolbar,
        };
        toolbar.IsCheckedChanged += (_, _) =>
        {
            _settings.ShowToolbar = toolbar.IsChecked == true;
            _settings.Save();
            FloatingWindowsChanged?.Invoke();
        };
        stack.Children.Add(toolbar);
        stack.Children.Add(Note(_text.ShowToolbarNote));

        // The escape hatch for one unverified platform behaviour, worded as a symptom rather than
        // as a mechanism: nobody reading this knows what WS_EX_NOACTIVATE is, and they should not
        // need to. See OverlayStyleOptions.NoActivate for what is actually uncertain. Behind
        // Advanced: it exists for exactly one failure mode, and someone who has not hit it should
        // not be weighing it.
        var focusable = new CheckBox
        {
            Content = _text.ToolbarCanTakeFocus,
            IsChecked = _settings.ToolbarCanTakeFocus,
        };
        focusable.IsCheckedChanged += (_, _) =>
        {
            _settings.ToolbarCanTakeFocus = focusable.IsChecked == true;
            _settings.Save();
            FloatingWindowsChanged?.Invoke();
        };
        stack.Children.Add(Advanced(focusable, Note(_text.ToolbarCanTakeFocusNote)));

        var frame = new CheckBox
        {
            Content = _text.ShowCaptureFrame,
            IsChecked = _settings.ShowCaptureFrame,
        };
        frame.IsCheckedChanged += (_, _) =>
        {
            _settings.ShowCaptureFrame = frame.IsChecked == true;
            _settings.Save();
            FloatingWindowsChanged?.Invoke();
        };
        stack.Children.Add(frame);
        stack.Children.Add(Note(_text.ShowCaptureFrameNote));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Button(_text.PreviewOverlay, () =>
            _overlay.ShowTranslation("Y'shtola", "تعال، فالأثير هنا يزداد اضطراباً.")));
        buttons.Children.Add(Button(_text.ShowHideOverlay, () => _status.Text = _overlay.ToggleHidden()
            ? _text.OverlayShown : _text.OverlayHidden));
        buttons.Children.Add(Button(_text.ResetOverlayPosition, () =>
        {
            vertical.Value = OverlayPlacement.DefaultVertical;
            horizontal.Value = OverlayPlacement.DefaultHorizontal;
        }));
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

    /// <summary>
    /// What is on screen, how often a translation may arrive, and whether the session cap applies.
    ///
    /// <para>
    /// On the Translating tab, not beside the auto-watch button on Hotkeys where these first
    /// landed. That placement had a rationale — it is where someone who has just switched auto-watch
    /// on is looking — and it was wrong: a tab called Hotkeys is where you go to change a key
    /// binding, so three settings that have nothing to do with keys were invisible to anyone not
    /// already told they were there. Whether the screen holds a dialogue box or a film is a fact
    /// about what is being translated, which is what this tab is for.
    /// </para>
    /// </summary>
    private void AddWatchPacing(StackPanel stack)
    {
        stack.Children.Add(Section(_text.WatchMode));

        // Order matters: Auto last, because the two named ones say what the choice is ABOUT. A
        // list that opens with "work it out for me" has to be read backwards to be understood.
        var modes = new[] { WatchMode.Dialogue, WatchMode.Video, WatchMode.Auto };

        var mode = new ComboBox
        {
            ItemsSource = modes.Select(_text.WatchModeName).ToList(),
            SelectedIndex = Array.IndexOf(modes, _settings.WatchMode),
            Width = 240,
        };
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedIndex < 0) return;

            _settings.WatchMode = modes[mode.SelectedIndex];
            _settings.Save();
            _status.Text = string.Format(_text.WatchModeSetTo, _text.WatchModeName(_settings.WatchMode));

            // The toolbar carries the same switch, so it has to follow one changed here or the two
            // disagree about what the app is doing.
            WatchModeChanged?.Invoke();
        };
        stack.Children.Add(Row(_text.WatchMode, mode, labelWidth: 190));
        stack.Children.Add(Note(_text.WatchModeNote));
        stack.Children.Add(Note(_text.WatchModeAutoNote));

        // Zero is "Automatic", shown as a word rather than a 0 - a number that means "no number"
        // is a puzzle, and this control exists because someone asked to be able to set it plainly.
        var seconds = new ComboBox
        {
            ItemsSource = new[] { _text.SecondsBetweenAutomatic, "1", "2", "3", "4", "5", "8" },
            Width = 240,
        };
        seconds.SelectedIndex = _settings.SecondsBetweenTranslations switch
        {
            <= 0 => 0,
            <= 1 => 1, <= 2 => 2, <= 3 => 3, <= 4 => 4, <= 5 => 5, _ => 6,
        };
        seconds.SelectionChanged += (_, _) =>
        {
            _settings.SecondsBetweenTranslations = seconds.SelectedIndex switch
            {
                <= 0 => 0, 6 => 8, var i => i,
            };
            _settings.Save();
        };
        stack.Children.Add(Row(_text.SecondsBetweenTranslations, seconds, labelWidth: 190));
        stack.Children.Add(Note(_text.SecondsBetweenNote));

        var unlimited = new CheckBox
        {
            Content = _text.WatchWithoutLimit,
            IsChecked = _settings.WatchWithoutLimit,
        };
        unlimited.IsCheckedChanged += (_, _) =>
        {
            _settings.WatchWithoutLimit = unlimited.IsChecked == true;
            _settings.Save();
        };

        // Behind Advanced, which is where this checkbox was always headed - it shipped
        // deliberately plain in v0.5.3 so it could move behind whichever advanced concept landed
        // first. Switching off the only session guard there is should take one extra click.
        stack.Children.Add(Advanced(unlimited, Note(_text.WatchWithoutLimitNote)));
    }

    /// <summary>
    /// Simple by default, one control reveals the rest — the toolbar's expander owns this concept
    /// and Settings consumes it rather than inventing a second one, so the app has exactly one
    /// definition of "advanced". Collapsed on every open: what is inside is what almost nobody
    /// should touch, and a sticky-open advanced section stops being advanced.
    /// </summary>
    private Control Advanced(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var child in children) panel.Children.Add(child);

        return new Expander
        {
            Header = _text.AdvancedSection,
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = panel,
        };
    }

    /// <summary>Raised when the watch mode is changed here, so the toolbar's button can follow.</summary>
    public event Action? WatchModeChanged;

    /// <summary>
    /// Raised by the Settings copy of the move-mode button. The App owns the mode, because it is
    /// the only thing holding both windows it unlocks — and because a mode that two surfaces can
    /// each set independently is a mode they can disagree about.
    /// </summary>
    public event Action? MoveModeToggled;

    /// <summary>
    /// Re-reads the two position sliders after the panel has been dragged. Dragging and the
    /// sliders are one setting seen twice, so leaving the sliders showing where the panel used to
    /// be would mean the next slider nudge teleports it back.
    /// </summary>
    public void ReloadOverlayPlacement()
    {
        if (_overlayVertical is { } vertical) vertical.Value = _settings.OverlayVertical;
        if (_overlayHorizontal is { } horizontal) horizontal.Value = _settings.OverlayHorizontal;
    }

    private Slider? _overlayVertical;
    private Slider? _overlayHorizontal;

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
        stack.Children.Add(_pace);

        // One button, all twelve questions, plain words, worst news first. Every failure that has
        // reached a real user so far was something the app could have known and said; this is
        // where it says it, and its output IS the bug report - which is why it sits above the
        // router log rather than below it.
        stack.Children.Add(Section(_text.HealthSection));
        stack.Children.Add(Note(_text.HealthNote));

        var healthResults = new StackPanel { Spacing = 6 };
        var healthButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        healthButtons.Children.Add(Button(_text.HealthRun, () => _ = RunHealthCheckAsync(healthResults)));

        // The same facts as the health check, packaged to leave the machine. Copied to the
        // clipboard because that is the one export every user already knows how to deliver -
        // support here happens in Facebook comments, not issue trackers - and saved to the
        // Desktop as well so "send the file" also works.
        healthButtons.Children.Add(Button(_text.ReportButton, () => _ = CopyReportAsync()));
        stack.Children.Add(healthButtons);
        stack.Children.Add(healthResults);

        // Where the source is. Under the AGPL that is not a courtesy - the whole point of the
        // licence is that whoever ends up with a copy can get the code it was built from - and a
        // link in a readme does not travel with a zip somebody was handed. The URL is machine text,
        // so it stays left-to-right in the mirrored layout.
        stack.Children.Add(Section(_text.LicenceSection));
        stack.Children.Add(Note(_text.LicenceNote));
        stack.Children.Add(Note("https://github.com/BaselMGAG/glass_hud_translator", machine: true));

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
        var lanes = ActiveLanes()
            .Select(lane =>
            {
                // The display name and a slot number, never the raw "gemini#2". This line is read
                // by someone checking their own setup, and it sits in a paragraph that mirrors in
                // Arabic - a Latin lane id would reorder inside it.
                var name = lane.Slot > 1
                    ? $"{lane.Provider.Label} — {string.Format(_text.KeySlot, lane.Slot)}"
                    : lane.Provider.Label;

                var tier = lane.Provider.IsPaid ? $" ({_text.TierPaid})" : "";
                return lane.Live ? $"{name}{tier}" : $"{name} — {_text.NoKeySkipped}";
            })
            .ToList();

        _laneSummary.Text = lanes.Count == 0
            ? _text.NoLanes
            : string.Join("\n", lanes.Select((lane, i) => $"{i + 1}.  {lane}"));
    }

    /// <summary>
    /// The lanes the router will actually walk, in order, as this screen understands them.
    ///
    /// <para>
    /// Built the same way <c>AppServices.BuildLanes</c> builds the real thing, and that duplication
    /// is the point of the readout existing: it is the only control here that reports what the
    /// ROUTER sees rather than what a text box contains, and those two disagreeing silently is the
    /// bug it was added to catch. An empty extra key slot is omitted, because the router skips it
    /// in silence too.
    /// </para>
    /// </summary>
    private IEnumerable<(ProviderConfig Provider, int Slot, bool Live)> ActiveLanes()
    {
        foreach (var provider in _services.Models.Enabled(includeDevOnly: !PlatformServices.IsWindows))
        {
            foreach (var slot in provider.KeySlots())
            {
                var live = provider.Secret is null || _services.Secrets.Has(provider.SecretSlot(slot));
                if (!live && slot > 1) continue;

                yield return (provider, slot, live);
            }
        }
    }

    private void LoadSecrets()
    {
        foreach (var (name, box) in _keyBoxes)
            box.Text = _services.Secrets.Get(name) ?? "";
    }

    /// <summary>
    /// The single way a key reaches the store, used by the Save button, by leaving a key box, and
    /// by a successful test. Writing it three ways would be three chances for one of them to be
    /// the path nobody took.
    ///
    /// <para>
    /// The lane summary is refreshed on every write, because it is the only thing on this screen
    /// that reports what the ROUTER will see rather than what the text box contains - and those
    /// were exactly the two things that disagreed.
    /// </para>
    /// </summary>
    private bool PersistKey(string name, string? text)
    {
        var trimmed = text?.Trim();
        var stored = _services.Secrets.Get(name);

        // Nothing changed. Skip the work: every write is a DPAPI encrypt and a file rewrite, and
        // this now runs on every focus change rather than on a button press.
        if (string.Equals(stored ?? "", trimmed ?? "", StringComparison.Ordinal)) return false;

        if (string.IsNullOrWhiteSpace(trimmed)) _services.Secrets.Delete(name);
        else _services.Secrets.Set(name, trimmed);

        UpdateLaneSummary();
        return true;
    }

    private void SaveSecrets()
    {
        var saved = 0;
        foreach (var (name, box) in _keyBoxes)
        {
            PersistKey(name, box.Text);
            if (!string.IsNullOrWhiteSpace(box.Text)) saved++;
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

        // Proposals are computed while the user is already aiming, and drawn whenever they arrive.
        // The picker owes them nothing: if the OCR pass is slow, or finds nothing, or the user has
        // finished before it returns, the flow is exactly what it was before proposals existed.
        if (screenshot is not null)
        {
            _ = Task.Run(async () =>
            {
                var candidates = await ProposeRegionsAsync(screenshot);
                if (candidates.Count > 0)
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                        () => picker.ShowProposals(candidates));
            });
        }

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

        var profile = await SaveRegionAsync(profileName, region);

        _settings.LastRegionProfile = profileName;
        _settings.Save();

        _status.Text = string.Format(_text.RegionSaved, _text.RegionName(profileName),
            profile.RelWidth.ToString("P0"), profile.RelHeight.ToString("P0"));

        RegionChanged?.Invoke();
    }

    /// <summary>
    /// Turns a rectangle in physical screen pixels into a stored region profile.
    ///
    /// <para>
    /// Shared by the picker and by the draggable capture frame, and shared deliberately rather than
    /// copied. The conversion is the fiddly part — relative to the game's client area rather than
    /// to the screen, so the profile survives the window being moved, falling back to the whole
    /// desktop when there is no game window and to the primary monitor when there is no desktop
    /// either. Two copies of that would drift, and the symptom of drift is a region that is subtly
    /// in the wrong place only for whichever route the user happened to take.
    /// </para>
    /// </summary>
    private async Task<RegionProfile> SaveRegionAsync(string profileName, CaptureRegion region)
    {
        var desktop = PlatformServices.VirtualDesktop();
        var game = PlatformServices.FindGameWindow(
            _services.Profile.WindowTitles, _services.Profile.ProcessNames);

        var origin = game?.ClientArea
                     ?? (desktop.IsEmpty
                         ? new CaptureRegion(0, 0,
                             Screens.Primary?.Bounds.Width ?? 1920,
                             Screens.Primary?.Bounds.Height ?? 1080)
                         : desktop);

        var profile = RegionProfile.FromPixels(profileName, region.RelativeTo(origin),
            origin.Width, origin.Height, game?.Scaling ?? 1.0);

        await _services.Regions.SaveAsync(_services.Profile.Id, profile, CancellationToken.None);
        return profile;
    }

    /// <summary>
    /// Commits a rectangle the user dragged the capture frame to. Saves under whichever region
    /// profile is currently live, because that is the one the frame was drawn around.
    /// </summary>
    public async Task FrameAdjustedAsync(CaptureRegion region)
    {
        var name = _settings.LastRegionProfile;
        var profile = await SaveRegionAsync(name, region);

        _status.Text = string.Format(_text.FrameAdjusted,
            profile.RelWidth.ToString("P0"), profile.RelHeight.ToString("P0"));

        RegionChanged?.Invoke();
    }

    /// <summary>
    /// Drag a box around anything on screen and translate it once.
    ///
    /// <para>
    /// Same frozen still and same picker as choosing a region, in one-shot mode: the box commits on
    /// release rather than on Enter. What it does NOT do is touch anything the watched region owns
    /// — see <see cref="TranslationSession.SnipAsync"/>, where the list of things a snip must leave
    /// alone is longer than the feature itself.
    /// </para>
    /// </summary>
    public async Task SnipAsync()
    {
        _overlay.Clear();
        await Task.Delay(120);

        var desktop = PlatformServices.VirtualDesktop();
        var screenshot = PlatformServices.CaptureFullScreen();

        var picker = new RegionPickerWindow(
            _settings.LastRegionProfile, screenshot, TestRegionAsync, _text, snip: true);

        // Not ShowDialog against Settings. Settings is usually behind a fullscreen game when this
        // is reached from the toolbar, and a modal owned by a hidden window is a hang from the
        // outside. Awaiting the close gives the same sequencing without the ownership.
        var closed = new TaskCompletionSource();
        picker.Closed += (_, _) => closed.TrySetResult();
        picker.Show();
        await closed.Task;

        if (picker.Result is not { } picked)
        {
            _status.Text = _text.SnipCancelled;
            return;
        }

        await _session.SnipAsync(screenshot is null ? picked : picked.Translate(desktop.X, desktop.Y));
    }

    /// <summary>
    /// Re-reads the language from settings and rebuilds, for a caller that changed it from
    /// outside — the first-run wizard being the one such caller. The same sequence the language
    /// ComboBox runs; a no-op when nothing changed.
    /// </summary>
    public void ReloadLanguage()
    {
        if (_text.Language == _settings.Language) return;

        _text = UiText.For(_settings.Language);
        Build();
        LoadSecrets();
        UpdateLaneSummary();
        _ = RefreshAsync();
    }

    /// <summary>
    /// Raised when the stored capture region changes, so the visible frame can move to match it
    /// rather than outlining where the region used to be.
    /// </summary>
    public event Action? RegionChanged;

    /// <summary>
    /// Raised when the toolbar or the capture frame is switched on or off, or when the toolbar's
    /// focus behaviour changes. One event rather than three, because the App's response to all of
    /// them is the same: re-read the settings and apply them to the live windows.
    /// </summary>
    public event Action? FloatingWindowsChanged;

    /// <summary>
    /// Gathers the facts, hands them to <see cref="Core.Diagnostics.HealthCheck"/> for judgement,
    /// and renders the sentences. The split matters: everything in here is Win32 calls, live key
    /// probes and file checks that need a running install, and everything in there is the logic —
    /// which is why the logic has tests and this has none.
    /// </summary>
    private async Task RunHealthCheckAsync(StackPanel results)
    {
        results.Children.Clear();
        results.Children.Add(Note(_text.HealthRunning));

        try
        {
            var inputs = await Task.Run(GatherHealthInputsAsync);
            var findings = Core.Diagnostics.HealthCheck.Run(inputs, _text);

            results.Children.Clear();
            foreach (var finding in findings) results.Children.Add(HealthRow(finding));
        }
        catch (Exception e)
        {
            results.Children.Clear();
            results.Children.Add(Warning($"{_text.DiagnosticsFailed} {e.Message}"));
        }
    }

    /// <summary>
    /// Runs the full health check and packages everything a bug report needs: version, machine,
    /// settings that matter, every finding, quota, cache, and the tails of both logs. One click,
    /// clipboard plus a Desktop file, so "what should I send you?" stops being a question.
    /// </summary>
    private async Task CopyReportAsync()
    {
        _status.Text = _text.ReportBuilding;

        try
        {
            var inputs = await Task.Run(GatherHealthInputsAsync);
            var findings = Core.Diagnostics.HealthCheck.Run(inputs, _text);
            var report = ComposeReport(inputs, findings);

            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(report);

            var saved = TrySaveReport(report);
            _status.Text = saved is null
                ? _text.ReportCopiedNoFile
                : string.Format(_text.ReportCopied, saved);
        }
        catch (Exception e)
        {
            _status.Text = $"{_text.DiagnosticsFailed} {e.Message}";
        }
    }

    private string ComposeReport(
        Core.Diagnostics.HealthInputs inputs, IReadOnlyList<Core.Diagnostics.HealthFinding> findings)
    {
        var report = new System.Text.StringBuilder();

        report.AppendLine("=== Glass HUD Translator diagnostic report ===");
        report.AppendLine($"version: {UpdateCheck.RunningVersion?.ToString() ?? "0.0.0-dev"}");
        report.AppendLine($"os: {Environment.OSVersion.VersionString}");
        report.AppendLine($"platform: {PlatformServices.Description}");
        if (AppSettings.SafeMode) report.AppendLine("SAFE MODE (saved settings ignored)");
        report.AppendLine($"profile: {_services.Profile.Id} / region: {_settings.LastRegionProfile} "
                          + $"/ language: {_settings.Language} / mode: {_settings.WatchMode}");
        report.AppendLine();

        // ASCII severity markers, deliberately: this text lives on clipboards and in chat apps,
        // outside every font decision this project controls.
        report.AppendLine("--- health check ---");
        foreach (var finding in findings)
        {
            var mark = finding.Severity switch
            {
                Core.Diagnostics.HealthSeverity.Problem => "[X]",
                Core.Diagnostics.HealthSeverity.Warning => "[!]",
                _ => "[OK]",
            };
            report.AppendLine($"{mark} {finding.Text}");
        }

        report.AppendLine();
        report.AppendLine("--- counters ---");
        report.AppendLine(_quota.Text);
        report.AppendLine(_cache.Text);
        if (_pace.Text is { Length: > 0 } pace) report.AppendLine(pace);

        report.AppendLine();
        report.AppendLine("--- router log (latest first) ---");
        foreach (var line in _services.RouterLog.AsEnumerable().Reverse().Take(15))
            report.AppendLine(line);

        if (Core.Diagnostics.StartupLog.Path is { } logPath && File.Exists(logPath))
        {
            report.AppendLine();
            report.AppendLine("--- startup.log ---");
            try
            {
                report.AppendLine(File.ReadAllText(logPath).TrimEnd());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                report.AppendLine($"(unreadable: {e.Message})");
            }
        }

        return report.ToString();
    }

    /// <summary>Desktop, because that is where a non-technical user can find a file again.</summary>
    private static string? TrySaveReport(string report)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop)) return null;

            var path = Path.Combine(desktop, "GlassHudTranslator-report.txt");
            File.WriteAllText(path, report);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or System.Security.SecurityException)
        {
            // The clipboard copy already succeeded; the file is the bonus, not the point.
            return null;
        }
    }

    private async Task<Core.Diagnostics.HealthInputs> GatherHealthInputsAsync()
    {
        var game = PlatformServices.FindGameWindow(
            _services.Profile.WindowTitles, _services.Profile.ProcessNames);

        var wholeScreen = !_services.Profile.IsWindowBound;

        // Every key that exists gets one real request, exactly as the Test button sends one.
        // Empty slots are skipped rather than reported - their emptiness is the normal state -
        // so "no keys at all" falls out as an empty list, which the judge reads correctly.
        var lanes = new List<Core.Diagnostics.LaneHealth>();
        foreach (var provider in _services.Models.Providers)
        {
            if (provider.Secret is null) continue;

            foreach (var slot in provider.KeySlots())
            {
                if (string.IsNullOrWhiteSpace(_services.Secrets.Get(provider.SecretSlot(slot))))
                    continue;

                var lane = ProviderFactory.Create(provider, _services.Http, _services.Secrets, slot);
                var probe = await KeyProbe.TestAsync(lane, TimeSpan.FromSeconds(20), CancellationToken.None);
                lanes.Add(new Core.Diagnostics.LaneHealth(provider.LaneName(slot), probe.Status, probe.Detail));
            }
        }

        // OCR is probed by doing OCR, for the reason the key probe translates: the only thing that
        // proves the natives loaded is the natives running. A tiny rendered frame keeps it cheap,
        // and a throw here is precisely the antivirus-quarantine case worth catching.
        bool ocrWorks;
        string? ocrDetail = null;
        try
        {
            var probeFrame = Core.Diagnostics.SyntheticFrames.Render(
                new Core.Diagnostics.SyntheticLine(null, "health check"));
            _ = await _services.Ocr.RecognizeAsync(probeFrame, CancellationToken.None);
            ocrWorks = true;
        }
        catch (Exception e)
        {
            ocrWorks = false;
            ocrDetail = e.Message;
        }

        var scaling = game?.Scaling
                      ?? await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                          () => Screens.ScreenFromWindow(this)?.Scaling ?? RenderScaling);

        return new Core.Diagnostics.HealthInputs
        {
            SystemLanguage = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            InterfaceLanguage = _settings.Language,
            GameWindowTitle = game?.Title,
            CanCapture = game?.CanCapture ?? true,
            CaptureBlocker = game is { CanCapture: false } ? game.Message : null,
            ProfileTargetsWholeScreen = wholeScreen,
            ProfileName = _services.Profile.DisplayName,
            DisplayScaling = scaling,
            Lanes = lanes,
            ProcessorCount = Environment.ProcessorCount,
            MemoryGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0,
            OcrAvailable = ocrWorks,
            OcrDetail = ocrDetail,
            RegionSaved = await _services.Regions.HasAsync(
                _services.Profile.Id, _settings.LastRegionProfile, CancellationToken.None),
            LastOcrConfidence = _session.LastOcrConfidence,
        };
    }

    /// <summary>
    /// One finding as one row: a coloured severity word, then the sentence. A word rather than a
    /// tick glyph, deliberately — the bundled Arabic font has no symbols, and one unresolvable
    /// codepoint has already poisoned glyph fallback for a whole window once.
    /// </summary>
    private Control HealthRow(Core.Diagnostics.HealthFinding finding)
    {
        var (colour, word) = finding.Severity switch
        {
            Core.Diagnostics.HealthSeverity.Problem => ("#f28b82", _text.HealthProblemWord),
            Core.Diagnostics.HealthSeverity.Warning => ("#fdd663", _text.HealthWarningWord),
            _ => ("#81c995", _text.HealthOkWord),
        };

        var chip = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse(colour)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 10, 0),
            Child = new TextBlock
            {
                Text = word,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse(colour)),
            },
        };

        var body = new TextBlock
        {
            Text = finding.Text,
            FontSize = 13,
            LineSpacing = 3,
            Foreground = new SolidColorBrush(Color.Parse("#e8eaed")),
            TextWrapping = TextWrapping.Wrap,
        };

        var row = new DockPanel { Margin = new Thickness(0, 2) };
        DockPanel.SetDock(chip, Dock.Left);
        row.Children.Add(chip);
        row.Children.Add(body);

        // Machine findings are lane lists and window titles; mirrored, the lane order - which is
        // the cost policy - reads backwards.
        if (finding.Machine) row.FlowDirection = FlowDirection.LeftToRight;

        return row;
    }

    /// <summary>
    /// Reads a crop the picker hands over, so it can show the user exactly what the OCR sees before
    /// they commit to it. Costs no API quota - OCR only, no translation.
    ///
    /// <para>
    /// Takes the pixels rather than a rectangle, and that is the fix rather than a tidy-up: this
    /// used to re-capture the screen while the picker was on top of it, so the "preview" was the
    /// picker's own rendering with the selection box drawn across the text being tested. Returns
    /// the whole result now, so the picker can report the confidence beside the text.
    /// </para>
    /// </summary>
    private Task<Core.Ocr.OcrResult> TestRegionAsync(Frame crop) =>
        _services.Ocr.RecognizeAsync(crop, CancellationToken.None);

    /// <summary>
    /// Finds where the text is, so the picker can ask "is this the dialogue?" instead of relying
    /// on the user to know. One full-frame OCR at native resolution with automatic page
    /// segmentation, fed through <see cref="RegionFinder"/>; candidates come back in the still's
    /// own coordinates, which is the space the picker draws in.
    ///
    /// <para>
    /// Cropped to the game window when there is one, both for speed and because the classifier's
    /// geometry — "dialogue sits in the bottom third" — means the bottom third of the GAME, not of
    /// a two-monitor desktop with the game on the left. Never throws: a proposal is a bonus, and a
    /// picker that fails to open because a suggestion engine crashed has inverted its own value.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<RegionCandidate>> ProposeRegionsAsync(Frame still)
    {
        try
        {
            var desktop = PlatformServices.VirtualDesktop();
            var game = PlatformServices.FindGameWindow(
                _services.Profile.WindowTitles, _services.Profile.ProcessNames);

            // The game's client area, translated into the still's pixel space (the still's origin
            // is the virtual desktop's origin, which is negative on some layouts).
            var crop = game?.ClientArea is { Width: > 0, Height: > 0 } client && !desktop.IsEmpty
                ? client.Translate(-desktop.X, -desktop.Y).ClampTo(
                    new CaptureRegion(0, 0, still.Width, still.Height))
                : new CaptureRegion(0, 0, still.Width, still.Height);

            if (crop.Width < 100 || crop.Height < 100) return [];

            using var engine = PlatformServices.CreateOcrEngine(new Core.Ocr.TesseractOptions
            {
                // PSM 3: fully automatic layout analysis. The per-line engine uses 6 ("one uniform
                // block"), which on a full screen merges the hotbar into the dialogue.
                PageSegmentationMode = 3,
                Preprocess = new Core.Ocr.OcrPreprocessOptions { UpscaleFactor = 1 },
            });

            var read = await engine.RecognizeAsync(still.Crop(crop), CancellationToken.None);
            var found = RegionFinder.Propose(read.Words, crop.Width, crop.Height);

            // Back into still coordinates, so the picker's letterbox mapping applies unchanged.
            return [.. found.Select(c => c with { Bounds = c.Bounds.Translate(crop.X, crop.Y) })];
        }
        catch (Exception)
        {
            return [];
        }
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

    /// <summary>
    /// Raised while a position slider is being dragged. The App owns where the overlay goes,
    /// because only it knows which rectangle is the game; this window only knows the fractions.
    /// </summary>
    public event Action? OverlayPlacementChanged;

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

            // Not the live screen, so it does not claim to be. A rendered frame with a known-good
            // line: if nothing comes back the pipeline is broken, not the capture region.
            var outcome = await _services.Pipeline.ProcessAsync(frame, regionKey: null,
                Core.Pipeline.SourceKind.RecordedFrame, Core.Pipeline.ProcessOptions.Manual,
                CancellationToken.None);

            if (outcome.Result is not { } result)
            {
                // ShowError, not just a status line. The overlay is showing "جارٍ الترجمة" from
                // line one of this method and nothing else will ever clear it - which is the
                // v0.1.0 bug in CLAUDE.md exactly: a failure reported only to the Settings status
                // line, leaving the overlay stuck on "translating", which reads as a hang.
                _overlay.ShowError(_text.NoTextInRegion, _text.IsRightToLeft);
                _status.Text = $"{_text.TestFailed} {_text.NoTextInRegion}";
                return;
            }

            if (result.IsFallbackEnglish)
                _overlay.ShowFallbackEnglish(outcome.Speaker, result.Text);
            else
                _overlay.ShowTranslation(outcome.Speaker, result.Text);

            _status.Text = string.Format(_text.TestResult,
                result.Provider, result.Model,
                outcome.Total.TotalMilliseconds.ToString("F0"), result.Outcome);
        }
        catch (Exception e)
        {
            _status.Text = $"{_text.TestFailed} {e.Message}";
        }

        await RefreshAsync();
    }

    /// <summary>
    /// What auto-watch has measured about the thing it is watching, in a sentence.
    ///
    /// <para>
    /// Everything else in this app runs on a number somebody chose in advance. The pacing does not:
    /// it times the gaps between lines and tightens its own deadline to match, so a dialogue box
    /// that advances every eight seconds and subtitles that change every three get different
    /// timings without anyone being asked which is which. This line is that made visible — if the
    /// overlay feels slow, the first useful question is what rhythm the app thinks it is watching.
    /// </para>
    /// </summary>
    private string DescribePace()
    {
        if (_session.WatchStats is not { } stats) return "";

        var pace = stats.Outrunning
            ? _text.OutrunningTheFloor
            : stats.Cadence is { } cadence
                ? string.Format(_text.LearnedPace, cadence.TotalSeconds.ToString("0.0"))
                : _text.LearnedPaceUnknown;

        // In Auto, what it has decided matters more than the cadence - it is the thing the user
        // did not choose, so it is the thing they have to be able to check.
        if (_settings.WatchMode == WatchMode.Auto && _session.ContentVerdict is { } verdict)
        {
            pace += "   ·   " + (verdict.Kind == ContentKind.Unknown
                ? _text.ContentUndecided
                : string.Format(_text.ContentDecided, _text.WatchModeName(verdict.Running)));
        }

        return pace;
    }

    private async Task RefreshAsync()
    {
        try
        {
            // Per LANE, not per provider. Usage is recorded under the lane that answered, so
            // reading it back per provider would hide everything a second or third key spent -
            // which is the one question adding a second key raises.
            var limits = _services.Models.LimitsFor(
                ActiveLanes().Select(lane => lane.Provider.LaneName(lane.Slot)));

            var quota = await _services.Quota.SnapshotAsync(limits, CancellationToken.None);
            var stats = await _services.Cache.GetStatsAsync(CancellationToken.None);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _quota.Text = $"{_text.QuotaToday}  " +
                              string.Join("   ·   ", quota.Select(q => q.ToString()));
                _pace.Text = DescribePace();
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

    /// <summary>
    /// <paramref name="labelWidth"/> is a MINIMUM, not a width. As a fixed width it silently
    /// truncated any label longer than it: "Position, top to bo", and in Arabic
    /// «الموضع من أعلى إلى أسفل» clipped to «ع من أعلى إلى أسفل», which is not a word. Alignment
    /// across rows is worth having, but never at the price of a label that lies - a translated
    /// string is longer than its English original often enough that a fixed width is a trap set
    /// for whichever language is not the one being looked at.
    /// </summary>
    private static Control Row(string label, Control control, double labelWidth = 110) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        Children =
        {
            new TextBlock { Text = label, MinWidth = labelWidth, VerticalAlignment = VerticalAlignment.Center },
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
