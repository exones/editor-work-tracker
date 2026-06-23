using DaVinciTimeTracker.Core.Resolve;
using Serilog;
using System.Diagnostics;
using System.Text;

namespace DaVinciTimeTracker.Core.Utilities;

/// <summary>
/// Metadata about a discovered Python interpreter.
/// </summary>
public record PythonCandidate(
    string Path,
    Version? Version,         // null if probe failed
    bool Is64Bit,             // false if 32-bit or probe failed
    string Source,            // e.g. "PATH", "PyManager", "WindowsApps", "ProgramFiles"
    string? CompatibilityNote // null = OK; non-null = warning text shown in Troubleshooter
)
{
    /// <summary>True when this interpreter meets the minimum requirements (64-bit, ≥ 3.6).</summary>
    public bool MeetsMinimumRequirements =>
        Is64Bit && Version is not null && Version >= new Version(3, 6);

    /// <summary>Human-readable summary for display.</summary>
    public string DisplayLabel =>
        Version is null
            ? $"{Path} (probe failed)"
            : $"{Path} — Python {Version.Major}.{Version.Minor}.{Version.Build} {(Is64Bit ? "64-bit" : "32-bit")}";
}

public static class PythonPathResolver
{
    private static readonly IResolvePlatform _platform = ResolvePlatformFactory.Create();

    /// <summary>
    /// Finds the best Python executable for DaVinci Resolve scripting.
    /// Probes each candidate for version and bitness; ranks 64-bit 3.10–3.12 highest.
    /// Falls back to first-found if no compatibility test is possible.
    /// </summary>
    public static string? FindPythonExecutable(ILogger logger, string? scriptPath = null)
    {
        logger.Information("Searching for Python executable...");

        // Env var always wins — explicit user override
        var envPython = Environment.GetEnvironmentVariable("DAVINCI_TRACKER_PYTHON");
        if (!string.IsNullOrEmpty(envPython) && File.Exists(envPython))
        {
            logger.Information("Found Python via DAVINCI_TRACKER_PYTHON env var: {Path}", envPython);
            return envPython;
        }

        var candidates = CollectAndProbe(logger);

        if (candidates.Count == 0)
        {
            logger.Warning("No Python executable found. Install Python 3.10–3.12 (64-bit) or set DAVINCI_TRACKER_PYTHON.");
            return null;
        }

        // If RESOLVE_SCRIPT_LIB is resolvable, run fusionscript compatibility probe
        var resolveLib = _platform.GetFusionScriptLibPath();
        if (!string.IsNullOrEmpty(resolveLib) && File.Exists(resolveLib) &&
            scriptPath != null && File.Exists(scriptPath))
        {
            logger.Information("Testing {Count} Python candidate(s) for DaVinci (fusionscript) compatibility...", candidates.Count);
            foreach (var candidate in candidates)
            {
                var result = TestFusionScriptCompatibility(candidate.Path, resolveLib, logger);
                if (result == FusionScriptTestResult.Compatible)
                {
                    logger.Information("Selected DaVinci-compatible Python: {Path}", candidate.Path);
                    return candidate.Path;
                }
                if (result == FusionScriptTestResult.DaVinciNotRunning)
                {
                    logger.Debug("DaVinci not running yet, cannot test fusionscript compatibility. Using first ranked Python.");
                    break;
                }
            }
        }

        var first = candidates[0];
        logger.Information("Using Python (set DAVINCI_TRACKER_PYTHON to override): {Path}", first.Path);
        return first.Path;
    }

    public enum FusionScriptTestResult { Compatible, Incompatible, DaVinciNotRunning }

    public static FusionScriptTestResult TestFusionScriptCompatibility(
        string pythonPath, string resolveLib, ILogger logger)
    {
        const string testScript = """
import sys, os
dv_dir = os.path.dirname(os.environ.get('RESOLVE_SCRIPT_LIB', ''))
if dv_dir and hasattr(os, 'add_dll_directory'):
    try: os.add_dll_directory(dv_dir)
    except Exception: pass
try:
    import importlib.machinery, importlib.util
    lib = os.environ.get('RESOLVE_SCRIPT_LIB', '')
    loader = importlib.machinery.ExtensionFileLoader('fusionscript', lib)
    spec = importlib.util.spec_from_loader('fusionscript', loader)
    mod = importlib.util.module_from_spec(spec)
    print('LOADED')
except SystemError as e:
    print(f'SYSERR:{e}')
    sys.exit(1)
except Exception as e:
    print(f'ERR:{e}')
    sys.exit(2)
""";
        var tempScript = Path.Combine(Path.GetTempPath(), $"dtt_probe_{Guid.NewGuid():N}.py");
        try
        {
            File.WriteAllText(tempScript, testScript, Encoding.UTF8);
            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{tempScript}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // Inject the full deterministic env (PYTHONHOME, RESOLVE_SCRIPT_LIB/API, PYTHONPATH)
            ResolveEnvironment.ApplyTo(psi, pythonPath, _platform);

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd().Trim();
            var stderr = process.StandardError.ReadToEnd().Trim();
            var exited = process.WaitForExit(8000);

            if (!exited) { process.Kill(); logger.Debug("  {Python}: fusionscript test timed out", pythonPath); return FusionScriptTestResult.DaVinciNotRunning; }
            if (stdout.StartsWith("LOADED")) { logger.Debug("  {Python}: fusionscript loaded OK", pythonPath); return FusionScriptTestResult.Compatible; }
            if (stdout.StartsWith("SYSERR") || stderr.Contains("initialization of fusionscript failed")) { logger.Debug("  {Python}: incompatible — {Detail}", pythonPath, stdout); return FusionScriptTestResult.Incompatible; }
            logger.Debug("  {Python}: inconclusive ({Stdout})", pythonPath, stdout);
            return FusionScriptTestResult.DaVinciNotRunning;
        }
        catch (Exception ex) { logger.Debug("  {Python}: probe exception: {Error}", pythonPath, ex.Message); return FusionScriptTestResult.DaVinciNotRunning; }
        finally { try { File.Delete(tempScript); } catch { } }
    }

