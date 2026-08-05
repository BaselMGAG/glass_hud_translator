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
