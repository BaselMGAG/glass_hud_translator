using GlassHudTranslator.Core.Capture;
using GlassHudTranslator.Core.Config;
using GlassHudTranslator.Core.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace GlassHudTranslator.App.Views;

/// <summary>What each button does. One record so the wiring is in one place and visibly complete.</summary>
public sealed record ToolbarActions(
    Action TranslateNow,
    Action ToggleAutoWatch,
    Action Snip,
    Action Retry,
    Action PickRegion,
    Action ToggleMoveMode,
    Action ToggleCaptureFrame,
    Action ToggleOverlay,
    Action OpenSettings,
    Action ToggleWatchMode,
    Action ToggleDiacritics,
    Action ToggleDialect,
    Action ToggleRecording,
    Action ToggleReadAgain,
    Action PinCorrection,
    Action Quit);

/// <summary>
/// A strip of buttons that floats over the game.
///
/// <para>
/// It exists because of one sentence in a player's report — «كل ما يحصل مشكله اخش علي الاعدادات
/// نفسها», every time something goes wrong he has to go into Settings — and because the honest
/// reading of that is not "add a shortcut to Settings". Every action this app has was reachable
/// only by a key combination the user had to memorise, or by a window that sits behind a fullscreen
/// game. Ctrl+Shift+S fixed getting back to the window. This is the half that means you do not have
/// to.
/// </para>
///
/// <para>
/// <b>Six buttons, then one that opens the rest.</b> A long strip of unlabelled glyphs is a
/// guessing game for anyone, and the reader here is explicitly someone whose English is not good
/// and who is not technical — that is why the Egyptian manual exists. So: six, larger, with names
/// on hover in both languages, and an
/// expander that is itself visible from the simple view so the advanced set is discoverable rather
/// than hidden behind a preference. The same idea as the Basic/Advanced split still sitting on the
/// roadmap, and deliberately the SAME idea: if that ships separately with its own notion of
/// "advanced", the app will have two definitions that can disagree.
/// </para>
///
/// <para>
/// Three implementation choices are load-bearing rather than incidental. The icons are geometry
/// compiled into the assembly, never font glyphs — see <see cref="Icons"/> for the incident that
/// settles that. The window drags itself by assigning <see cref="Window.Position"/> from screen
/// coordinates rather than calling <c>BeginMoveDrag</c>, because that helper works through
/// <c>WM_NCLBUTTONDOWN</c> and this window refuses activation; doing the arithmetic ourselves
/// removes a dependency on behaviour nobody here can test. And the whole thing is excluded from
/// screen capture like the overlay: a strip of bright icons inside the captured rectangle is just
/// more shapes for Tesseract to guess at.
/// </para>
/// </summary>
public sealed class ToolbarWindow : FloatingWindow
{
    private const double IconSize = 20;
    private const double ButtonSize = 34;

    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#e8eaed"));
    private static readonly IBrush ActiveInk = new SolidColorBrush(Color.Parse("#8ab4f8"));
    private static readonly IBrush ActiveFill = new SolidColorBrush(Color.Parse("#8ab4f8"), 0.16);

