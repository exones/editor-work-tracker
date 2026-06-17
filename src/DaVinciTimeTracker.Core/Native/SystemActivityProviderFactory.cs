using Serilog;
using System.Runtime.InteropServices;

namespace DaVinciTimeTracker.Core.Native;

/// <summary>
/// Creates the appropriate ISystemActivityProvider for the current OS at runtime.
/// No #if guards needed: the factory uses RuntimeInformation so all three
/// implementation classes compile on any platform; only the Windows one carries
/// [SupportedOSPlatform("windows")] for the Roslyn analyzer.
/// </summary>
public static class SystemActivityProviderFactory
{
    public static ISystemActivityProvider Create(ILogger logger) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new WindowsSystemActivityProvider()
            : new MacOsSystemActivityProvider(logger);
}
