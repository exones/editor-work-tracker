using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace DaVinciTimeTracker.Core.Utilities;

public static class PythonPathResolver
{
    /// <summary>
    /// Finds the best Python executable for DaVinci Resolve scripting.
    ///
    /// The tricky part: fusionscript.dll (DaVinci's C extension) requires a specific
    /// Python process environment to initialize. Python.org builds can fail with
    /// "initialization of fusionscript failed without raising an exception" even though
    /// the Python version itself is compatible (stable ABI). Windows Store Python
    /// (and launchers like PyManager) tend to work because they set up the process
    /// environment differently.
    ///
    /// Strategy: collect all candidates, test each against fusionscript when DaVinci
    /// is running, return the first that works. Falls back to first-found if no test
    /// is possible (DaVinci not yet running at startup).
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

        var candidates = CollectCandidates(logger);

        if (candidates.Count == 0)
        {
            logger.Warning("No Python executable found. Install Python or set DAVINCI_TRACKER_PYTHON.");
            return null;
        }

        // If we have a script and RESOLVE_SCRIPT_LIB is set (DaVinci installed),
        // test each candidate for actual fusionscript compatibility.
        var resolveLib = Environment.GetEnvironmentVariable("RESOLVE_SCRIPT_LIB");
        if (!string.IsNullOrEmpty(resolveLib) && File.Exists(resolveLib) && scriptPath != null && File.Exists(scriptPath))
        {
            logger.Information("Testing {Count} Python candidates for DaVinci (fusionscript) compatibility...", candidates.Count);
            foreach (var candidate in candidates)
            {
                var result = TestFusionScriptCompatibility(candidate, resolveLib, logger);
                if (result == FusionScriptTestResult.Compatible)
                {
                    logger.Information("Selected DaVinci-compatible Python: {Path}", candidate);
                    return candidate;
                }
                if (result == FusionScriptTestResult.DaVinciNotRunning)
                {
                    // DaVinci not running — we can't tell which Python works.
                    // Fall through to first-found selection below.
                    logger.Debug("DaVinci not running yet, cannot test fusionscript compatibility. Using first Python found.");
                    break;
                }
                // FusionScriptTestResult.Incompatible → try next candidate
            }
        }

