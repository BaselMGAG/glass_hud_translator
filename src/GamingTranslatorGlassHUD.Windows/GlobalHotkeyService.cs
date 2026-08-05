using System.Runtime.Versioning;
using GamingTranslatorGlassHUD.Core.Platform;
using GamingTranslatorGlassHUD.Interop;

namespace GamingTranslatorGlassHUD.Windows;

/// <summary>
/// Global hotkeys via RegisterHotKey.
///
/// <para>
/// Explicitly not a WH_KEYBOARD_LL hook. A low-level keyboard hook sees every keystroke on the
/// machine, which is exactly the behaviour antivirus heuristics flag, and it would be a bad thing
/// to ask someone to whitelist. RegisterHotKey asks the OS for four specific combinations and
/// receives nothing else, and it still fires while the game holds focus.
/// </para>
///
/// <para>
/// Passing a null window handle binds the hotkey to the calling <em>thread</em>, so WM_HOTKEY
/// arrives in that thread's message queue and no window class needs registering. The catch is that
/// registration and the message pump must happen on the same thread, which is why this owns a
/// dedicated one rather than borrowing the UI thread.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GlobalHotkeyService : IHotkeyService
{
    private readonly Lock _gate = new();

    private Thread? _pump;
    private uint _threadId;
    private bool _disposed;

    public bool IsSupported => true;

    public event Action<HotkeyAction>? Pressed;

    public IReadOnlyList<HotkeyRegistration> Register(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Unregister();

            var ready = new TaskCompletionSource<IReadOnlyList<HotkeyRegistration>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _pump = new Thread(() => Pump(bindings, ready))
            {
                IsBackground = true,
                Name = "hotkeys",
            };
            _pump.Start();

            return ready.Task.GetAwaiter().GetResult();
        }
    }

    private void Pump(
        IReadOnlyDictionary<HotkeyAction, Hotkey> bindings,
        TaskCompletionSource<IReadOnlyList<HotkeyRegistration>> ready)
    {
        _threadId = NativeMethods.GetCurrentThreadId();

        var results = new List<HotkeyRegistration>();
        var byId = new Dictionary<int, HotkeyAction>();
        var nextId = 1;

        foreach (var (action, hotkey) in bindings)
        {
            if (!hotkey.IsValid)
            {
                results.Add(new HotkeyRegistration(action, hotkey, false,
                    hotkey.VirtualKey == 0 ? $"Unknown key '{hotkey.Key}'." : "A modifier is required."));
                continue;
            }

            var id = nextId++;
            // MOD_NOREPEAT, or holding the key down fires continuously and floods the API.
            var modifiers = ToWin32(hotkey.Modifiers) | NativeMethods.ModNoRepeat;

            if (NativeMethods.RegisterHotKey(IntPtr.Zero, id, modifiers, hotkey.VirtualKey))
            {
                byId[id] = action;
                results.Add(new HotkeyRegistration(action, hotkey, true));
            }
            else
            {
                // Almost always ERROR_HOTKEY_ALREADY_REGISTERED: another running application got
                // there first. Only this binding fails, so report it and keep the rest.
                var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                results.Add(new HotkeyRegistration(action, hotkey, false,
                    error == 1409
                        ? $"{hotkey} is already taken by another application."
                        : $"Could not register {hotkey} (error {error})."));
            }
        }

        ready.SetResult(results);

        while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.Message != NativeMethods.WmHotkey) continue;

            if (byId.TryGetValue((int)message.WParam, out var action))
                Pressed?.Invoke(action);
        }

        foreach (var id in byId.Keys)
            NativeMethods.UnregisterHotKey(IntPtr.Zero, id);
    }

    private static uint ToWin32(HotkeyModifiers modifiers)
    {
        uint result = 0;
        if ((modifiers & HotkeyModifiers.Alt) != 0) result |= NativeMethods.ModAlt;
        if ((modifiers & HotkeyModifiers.Control) != 0) result |= NativeMethods.ModControl;
        if ((modifiers & HotkeyModifiers.Shift) != 0) result |= NativeMethods.ModShift;
        if ((modifiers & HotkeyModifiers.Windows) != 0) result |= NativeMethods.ModWin;
        return result;
    }

    public void Unregister()
    {
        if (_pump is null) return;

        // Ends the GetMessage loop, which unregisters on the thread that registered - required,
        // since a hotkey bound to a thread cannot be released from another one.
        NativeMethods.PostThreadMessage(_threadId, NativeMethods.WmQuit, IntPtr.Zero, IntPtr.Zero);
        _pump.Join(TimeSpan.FromSeconds(2));
        _pump = null;
        _threadId = 0;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Unregister();
        }
    }
}
