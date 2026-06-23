namespace DaVinciTimeTracker.Core.Resolve;

/// <summary>
/// Linux stub for IResolvePlatform.
///
/// Future implementation notes:
///   Standard install: /opt/resolve/
///   ISO install:      /home/resolve/
///   RESOLVE_SCRIPT_LIB = /opt/resolve/libs/Fusion/fusionscript.so
///   RESOLVE_SCRIPT_API = /opt/resolve/Developer/Scripting
/// </summary>
public sealed class LinuxResolvePlatform : IResolvePlatform
{
    private static readonly string ResolveRoot =
        Directory.Exists("/opt/resolve") ? "/opt/resolve" : "/home/resolve";

    public string GetResolveInstallDirectory() => ResolveRoot;

    public string? GetFusionScriptLibPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("RESOLVE_SCRIPT_LIB");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv)) return fromEnv;
        var computed = Path.Combine(ResolveRoot, "libs", "Fusion", "fusionscript.so");
        return File.Exists(computed) ? computed : null;
    }

    public string? GetScriptApiPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("RESOLVE_SCRIPT_API");
        if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(fromEnv)) return fromEnv;
        var computed = Path.Combine(ResolveRoot, "Developer", "Scripting");
        return Directory.Exists(computed) ? computed : null;
    }

    public string? GetScriptingModulesPath()
    {
        var api = GetScriptApiPath();
        if (api is null) return null;
        var modules = Path.Combine(api, "Modules");
        return Directory.Exists(modules) ? modules : null;
    }

    public IEnumerable<string> GetResolveProcessNames() =>
        ["resolve", "Resolve"];

    public IEnumerable<string> GetPythonCandidatePaths() =>
    [
        "/usr/bin/python3",
        "/usr/local/bin/python3",
        "/usr/bin/python3.12",
        "/usr/bin/python3.11",
        "/usr/bin/python3.10",
    ];
}
