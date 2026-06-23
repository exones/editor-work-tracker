using System.Runtime.Versioning;

namespace DaVinciTimeTracker.Core.Resolve;

/// <summary>
/// macOS stub for IResolvePlatform.
///
/// Future implementation notes:
///   RESOLVE_SCRIPT_LIB = /Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/Libraries/Fusion/fusionscript.so
///   RESOLVE_SCRIPT_API = /Library/Application Support/Blackmagic Design/DaVinci Resolve/Developer/Scripting
///   Note: macOS path does NOT include the "Support" segment unlike Windows.
///
///   scriptapp("Resolve") may bind to the machine's LAN IP instead of 127.0.0.1;
///   if it returns None, try scriptapp("Resolve", lan_ip).
/// </summary>
[SupportedOSPlatform("osx")]
public sealed class MacResolvePlatform : IResolvePlatform
{
    private const string ResolveApp =
        "/Applications/DaVinci Resolve/DaVinci Resolve.app";

    private const string ScriptApiBase =
        "/Library/Application Support/Blackmagic Design/DaVinci Resolve/Developer/Scripting";

    public string GetResolveInstallDirectory() =>
        Path.Combine(ResolveApp, "Contents", "Libraries", "Fusion");

    public string? GetFusionScriptLibPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("RESOLVE_SCRIPT_LIB");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv)) return fromEnv;
        var computed = Path.Combine(ResolveApp, "Contents", "Libraries", "Fusion", "fusionscript.so");
        return File.Exists(computed) ? computed : null;
    }

    public string? GetScriptApiPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("RESOLVE_SCRIPT_API");
        if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(fromEnv)) return fromEnv;
        return Directory.Exists(ScriptApiBase) ? ScriptApiBase : null;
    }

    public string? GetScriptingModulesPath()
    {
        var api = GetScriptApiPath();
        if (api is null) return null;
        var modules = Path.Combine(api, "Modules");
        return Directory.Exists(modules) ? modules : null;
    }

    public IEnumerable<string> GetResolveProcessNames() =>
        ["Resolve", "DaVinci Resolve"];

    public IEnumerable<string> GetPythonCandidatePaths() =>
    [
        "/usr/local/bin/python3",
        "/usr/bin/python3",
        "/opt/homebrew/bin/python3",
        "/opt/homebrew/bin/python3.12",
        "/opt/homebrew/bin/python3.11",
        "/opt/homebrew/bin/python3.10",
    ];
}
