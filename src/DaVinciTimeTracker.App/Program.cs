using DaVinciTimeTracker.App;
using DaVinciTimeTracker.Core.Monitors;
using DaVinciTimeTracker.Core.Models;
using DaVinciTimeTracker.Core.Resolve;
using DaVinciTimeTracker.Core.Services;
using DaVinciTimeTracker.Core.Utilities;
using DaVinciTimeTracker.Data;
using DaVinciTimeTracker.Data.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File(AppPaths.LogFilePath, rollingInterval: RollingInterval.Day, encoding: System.Text.Encoding.UTF8)
    .CreateLogger();

Log.Information("DaVinci Time Tracker starting...");
Log.Information("User data directory: {DataDir}", AppPaths.UserDataDirectory);
Log.Information("Database path: {DbPath}", AppPaths.DatabasePath);
Log.Information("Logs directory: {LogsDir}", AppPaths.LogsDirectory);

try
{
    // Application setup
    ApplicationConfiguration.Initialize();

    // Start web server in background
    var webServerTask = Task.Run(async () =>
    {
        var builder = WebApplication.CreateBuilder();

        // Set content root to application base directory for Windows Forms app
        builder.Environment.ContentRootPath = AppPaths.ApplicationDirectory;
        builder.Environment.WebRootPath = AppPaths.WwwRootPath;

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(DaVinciTimeTracker.Web.Controllers.ApiController).Assembly);
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            });
        });

        // Database
        builder.Services.AddDbContext<TimeTrackerDbContext>(options =>
            options.UseSqlite(AppPaths.DatabaseConnectionString));

        // Services
        builder.Services.AddScoped<SessionRepository>();
        builder.Services.AddSingleton<StatisticsService>();
        builder.Services.AddSingleton<SessionManager>(sp => AppState.SessionManager);

        builder.WebHost.UseUrls("http://localhost:5555");

        var app = builder.Build();

        // Apply migrations
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackerDbContext>();
            db.Database.Migrate();
            Log.Information("Database initialized");

            // Crash recovery: Close all open sessions
            var openSessions = db.ProjectSessions.Where(s => s.EndTime == null).ToList();
            if (openSessions.Any())
            {
                Log.Warning("Found {Count} open session(s) from previous run - closing them", openSessions.Count);
                foreach (var session in openSessions)
                {
                    // Use FlushedEnd if available (last known good state), otherwise use UtcNow
                    session.EndTime = session.FlushedEnd ?? DateTime.UtcNow;
                    Log.Information("Closed orphaned session: {ProjectName} (Started: {Start}, Ended: {End})",
                        session.ProjectName,
                        session.StartTime,
                        session.EndTime);
                }
                db.SaveChanges();
                Log.Information("All orphaned sessions closed");
            }
        }

        app.UseCors();

        var wwwrootPath = AppPaths.WwwRootPath;
        var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wwwrootPath);

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider,
            RequestPath = ""
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            RequestPath = ""
        });

        app.MapControllers();

        Log.Information("Web server starting on http://localhost:5555");
        await app.RunAsync();
    });

    // Initialize tracking services
    var pythonPath = PythonPathResolver.FindPythonExecutable(Log.Logger);
    if (pythonPath == null)
    {
        var errorMsg = "Python not found. Please install Python 3.8+ or set DAVINCI_TRACKER_PYTHON environment variable to python.exe path.";
        Log.Fatal(errorMsg);
        MessageBox.Show(errorMsg, "Python Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return;
    }

    if (!PythonPathResolver.ValidatePythonInstallation(pythonPath, Log.Logger))
    {
        var errorMsg = $"Python installation at '{pythonPath}' is not valid.";
        Log.Fatal(errorMsg);
        MessageBox.Show(errorMsg, "Invalid Python Installation", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return;
    }

    var scriptPath = AppPaths.PythonScriptPath;
    if (!File.Exists(scriptPath))
    {
        var errorMsg = $"Python script not found at: {scriptPath}";
        Log.Fatal(errorMsg);
        MessageBox.Show(errorMsg, "Script Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return;
    }

    Log.Information("Using Python: {PythonPath}", pythonPath);
    Log.Information("Using script: {ScriptPath}", scriptPath);

    var resolveApiClient = new ResolveApiClient(pythonPath, scriptPath, Log.Logger);
    var resolveMonitor = new DaVinciResolveMonitor(resolveApiClient, Log.Logger);
    var activityMonitor = new ActivityMonitor(Log.Logger, checkIntervalMs: 5000, inactivityThresholdMinutes: 1);
    var sessionManager = new SessionManager(Log.Logger);
    var timeTrackingService = new TimeTrackingService(resolveMonitor, activityMonitor, sessionManager, Log.Logger);

    AppState.TimeTrackingService = timeTrackingService;
    AppState.SessionManager = sessionManager;
    AppState.SessionRepository = new SessionRepository(
        new TimeTrackerDbContext(
            new DbContextOptionsBuilder<TimeTrackerDbContext>()
                .UseSqlite(AppPaths.DatabaseConnectionString)
                .Options));

    // Wire up session events to save to database
    var saveSession = async (ProjectSession session) =>
    {
        try
        {
            using var dbContext = new TimeTrackerDbContext(
                new DbContextOptionsBuilder<TimeTrackerDbContext>()
                    .UseSqlite(AppPaths.DatabaseConnectionString)
                    .Options);
            var repo = new SessionRepository(dbContext);
            await repo.SaveSessionAsync(session);
            Log.Debug("Session saved to database: {ProjectName}", session.ProjectName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save session to database");
        }
    };

    sessionManager.SessionEnded += async (sender, session) => await saveSession(session);

    // Periodic save every 30 seconds for all active states (GraceStart, Tracking, GraceEnd)
    var periodicSaveTimer = new System.Timers.Timer(30000);
    periodicSaveTimer.Elapsed += async (sender, e) =>
    {
        var currentState = sessionManager.CurrentState;

        // Save for all non-NotTracking states
        if (currentState != TrackingState.NotTracking)
        {
            var currentSession = sessionManager.GetCurrentSession();
            if (currentSession != null)
            {
                // Update FlushedEnd for crash recovery
                currentSession.FlushedEnd = DateTime.UtcNow;
                await saveSession(currentSession);
            }
        }
    };
    periodicSaveTimer.Start();

    // Start tracking
    timeTrackingService.Start();

    // Run tray application
    Application.Run(new TrayApplicationContext());

    // Cleanup
    timeTrackingService.Stop();
    periodicSaveTimer.Stop();
    periodicSaveTimer.Dispose();
    timeTrackingService.Dispose();
    Log.Information("DaVinci Time Tracker stopped");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application failed to start");
    MessageBox.Show($"Failed to start application: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
finally
{
    Log.CloseAndFlush();
}

public static class AppState
{
    public static TimeTrackingService TimeTrackingService { get; set; } = null!;
    public static SessionManager SessionManager { get; set; } = null!;
    public static SessionRepository SessionRepository { get; set; } = null!;
}
