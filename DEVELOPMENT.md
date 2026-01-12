# Development Guide

Complete development guide for working on DaVinci Time Tracker.

## Table of Contents

- [Getting Started](#getting-started)
- [Project Structure](#project-structure)
- [Building](#building)
- [Debugging](#debugging)
- [Code Organization Rules](#code-organization-rules)
- [Common Development Tasks](#common-development-tasks)
- [Testing](#testing)
- [Troubleshooting Dev Issues](#troubleshooting-dev-issues)

## Getting Started

### Prerequisites

1. **.NET 9 SDK** - https://dotnet.microsoft.com/download/dotnet/9.0
2. **Python 3.8+** - https://www.python.org/downloads/
3. **DaVinci Resolve Studio** - For testing (optional for dev work)
4. **PowerShell 7+** - For build scripts
5. **IDE**: Visual Studio 2022, Rider, or VS Code with C# extension

### Clone and Setup

```powershell
# Clone (if not already)
cd c:\code\everix3\code\utils\davinci-time-tracker

# Restore dependencies
dotnet restore

# Verify build
dotnet build
```

### First Run

```powershell
# Build and run in Debug mode
dotnet run --project src\DaVinciTimeTracker.App\DaVinciTimeTracker.App.csproj

# Or use Visual Studio/Rider:
# Open src/DaVinciTimeTracker.App/DaVinciTimeTracker.App.csproj
# Press F5 to debug
```

**Important**: Debug builds use 3-second/5-second grace periods for faster testing.

## Project Structure

```
davinci-time-tracker/
│
├── src/
│   ├── DaVinciTimeTracker.App/          # Main Windows Forms app
│   │   ├── Program.cs                    # Entry point, DI setup, web server
│   │   ├── TrayApplicationContext.cs     # System tray management
│   │   └── AutoStartManager.cs           # Windows startup integration
│   │
│   ├── DaVinciTimeTracker.Core/         # Business logic
│   │   ├── Configuration/
│   │   │   └── TrackingConfiguration.cs  # Grace period configuration
│   │   ├── Models/
│   │   │   ├── ProjectSession.cs         # Session entity
│   │   │   ├── TrackingState.cs          # State machine states
│   │   │   └── ProjectStatistics.cs      # Statistics DTO
│   │   ├── Monitors/
│   │   │   ├── DaVinciResolveMonitor.cs  # DaVinci project/focus monitoring
│   │   │   └── ActivityMonitor.cs        # User activity monitoring
│   │   ├── Services/
│   │   │   ├── SessionManager.cs         # ⭐ STATE MACHINE (CRITICAL)
│   │   │   ├── TimeTrackingService.cs    # Event router/orchestrator
│   │   │   └── StatisticsService.cs      # Statistics calculations
│   │   ├── Resolve/
│   │   │   ├── ResolveApiClient.cs       # Python subprocess wrapper
│   │   │   └── resolve_api.py            # Python script for DaVinci API
│   │   ├── Native/
│   │   │   └── WindowsApi.cs             # P/Invoke declarations
│   │   └── Utilities/
│   │       ├── AppPaths.cs               # Centralized path management
│   │       └── PythonPathResolver.cs     # Auto-detect Python
│   │
│   ├── DaVinciTimeTracker.Data/         # Database layer
│   │   ├── TimeTrackerDbContext.cs       # EF Core DbContext
│   │   ├── Repositories/
│   │   │   └── SessionRepository.cs      # Session CRUD operations
│   │   └── Migrations/                   # EF Core migrations
│   │
│   └── DaVinciTimeTracker.Web/          # Dashboard
│       ├── Controllers/
│       │   └── ApiController.cs          # REST API endpoints
│       ├── Program.cs                    # (Minimal - not used standalone)
│       └── wwwroot/
│           ├── index.html                # Dashboard UI
│           ├── css/styles.css            # Styles
│           └── js/dashboard.js           # Frontend JavaScript
│
├── .cursor/rules/                       # Cursor AI coding rules
├── build-release.ps1                    # Build & deployment script
├── quick-install.ps1                    # End-user installer
├── quick-install.bat                    # Batch wrapper for installer
├── Directory.Build.props                # MSBuild isolation (important!)
├── README.md                            # Main documentation
├── ARCHITECTURE.md                      # Technical architecture
├── DEVELOPMENT.md                       # This file
├── DEPLOYMENT.md                        # Deployment guide
└── DELIVERY-PIPELINE-SUMMARY.md         # Build/delivery process
```

### Key Files to Understand

| File                       | Purpose             | When to Modify                |
| -------------------------- | ------------------- | ----------------------------- |
| `SessionManager.cs`        | State machine logic | Changing tracking behavior    |
| `TimeTrackingService.cs`   | Event routing       | Adding new monitors/events    |
| `DaVinciResolveMonitor.cs` | DaVinci detection   | DaVinci API changes           |
| `Program.cs` (App)         | Application setup   | Changing configuration, ports |
| `TrackingConfiguration.cs` | Grace periods       | Adjusting timing behavior     |
| `AppPaths.cs`              | File locations      | Changing where data is stored |

## Building

### Using dotnet CLI (Recommended for dev)

```powershell
# Build Debug (3s/5s grace periods)
dotnet build -c Debug

# Build Release (3min/10min grace periods)
dotnet build -c Release

# Run without building
dotnet run --project src\DaVinciTimeTracker.App\DaVinciTimeTracker.App.csproj -c Debug

# Clean build
dotnet clean && dotnet build
```

### Using build-release.ps1 (For creating releases)

```powershell
# Build and deploy to network share
.\build-release.ps1

# Build without deploying
.\build-release.ps1 -NetworkShare ""

# Skip build (repackage only)
.\build-release.ps1 -SkipBuild
```

**What build-release.ps1 does**:

1. Cleans previous build
2. Builds in Release mode
3. Creates ZIP package with version (`yyyyMMdd-HHmmss`)
4. Copies to network share (`F:\Linia\DaVinciTracker`)
5. Copies installer scripts
6. Generates `version.json` and `INSTALL.txt`

### Build System Notes

**Important**: This project uses `Directory.Build.props` to isolate from the parent Everix build system. DO NOT delete this file.

```xml
<Project>
  <!-- Prevents parent Directory.Build.props from being imported -->
  <PropertyGroup>
    <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
  </PropertyGroup>
</Project>
```

**Do NOT use `ben.ps1`** (Everix build system) for this project. Use `dotnet` CLI directly.

## Debugging

### Visual Studio / Rider

1. Open `src/DaVinciTimeTracker.App/DaVinciTimeTracker.App.csproj`
2. Set breakpoints
3. Press F5 (Start Debugging) or Ctrl+F5 (Start Without Debugging)

**Tip**: Debug builds use 3s/5s grace periods for faster testing.

### Debugging State Machine

**Watch these variables** in `SessionManager.cs`:

```csharp
_state                 // Current state
_sessionStartTime      // When GraceStart began
_graceEndStartTime     // When GraceEnd began
_currentSession        // Current session (or null)
```

**Useful breakpoints**:

- `SessionManager.CheckStateTransitions()` - State transition logic
- `SessionManager.HandleProjectChanged()` - Project change events
- `SessionManager.StartSession()` - Session creation
- `SessionManager.EndCurrentSession()` - Session completion

### Debugging DaVinci API

**Python script issues**:

```powershell
# Test Python script manually
cd src\DaVinciTimeTracker.Core\Resolve
python resolve_api.py

# Should output: None (if no project open) or project name
```

**Check Python path resolution**:

```csharp
// Add breakpoint in ResolveApiClient.cs constructor
var pythonPath = PythonPathResolver.FindPythonExecutable(logger);
// Inspect pythonPath
```

### Debugging Database

```powershell
# Install sqlite3 command-line tool
# Or use https://sqlitebrowser.org/

# Query database
sqlite3 "$env:LOCALAPPDATA\DaVinciTimeTracker\davinci-timetracker.db"

# Useful queries:
sqlite> SELECT * FROM ProjectSessions ORDER BY StartTime DESC LIMIT 10;
sqlite> SELECT * FROM ProjectSessions WHERE EndTime IS NULL;
sqlite> SELECT ProjectName, COUNT(*) FROM ProjectSessions GROUP BY ProjectName;
```

### Log Debugging

**View logs live**:

```powershell
Get-Content "$env:LOCALAPPDATA\DaVinciTimeTracker\logs\davinci-tracker-*.log" -Tail 50 -Wait
```

**Filter logs**:

```powershell
# State transitions
Get-Content "$env:LOCALAPPDATA\DaVinciTimeTracker\logs\*.log" | Select-String "State|Transition"

# Errors
Get-Content "$env:LOCALAPPDATA\DaVinciTimeTracker\logs\*.log" | Select-String "Error|Exception"

# Grace periods
Get-Content "$env:LOCALAPPDATA\DaVinciTimeTracker\logs\*.log" | Select-String "Grace"
```

**Increase log verbosity**:

Edit `Program.cs`:

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()  // Change from Information to Debug
    .WriteTo.File(AppPaths.LogFilePath, rollingInterval: RollingInterval.Day)
    .CreateLogger();
```

## Code Organization Rules

### Coding Standards

Follow the rules in `.cursor/rules/` folder:

1. **`.cursor/rules/coding.mdc`** - General C# practices
2. **`.cursor/rules/csharp.mdc`** - C# specific conventions
3. **`.cursor/rules/powershell.mdc`** - PowerShell script standards

### Key Conventions

#### Variable Naming

```csharp
// ✅ GOOD: Explicit, descriptive names
var hestonCalibrationSettings = GetSettings();
var isTradeExpired = trade.FixingDate > asOfDate || trade.IsMatured;

// ❌ BAD: Abbreviated or unclear
var calibSettings = GetSettings();
var expired = trade.FixingDate > asOfDate || trade.IsMatured;
```

#### Method Calls

```csharp
// ✅ GOOD: Extract complex subcalls
var pythonPath = PythonPathResolver.FindPythonExecutable(_logger);
var process = CreateProcess(pythonPath, scriptPath);
var result = await ExecuteAsync(process);

// ❌ BAD: Nested calls
var result = await ExecuteAsync(CreateProcess(
    PythonPathResolver.FindPythonExecutable(_logger), scriptPath));
```

#### Conditionals

```csharp
// ✅ GOOD: Single clause directly in if
if (singleCondition) { ... }

// ✅ GOOD: Multiple clauses in explicit variable
var isSessionValid = isDaVinciInFocus && isUserActive && currentProject != null;
if (isSessionValid) { ... }

// ❌ BAD: Multiple clauses directly
if (isDaVinciInFocus && isUserActive && currentProject != null) { ... }
```

#### Never Nest (Early Returns)

```csharp
// ✅ GOOD: Early returns
if (!weNeedIt) return false;
if (!allIsRight) throw new Exception();
WeDoUsefulStuff();

// ❌ BAD: Nested if/else
if (weNeedIt) {
    if (allIsRight) {
        WeDoUsefulStuff();
    } else {
        throw new Exception();
    }
}
```

### Dependency Injection

All services use constructor injection:

```csharp
public class SessionManager
{
    private readonly ILogger _logger;

    public SessionManager(ILogger logger)
    {
        _logger = logger;
    }
}
```

**Register in `Program.cs`**:

```csharp
builder.Services.AddSingleton<SessionManager>();
```

### IDisposable Pattern

For classes with unmanaged resources (timers, file handles):

```csharp
public class MyMonitor : IDisposable
{
    private Timer _timer;
    private bool _disposed;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _timer?.Stop();
            _timer?.Dispose();
        }
        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    ~MyMonitor()
    {
        Dispose(disposing: false);
    }
}
```

## Common Development Tasks

### Adding a New Monitor

1. **Create monitor class** in `src/DaVinciTimeTracker.Core/Monitors/`
2. **Implement `IMonitor` interface** (if exists) or follow existing pattern
3. **Define events** for state changes
4. **Implement `IDisposable`** for cleanup
5. **Register in DI** (`Program.cs`)
6. **Wire up events** in `TimeTrackingService.cs`

Example:

```csharp
public class NewMonitor : IMonitor, IDisposable
{
    public event EventHandler<string>? SomethingChanged;

    private readonly Timer _timer;

    public NewMonitor(ILogger logger)
    {
        _timer = new Timer(1000);
        _timer.Elapsed += OnTimerElapsed;
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        // Detect changes
        SomethingChanged?.Invoke(this, "value");
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
```

### Adding a New State

1. **Update `TrackingState` enum** in `src/DaVinciTimeTracker.Core/Models/TrackingState.cs`
2. **Add state field** in `SessionManager` if needed
3. **Implement transition logic** in `SessionManager.CheckStateTransitions()`
4. **Add event handlers** for entering/exiting the state
5. **Update logs** to include new state
6. **Update `ARCHITECTURE.md`** with state diagram

### Changing Grace Periods

**For development/testing**:

```csharp
// src/DaVinciTimeTracker.Core/Configuration/TrackingConfiguration.cs
#if DEBUG
    public static readonly TimeSpan GraceStartDuration = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan GraceEndDuration = TimeSpan.FromSeconds(10);
#else
    // Production values unchanged
#endif
```

**For production**:

```csharp
#else
    public static readonly TimeSpan GraceStartDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan GraceEndDuration = TimeSpan.FromMinutes(15);
#endif
```

### Adding a New API Endpoint

1. **Add method** in `src/DaVinciTimeTracker.Web/Controllers/ApiController.cs`
2. **Follow RESTful conventions**
3. **Use async/await**
4. **Return proper HTTP status codes**
5. **Update `dashboard.js`** to call the endpoint

Example:

```csharp
[HttpGet("sessions/{projectName}")]
public async Task<IActionResult> GetProjectSessions(string projectName)
{
    try
    {
        var sessions = await _repository.GetSessionsByProjectAsync(projectName);
        return Ok(sessions);
    }
    catch (Exception ex)
    {
        _logger.Error(ex, "Failed to get sessions for {Project}", projectName);
        return StatusCode(500, new { error = ex.Message });
    }
}
```

### Adding Database Migrations

```powershell
# Navigate to Data project
cd src\DaVinciTimeTracker.Data

# Add migration
dotnet ef migrations add MigrationName --startup-project ../DaVinciTimeTracker.App

# Apply migration (or it happens automatically on app start)
dotnet ef database update --startup-project ../DaVinciTimeTracker.App
```

**Important**: Migrations run automatically on app startup in `Program.cs`:

```csharp
using (var db = scope.ServiceProvider.GetRequiredService<TimeTrackerDbContext>())
{
    db.Database.Migrate();
}
```

### Modifying the Dashboard

1. **HTML**: Edit `src/DaVinciTimeTracker.Web/wwwroot/index.html`
2. **CSS**: Edit `src/DaVinciTimeTracker.Web/wwwroot/css/styles.css`
3. **JavaScript**: Edit `src/DaVinciTimeTracker.Web/wwwroot/js/dashboard.js`

**Note**: Changes require rebuild because files are copied to App output via `.csproj`:

```xml
<Content Include="../DaVinciTimeTracker.Web/wwwroot/**/*.*">
    <Link>wwwroot/%(RecursiveDir)%(FileName)%(Extension)</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

**Rebuild after changes**:

```powershell
dotnet build src\DaVinciTimeTracker.App\DaVinciTimeTracker.App.csproj
```

## Testing

### Manual Testing Checklist

#### Happy Path

- [ ] Open DaVinci project
- [ ] Wait for GraceStart (3s in Debug)
- [ ] Verify transition to Tracking
- [ ] Work for a while
- [ ] Close DaVinci
- [ ] Verify GraceEnd (5s in Debug)
- [ ] Verify session ends after grace period
- [ ] Check dashboard shows correct time

#### Edge Cases

- [ ] Open project, immediately defocus → Should end immediately (no tracking)
- [ ] Work, defocus, refocus within grace → Should resume tracking
- [ ] Work, defocus, wait past grace → Should end session
- [ ] Change projects → Should end old, start new
- [ ] Idle for 1 minute → Should enter GraceEnd
- [ ] Resume activity within grace → Should resume tracking

#### Crash Recovery

- [ ] Start tracking
- [ ] Kill process (Task Manager)
- [ ] Restart app
- [ ] Check logs for "Found X open session(s)" message
- [ ] Verify orphaned sessions are closed with `FlushedEnd` timestamp

#### UI Testing

- [ ] Dashboard loads at `http://localhost:5555`
- [ ] Projects display with correct times
- [ ] "Currently Tracking" badge appears
- [ ] Delete project works with confirmation
- [ ] Last Activity shows correct time (not timezone offset)

### Automated Testing (Future)

**Not currently implemented**, but recommended:

```csharp
// Example test structure
[TestFixture]
public class SessionManagerTests
{
    [Test]
    public void GraceStart_ImmediateDefocus_EndsSession()
    {
        // Arrange
        var manager = new SessionManager(logger);

        // Act
        manager.HandleProjectChanged("TestProject");
        manager.HandleFocusLost();

        // Assert
        Assert.AreEqual(TrackingState.NotTracking, manager.CurrentState);
    }
}
```

## Troubleshooting Dev Issues

### Build Failures

#### "The type or namespace name 'X' could not be found"

```powershell
# Solution: Restore NuGet packages
dotnet restore
dotnet clean
dotnet build
```

#### "Directory.Build.props from parent interfering"

**Symptom**: Build errors about missing Everix dependencies

**Solution**: Ensure `Directory.Build.props` exists at project root with:

```xml
<ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
```

### Runtime Errors

#### "Python script not found"

**Symptom**: `FileNotFoundException` for `resolve_api.py`

**Solution**: Check `.csproj` has proper `<Content>` entry:

```xml
<Content Include="..\DaVinciTimeTracker.Core\Resolve\resolve_api.py">
    <Link>resolve_api.py</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

#### "Port 5555 already in use"

**Symptom**: `IOException` when starting web server

**Solutions**:

1. Kill process using port: `Get-Process -Name *DaVinci* | Stop-Process`
2. Or change port in `Program.cs`

#### "Database is locked"

**Symptom**: `SqliteException: database is locked`

**Solutions**:

1. Ensure only one instance is running
2. Delete `.db-shm` and `.db-wal` files (after closing app)
3. Check if another process has the DB open

### Dashboard Issues

#### "404 Not Found" for dashboard

**Symptom**: `http://localhost:5555` returns 404

**Solutions**:

1. Ensure `wwwroot` files are in build output
2. Check `Program.cs` has correct `UseStaticFiles` configuration
3. Rebuild: `dotnet build -c Debug`

#### JavaScript console errors

**Symptom**: Dashboard loads but doesn't update

**Solutions**:

1. Check browser console (F12)
2. Verify API endpoint is working: `http://localhost:5555/api/statistics`
3. Check CORS configuration in `Program.cs`

## Development Workflow

### Typical Development Session

1. **Start IDE** (Visual Studio/Rider/VS Code)
2. **Open project** (`src/DaVinciTimeTracker.App/DaVinciTimeTracker.App.csproj`)
3. **Make changes**
4. **Build** (Ctrl+Shift+B or `dotnet build`)
5. **Debug** (F5) - App starts in system tray
6. **Test** - Open DaVinci, verify tracking
7. **Check logs** - View latest log file
8. **Stop** (Shift+F5 or right-click tray → Exit)
9. **Commit** - Follow git workflow

### Before Committing

- [ ] Code builds without warnings
- [ ] Follows coding standards (`.cursor/rules/`)
- [ ] Tested manually (at least happy path)
- [ ] Logs are informative (not too verbose, not too sparse)
- [ ] No hardcoded paths or secrets
- [ ] Updated documentation if needed (README, ARCHITECTURE)

### Creating a Release

```powershell
# 1. Test thoroughly in Debug mode
dotnet run --project src\DaVinciTimeTracker.App\DaVinciTimeTracker.App.csproj -c Debug

# 2. Build and deploy release
.\build-release.ps1

# 3. Test installation
.\quick-install.ps1

# 4. Verify tracking works
# Open DaVinci, work, check dashboard

# 5. Notify colleagues
# Share: F:\Linia\DaVinciTracker\quick-install.bat
```

## IDE-Specific Tips

### Visual Studio 2022

- **Solution**: Open `src/DaVinciTimeTracker.App/DaVinciTimeTracker.App.csproj`
- **Run/Debug**: Press F5
- **Build Output**: View → Output → Show output from: Build
- **NuGet**: Tools → NuGet Package Manager → Manage NuGet Packages for Solution

### JetBrains Rider

- **Solution**: Open `src/DaVinciTimeTracker.App/DaVinciTimeTracker.App.csproj`
- **Run/Debug**: Shift+F10 / Shift+F9
- **Build Output**: View → Tool Windows → Build
- **NuGet**: Tools → NuGet → Manage NuGet Packages

### VS Code

- **Open Folder**: `c:\code\everix3\code\utils\davinci-time-tracker`
- **Extensions**: Install C# extension
- **Build**: Ctrl+Shift+B (select "build" task)
- **Debug**: F5 (after configuring launch.json)

## Performance Profiling

### Memory Leaks

**Watch for**:

- Undisposed `Timer` objects
- Event handlers not unsubscribed
- Database connections not closed

**Tools**:

- Visual Studio Diagnostic Tools (Alt+F2)
- dotMemory (JetBrains)

### CPU Usage

Should be minimal (< 1% average). If high:

- Check polling intervals (too frequent?)
- Check for infinite loops in event handlers
- Profile with Visual Studio Performance Profiler

## Resources

### Documentation

- **README.md** - Overview and user guide
- **ARCHITECTURE.md** - Technical design and state machine
- **DEPLOYMENT.md** - Installation and configuration
- **DELIVERY-PIPELINE-SUMMARY.md** - Build/release process

### External References

- **.NET 9 Docs**: https://learn.microsoft.com/en-us/dotnet/
- **Entity Framework Core**: https://learn.microsoft.com/en-us/ef/core/
- **Serilog**: https://serilog.net/
- **DaVinci Resolve API**: `C:\ProgramData\Blackmagic Design\DaVinci Resolve\Support\Developer\Scripting\README.txt`
- **Windows API**: https://learn.microsoft.com/en-us/windows/win32/api/

## Getting Help

1. **Read the docs** - Start with README, then ARCHITECTURE
2. **Check logs** - Most issues show up in logs
3. **Search codebase** - Use IDE search for similar patterns
4. **Consult AI** - Provide context from ARCHITECTURE.md
5. **Ask maintainer** - [Contact Info]

## Final Notes

- **State machine is critical** - Understand it before making changes
- **Test in Debug mode** - 3s/5s grace periods save time
- **Log everything important** - Future you will thank you
- **Follow coding standards** - Consistency matters
- **Document non-obvious decisions** - Update ARCHITECTURE.md

**Happy coding!** 🚀
