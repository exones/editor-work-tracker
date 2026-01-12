using System;
using System.IO;

namespace DaVinciTimeTracker.Core.Utilities;

/// <summary>
/// Provides centralized path management for application data following Windows best practices
/// </summary>
public static class AppPaths
{
    private const string AppName = "DaVinciTimeTracker";

    /// <summary>
    /// Gets the user-specific application data directory
    /// Default: %LOCALAPPDATA%\DaVinciTimeTracker\
    /// Example: C:\Users\JohnDoe\AppData\Local\DaVinciTimeTracker\
    /// </summary>
    public static string UserDataDirectory
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDataDir = Path.Combine(localAppData, AppName);

            // Ensure directory exists
            if (!Directory.Exists(appDataDir))
            {
                Directory.CreateDirectory(appDataDir);
            }

            return appDataDir;
        }
    }

    /// <summary>
    /// Gets the full path to the SQLite database file
    /// </summary>
    public static string DatabasePath => Path.Combine(UserDataDirectory, "davinci-timetracker.db");

    /// <summary>
    /// Gets the SQLite connection string
    /// </summary>
    public static string DatabaseConnectionString => $"Data Source={DatabasePath}";

    /// <summary>
    /// Gets the logs directory path
    /// Logs are stored in the user data directory, not the application directory
    /// </summary>
    public static string LogsDirectory
    {
        get
        {
            var logsDir = Path.Combine(UserDataDirectory, "logs");

            // Ensure directory exists
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }

            return logsDir;
        }
    }

    /// <summary>
    /// Gets the log file path with date pattern for Serilog
    /// </summary>
    public static string LogFilePath => Path.Combine(LogsDirectory, "davinci-tracker-.log");

    /// <summary>
    /// Gets the application executable directory (where the .exe is located)
    /// This is where wwwroot, resolve_api.py, and other static assets are located
    /// </summary>
    public static string ApplicationDirectory => AppDomain.CurrentDomain.BaseDirectory;

    /// <summary>
    /// Gets the path to the Python script
    /// </summary>
    public static string PythonScriptPath => Path.Combine(ApplicationDirectory, "resolve_api.py");

    /// <summary>
    /// Gets the path to the wwwroot directory
    /// </summary>
    public static string WwwRootPath => Path.Combine(ApplicationDirectory, "wwwroot");
}
