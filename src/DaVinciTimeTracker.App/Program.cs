using DaVinciTimeTracker.App;
using DaVinciTimeTracker.App.Hotkeys;
using DaVinciTimeTracker.Core.Configuration;
using DaVinciTimeTracker.Core.Diagnostics;
using DaVinciTimeTracker.Core.Monitors;
using DaVinciTimeTracker.Core.Models;
using DaVinciTimeTracker.Core.Native;
using DaVinciTimeTracker.Core.NodeToggle;
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
using Microsoft.Toolkit.Uwp.Notifications;
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
    // Suppress ASP.NET Core / EF Core / Kestrel chatter — keep only warnings and above from Microsoft/System
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
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

    // All AppState singletons that the DI container resolves via factory lambdas must be set
    // BEFORE the web server starts. If a request arrives before they're set the factory returns
    // null and ASP.NET throws "Unable to resolve service" → 500 on every endpoint.

    // Platform-specific OS activity/focus provider
    var systemActivity = SystemActivityProviderFactory.Create(Log.Logger);

    // Shared tracking context — written by monitors, read by the SessionManager reducer
    var trackingContext = new TrackingContext();
    AppState.TrackingContext = trackingContext;

    // SessionManager needs no Python — create immediately
    var sessionManager = new SessionManager(Log.Logger);
    AppState.SessionManager = sessionManager;

    // NodeToggleService needs no Python at construction — Python path is set after detection
    var nodeToggleService = new NodeToggleService(
        "", "", AppPaths.NodeToggleConfigPath, Log.Logger);
    AppState.NodeToggleService = nodeToggleService;

    // DiagnosticsService placeholder — real instance (with resolved Python) created after Python detection
    var platform = ResolvePlatformFactory.Create();
    AppState.DiagnosticsService = new ResolveDiagnosticsService(
        platform, SystemActivityProviderFactory.Create(Log.Logger),
        nodeToggleService.GetApiClient(), "", Log.Logger);

    // Load user settings before the web server and tracking components start
    var userSettingsService = new UserSettingsService(AppPaths.UserSettingsPath, Log.Logger);
    AppState.UserSettingsService = userSettingsService;

    // Wire settings into TrackingConfiguration so grace periods use saved values
    TrackingConfiguration.Configure(userSettingsService);

    // Register app for toast notifications
    ToastNotificationManagerCompat.OnActivated += toastArgs =>
    {
        // Handle toast activation (e.g., user clicks the notification)
        // For now, just log it
        Log.Information("Toast notification activated");
    };

    // Start web server in background
    var webServerTask = Task.Run(async () =>
    {
        var builder = WebApplication.CreateBuilder();

        // Route ASP.NET Core framework logs (DI failures, 500 errors, middleware) into Serilog
        builder.Host.UseSerilog(Log.Logger);

        // Set content root to application base directory for Windows Forms app
        builder.Environment.ContentRootPath = AppPaths.ApplicationDirectory;
        builder.Environment.WebRootPath = AppPaths.WwwRootPath;

        builder.Services.AddControllers()
            .AddJsonOptions(opts =>
                opts.JsonSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter()))
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
        builder.Services.AddScoped<ProjectRepository>();
        builder.Services.AddSingleton<StatisticsService>();
        builder.Services.AddSingleton<SessionManager>(sp => AppState.SessionManager);
        builder.Services.AddSingleton<NodeToggleService>(sp => AppState.NodeToggleService);
        builder.Services.AddSingleton<TrackingContext>(sp => AppState.TrackingContext);
        builder.Services.AddSingleton<UserSettingsService>(sp => AppState.UserSettingsService);
        builder.Services.AddSingleton<ResolveDiagnosticsService>(sp => AppState.DiagnosticsService);

        builder.WebHost.UseUrls("http://localhost:5555");

        var app = builder.Build();

        // Apply migrations
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TimeTrackerDbContext>();
            db.Database.Migrate();
            Log.Information("Database initialized");

            // Crash recovery: Finalize all open sessions from previous run for current user only
            var currentUser = Environment.UserName;
            var openSessions = db.ProjectSessions
                .Where(s => s.EndTime == null && s.UserName == currentUser)
                .ToList();

            if (openSessions.Any())
            {
                Log.Warning("Crash recovery: Found {Count} open session(s) for user {UserName} from previous run - finalizing them",
                    openSessions.Count, currentUser);

                foreach (var session in openSessions)
                {
                    if (session.StartTime == DateTime.MinValue)
                    {
                        Log.Information("Removing GraceStart placeholder: {ProjectName} for user {UserName}",
                            session.ProjectName, session.UserName);
                        db.ProjectSessions.Remove(session);
                    }
                    else
                    {
                        session.EndTime = session.FlushedEnd ?? DateTime.UtcNow;
                        var duration = session.EndTime.Value - session.StartTime;
                        Log.Information("Finalized session: {ProjectName} for user {UserName} (Duration: {Duration:hh\\:mm\\:ss})",
                            session.ProjectName, session.UserName, duration);
                    }
                }

                db.SaveChanges();
                Log.Information("Crash recovery complete for user {UserName} - all sessions finalized. Monitoring will resume and detect any active DaVinci projects.", currentUser);
            }

            // Crash recovery: finalize any open activity entries
            var tempRepo = new SessionRepository(db);
            tempRepo.FinaliseOpenActivities(currentUser, db);
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
    var scriptPath = AppPaths.PythonScriptPath;
    if (!File.Exists(scriptPath))
    {
        var errorMsg = $"Python script not found at: {scriptPath}";
        Log.Fatal(errorMsg);
        MessageBox.Show(errorMsg, "Script Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return;
    }

    // Pass scriptPath so the resolver can test fusionscript compatibility when DaVinci is running
    var pythonPath = PythonPathResolver.FindPythonExecutable(Log.Logger, scriptPath);
    if (pythonPath == null)
    {
        const string troubleshooterHint = "\n\nOpen the Troubleshooter in the dashboard (http://localhost:5555 → 🔧 Troubleshooter tab) for guided diagnosis and fix options.";
        var errorMsg = "Python not found. Need Python 3.10–3.12, 64-bit.\n\nOptions:\n• Install Python 3.12: winget install Python.Python.3.12\n• Set DAVINCI_TRACKER_PYTHON env var to an existing python.exe" + troubleshooterHint;
        Log.Fatal("Python not found — tracking cannot start");
        MessageBox.Show(errorMsg, "Python Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return;
    }

    if (!PythonPathResolver.ValidatePythonInstallation(pythonPath, Log.Logger))
    {
        var errorMsg = $"Python at '{pythonPath}' failed basic validation (python --version failed).\n\nTry setting DAVINCI_TRACKER_PYTHON to a different interpreter.\n\nOpen http://localhost:5555 → 🔧 Troubleshooter for details.";
        Log.Fatal("Python installation invalid: {Path}", pythonPath);
        MessageBox.Show(errorMsg, "Invalid Python Installation", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return;
    }

    Log.Information("Using Python: {PythonPath}", pythonPath);
    Log.Information("Using script: {ScriptPath}", scriptPath);

    var resolveApiClient = new ResolveApiClient(pythonPath, scriptPath, Log.Logger);
    var resolveMonitor   = new DaVinciResolveMonitor(resolveApiClient, trackingContext, systemActivity, Log.Logger);
    var activityMonitor  = new ActivityMonitor(trackingContext, systemActivity, Log.Logger, checkIntervalMs: 5000,
        inactivityThresholdMinutes: userSettingsService.Current.InactivityThresholdMinutes);
    // sessionManager was already created before the web server — reuse it
    var timeTrackingService = new TimeTrackingService(resolveMonitor, activityMonitor, sessionManager, trackingContext, Log.Logger);

    // Activity tracking
    var activityTracker = new ActivityTracker(resolveMonitor, trackingContext, sessionManager, Log.Logger);
    activityTracker.ActivityEnded += async entry =>
    {
        try
        {
            using var dbCtx = new TimeTrackerDbContext(
                new DbContextOptionsBuilder<TimeTrackerDbContext>()
                    .UseSqlite(AppPaths.DatabaseConnectionString)
                    .Options);
            var repo = new SessionRepository(dbCtx);
            await repo.SaveActivityAsync(entry);
            Log.Debug("Activity segment saved: {ActivityType} kind={Kind} ({Duration:mm\\:ss})",
                entry.ActivityType, entry.Kind, (entry.EndTime ?? DateTime.UtcNow) - entry.StartTime);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save activity entry");
        }
    };

    // Now that Python is resolved, wire the correct executable into NodeToggleService
    nodeToggleService.SetPythonExecutable(pythonPath, AppPaths.NodeToggleScriptPath);

    // Diagnostics service — needs the resolved Python and the live NodeToggleApiClient
    var diagnosticsService = new ResolveDiagnosticsService(
        ResolvePlatformFactory.Create(),
        systemActivity,
        nodeToggleService.GetApiClient(),
        pythonPath,
        Log.Logger);
    AppState.DiagnosticsService = diagnosticsService;
    var hotkeyManager = HotkeyManagerFactory.Create(Log.Logger);
    hotkeyManager.Reload(nodeToggleService.GetAll());
    nodeToggleService.ConfigChanged += groups => hotkeyManager.Reload(groups);
    hotkeyManager.HotkeyTriggered += groupId =>
    {
        _ = Task.Run(async () =>
        {
            var (success, enabled) = await nodeToggleService.ExecuteByIdAsync(groupId);
            if (!success)
                Log.Warning("NodeToggle: hotkey for group {Id} failed", groupId);
        });
    };

    AppState.TimeTrackingService = timeTrackingService;
    // AppState.SessionManager and AppState.NodeToggleService were set before the web server started
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

    sessionManager.SessionEnded   += async (sender, session) => await saveSession(session);

    // Upsert the Projects row whenever a new session starts (creates the row if first time)
    sessionManager.SessionStarted += async (sender, session) =>
    {
        try
        {
            using var dbCtx = new TimeTrackerDbContext(
                new DbContextOptionsBuilder<TimeTrackerDbContext>()
                    .UseSqlite(AppPaths.DatabaseConnectionString)
                    .Options);
            await new ProjectRepository(dbCtx).UpsertAsync(session.ProjectName);
        }
        catch (Exception ex) { Log.Error(ex, "Failed to upsert project row for {ProjectName}", session.ProjectName); }
    };

    // Periodic save every 30 seconds for actively tracking sessions (NOT GraceStart)
    var periodicSaveTimer = new System.Timers.Timer(30000);
    periodicSaveTimer.Elapsed += async (sender, e) =>
    {
        var currentState = sessionManager.CurrentState;

        // Only save sessions that are actually tracking (Tracking or GraceEnd)
        // Don't save GraceStart placeholders - they're not real sessions yet
        if (currentState == TrackingState.Tracking || currentState == TrackingState.GraceEnd)
        {
            var currentSession = sessionManager.GetCurrentSession();
            if (currentSession != null && currentSession.StartTime != DateTime.MinValue)
            {
                currentSession.FlushedEnd = DateTime.UtcNow;
                await saveSession(currentSession);
                Log.Debug("Periodic save: {ProjectName} in state {State}",
                    currentSession.ProjectName, currentState);
            }

            // Flush the open activity entry for crash recovery
            activityTracker.FlushCurrentEntry();
            var currentEntry = activityTracker.CurrentEntry;
            if (currentEntry != null)
            {
                try
                {
                    using var dbCtx = new TimeTrackerDbContext(
                        new DbContextOptionsBuilder<TimeTrackerDbContext>()
                            .UseSqlite(AppPaths.DatabaseConnectionString)
                            .Options);
                    await new SessionRepository(dbCtx).SaveActivityAsync(currentEntry);
                }
                catch (Exception ex) { Log.Error(ex, "Failed to flush activity entry"); }
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
    activityTracker.Dispose();
    hotkeyManager.Dispose();
    nodeToggleService.Dispose();
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
    public static TimeTrackingService      TimeTrackingService  { get; set; } = null!;
    public static SessionManager           SessionManager       { get; set; } = null!;
    public static SessionRepository        SessionRepository    { get; set; } = null!;
    public static NodeToggleService        NodeToggleService    { get; set; } = null!;
    public static TrackingContext          TrackingContext       { get; set; } = null!;
    public static UserSettingsService      UserSettingsService  { get; set; } = null!;
    public static ResolveDiagnosticsService DiagnosticsService  { get; set; } = null!;
}
