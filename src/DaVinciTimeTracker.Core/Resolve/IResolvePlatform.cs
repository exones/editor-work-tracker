namespace DaVinciTimeTracker.Core.Resolve;

/// <summary>
/// Platform abstraction for DaVinci Resolve installation paths.
/// Implementations: WindowsResolvePlatform (active), MacResolvePlatform / LinuxResolvePlatform (stubs).
/// Created via ResolvePlatformFactory.Create().
///
/// Exact documented paths (X-Raym mirror, 8 May 2026):
///   Windows:  RESOLVE_SCRIPT_API = %PROGRAMDATA%\Blackmagic Design\DaVinci Resolve\Support\Developer\Scripting
///             RESOLVE_SCRIPT_LIB = C:\Program Files\Blackmagic Design\DaVinci Resolve\fusionscript.dll
///   macOS:    RESOLVE_SCRIPT_API = /Library/Application Support/Blackmagic Design/DaVinci Resolve/Developer/Scripting
///             RESOLVE_SCRIPT_LIB = /Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/Libraries/Fusion/fusionscript.so
///   Linux:    RESOLVE_SCRIPT_API = /opt/resolve/Developer/Scripting
///             RESOLVE_SCRIPT_LIB = /opt/resolve/libs/Fusion/fusionscript.so
/// PYTHONPATH += {RESOLVE_SCRIPT_API}/Modules on all platforms.
/// </summary>
public interface IResolvePlatform
{
    /// <summary>Path to fusionscript binary (RESOLVE_SCRIPT_LIB).</summary>
    string? GetFusionScriptLibPath();

    /// <summary>Path to the scripting API root (RESOLVE_SCRIPT_API).</summary>
    string? GetScriptApiPath();

    /// <summary>Path to the scripting Modules folder to add to PYTHONPATH.</summary>
    string? GetScriptingModulesPath();

    /// <summary>Root install directory of DaVinci Resolve (parent of the fusionscript binary).</summary>
    string GetResolveInstallDirectory();

    /// <summary>Ordered list of Python executable paths to probe as candidates.</summary>
    IEnumerable<string> GetPythonCandidatePaths();

    /// <summary>Process names used to detect whether DaVinci Resolve is running.</summary>
    IEnumerable<string> GetResolveProcessNames();
}
