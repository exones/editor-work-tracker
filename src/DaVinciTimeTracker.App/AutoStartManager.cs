using Serilog;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DaVinciTimeTracker.App;

public class AutoStartManager
{
    private readonly ILogger _logger;
    private readonly string _startupFolderPath;
    private readonly string _shortcutPath;
    private readonly string _appPath;

    public AutoStartManager(ILogger logger)
    {
        _logger = logger;
        _startupFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup));
        _shortcutPath = Path.Combine(_startupFolderPath, "DaVinci Time Tracker.lnk");
        _appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine application path");
    }

    public bool IsAutoStartEnabled()
    {
        var shortcutExists = System.IO.File.Exists(_shortcutPath);
        _logger.Debug("Auto-start check: {Exists} (Path: {Path})", shortcutExists, _shortcutPath);
        return shortcutExists;
    }

    public bool EnableAutoStart()
    {
        try
        {
            _logger.Information("Enabling auto-start with Windows");

            // Use COM dynamically to avoid compile-time dependencies
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                throw new InvalidOperationException("WScript.Shell not available");
            }

            object? shell = null;
            object? shortcut = null;

            try
            {
                shell = Activator.CreateInstance(shellType)!;
                dynamic shellDynamic = shell;
                shortcut = shellDynamic.CreateShortcut(_shortcutPath);
                dynamic shortcutDynamic = shortcut;

                shortcutDynamic.TargetPath = _appPath;
                shortcutDynamic.WorkingDirectory = Path.GetDirectoryName(_appPath);
                shortcutDynamic.Description = "DaVinci Time Tracker - Auto-start with Windows";
                shortcutDynamic.Save();

                _logger.Information("Auto-start enabled successfully");
                return true;
            }
            finally
            {
                if (shortcut != null)
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }

                if (shell != null)
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to enable auto-start");
            return false;
        }
    }

    public bool DisableAutoStart()
    {
        try
        {
            _logger.Information("Disabling auto-start with Windows");

            if (System.IO.File.Exists(_shortcutPath))
            {
                System.IO.File.Delete(_shortcutPath);
                _logger.Information("Auto-start disabled successfully");
                return true;
            }

            _logger.Warning("Auto-start shortcut not found (already disabled?)");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to disable auto-start");
            return false;
        }
    }
}
