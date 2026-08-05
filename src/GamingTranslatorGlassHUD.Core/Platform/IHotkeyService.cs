namespace GamingTranslatorGlassHUD.Core.Platform;

/// <summary>
/// Deliberately avoids F1-F12: FFXIV binds those to party-member targeting by default (brief 2.6).
/// </summary>
public enum HotkeyAction
{
    /// <summary>Ctrl+Shift+R - region picker.</summary>
    PickRegion,

    /// <summary>Ctrl+Shift+T - translate what is on screen now. The default mode.</summary>
    TranslateNow,

    /// <summary>Ctrl+Shift+A - toggle auto-watch.</summary>
    ToggleAutoWatch,

    /// <summary>Ctrl+Shift+F - correct the current translation.</summary>
    FlagTranslation,
}

/// <summary>
/// Global hotkeys. Implemented on Windows with RegisterHotKey, never with a low-level keyboard
/// hook: WH_KEYBOARD_LL is the pattern antivirus heuristics flag, and RegisterHotKey already fires
/// while FFXIV has focus (brief 2.6, 16).
/// </summary>
public interface IHotkeyService : IDisposable
{
    bool IsSupported { get; }

    event Action<HotkeyAction>? Pressed;

    /// <summary>Returns the actions that could not be registered, usually because of a clash.</summary>
    IReadOnlyList<HotkeyAction> Register();
}

/// <summary>Used on macOS, where there is no game to be focused anyway.</summary>
public sealed class NullHotkeyService : IHotkeyService
{
    public bool IsSupported => false;

    public event Action<HotkeyAction>? Pressed;

    public IReadOnlyList<HotkeyAction> Register()
    {
        _ = Pressed;   // silences the unused-event warning without suppressing it globally
        return Enum.GetValues<HotkeyAction>();
    }

    public void Dispose() { }
}