    // ── Candidate discovery + probing ─────────────────────────────────────────

    /// <summary>
    /// Collects all Python candidates from the platform provider, probes each for
    /// version and bitness, then ranks them: 64-bit 3.10–3.12 first, then 3.6–3.9,
    /// then 3.13+. 32-bit and <3.6 interpreters are excluded.
    /// </summary>
    internal static List<PythonCandidate> CollectAndProbe(ILogger logger)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var raw = new List<(string path, string source)>();

        void Add(string path, string source)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { path = Path.GetFullPath(path); } catch { return; }
            if (File.Exists(path) && seen.Add(path))
                raw.Add((path, source));
        }

        // Ordered by preference source
        foreach (var p in _platform.GetPythonCandidatePaths())
        {
            var source = ClassifySource(p);
            Add(p, source);
        }

        // Probe each for version + bitness
        var probed = new List<PythonCandidate>();
        foreach (var (path, source) in raw)
        {
            var (version, is64Bit) = ProbeVersionAndBitness(path);

            // Exclude 32-bit or <3.6 — cannot load fusionscript
            if (!is64Bit) { logger.Debug("Excluding 32-bit Python: {Path}", path); continue; }
            if (version is not null && version < new Version(3, 6)) { logger.Debug("Excluding Python <3.6: {Path}", path); continue; }

            string? note = null;
            if (version is not null)
            {
                if (version.Major == 3 && version.Minor >= 13)
                    note = $"Python {version.Major}.{version.Minor} may be incompatible with older Resolve versions (3.10–3.12 recommended)";
                else if (version.Major == 3 && version.Minor < 10)
                    note = $"Python {version.Major}.{version.Minor} works but 3.10–3.12 is preferred";
            }

            probed.Add(new PythonCandidate(path, version, is64Bit, source, note));
        }

        if (probed.Count > 0)
            logger.Information("Found {Count} Python candidate(s): {Paths}", probed.Count,
                string.Join(", ", probed.Select(c => c.DisplayLabel)));

        // Rank: 3.10–3.12 first, then 3.6–3.9, then 3.13+, then unknown version
        return [.. probed.OrderBy(RankCandidate)];
    }

    private static int RankCandidate(PythonCandidate c)
    {
        if (c.Version is null) return 99;
        var minor = c.Version.Minor;
        return c.Version.Major switch
        {
            3 when minor >= 10 && minor <= 12 => 0,  // ideal
            3 when minor >= 6  && minor <= 9  => 1,  // acceptable
            3 when minor >= 13               => 2,  // may have issues
            _ => 99
        };
    }

    private static string ClassifySource(string path)
    {
        if (path.Contains("PyManager", StringComparison.OrdinalIgnoreCase)) return "PyManager";
        if (path.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) return "WindowsApps";
        if (path.Contains("DaVinci Resolve", StringComparison.OrdinalIgnoreCase)) return "DaVinci-bundled";
        if (path.Contains("Program Files", StringComparison.OrdinalIgnoreCase)) return "ProgramFiles";
        if (path.Contains("Programs\\Python", StringComparison.OrdinalIgnoreCase)) return "UserInstall";
        return "PATH";
    }

    private static (Version? version, bool is64Bit) ProbeVersionAndBitness(string pythonPath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = "-c \"import sys,struct; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}|{struct.calcsize(\\\"P\\\")==8}')\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                }
            };
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            var parts = stdout.Split('|');
            if (parts.Length == 2 && Version.TryParse(parts[0], out var ver))
                return (ver, parts[1].Equals("True", StringComparison.OrdinalIgnoreCase));
        }
        catch { }
        return (null, false);
    }

    // ── Legacy helpers ────────────────────────────────────────────────────────

    public static bool ValidatePythonInstallation(string pythonPath, ILogger logger)
    {
        if (!File.Exists(pythonPath))
        {
            logger.Error("Python executable not found at: {Path}", pythonPath);
            return false;
        }
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var version = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            var exited = process.WaitForExit(5000);

            if (!exited) { process.Kill(); logger.Error("Python validation timed out"); return false; }
            if (process.ExitCode == 0) { logger.Information("Python validation successful: {Version}", version.Trim()); return true; }
            var errorMessage = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : "No error details available";
            logger.Error("Python validation failed with exit code: {ExitCode}. Error: {Error}", process.ExitCode, errorMessage);
            return false;
        }
        catch (Exception ex) { logger.Error(ex, "Failed to validate Python installation"); return false; }
    }
}