    private readonly AppSettings _settings;
    private readonly StackPanel _advanced;
    private readonly Border _shell;
    private readonly Panel _handle;
    private readonly Button _expander;

    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);

    private UiText _text;
    private PixelPoint _grabbedAt;
    private PixelPoint _windowWasAt;
    private bool _dragging;

    public ToolbarWindow(UiText text, AppSettings settings, ToolbarActions actions)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(actions);

        _text = text;
        _settings = settings;

        Title = "Glass HUD Translator toolbar";
        SizeToContent = SizeToContent.WidthAndHeight;

        // Never activated on show. Without this, simply displaying the toolbar at startup pulls
        // focus off whatever the user was doing.
        ShowActivated = false;

        // Not Brushes.Transparent. A window drawn with zero alpha is hit-tested as absent by the
        // compositor, so every click would land on the game behind it however the extended styles
        // are set. One part in 255 is invisible and present.
        Background = BarelyThere;

        var grip = new Panel
        {
            Width = 18,
            Height = ButtonSize,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Children = { Icons.Draw(Icons.Grip, 16, new SolidColorBrush(Color.Parse("#6f7580"))) },
        };

        grip.PointerPressed += OnGripPressed;
        grip.PointerMoved += OnGripMoved;
        grip.PointerReleased += OnGripReleased;
        ToolTip.SetTip(grip, BilingualTip.For(_text, UiText.En.ToolbarMove, UiText.Ar.ToolbarMove));

        var simple = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children =
            {
                Make("translate", Icons.TranslateNow, t => t.ToolbarTranslateNow, actions.TranslateNow,
                    HotkeyAction.TranslateNow),
                Make("watch", Icons.AutoWatch, t => t.ToolbarAutoWatch, actions.ToggleAutoWatch,
                    HotkeyAction.ToggleAutoWatch),
                Make("snip", Icons.Snip, t => t.ToolbarSnip, actions.Snip),
                Make("retry", Icons.Retry, t => t.ToolbarRetry, actions.Retry,
                    HotkeyAction.RetryTranslation),
                Make("region", Icons.PickRegion, t => t.ToolbarRegion, actions.PickRegion,
                    HotkeyAction.PickRegion),

                // In the simple row, not behind the expander. Moving the two things that sit over
                // your game is the first thing anyone wants to do about them, and asking a player
                // to find an expander first would be asking them to accept the layout we chose.
                Make("move", Icons.Move, t => t.ToolbarMoveMode, actions.ToggleMoveMode),
                Make("hide", Icons.HideOverlay, t => t.ToolbarHideOverlay, actions.ToggleOverlay,
                    HotkeyAction.ToggleOverlay),
                Make("settings", Icons.Settings, t => t.ToolbarSettings, actions.OpenSettings,
                    HotkeyAction.OpenSettings),
            },
        };

        _advanced = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            IsVisible = settings.ToolbarExpanded,
            Children =
            {
                Divider(),
                Make("frame", Icons.CaptureFrame, t => t.ToolbarCaptureFrame, actions.ToggleCaptureFrame),
                Make("mode", Icons.WatchDialogue, t => t.ToolbarWatchMode, actions.ToggleWatchMode),
                Make("tashkeel", Icons.Diacritics, t => t.ToolbarDiacritics, actions.ToggleDiacritics),

                // Both of these were Settings-only. A dialect is a two-value choice and recording
                // is a switch, and either can be wanted without leaving a game - which is the test
                // for what belongs here.
                Make("dialect", Icons.Dialect, t => t.ToolbarDialect, actions.ToggleDialect),
                Make("recording", Icons.Recording, t => t.ToolbarRecording, actions.ToggleRecording),
                Make("readagain", Icons.ReadAgain, t => t.ToolbarReadAgain, actions.ToggleReadAgain),
                Make("pin", Icons.PinCorrection, t => t.ToolbarPinCorrection, actions.PinCorrection,
                    HotkeyAction.FlagTranslation),
                Make("quit", Icons.Quit, t => t.ToolbarQuit, actions.Quit),
            },
        };

        _expander = Make("expand", settings.ToolbarExpanded ? Icons.Less : Icons.More,
            t => settings.ToolbarExpanded ? t.ToolbarLess : t.ToolbarMore, ToggleExpanded);

        var collapse = Make("collapse", Icons.Collapse, t => t.ToolbarCollapse, () => Collapsed = true);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { grip, simple, _expander, _advanced, Divider(), collapse },
        };

        _shell = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#12141a"), 0.92),
            BorderBrush = new SolidColorBrush(Color.Parse("#2b2f38")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 4),
            Child = row,
        };

        // Deliberately left-to-right even in the Arabic interface. The order of these buttons is
        // not a sentence - it is a fixed spatial arrangement people build muscle memory for, and
        // mirroring it would move every control the moment somebody switched language. The tooltips
        // carry the language; the layout carries the habit.
        _shell.FlowDirection = FlowDirection.LeftToRight;

        _handle = new Panel
        {
            IsVisible = false,
            Cursor = new Cursor(StandardCursorType.Hand),
            Children =
            {
                new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#12141a"), 0.92),
                    BorderBrush = new SolidColorBrush(Color.Parse("#2b2f38")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(6),
                    Child = Icons.Draw(Icons.TranslateNow, 18, Ink),
                },
            },
        };

        _handle.PointerPressed += (_, e) =>
        {
            Collapsed = false;
            e.Handled = true;
        };

        ToolTip.SetTip(_handle, BilingualTip.For(_text, UiText.En.ToolbarShow, UiText.Ar.ToolbarShow));

        Content = new Panel { Children = { _shell, _handle } };
    }

    /// <summary>
    /// Shrunk to a single button. Not "hidden": a toolbar that can disappear entirely needs its own
    /// hotkey to come back, and adding a seventh binding to bring back the thing that exists so you
    /// do not have to remember bindings would be an odd way round.
    /// </summary>
    public bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value) return;

            _collapsed = value;
            _shell.IsVisible = !value;
            _handle.IsVisible = value;
        }
    }

    private bool _collapsed;

    /// <summary>
    /// Clickable, never focused unless the user has asked otherwise, and hidden from capture like
    /// everything else this app floats over the game.
    /// </summary>
    protected override OverlayStyleOptions StyleOptions => OverlayStyleOptions.Interactive with
    {
        NoActivate = !_settings.ToolbarCanTakeFocus,
    };

    /// <summary>
    /// Repaints the toggles so the strip reflects what is actually on. A toolbar whose auto-watch
    /// button looks the same whether auto-watch is running is a row of shapes rather than a status.
    /// </summary>
    /// <param name="running">
    /// Which fixed mode <see cref="WatchMode.Auto"/> has settled on, when it has and when it is
    /// running at all. Null in the two fixed modes, where the question does not arise.
    /// </param>
    public void ShowState(bool autoWatch, bool overlayHidden, bool captureFrame, bool diacritics,
        WatchMode mode, bool moveMode = false, bool egyptian = false, bool recordable = false,
        bool readAgain = false, WatchMode? running = null)
    {
        Highlight("watch", autoWatch);
        Highlight("hide", overlayHidden);
        Highlight("frame", captureFrame);
        Highlight("tashkeel", diacritics);
        Highlight("move", moveMode);
        Highlight("dialect", egyptian);
        Highlight("recording", recordable);
        Highlight("readagain", readAgain);

        // Three states on one button, so the ICON has to carry which one - a lit/unlit pair cannot
        // express three. Dialogue box, film strip, gauge; lit whenever it is not the default.
        //
        // <b>Except in Auto, where the useful fact is which of the two it has DECIDED on.</b> The
        // gauge says the app is choosing for you and then never says what it chose - reported as
        // "the auto mode does not tell you which mode is on", and true: the announcement on the
        // overlay is one line at the instant of the switch, and thirty seconds later there was
        // nothing on screen that answered the question. So a running Auto shows the mode it is
        // actually running, and stays lit, which is what separates it from having picked that mode
        // by hand. Undecided Auto keeps the gauge.
        _modeIcon = (mode, running) switch
        {
            (WatchMode.Auto, WatchMode.Video) => Icons.WatchMode,
            (WatchMode.Auto, WatchMode.Dialogue) => Icons.WatchDialogue,
            (WatchMode.Auto, _) => Icons.WatchAuto,
            (WatchMode.Video, _) => Icons.WatchMode,
            _ => Icons.WatchDialogue,
        };

        Highlight("mode", mode != WatchMode.Dialogue);
    }

    private IconGeometry _modeIcon = Icons.WatchDialogue;

    /// <summary>
    /// Re-renders every tooltip in the new interface language. Both languages are always present,
    /// so this only changes which one leads - but that is the one the user reads first, and the
    /// language switch is supposed to take effect without a restart everywhere else.
    /// </summary>
    public void UseLanguage(UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;

        foreach (var (key, button) in _buttons)
            if (_tips.TryGetValue(key, out var pick))
                ToolTip.SetTip(button, BilingualTip.For(_text,
                    pick(UiText.En), pick(UiText.Ar), _hotkeys.GetValueOrDefault(key)));
    }

    /// <summary>
    /// Puts the toolbar where it was left, or at the top of the game window the first time.
    ///
    /// <para>
    /// Clamped onto a monitor either way. A remembered coordinate is only as good as the display
    /// layout it was saved under, and a toolbar last seen at x=2400 on a machine that no longer has
    /// a second screen is indistinguishable from a toolbar that stopped working.
    /// </para>
    /// </summary>
    public void PlaceNear(CaptureRegion game)
    {
        var size = new PixelSize(
            (int)Math.Ceiling(Math.Max(Bounds.Width, 320) * SafeScaling()),
            (int)Math.Ceiling(Math.Max(Bounds.Height, 44) * SafeScaling()));

        var desired = _settings is { ToolbarX: { } x, ToolbarY: { } y }
            ? new PixelPoint(x, y)
            : new PixelPoint(game.X + Math.Max(0, (game.Width - size.Width) / 2), game.Y + 8);

        Position = ClampToAScreen(desired, size);
    }

    private double SafeScaling() =>
        double.IsNaN(RenderScaling) || RenderScaling <= 0 ? 1.0 : RenderScaling;

    // ── dragging ──────────────────────────────────────────────────────────────────────────────
    // Done by hand rather than through BeginMoveDrag. That helper asks the window manager to run a
    // modal move loop, which is exactly the kind of thing a WS_EX_NOACTIVATE window is least likely
    // to be given - and it cannot be rehearsed on the machine this was written on. Screen
    // coordinates from PointToScreen are exact at any DPI and behave identically on both platforms.

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _dragging = true;
        _grabbedAt = this.PointToScreen(e.GetPosition(this));
        _windowWasAt = Position;

        e.Pointer.Capture((IInputElement?)sender);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;

        // PointToScreen is evaluated against the window's CURRENT position, so this stays the true
        // pointer location however far the window has already been moved this drag. Anchoring to
        // where the grab started rather than to the last frame means rounding cannot accumulate.
        var now = this.PointToScreen(e.GetPosition(this));

        Position = new PixelPoint(
            _windowWasAt.X + (now.X - _grabbedAt.X),
            _windowWasAt.Y + (now.Y - _grabbedAt.Y));
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        e.Pointer.Capture(null);

        var size = new PixelSize(
            (int)Math.Ceiling(Bounds.Width * SafeScaling()),
            (int)Math.Ceiling(Bounds.Height * SafeScaling()));

        Position = ClampToAScreen(Position, size);

        _settings.ToolbarX = Position.X;
        _settings.ToolbarY = Position.Y;
        _settings.Save();
    }

    // ── building buttons ──────────────────────────────────────────────────────────────────────

    private readonly Dictionary<string, Func<UiText, string>> _tips = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _hotkeys = new(StringComparer.Ordinal);

    private Button Make(string key, IconGeometry icon, Func<UiText, string> tip, Action onClick,
        HotkeyAction? boundTo = null)
    {
        var button = new Button
        {
            Width = ButtonSize,
            Height = ButtonSize,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = Icons.Draw(icon, IconSize, Ink),
        };

        button.Click += (_, _) => onClick();

        _tips[key] = tip;
        if (boundTo is { } action) _hotkeys[key] = _settings.HotkeyFor(action).ToString();

        ToolTip.SetTip(button, BilingualTip.For(_text,
            tip(UiText.En), tip(UiText.Ar), _hotkeys.GetValueOrDefault(key)));

        _buttons[key] = button;
        return button;
    }

    private void ToggleExpanded()
    {
        _settings.ToolbarExpanded = !_settings.ToolbarExpanded;
        _settings.Save();

        _advanced.IsVisible = _settings.ToolbarExpanded;
        _expander.Content = Icons.Draw(_settings.ToolbarExpanded ? Icons.Less : Icons.More, IconSize, Ink);

        ToolTip.SetTip(_expander, BilingualTip.For(_text,
            _settings.ToolbarExpanded ? UiText.En.ToolbarLess : UiText.En.ToolbarMore,
            _settings.ToolbarExpanded ? UiText.Ar.ToolbarLess : UiText.Ar.ToolbarMore));
    }

    private void Highlight(string key, bool on)
    {
        if (!_buttons.TryGetValue(key, out var button)) return;

        button.Background = on ? ActiveFill : Brushes.Transparent;
        if (button.Content is Control) button.Content = Icons.Draw(IconFor(key), IconSize, on ? ActiveInk : Ink);
    }

    private IconGeometry IconFor(string key) => key switch
    {
        "watch" => Icons.AutoWatch,
        "hide" => Icons.HideOverlay,
        "frame" => Icons.CaptureFrame,
        "tashkeel" => Icons.Diacritics,
        "move" => Icons.Move,
        "dialect" => Icons.Dialect,
        "recording" => Icons.Recording,
        "readagain" => Icons.ReadAgain,
        "mode" => _modeIcon,
        _ => Icons.TranslateNow,
    };

    private static Control Divider() => new Border
    {
        Width = 1,
        Margin = new Thickness(4, 7),
        Background = new SolidColorBrush(Color.Parse("#2b2f38")),
    };

    /// <summary>
    /// Snapshots the strip exactly as drawn, for <c>--toolbar-test</c>.
    ///
    /// <para>
    /// The icons are geometry parsed from strings, so a typo in one is an exception thrown while
    /// this window is being built - on a user's machine, at startup. No compiler catches that and
    /// no unit test can, because the parser is Avalonia's and the test project does not reference
    /// it. Drawing the real control is the check.
    /// </para>
    /// </summary>
    public void ShowAdvanced(bool on) => _advanced.IsVisible = on;

    public void SaveSnapshot(string path)
    {
        _shell.Measure(Size.Infinity);
        _shell.Arrange(new Rect(_shell.DesiredSize));

        var width = (int)Math.Ceiling(Math.Max(1, _shell.Bounds.Width));
        var height = (int)Math.Ceiling(Math.Max(1, _shell.Bounds.Height));

        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize(width, height), new Vector(96, 96));

        bitmap.Render(_shell);
        bitmap.Save(path);
    }
}
