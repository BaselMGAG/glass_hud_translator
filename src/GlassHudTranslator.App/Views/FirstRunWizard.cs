using System.Globalization;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Storage;
using GlassHudTranslator.Core.Translation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GlassHudTranslator.App.Views;

/// <summary>
/// The first-run wizard: language, key, game, region — four questions, every one skippable, and
/// the whole thing never seen again once answered.
///
/// <para>
/// It exists because the most expensive failure this project has had was a first run. A user with
/// no key and a Save button parked off-screen produced four rounds of provider archaeology, and
/// the log line that named the real cause was read as a symptom. Every step here is one of the
/// detections doing its work at the moment it matters: the language step is preselected from
/// Windows' own locale, the game step names the window it can already see, the fullscreen blocker
/// is said before the first translation rather than diagnosed after it, and the key step tests
/// with one real request and saves on success — because a Test button that validates without
/// saving is the exact lie that cost v0.5.0 its first day.
/// </para>
///
/// <para>
/// Deliberately a guide, not a gate: Skip is always available, nothing nags afterwards, and every
/// choice lives in Settings for later. The audience includes people helping someone else set up,
/// which is why the language step shows both languages in their own script before any choice has
/// been made.
/// </para>
/// </summary>
public sealed class FirstRunWizard : Window
{
    private readonly AppServices _services;
    private readonly AppSettings _settings;

    private UiText _t;
    private int _step;

    private readonly StackPanel _host = new() { Spacing = 14, Margin = new Thickness(26, 22) };

    // Step state that survives Back/Next.
    private ComboBox? _providerBox;
    private TextBox? _keyBox;
    private TextBlock? _keyVerdict;
    private ComboBox? _gameBox;
    private List<string> _profileIds = [];

    public FirstRunWizard(AppServices services, AppSettings settings)
    {
        _services = services;
        _settings = settings;
        _t = UiText.For(settings.Language);

        Title = "Glass HUD Translator";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#101216"));

        Content = _host;
        ShowStep(0);
    }

    /// <summary>True when the user chose "draw the box now" — the caller opens the picker, with
    /// its proposals, the moment this dialog closes.</summary>
    public bool DrawRegionRequested { get; private set; }

