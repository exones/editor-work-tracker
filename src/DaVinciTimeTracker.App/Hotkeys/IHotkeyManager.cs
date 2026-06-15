using DaVinciTimeTracker.Core.NodeToggle;

namespace DaVinciTimeTracker.App.Hotkeys;

/// <summary>
/// Platform-agnostic interface for global hotkey registration.
/// Implementations: WindowsHotkeyManager (real), MacOsHotkeyManager (stub), LinuxHotkeyManager (stub).
/// </summary>
public interface IHotkeyManager : IDisposable
{
    /// <summary>
    /// Fired when a registered hotkey is pressed.
    /// Argument is the ToggleGroup.Id whose hotkey matched.
    /// </summary>
    event Action<string> HotkeyTriggered;

    /// <summary>
    /// Re-registers all hotkeys from the given group list.
    /// Unregisters any hotkeys no longer in the list.
    /// Call whenever the config changes.
    /// </summary>
    void Reload(IEnumerable<ToggleGroup> groups);
}
