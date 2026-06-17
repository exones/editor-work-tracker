namespace DaVinciTimeTracker.Core.Native;

/// <summary>
/// Platform abstraction for OS-level activity and focus detection.
/// Implementations: WindowsSystemActivityProvider (real), MacOsSystemActivityProvider (stub).
/// Created via SystemActivityProviderFactory.Create().
/// </summary>
public interface ISystemActivityProvider
{
    /// <summary>Time since the last user input event (keyboard or mouse).</summary>
    TimeSpan GetIdleTime();

    /// <summary>Returns true if any DaVinci Resolve process is running.</summary>
    bool IsDaVinciResolveRunning();

    /// <summary>Returns true if a DaVinci Resolve window currently has foreground focus.</summary>
    bool IsDaVinciInFocus();
}
