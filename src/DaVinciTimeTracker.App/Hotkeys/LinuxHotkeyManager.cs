using DaVinciTimeTracker.Core.NodeToggle;
using Serilog;

namespace DaVinciTimeTracker.App.Hotkeys;

/// <summary>
/// Stub implementation for Linux.
///
/// Future implementation paths:
///   X11:     XGrabKey via libX11.so P/Invoke.
///   Wayland: No stable global-hotkey API exists as of 2026.
///            Best current workaround is a dedicated listener daemon
///            (e.g. ydotool, keyd) that calls the /api/node-toggles/{id}/execute endpoint.
///
///   X11 P/Invoke sketch (future):
///     [DllImport("libX11.so.6")]
///     static extern int XGrabKey(IntPtr display, int keycode, uint modifiers,
///         IntPtr grab_window, bool owner_events, int pointer_mode, int keyboard_mode);
/// </summary>
public sealed class LinuxHotkeyManager : IHotkeyManager
{
    public event Action<string> HotkeyTriggered = delegate { };

    public LinuxHotkeyManager(ILogger logger)
    {
        logger.Warning(
            "NodeToggle: Global hotkeys are not yet implemented on Linux. " +
            "X11 future path: XGrabKey via libX11. Wayland: no stable global API — " +
            "use the HTTP endpoint /api/node-toggles/{id}/execute from a Wayland-compatible tool. " +
            "Hotkeys defined in node-toggles.json will be ignored.");
    }

    public void Reload(IEnumerable<ToggleGroup> groups) { /* no-op */ }

    public void Dispose() { /* no-op */ }
}
