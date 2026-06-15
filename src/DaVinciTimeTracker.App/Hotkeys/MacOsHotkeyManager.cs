using DaVinciTimeTracker.Core.NodeToggle;
using Serilog;

namespace DaVinciTimeTracker.App.Hotkeys;

/// <summary>
/// Stub implementation for macOS.
///
/// Future implementation path:
///   Use CGEventTap via P/Invoke into CoreGraphics.framework.
///   Requires the user to grant Accessibility permission in
///   System Preferences → Privacy and Security → Accessibility.
///
///   P/Invoke signature (future):
///     [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
///     static extern IntPtr CGEventTapCreate(CGEventTapLocation, CGEventTapPlacement,
///         CGEventTapOptions, ulong, CGEventTapCallBack, IntPtr);
/// </summary>
public sealed class MacOsHotkeyManager : IHotkeyManager
{
    public event Action<string> HotkeyTriggered = delegate { };

    public MacOsHotkeyManager(ILogger logger)
    {
        logger.Warning(
            "NodeToggle: Global hotkeys are not yet implemented on macOS. " +
            "Future implementation will use CGEventTap (requires Accessibility permission). " +
            "Hotkeys defined in node-toggles.json will be ignored.");
    }

    public void Reload(IEnumerable<ToggleGroup> groups) { /* no-op */ }

    public void Dispose() { /* no-op */ }
}
