using Serilog;
using System;
using System.IO;
using System.Linq;

namespace DaVinciTimeTracker.Core.Utilities;

public static class PythonPathResolver
{
    public static string? FindPythonExecutable(ILogger logger)
    {
        logger.Information("Searching for Python executable...");

        // 1. Check environment variable first (user can override)
        var envPython = Environment.GetEnvironmentVariable("DAVINCI_TRACKER_PYTHON");
        if (!string.IsNullOrEmpty(envPython) && File.Exists(envPython))
        {
            logger.Information("Found Python via DAVINCI_TRACKER_PYTHON env var: {Path}", envPython);
            return envPython;
        }

        // 2. Check PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            var pythonInPath = pathEnv.Split(Path.PathSeparator)
                .Select(p => Path.Combine(p, "python.exe"))
                .FirstOrDefault(File.Exists);

            if (pythonInPath != null)
            {
                logger.Information("Found Python in PATH: {Path}", pythonInPath);
                return pythonInPath;
            }
        }

        // 3. Check common installation locations (Windows)
        var commonPaths = new[]
        {
            // Python.org installations
            @"C:\Program Files\Python312\python.exe",
            @"C:\Program Files\Python311\python.exe",
            @"C:\Program Files\Python310\python.exe",
            @"C:\Program Files\Python39\python.exe",
            @"C:\Program Files\Python38\python.exe",

            // 32-bit Python on 64-bit Windows
            @"C:\Program Files (x86)\Python312\python.exe",
            @"C:\Program Files (x86)\Python311\python.exe",
            @"C:\Program Files (x86)\Python310\python.exe",

            // User-specific installations
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Python", "Python312", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Python", "Python311", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Python", "Python310", "python.exe"),

            // Microsoft Store installation
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "python3.exe"),
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                logger.Information("Found Python at common location: {Path}", path);
                return path;
            }
        }

        // 4. Search Program Files directories
        var programFilesDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (var programFilesDir in programFilesDirs.Where(Directory.Exists))
        {
            var pythonDirs = Directory.GetDirectories(programFilesDir, "Python*", SearchOption.TopDirectoryOnly);
            foreach (var pythonDir in pythonDirs.OrderByDescending(d => d))
            {
                var pythonExe = Path.Combine(pythonDir, "python.exe");
                if (File.Exists(pythonExe))
                {
                    logger.Information("Found Python via directory search: {Path}", pythonExe);
                    return pythonExe;
                }
            }
        }

        logger.Warning("Python executable not found. Please install Python or set DAVINCI_TRACKER_PYTHON environment variable.");
        return null;
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
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
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
            var exited = process.WaitForExit(5000); // 5 second timeout

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
