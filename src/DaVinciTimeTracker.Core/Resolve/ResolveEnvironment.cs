namespace DaVinciTimeTracker.Core.Resolve;

/// <summary>
/// Builds the environment variable block to inject into every spawned Python child process.
///
/// Key fix: setting PYTHONHOME scopes the Python ABI binding to the chosen interpreter
/// and defeats DaVinci Resolve's "highest registry-registered Python" selection that
/// causes 0xC0000005 crashes with Python 3.13/3.14 (see davinci-resolve-mcp issue #26).
///
/// Also ensures RESOLVE_SCRIPT_API / RESOLVE_SCRIPT_LIB / PYTHONPATH are always set,
/// eliminating the most common setup-free failure mode on clean machines.
/// </summary>
public static class ResolveEnvironment
{
    /// <summary>
    /// Returns the environment variables to merge into <see cref="System.Diagnostics.ProcessStartInfo.Environment"/>
    /// before starting any Python child process that calls DaVinci Resolve scripting APIs.
    /// </summary>
    /// <param name="pythonExePath">The exact python.exe chosen by PythonPathResolver.</param>
    /// <param name="platform">Platform-specific path resolver.</param>
    public static Dictionary<string, string> ForInterpreter(string pythonExePath, IResolvePlatform platform)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // PYTHONHOME — scopes Resolve's Python ABI binding to our chosen interpreter.
        // Without this, Resolve scans HKLM\SOFTWARE\Python\PythonCore and binds to the
        // highest registered version, segfaulting if that version is incompatible.
        if (!string.IsNullOrEmpty(pythonExePath) && File.Exists(pythonExePath))
        {
            var pythonHome = Path.GetDirectoryName(pythonExePath);
            if (!string.IsNullOrEmpty(pythonHome))
                env["PYTHONHOME"] = pythonHome;
        }

        // RESOLVE_SCRIPT_LIB — path to fusionscript binary
        var lib = platform.GetFusionScriptLibPath();
        if (!string.IsNullOrEmpty(lib))
            env["RESOLVE_SCRIPT_LIB"] = lib;

        // RESOLVE_SCRIPT_API — scripting root used by DaVinciResolveScript.py internally
        var api = platform.GetScriptApiPath();
        if (!string.IsNullOrEmpty(api))
            env["RESOLVE_SCRIPT_API"] = api;

        // PYTHONPATH — prepend the Modules folder so `import DaVinciResolveScript` works
        var modules = platform.GetScriptingModulesPath();
        if (!string.IsNullOrEmpty(modules))
        {
            var existing = Environment.GetEnvironmentVariable("PYTHONPATH") ?? "";
            var separator = Path.PathSeparator.ToString();
            env["PYTHONPATH"] = string.IsNullOrEmpty(existing)
                ? modules
                : $"{modules}{separator}{existing}";
        }

        return env;
    }

    /// <summary>
    /// Applies the computed environment block onto an existing ProcessStartInfo.
    /// Call this after constructing ProcessStartInfo and before Process.Start().
    /// </summary>
    public static void ApplyTo(System.Diagnostics.ProcessStartInfo psi,
        string pythonExePath, IResolvePlatform platform)
    {
        foreach (var (key, value) in ForInterpreter(pythonExePath, platform))
            psi.Environment[key] = value;
    }
}
