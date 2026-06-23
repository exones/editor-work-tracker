using System.Runtime.InteropServices;

namespace DaVinciTimeTracker.Core.Resolve;

/// <summary>
/// Creates the appropriate IResolvePlatform for the current OS at runtime.
/// Windows is fully implemented; macOS and Linux are stubs pending a non-Windows host.
/// </summary>
public static class ResolvePlatformFactory
{
    public static IResolvePlatform Create() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new WindowsResolvePlatform() :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)     ? new MacResolvePlatform() :
                                                               new LinuxResolvePlatform();
}
