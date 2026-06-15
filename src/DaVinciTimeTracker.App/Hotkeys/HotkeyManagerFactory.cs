using DaVinciTimeTracker.Core.NodeToggle;
using Serilog;
using System.Runtime.InteropServices;

namespace DaVinciTimeTracker.App.Hotkeys;

/// <summary>
/// Creates the appropriate IHotkeyManager for the current OS at runtime.
/// No #if guards needed: the factory uses RuntimeInformation so all three
/// implementation classes compile on any platform; only the Windows one
/// carries [SupportedOSPlatform("windows")] for the Roslyn analyzer.
/// </summary>
public static class HotkeyManagerFactory
{
    public static IHotkeyManager Create(ILogger logger) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new WindowsHotkeyManager(logger) :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)     ? new MacOsHotkeyManager(logger) :
                                                               new LinuxHotkeyManager(logger);
}
