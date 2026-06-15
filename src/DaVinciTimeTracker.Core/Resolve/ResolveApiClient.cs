using System.Diagnostics;
using System.Text;
using Serilog;

namespace DaVinciTimeTracker.Core.Resolve;

public class ResolveApiClient
{
    private readonly string _pythonPath;
    private readonly string _scriptPath;
    private readonly ILogger _logger;
    private string? _lastErrorMessage;
    private int _consecutiveErrors = 0;
    private DateTime _lastErrorLogTime = DateTime.MinValue;

    public ResolveApiClient(string pythonPath, string scriptPath, ILogger logger)
    {
        _pythonPath = pythonPath;
        _scriptPath = scriptPath;
        _logger = logger;

        // Log configuration for troubleshooting
        _logger.Information("DaVinci API Client configured:");
        _logger.Information("  Python path: {PythonPath}", pythonPath);
        _logger.Information("  Script path: {ScriptPath}", scriptPath);

        // Validate paths exist
        if (!File.Exists(pythonPath) && !CanFindInPath(pythonPath))
        {
            _logger.Error("Python executable not found at: {PythonPath}", pythonPath);
        }

        if (!File.Exists(scriptPath))
        {
            _logger.Error("DaVinci API script not found at: {ScriptPath}", scriptPath);
        }
    }

