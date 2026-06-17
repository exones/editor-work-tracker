using Serilog;

namespace DaVinciTimeTracker.Core.Native;

/// <summary>
/// macOS stub implementation of ISystemActivityProvider.
///
/// Degraded-but-functional defaults:
///   GetIdleTime()            → TimeSpan.Zero   (treat user as always active)
///   IsDaVinciInFocus()       → true             (assume focused; won't lose sessions)
///   IsDaVinciResolveRunning() → real via Process API (cross-platform)
///
/// Future implementation paths:
///   Idle:  CGEventSourceSecondsSinceLastEventType via P/Invoke into CoreGraphics.framework
///   Focus: NSWorkspace.frontmostApplication via P/Invoke into AppKit
///
/// Note: tracking will work on macOS but won't stop when the user walks away
/// (no idle detection) and won't pause when DaVinci loses focus.
/// </summary>
public sealed class MacOsSystemActivityProvider : ISystemActivityProvider
{
    private readonly ILogger _logger;
    private bool _warned;

    public MacOsSystemActivityProvider(ILogger logger)
    {
        _logger = logger;
    }

    public TimeSpan GetIdleTime()
    {
        WarnOnce();
        // Treat user as always active on macOS (no idle detection yet)
        return TimeSpan.Zero;
    }

    public bool IsDaVinciResolveRunning()
    {
        // System.Diagnostics.Process works cross-platform
        var names = new[] { "Resolve", "DaVinciResolve", "resolve" };
        foreach (var name in names)
        {
            var procs = System.Diagnostics.Process.GetProcessesByName(name);
            if (procs.Length > 0)
            {
                foreach (var p in procs) p.Dispose();
                return true;
            }
        }
        return false;
    }

    public bool IsDaVinciInFocus()
    {
        WarnOnce();
        // Assume focused on macOS (no focus detection yet — avoids missed sessions)
        return true;
    }

    private void WarnOnce()
    {
        if (_warned) return;
        _warned = true;
        _logger.Warning(
            "macOS system activity provider is a stub: idle detection and focus detection " +
            "are not implemented. Future path: CGEventSourceSecondsSinceLastEventType (idle), " +
            "NSWorkspace.frontmostApplication (focus).");
    }
}