    /// <summary>Rebuilds one step. Public for --wizard-test, which walks all four for the camera.</summary>
    public void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, 3);
        _host.Children.Clear();

        // The language decides direction and font for everything after step 0. Machine rows opt
        // back out locally, exactly as Settings does.
        FlowDirection = _t.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        FontFamily = _t.IsRightToLeft ? Fonts.Arabic : FontFamily.Default;

        switch (_step)
        {
            case 0: BuildLanguageStep(); break;
            case 1: BuildKeyStep(); break;
            case 2: BuildGameStep(); break;
            default: BuildRegionStep(); break;
        }

        if (_step > 0) _host.Children.Add(NavigationRow());
    }

    // ── step 0: language ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Asked before anything else is shown, in both scripts at once, because until it is answered
    /// there is no language to ask it in. Detection 1 chooses which button leads: Windows in
    /// Arabic puts Arabic first and highlighted — the person this app was built for should not
    /// start their first run on the wrong side of the very problem it solves.
    /// </summary>
    private void BuildLanguageStep()
    {
        FlowDirection = FlowDirection.LeftToRight;

        _host.Children.Add(new TextBlock
        {
            Text = $"{UiText.En.WizardWelcome} · {UiText.Ar.WizardWelcome}",
            FontSize = 20,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var arabicFirst = string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ar",
            StringComparison.OrdinalIgnoreCase);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10),
        };

        Control LanguageButton(UiLanguage language, string label, bool leads)
        {
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = label,
                    FontSize = 18,
                    FontFamily = language == UiLanguage.Arabic ? Fonts.Arabic : FontFamily.Default,
                    LineSpacing = language == UiLanguage.Arabic ? 4 : 0,
                },
                Padding = new Thickness(34, 12),
                Background = leads
                    ? new SolidColorBrush(Color.Parse("#8ab4f8"), 0.18)
                    : new SolidColorBrush(Color.Parse("#1c1f26")),
                BorderBrush = new SolidColorBrush(Color.Parse(leads ? "#8ab4f8" : "#343943")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
            };
            button.Click += (_, _) => PickLanguage(language);
            return button;
        }

        var arabic = LanguageButton(UiLanguage.Arabic, "العربية", arabicFirst);
        var english = LanguageButton(UiLanguage.English, "English", !arabicFirst);

        buttons.Children.Add(arabicFirst ? arabic : english);
        buttons.Children.Add(arabicFirst ? english : arabic);
        _host.Children.Add(buttons);
    }

    private void PickLanguage(UiLanguage language)
    {
        _settings.Language = language;
        _settings.Save();
        _t = UiText.For(language);
        ShowStep(1);
    }

    // ── step 1: the key ───────────────────────────────────────────────────────────────────────

    private void BuildKeyStep()
    {
        Heading(_t.WizardStepKey);
        Body(_t.WizardKeyWhy);

        // Free lanes only. The wizard is minutes-old-user territory; the paid providers stay in
        // Settings for the people who already know they want them.
        var free = _services.Models.Providers
            .Where(p => p.Secret is not null && !p.IsPaid
                        && !string.Equals(p.Tier, "local", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var provider in free.Where(p => p.KeyUrl is { Length: > 0 }))
        {
            var link = new Button
            {
                Content = new TextBlock
                {
                    Text = $"{provider.Name}  —  {provider.KeyUrl}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#8ab4f8")),
                },
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2),
                FlowDirection = FlowDirection.LeftToRight,
                HorizontalAlignment = _t.IsRightToLeft
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left,
            };
            link.Click += (_, _) => OpenUrl(provider.KeyUrl!);
            _host.Children.Add(link);
        }

        _providerBox ??= new ComboBox
        {
            ItemsSource = free.Select(p => p.Name).ToList(),
            SelectedIndex = free.Count > 0 ? 0 : -1,
            Width = 170,
            FlowDirection = FlowDirection.LeftToRight,
        };

        _keyBox ??= new TextBox
        {
            Watermark = _t.PasteKeyHere,
            Width = 280,
            FlowDirection = FlowDirection.LeftToRight,
        };

        _keyVerdict ??= new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };

        var test = new Button { Content = _t.TestKey };
        test.Click += (_, _) => _ = TestAndSaveAsync(free);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            FlowDirection = FlowDirection.LeftToRight,
        };
        row.Children.Add(_providerBox);
        row.Children.Add(_keyBox);
        row.Children.Add(test);

        _host.Children.Add(row);
        _host.Children.Add(_keyVerdict);
    }

    /// <summary>
    /// One real request, and saved the moment it works — the two halves that must never separate
    /// again. A Test button that said «يعمل» without persisting is how v0.5.0 shipped a user with
    /// no key and four rounds of misdiagnosis.
    /// </summary>
    private async Task TestAndSaveAsync(List<ProviderConfig> free)
    {
        if (_providerBox is not { SelectedIndex: >= 0 } || _keyBox is null || _keyVerdict is null) return;
        var provider = free[_providerBox.SelectedIndex];

        var typed = _keyBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(typed))
        {
            _keyVerdict.Text = _t.KeyMissing;
            return;
        }

        _keyVerdict.Text = _t.TestingKey;
        _keyVerdict.Foreground = new SolidColorBrush(Color.Parse("#c8ccd0"));

        var secretName = provider.SecretSlot(1);
        var probe = ProviderFactory.Create(provider, _services.Http,
            new ProbeKeyStore(secretName, typed), slot: 1);

        var result = await Task.Run(() =>
            KeyProbe.TestAsync(probe, TimeSpan.FromSeconds(20), CancellationToken.None));

        switch (result.Status)
        {
            case KeyStatus.Working:
                _services.Secrets.Set(secretName, typed);
                _keyVerdict.Text = _t.KeyWorksSaved;
                _keyVerdict.Foreground = new SolidColorBrush(Color.Parse("#81c995"));
                break;

            case KeyStatus.Rejected:
                _keyVerdict.Text = _t.KeyRejected;
                _keyVerdict.Foreground = new SolidColorBrush(Color.Parse("#f28b82"));
                break;

            default:
                _keyVerdict.Text = _t.KeyUnknown;
                _keyVerdict.Foreground = new SolidColorBrush(Color.Parse("#fdd663"));
                break;
        }
    }

    private sealed class ProbeKeyStore(string name, string value) : ISecretStore
    {
        public string? Get(string secretName) => secretName == name ? value : null;

        public bool Has(string secretName) => secretName == name;

        public void Set(string secretName, string secretValue) { }

        public void Delete(string secretName) { }
    }

    // ── step 2: the game ──────────────────────────────────────────────────────────────────────

    private void BuildGameStep()
    {
        Heading(_t.WizardStepGame);
        Body(_t.WizardGameWhy);

        _profileIds = [.. _services.AvailableProfiles];
        var names = _profileIds
            .Select(id => _services.Profiles.LoadOrFallback(id).DisplayName)
            .ToList();

        if (_gameBox is null)
        {
            // Detection 4, at the moment it helps: if a window matching a known profile is open
            // right now, that profile leads. Someone who starts the game first — which the README
            // tells them to — should find their game already chosen.
            var detected = DetectRunningProfile();

            _gameBox = new ComboBox
            {
                ItemsSource = names,
                Width = 300,
                SelectedIndex = Math.Max(0,
                    _profileIds.IndexOf(detected.ProfileId ?? _services.Profile.Id)),
            };

            if (detected is { ProfileId: not null, WindowTitle: not null })
            {
                _host.Children.Add(new TextBlock
                {
                    Text = string.Format(_t.WizardGameFound, detected.WindowTitle),
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.Parse("#81c995")),
                    FlowDirection = FlowDirection.LeftToRight,
                    HorizontalAlignment = _t.IsRightToLeft
                        ? HorizontalAlignment.Right
                        : HorizontalAlignment.Left,
                });
            }
        }

        _host.Children.Add(_gameBox);

        // Detection 2, said here rather than discovered at the first translation: an exclusive-
        // fullscreen game produces nothing but black frames, and the README paragraph about it is
        // read by nobody at the moment it applies.
        var chosen = _gameBox.SelectedIndex >= 0 && _gameBox.SelectedIndex < _profileIds.Count
            ? _services.Profiles.LoadOrFallback(_profileIds[_gameBox.SelectedIndex])
            : null;

        if (chosen is not null && chosen.IsWindowBound)
        {
            var window = PlatformServices.FindGameWindow(chosen.WindowTitles, chosen.ProcessNames);
            if (window is { CanCapture: false })
            {
                _host.Children.Add(new TextBlock
                {
                    Text = string.Format(_t.HealthGameBlocked, window.Title),
                    FontSize = 13,
                    LineSpacing = 3,
                    Foreground = new SolidColorBrush(Color.Parse("#fdd663")),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }
    }

    private (string? ProfileId, string? WindowTitle) DetectRunningProfile()
    {
        var open = PlatformServices.ListOpenWindows();
        if (open.Count == 0) return (null, null);

        foreach (var id in _profileIds)
        {
            var profile = _services.Profiles.LoadOrFallback(id);
            foreach (var fragment in profile.WindowTitles)
            {
                var match = open.FirstOrDefault(w =>
                    w.Title.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return (id, match.Title);
            }
        }

        return (null, null);
    }

    // ── step 3: the region, and out ───────────────────────────────────────────────────────────

    private void BuildRegionStep()
    {
        Heading(_t.WizardStepDone);
        Body(_t.WizardDoneWhy);

        // The one hotkey worth memorising, as machine text on its own line.
        _host.Children.Add(new TextBlock
        {
            Text = $"{_settings.HotkeyFor(Core.Platform.HotkeyAction.TranslateNow)}  —  {_t.HotkeyTranslateNow}",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.Parse("#8ab4f8")),
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = _t.IsRightToLeft
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left,
        });

        var draw = new Button
        {
            Content = _t.WizardDrawNow,
            Padding = new Thickness(20, 8),
            Background = new SolidColorBrush(Color.Parse("#8ab4f8"), 0.18),
            BorderBrush = new SolidColorBrush(Color.Parse("#8ab4f8")),
            BorderThickness = new Thickness(1),
        };
        draw.Click += (_, _) =>
        {
            DrawRegionRequested = true;
            Finish();
        };

        var later = new Button { Content = _t.WizardLater, Padding = new Thickness(20, 8) };
        later.Click += (_, _) => Finish();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(draw);
        row.Children.Add(later);
        _host.Children.Add(row);
    }

    // ── plumbing ──────────────────────────────────────────────────────────────────────────────

    private void Finish()
    {
        ApplyGameChoice();

        // Done means done: the wizard never appears again, whatever was skipped. Nagging a person
        // who chose to skip is how setup flows teach people to fear starting the app.
        _settings.HasCompletedFirstRun = true;
        _settings.Save();
        Close();
    }

    private void ApplyGameChoice()
    {
        if (_gameBox is not { SelectedIndex: >= 0 } box || box.SelectedIndex >= _profileIds.Count)
            return;

        var id = _profileIds[box.SelectedIndex];
        _services.SwitchProfile(id);
        _settings.ProfileId = id;
    }

    private Control NavigationRow()
    {
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };

        var skip = new Button
        {
            Content = _t.WizardSkip,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#9aa0a6")),
        };
        skip.Click += (_, _) => Finish();
        DockPanel.SetDock(skip, Dock.Left);
        row.Children.Add(skip);

        var forward = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var back = new Button { Content = _t.WizardBack };
        back.Click += (_, _) => ShowStep(_step - 1);
        forward.Children.Add(back);

        if (_step < 3)
        {
            var next = new Button
            {
                Content = _t.WizardNext,
                Background = new SolidColorBrush(Color.Parse("#8ab4f8"), 0.18),
                BorderBrush = new SolidColorBrush(Color.Parse("#8ab4f8")),
                BorderThickness = new Thickness(1),
            };
            next.Click += (_, _) =>
            {
                if (_step == 2) ApplyGameChoice();
                ShowStep(_step + 1);
            };
            forward.Children.Add(next);
        }

        row.Children.Add(forward);
        return row;
    }

    private void Heading(string text) => _host.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 19,
        LineSpacing = _t.IsRightToLeft ? 4 : 0,
        Foreground = Brushes.White,
    });

    private void Body(string text) => _host.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 13,
        LineSpacing = 3,
        Foreground = new SolidColorBrush(Color.Parse("#c8ccd0")),
        TextWrapping = TextWrapping.Wrap,
    });

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // The address is on the button; the user can type it. A wizard step must not crash
            // over a missing default browser.
        }
    }

    /// <summary>Snapshots the current step, for --wizard-test.</summary>
    public void SaveSnapshot(string path)
    {
        var width = (int)Width;
        var height = (int)Math.Ceiling(Math.Max(Bounds.Height, 180));

        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(this);
        bitmap.Save(path);
    }
}
