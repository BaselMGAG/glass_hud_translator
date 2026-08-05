using System.Text;

namespace GamingTranslatorGlassHUD.Core.Platform;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>
/// A global hotkey, parsed from and rendered back to strings like "Ctrl+Shift+T".
///
/// <para>
/// Every binding is user-configurable, because there is no key combination that is safe in every
/// game. Games bind F1-F12 (party targeting in FFXIV), MMOs bind most of Ctrl+letter to abilities,
/// and anything involving Alt risks colliding with the window manager. Rather than guess, the
/// defaults avoid the worst offenders and the user can rebind anything that clashes.
/// </para>
/// </summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, string Key)
{
    public bool HasModifier(HotkeyModifiers m) => (Modifiers & m) != 0;

    /// <summary>
    /// A hotkey with no modifier would swallow that key from the game entirely, which is never what
    /// anyone wants for a key they also need to play with.
    /// </summary>
    public bool IsValid => Modifiers != HotkeyModifiers.None && VirtualKey != 0;

    public uint VirtualKey => VirtualKeys.TryGetValue(Key.ToUpperInvariant(), out var vk) ? vk : 0;

    public static Hotkey? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        var modifiers = HotkeyModifiers.None;
        string? key = null;

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= HotkeyModifiers.Control; break;
                case "SHIFT": modifiers |= HotkeyModifiers.Shift; break;
                case "ALT": modifiers |= HotkeyModifiers.Alt; break;
                case "WIN" or "WINDOWS" or "CMD": modifiers |= HotkeyModifiers.Windows; break;
                default: key = part; break;
            }
        }

        return key is null ? null : new Hotkey(modifiers, key.ToUpperInvariant());
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        if (HasModifier(HotkeyModifiers.Control)) builder.Append("Ctrl+");
        if (HasModifier(HotkeyModifiers.Shift)) builder.Append("Shift+");
        if (HasModifier(HotkeyModifiers.Alt)) builder.Append("Alt+");
        if (HasModifier(HotkeyModifiers.Windows)) builder.Append("Win+");
        return builder.Append(Key).ToString();
    }

    /// <summary>
    /// Names accepted on the left, Win32 virtual-key codes on the right. Broad on purpose: whatever
    /// a given game has already claimed, something here will be free.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, uint> VirtualKeys = Build();

    private static Dictionary<string, uint> Build()
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        for (var c = 'A'; c <= 'Z'; c++) map[c.ToString()] = c;
        for (var d = '0'; d <= '9'; d++) map[d.ToString()] = d;

        // F13-F24 are the safest keys on the board: physical keyboards rarely have them, so games
        // almost never bind them, but they are reachable from macro keys and remappers.
        for (uint f = 1; f <= 24; f++) map[$"F{f}"] = 0x70 + f - 1;

        map["SPACE"] = 0x20;
        map["ENTER"] = map["RETURN"] = 0x0D;
        map["TAB"] = 0x09;
        map["ESC"] = map["ESCAPE"] = 0x1B;
        map["BACKSPACE"] = 0x08;
        map["INSERT"] = 0x2D;
        map["DELETE"] = 0x2E;
        map["HOME"] = 0x24;
        map["END"] = 0x23;
        map["PAGEUP"] = 0x21;
        map["PAGEDOWN"] = 0x22;
        map["LEFT"] = 0x25;
        map["UP"] = 0x26;
        map["RIGHT"] = 0x27;
        map["DOWN"] = 0x28;
        map["PAUSE"] = 0x13;
        map["SCROLLLOCK"] = 0x91;
        map["PRINTSCREEN"] = 0x2C;
        map["CAPSLOCK"] = 0x14;

        for (uint n = 0; n <= 9; n++) map[$"NUM{n}"] = 0x60 + n;
        map["NUMMULTIPLY"] = 0x6A;
        map["NUMADD"] = 0x6B;
        map["NUMSUBTRACT"] = 0x6D;
        map["NUMDECIMAL"] = 0x6E;
        map["NUMDIVIDE"] = 0x6F;

        map[";"] = 0xBA; map["="] = 0xBB; map[","] = 0xBC; map["-"] = 0xBD;
        map["."] = 0xBE; map["/"] = 0xBF; map["`"] = 0xC0; map["["] = 0xDB;
        map["\\"] = 0xDC; map["]"] = 0xDD; map["'"] = 0xDE;

        return map;
    }
}

public static class DefaultHotkeys
{
    /// <summary>
    /// Ctrl+Shift+letter: not bound by default in FFXIV, and unlikely to collide elsewhere.
    /// Everything here is overridable in Settings.
    /// </summary>
    public static IReadOnlyDictionary<HotkeyAction, Hotkey> All { get; } =
        new Dictionary<HotkeyAction, Hotkey>
        {
            [HotkeyAction.PickRegion] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "R"),
            [HotkeyAction.TranslateNow] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "T"),
            [HotkeyAction.ToggleAutoWatch] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "A"),
            [HotkeyAction.FlagTranslation] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "F"),
            [HotkeyAction.ToggleOverlay] = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, "H"),
        };

    public static string Describe(HotkeyAction action) => action switch
    {
        HotkeyAction.PickRegion => "Pick the capture region",
        HotkeyAction.TranslateNow => "Translate what is on screen now",
        HotkeyAction.ToggleAutoWatch => "Toggle auto-watch",
        HotkeyAction.FlagTranslation => "Correct the current translation",
        HotkeyAction.ToggleOverlay => "Show / hide the overlay",
        _ => action.ToString(),
    };
}
