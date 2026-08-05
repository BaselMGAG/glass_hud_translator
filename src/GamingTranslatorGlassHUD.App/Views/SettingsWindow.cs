using GamingTranslatorGlassHUD.Core.Capture;
using GamingTranslatorGlassHUD.Core.Platform;
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

    public SettingsWindow(AppServices services, OverlayWindow overlay)
    {
        _services = services;
        _overlay = overlay;

        Title = "GamingTranslatorGlassHUD";
        Width = 720;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = new ScrollViewer { Content = BuildBody() };

        _fontSize.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase_ValueProperty) _overlay.BodyFontSize = _fontSize.Value;
        };
        _opacity.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase_ValueProperty) _overlay.PanelOpacity = _opacity.Value;
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
            ? "Ctrl+Shift+R region   ·   Ctrl+Shift+T translate   ·   Ctrl+Shift+A auto-watch   ·   Ctrl+Shift+F correct"
            : "Global hotkeys are Windows-only. Use the buttons above on macOS."));

        stack.Children.Add(Section("Diagnostics"));
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

    private async Task PickRegionAsync(string profileName)
    {
        var picker = new RegionPickerWindow(profileName);
        await picker.ShowDialog(this);

        if (picker.Result is not { } region)
        {
            _status.Text = $"Region '{profileName}' unchanged.";
            return;
        }

        // On Windows this resolves against the FFXIV client rect (Session 2). On macOS there is no
        // game window, so the screen stands in - enough to exercise storage and the picker itself.
        var bounds = Screens.Primary?.Bounds;
        var width = bounds?.Width ?? 1920;
        var height = bounds?.Height ?? 1080;

        var profile = RegionProfile.FromPixels(profileName, region, width, height, uiScale: 1.0);
        await _services.Regions.SaveAsync(profile, CancellationToken.None);

        _status.Text = $"Saved '{profileName}' as {profile.RelWidth:P0} x {profile.RelHeight:P0} " +
                       $"of the client rect.";
    }

    private async Task TestTranslationAsync()
    {
        _overlay.ShowLoading("Y'shtola");
        _status.Text = "Translating...";

        try
        {
            _services.Pipeline.Register = _register.SelectedIndex == 1
                ? ArabicRegister.Egyptian
                : ArabicRegister.ModernStandard;

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
