namespace GlassHudTranslator.Core.Platform;

/// <summary>
/// Defaults deliberately avoid F1-F12, which games bind (FFXIV uses them for party targeting).
/// Every binding is user-configurable, because no combination is safe across every game.
/// </summary>
public enum HotkeyAction
{
    /// <summary>Open the region picker.</summary>
    PickRegion,

    /// <summary>Translate what is on screen right now. The default mode of operation.</summary>
    TranslateNow,

    /// <summary>Toggle auto-watch polling.</summary>
    ToggleAutoWatch,

    /// <summary>Correct the current translation and pin the correction.</summary>
    FlagTranslation,

    /// <summary>
    /// Show or hide the overlay without stopping translation. Needed for the moments the HUD is in
    /// the way - a boss mechanic under the text box, or a screenshot worth taking clean.
    /// </summary>
    ToggleOverlay,

    /// <summary>
    /// Bring the Settings window up from inside the game.
    ///
    /// <para>
    /// Added because everything that goes wrong sends the user to Settings, and until now getting
    /// there meant leaving the game and hunting for a window with no taskbar entry of its own.
    /// Reported as «كل ما يحصل مشكله اخش علي الاعدادات نفسها» — every problem means going into
    /// Settings — alongside a request for a way in from a toolbar. This was the cheap half of that;
    /// the toolbar is the other half.
    /// </para>
    /// </summary>
    OpenSettings,

    /// <summary>
    /// Drag a box around anything on screen and translate it once, leaving the watched region alone.
    ///
    /// <para>
    /// The toolbar has a button for this, so a binding is not strictly needed — but reaching for
    /// the mouse to press a button, in order to then use the mouse, is exactly the friction the
    /// hotkeys exist to remove. It is also the action most likely to be wanted mid-fight, on a
    /// tooltip or a quest marker that is not where the dialogue box is.
    /// </para>
    /// </summary>
    SnipTranslate,
}

public sealed record HotkeyRegistration(HotkeyAction Action, Hotkey Hotkey, bool Succeeded, string? Error = null);

/// <summary>
/// Global hotkeys. Implemented on Windows with RegisterHotKey, never with a low-level keyboard
/// hook: WH_KEYBOARD_LL is the pattern antivirus heuristics flag, and RegisterHotKey already fires
/// while the game has focus.
/// </summary>
public interface IHotkeyService : IDisposable
{
    bool IsSupported { get; }

    event Action<HotkeyAction>? Pressed;

    /// <summary>
    /// Binds the given combinations. Returns one result per action, because a clash with another
    /// running application fails that binding alone and the user needs to be told which one.
    /// </summary>
    IReadOnlyList<HotkeyRegistration> Register(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings);

    void Unregister();
}

/// <summary>Used off Windows, where there is no game holding focus anyway.</summary>
public sealed class NullHotkeyService : IHotkeyService
{
    public bool IsSupported => false;

    public event Action<HotkeyAction>? Pressed;

    public IReadOnlyList<HotkeyRegistration> Register(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings)
    {
        _ = Pressed;
        return bindings
            .Select(b => new HotkeyRegistration(b.Key, b.Value, false, "Global hotkeys are Windows-only."))
            .ToList();
    }

    public void Unregister() { }

    public void Dispose() { }
}