    private bool CanFindInPath(string executable)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = executable,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Run comprehensive sanity check when DaVinci is first discovered.
    /// Logs detailed diagnostics to help troubleshoot connection issues.
    /// </summary>
    public async Task<bool> RunSanityCheckAsync()
    {
        _logger.Information("========================================");
        _logger.Information("DaVinci Resolve API - Connection Test");
        _logger.Information("========================================");

        // Check 1: Python executable
        _logger.Information("✓ Checking Python executable...");
        if (!File.Exists(_pythonPath) && !CanFindInPath(_pythonPath))
        {
            _logger.Error("✗ FAIL: Python not found at: {PythonPath}", _pythonPath);
            _logger.Error("→ Set DAVINCI_TRACKER_PYTHON env var to a working Python path (e.g. C:\\Program Files\\PyManager\\python.exe)");
            _logger.Error("→ Or install Python 3.8+ and verify it appears in PATH");
            return false;
        }
        _logger.Information("  Found: {PythonPath}", _pythonPath);

        // Check 2: Python version
        try
        {
            var versionProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonPath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            versionProcess.Start();
            var versionOutput = await versionProcess.StandardOutput.ReadToEndAsync();
            await versionProcess.WaitForExitAsync();
            _logger.Information("  Version: {Version}", versionOutput.Trim());
        }
        catch (Exception ex)
        {
            _logger.Warning("  Could not check Python version: {Error}", ex.Message);
        }

        // Check 3: Script file
        _logger.Information("✓ Checking API script...");
        if (!File.Exists(_scriptPath))
        {
            _logger.Error("✗ FAIL: Script not found at: {ScriptPath}", _scriptPath);
            return false;
        }
        _logger.Information("  Found: {ScriptPath}", _scriptPath);

        // Check 4: Try API call
        _logger.Information("✓ Testing API connection to DaVinci Resolve...");
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonPath,
                    Arguments = $"\"{_scriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            _logger.Information("  Exit code: {ExitCode}", process.ExitCode);

            if (!string.IsNullOrWhiteSpace(output))
            {
                _logger.Information("  Output: {Output}", output.Trim());
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.Warning("  Stderr: {Error}", error.Trim());
            }

            // Check both stdout and stderr for error messages (case-insensitive)
            var combinedOutput = $"{output} {error}";
            var combinedLower = combinedOutput.ToLower();

            // Provide specific guidance based on error type
            if ((combinedLower.Contains("fusionscript") && combinedLower.Contains("initialization")) ||
                (combinedLower.Contains("fusion") && combinedLower.Contains("failed")))
            {
                _logger.Error("✗ FAIL: DaVinci Resolve Fusion script initialization failed");
                _logger.Error("→ fusionscript.dll PyInit returned NULL — DaVinci IPC handshake rejected");
                _logger.Error("→ Possible causes:");
                _logger.Error("  1. External scripting is DISABLED (MOST COMMON)");
                _logger.Error("     Fix: DaVinci Preferences → General → External scripting using → 'Local'");
                _logger.Error("  2. Python build incompatibility (Python.org builds can fail; PyManager/Store Python works)");
                _logger.Error("     Fix: Set DAVINCI_TRACKER_PYTHON=C:\\Program Files\\PyManager\\python.exe");
                _logger.Error("     Tip: Check stderr — [resolve_api] lines show which Python was used");
                _logger.Error("  3. DaVinci Resolve FREE (not Studio) — scripting API is Studio-only");
                _logger.Error("  4. DaVinci hasn't fully initialized its scripting server yet");
                _logger.Error("     Fix: Wait 10–15 seconds after Resolve opens");
                return false;
            }
            else if (combinedLower.Contains("modulenotfounderror") || combinedLower.Contains("no module named"))
            {
                _logger.Error("✗ FAIL: Python module 'DaVinciResolveScript' not found");
                _logger.Error("→ This usually means:");
                _logger.Error("  1. DaVinci Resolve Studio is not installed");
                _logger.Error("  2. DaVinci Resolve is the FREE version (API only in Studio)");
                _logger.Error("  3. Python cannot find the DaVinci Resolve API modules");
                _logger.Error("→ Solution: Install DaVinci Resolve Studio (not Free version)");
                return false;
            }
            else if (combinedLower.Contains("connectionrefusederror") || combinedLower.Contains("refused"))
            {
                _logger.Error("✗ FAIL: Cannot connect to DaVinci Resolve");
                _logger.Error("→ DaVinci Resolve is running but refusing API connections");
                _logger.Error("→ Check: Preferences → General → External scripting using");
                return false;
            }

            if (process.ExitCode == 0)
            {
                _logger.Information("✓ SUCCESS: DaVinci API connection working!");
                _logger.Information("========================================");
                return true;
            }
            else
            {
                _logger.Error("✗ FAIL: Script exited with code {ExitCode}", process.ExitCode);
                _logger.Information("========================================");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "✗ FAIL: Exception during API test");
            _logger.Information("========================================");
            return false;
        }
    }

    public async Task<string?> GetCurrentProjectNameAsync()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonPath,
                    Arguments = $"\"{_scriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !output.Contains("NO_PROJECT"))
            {
                var projectName = output.Trim();

                // Treat "Untitled Project" as no project open
                if (projectName.Equals("Untitled Project", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Debug("DaVinci opened without project (Untitled Project)");
                    _consecutiveErrors = 0; // Reset error counter on success
                    _lastErrorMessage = null;
                    return null;
                }

                _logger.Debug("DaVinci project detected: {ProjectName}", projectName);

                // Reset error tracking on successful API call
                _consecutiveErrors = 0;
                _lastErrorMessage = null;

                return projectName;
            }

            if (output.Contains("NO_PROJECT"))
            {
                _logger.Debug("No DaVinci project currently open");
                _consecutiveErrors = 0; // Reset error counter on success
                _lastErrorMessage = null;
                return null;
            }

            // Detailed error logging with debouncing (avoid spam)
            var currentError = $"{error}|{output}|{process.ExitCode}";
            var shouldLogFull = false;

            if (currentError != _lastErrorMessage)
            {
                // New error type - always log
                shouldLogFull = true;
                _consecutiveErrors = 1;
                _lastErrorMessage = currentError;
                _lastErrorLogTime = DateTime.UtcNow;
            }
            else
            {
                // Same error repeating
                _consecutiveErrors++;

                // Log full details every 30 seconds for recurring errors
                if ((DateTime.UtcNow - _lastErrorLogTime).TotalSeconds >= 30)
                {
                    shouldLogFull = true;
                    _lastErrorLogTime = DateTime.UtcNow;
                }
            }

            if (shouldLogFull)
            {
                _logger.Warning("DaVinci API call failed{Repeat}:",
                    _consecutiveErrors > 1 ? $" ({_consecutiveErrors} consecutive failures)" : "");
                _logger.Warning("  Exit code: {ExitCode}", process.ExitCode);
                _logger.Warning("  Python: {PythonPath}", _pythonPath);
                _logger.Warning("  Script: {ScriptPath}", _scriptPath);

                if (!string.IsNullOrWhiteSpace(output))
                {
                    _logger.Warning("  Stdout: {Output}", output.Trim());
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    _logger.Warning("  Stderr: {Error}", error.Trim());
                }
                else
                {
                    _logger.Warning("  Stderr: (empty)");
                }

                // Provide actionable guidance based on error type
                var combinedOutput = $"{output} {error}";
                var combinedLower = combinedOutput.ToLower();

                if ((combinedLower.Contains("fusionscript") && combinedLower.Contains("initialization")) ||
                    (combinedLower.Contains("fusion") && combinedLower.Contains("failed")))
                {
                    _logger.Error("→ fusionscript.dll PyInit rejected — DaVinci IPC handshake failed");
                    _logger.Error("→ Check stderr lines starting with [resolve_api] to see which Python is running");
                    _logger.Error("→ Fix 1: DaVinci Preferences → General → External scripting using → 'Local'");
                    _logger.Error("→ Fix 2: Python build issue — set DAVINCI_TRACKER_PYTHON=C:\\Program Files\\PyManager\\python.exe");
                    _logger.Error("→ Fix 3: Confirm DaVinci Resolve Studio (not Free) — scripting is Studio-only");
                }
                else if (combinedLower.Contains("modulenotfounderror") || combinedLower.Contains("no module named"))
                {
                    _logger.Error("→ Python module missing. DaVinci Resolve Studio may not be installed or Python API not available.");
                }
                else if (combinedLower.Contains("connectionrefusederror") || combinedLower.Contains("refused"))
                {
                    _logger.Error("→ Cannot connect to DaVinci Resolve. Ensure Resolve is running and external scripting is enabled.");
                }
                else if (string.IsNullOrWhiteSpace(error) && string.IsNullOrWhiteSpace(output))
                {
                    _logger.Error("→ Python script produced no output. Check if DaVinci Resolve Studio is installed and configured.");
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to execute DaVinci API script:");
            _logger.Error("  Python: {PythonPath}", _pythonPath);
            _logger.Error("  Script: {ScriptPath}", _scriptPath);
            return null;
        }
    }
}
