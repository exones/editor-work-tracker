using System.Runtime.Versioning;

namespace DaVinciTimeTracker.Core.Native;

/// <summary>
/// Windows implementation of ISystemActivityProvider.
/// Delegates to the static WindowsApi helpers which hold the Win32 P/Invokes.
/// If the TFM ever changes from net9.0-windows* to net9.0, wrap this class in
/// #if WINDOWS to prevent compilation on non-Windows toolchains.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemActivityProvider : ISystemActivityProvider
{
    public TimeSpan GetIdleTime() => WindowsApi.GetIdleTime();

    public bool IsDaVinciResolveRunning() => WindowsApi.IsDaVinciResolveRunning();

    public bool IsDaVinciInFocus() => WindowsApi.IsDaVinciResolveInFocus();
}
