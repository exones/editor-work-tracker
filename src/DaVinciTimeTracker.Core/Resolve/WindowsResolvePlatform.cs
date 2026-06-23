using System.Runtime.Versioning;

namespace DaVinciTimeTracker.Core.Resolve;

/// <summary>
/// Windows implementation of IResolvePlatform.
/// All hardcoded Resolve paths live here; callers use the interface.
///
/// If TFM ever changes from net9.0-windows* to net9.0, add #if WINDOWS guards
/// around this class to prevent compilation on non-Windows toolchains.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsResolvePlatform : IResolvePlatform
{
    private static readonly string ProgramData =
        Environment.GetEnvironmentVariable("PROGRAMDATA") ?? @"C:\ProgramData";

    private static readonly string ProgramFiles =
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // ── IResolvePlatform ──────────────────────────────────────────────────────

    public string GetResolveInstallDirectory() =>
        Path.Combine(ProgramFiles, "Blackmagic Design", "DaVinci Resolve");

    public string? GetFusionScriptLibPath()
    {
        // Honour env var if already set (DaVinci installer sets this on install)
        var fromEnv = Environment.GetEnvironmentVariable("RESOLVE_SCRIPT_LIB");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv)) return fromEnv;

        var computed = Path.Combine(GetResolveInstallDirectory(), "fusionscript.dll");
        return File.Exists(computed) ? computed : null;
    }

    public string? GetScriptApiPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("RESOLVE_SCRIPT_API");
        if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(fromEnv)) return fromEnv;

        // Windows path includes the extra "Support" segment (macOS does not)
        var computed = Path.Combine(ProgramData,
            "Blackmagic Design", "DaVinci Resolve", "Support", "Developer", "Scripting");
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
        ["Resolve", "DaVinciResolve", "resolve"];

    public IEnumerable<string> GetPythonCandidatePaths()
    {
        var resolveDir = GetResolveInstallDirectory();
        var paths = new List<string>
        {
            // DaVinci-bundled Python (some older Resolve versions)
            Path.Combine(resolveDir, "Python310", "python.exe"),
            Path.Combine(resolveDir, "Python", "Python310", "python.exe"),
            Path.Combine(resolveDir, "Python", "Python36", "python.exe"),

            // PyManager — known to work with fusionscript where Python.org may not
            @"C:\Program Files\PyManager\python.exe",
        };

        // Windows Store Python
        paths.Add(Path.Combine(LocalAppData, "Microsoft", "WindowsApps", "python.exe"));
        paths.Add(Path.Combine(LocalAppData, "Microsoft", "WindowsApps", "python3.exe"));

        // All python.exe entries in PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try { paths.Add(Path.Combine(dir.Trim(), "python.exe")); }
            catch { /* skip malformed path entries */ }
        }

        // Common Python.org locations (64-bit first, then user-local)
        string[] common =
        [
            @"C:\Program Files\Python313\python.exe",
            @"C:\Program Files\Python312\python.exe",
            @"C:\Program Files\Python311\python.exe",
            @"C:\Program Files\Python310\python.exe",
            @"C:\Program Files\Python39\python.exe",
            @"C:\Program Files\Python38\python.exe",
            Path.Combine(LocalAppData, "Programs", "Python", "Python313", "python.exe"),
            Path.Combine(LocalAppData, "Programs", "Python", "Python312", "python.exe"),
            Path.Combine(LocalAppData, "Programs", "Python", "Python311", "python.exe"),
            Path.Combine(LocalAppData, "Programs", "Python", "Python310", "python.exe"),
            // x86 paths intentionally absent — 32-bit cannot load fusionscript
        ];
        paths.AddRange(common);

        // Scan Program Files (64-bit only) for any Python* directory
        if (Directory.Exists(ProgramFiles))
        {
            foreach (var dir in Directory.GetDirectories(ProgramFiles, "Python*",
                         SearchOption.TopDirectoryOnly).OrderByDescending(d => d))
                paths.Add(Path.Combine(dir, "python.exe"));
        }

        return paths;
    }
}