        // Fallback: return first candidate (DaVinci not running or no script path)
        var first = candidates[0];
        logger.Information("Using first available Python (set DAVINCI_TRACKER_PYTHON to override): {Path}", first);
        return first;
    }

    public enum FusionScriptTestResult
    {
        Compatible,
        Incompatible,
        DaVinciNotRunning,
    }

    /// <summary>
    /// Runs the resolve_api script with the given Python and categorizes the result:
    /// - Compatible: fusionscript loaded and returned a project name or NO_PROJECT
    /// - Incompatible: fusionscript failed to initialize (ABI or process environment mismatch)
    /// - DaVinciNotRunning: couldn't determine (Resolve process not up yet)
    /// </summary>
    public static FusionScriptTestResult TestFusionScriptCompatibility(string pythonPath, string resolveLib, ILogger logger)
    {
        // Inline script: just load fusionscript — does not need DaVinci running to detect ABI issues,
        // but PyInit itself requires Resolve's IPC. We distinguish the two error types by message.
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

        // Write to a temp file — passing multiline scripts via -c on Windows is unreliable
        // because the Arguments string escaping doesn't preserve newlines correctly.
        var tempScript = Path.Combine(Path.GetTempPath(), $"dtt_probe_{Guid.NewGuid():N}.py");
        try
        {
            File.WriteAllText(tempScript, testScript, Encoding.UTF8);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{tempScript}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                }
            };

            // Pass RESOLVE_SCRIPT_LIB so the probe script can find the DLL
            process.StartInfo.Environment["RESOLVE_SCRIPT_LIB"] = resolveLib;

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd().Trim();
            var stderr = process.StandardError.ReadToEnd().Trim();
            var exited = process.WaitForExit(8000);

            if (!exited)
            {
                process.Kill();
                logger.Debug("  {Python}: fusionscript test timed out", pythonPath);
                return FusionScriptTestResult.DaVinciNotRunning;
            }

            if (stdout.StartsWith("LOADED"))
            {
                logger.Debug("  {Python}: fusionscript loaded OK", pythonPath);
                return FusionScriptTestResult.Compatible;
            }

            if (stdout.StartsWith("SYSERR") || stderr.Contains("initialization of fusionscript failed"))
            {
                logger.Debug("  {Python}: fusionscript ABI/IPC incompatible — {Detail}", pythonPath, stdout);
                return FusionScriptTestResult.Incompatible;
            }

            // Any other error (DLL not found, module not found) suggests DaVinci not running
            // or not installed — not a Python ABI problem
            logger.Debug("  {Python}: fusionscript test inconclusive ({Stdout})", pythonPath, stdout);
            return FusionScriptTestResult.DaVinciNotRunning;
        }
        catch (Exception ex)
        {
            logger.Debug("  {Python}: fusionscript test exception: {Error}", pythonPath, ex.Message);
            return FusionScriptTestResult.DaVinciNotRunning;
        }
        finally
        {
            try { File.Delete(tempScript); } catch { }
        }
    }

    /// <summary>
    /// Builds a deduplicated, ordered list of all Python candidate paths to evaluate.
    /// </summary>
    private static List<string> CollectCandidates(ILogger logger)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        void Add(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { path = Path.GetFullPath(path); } catch { return; }
            if (File.Exists(path) && seen.Add(path))
                result.Add(path);
        }

        // DaVinci-bundled Python (some older Resolve versions)
        var resolveInstallDir = @"C:\Program Files\Blackmagic Design\DaVinci Resolve";
        Add(Path.Combine(resolveInstallDir, "Python310", "python.exe"));
        Add(Path.Combine(resolveInstallDir, "Python", "Python310", "python.exe"));
        Add(Path.Combine(resolveInstallDir, "Python", "Python36", "python.exe"));

        // PyManager (known to work with fusionscript on some systems where Python.org doesn't)
        Add(@"C:\Program Files\PyManager\python.exe");

        // Windows Store Python — tends to load fusionscript correctly due to process setup
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Add(Path.Combine(localApp, "Microsoft", "WindowsApps", "python.exe"));
        Add(Path.Combine(localApp, "Microsoft", "WindowsApps", "python3.exe"));

        // All python.exe entries found in PATH (preserves PATH order)
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try { Add(Path.Combine(dir.Trim(), "python.exe")); } catch { }
        }

        // Common Python.org installation locations
        string[] commonPaths =
        [
            @"C:\Program Files\Python312\python.exe",
            @"C:\Program Files\Python311\python.exe",
            @"C:\Program Files\Python310\python.exe",
            @"C:\Program Files\Python39\python.exe",
            @"C:\Program Files\Python38\python.exe",
            @"C:\Program Files (x86)\Python312\python.exe",
            @"C:\Program Files (x86)\Python311\python.exe",
            @"C:\Program Files (x86)\Python310\python.exe",
            Path.Combine(localApp, "Programs", "Python", "Python312", "python.exe"),
            Path.Combine(localApp, "Programs", "Python", "Python311", "python.exe"),
            Path.Combine(localApp, "Programs", "Python", "Python310", "python.exe"),
        ];
        foreach (var p in commonPaths) Add(p);

        // Scan Program Files for any Python* directory
        foreach (var programFilesDir in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(Directory.Exists))
        {
            foreach (var dir in Directory.GetDirectories(programFilesDir, "Python*", SearchOption.TopDirectoryOnly)
                                         .OrderByDescending(d => d))
            {
                Add(Path.Combine(dir, "python.exe"));
            }
        }

        if (result.Count > 0)
            logger.Information("Found {Count} Python candidate(s): {Paths}", result.Count, string.Join(", ", result));

        return result;
    }

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

            if (!exited)
            {
                process.Kill();
                logger.Error("Python validation timed out");
                return false;
            }

            if (process.ExitCode == 0)
            {
                logger.Information("Python validation successful: {Version}", version.Trim());
                return true;
            }

            var errorMessage = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : "No error details available";
            logger.Error("Python validation failed with exit code: {ExitCode}. Error: {Error}", process.ExitCode, errorMessage);
            return false;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to validate Python installation");
            return false;
        }
    }
}
