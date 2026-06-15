using DaVinciTimeTracker.Core.NodeToggle;
using DaVinciTimeTracker.Core.Native;
using Serilog;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace DaVinciTimeTracker.App.Hotkeys;

/// <summary>
/// Windows implementation of IHotkeyManager using user32.dll RegisterHotKey.
///
/// Creates a hidden message-only NativeWindow to receive WM_HOTKEY messages.
/// Hotkey strings are parsed as: [Modifier+]Key, e.g. "Ctrl+Alt+D", "Shift+F5".
///
/// If the TFM ever changes from net9.0-windows* to net9.0, wrap this class
/// in #if WINDOWS to prevent compilation on non-Windows toolchains.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHotkeyManager : IHotkeyManager
{
    public event Action<string> HotkeyTriggered = delegate { };

    private readonly ILogger _logger;
    private HotkeyWindow? _window;

    // groupId → registered hotkey atom id
    private readonly Dictionary<string, int> _registrations = new();
    private int _nextId = 1;

    private const int WM_HOTKEY = 0x0312;

    public WindowsHotkeyManager(ILogger logger)
    {
        _logger = logger;

        // NativeWindow must be created on the UI thread (STA); the WinForms
        // message loop handles WM_HOTKEY dispatching automatically.
        _window = new HotkeyWindow();
        _window.HotkeyReceived += OnHotkeyReceived;
    }

    public void Reload(IEnumerable<ToggleGroup> groups)
    {
        UnregisterAll();

        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Hotkey))
                continue;

            if (!TryParseHotkey(group.Hotkey, out var modifiers, out var vk))
            {
                _logger.Warning("NodeToggle: cannot parse hotkey '{Hotkey}' for group '{Name}'",
                    group.Hotkey, group.Name);
                continue;
            }

            var id = _nextId++;
            var ok = WindowsApi.RegisterHotKey(_window!.Handle, id, modifiers, vk);
            if (ok)
            {
                _registrations[group.Id] = id;
                _logger.Information("NodeToggle: registered hotkey '{Hotkey}' (id={Id}) for '{Name}'",
                    group.Hotkey, id, group.Name);
            }
            else
            {
                var err = Marshal.GetLastWin32Error();
                _logger.Warning(
                    "NodeToggle: failed to register hotkey '{Hotkey}' for '{Name}' (Win32 error {Err}). " +
                    "Another app may be using this key combination.",
                    group.Hotkey, group.Name, err);
            }
        }
    }

    private void OnHotkeyReceived(int id)
    {
        var entry = _registrations.FirstOrDefault(kv => kv.Value == id);
        if (entry.Key is not null)
            HotkeyTriggered(entry.Key);
    }

    private void UnregisterAll()
    {
        if (_window is null) return;
        foreach (var (_, id) in _registrations)
            WindowsApi.UnregisterHotKey(_window.Handle, id);
        _registrations.Clear();
    }

    // ── Hotkey string parser ──────────────────────────────────────────────────

    private static bool TryParseHotkey(string hotkey, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        var parts = hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        // Last part is the key; all others are modifiers
        var keyPart = parts[^1];
        var modParts = parts[..^1];

        foreach (var mod in modParts)
        {
            switch (mod.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL": modifiers |= WindowsApi.MOD_CONTROL; break;
                case "ALT":     modifiers |= WindowsApi.MOD_ALT;     break;
                case "SHIFT":   modifiers |= WindowsApi.MOD_SHIFT;   break;
                case "WIN":
                case "WINDOWS": modifiers |= WindowsApi.MOD_WIN;    break;
                default:
                    return false; // unknown modifier
            }
        }

        // Use the Keys enum to resolve VK codes — it mirrors VK_* constants
        if (!Enum.TryParse<Keys>(keyPart, ignoreCase: true, out var key))
            return false;

        vk = (uint)key;
        return vk != 0;
    }

    // ── Inner NativeWindow ────────────────────────────────────────────────────

    private sealed class HotkeyWindow : NativeWindow
    {
        public event Action<int>? HotkeyReceived;

        public HotkeyWindow()
        {
            // Create a minimal hidden window to receive window messages
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
                HotkeyReceived?.Invoke(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        UnregisterAll();
        _window?.DestroyHandle();
        _window = null;
    }
}
