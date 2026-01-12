# DaVinci Time Tracker

A Windows system tray application that automatically tracks time spent on DaVinci Resolve projects.

## 📋 Project Overview

**Purpose**: Automatically track how much time you spend working on each DaVinci Resolve Studio project without manual intervention.

**Key Insight**: Uses DaVinci Resolve's scripting API to detect the active project and Windows APIs to track user activity, implementing an intelligent state machine with grace periods to accurately capture work sessions.

## 🎯 Quick Links

- **[Architecture Guide](ARCHITECTURE.md)** - Technical design, state machine, data flow
- **[Development Guide](DEVELOPMENT.md)** - Building, debugging, project structure
- **[Deployment Guide](DEPLOYMENT.md)** - Installation, portability, configuration
- **[Delivery Pipeline](DELIVERY-PIPELINE-SUMMARY.md)** - Build and distribution process

## ✨ Key Features

### Automatic Tracking

- **Zero manual input** - Just open a DaVinci project and work
- **Project detection** - Automatically identifies which project is open
- **Smart filtering** - Only tracks when DaVinci is focused AND user is active

### Intelligent State Machine

- **3-minute grace start** - No accidental tracking if you quickly switch away
- **10-minute grace end** - Keeps tracking during short breaks (coffee, bathroom)
- **Per-project sessions** - Each project gets independent tracking
- **Crash recovery** - Handles unexpected shutdowns gracefully

### User Experience

- **System tray app** - Runs quietly in the background
- **Web dashboard** - Modern UI at `http://localhost:5555`
- **Per-project stats** - See total time, session count, last activity
- **Project management** - Delete tracking data for specific projects
- **Auto-start option** - Start automatically with Windows

## 🔧 Requirements

| Requirement         | Details                                                      |
| ------------------- | ------------------------------------------------------------ |
| **OS**              | Windows 10+                                                  |
| **DaVinci Resolve** | Studio version (Free version doesn't support scripting API)  |
| **Python**          | 3.8+ (auto-detected or via `DAVINCI_TRACKER_PYTHON` env var) |
| **.NET**            | .NET 9 Desktop Runtime                                       |
| **PowerShell**      | PowerShell 7+ (for installation scripts)                     |

## 🚀 Quick Start

### For End Users (Installation)

**Option 1: Double-click installer (easiest)**

```
1. Navigate to: F:\Linia\DaVinciTracker
2. Double-click: quick-install.bat
3. Follow prompts
```

**Option 2: PowerShell installer**

```powershell
.\quick-install.ps1
```

The app will be installed to `%LOCALAPPDATA%\DaVinciTimeTracker` (no admin rights needed).

### For Developers

See **[DEVELOPMENT.md](DEVELOPMENT.md)** for complete setup instructions.

Quick build:

```powershell
dotnet build src\DaVinciTimeTracker.App\DaVinciTimeTracker.App.csproj -c Release
```

## 📁 File Locations

### Application Files (Read-only)

```
%LOCALAPPDATA%\DaVinciTimeTracker\
├── DaVinciTimeTracker.App.exe     # Main application
├── resolve_api.py                  # Python API script
├── *.dll                           # Dependencies
└── wwwroot\                        # Web dashboard
```

### User Data (Per-user, persistent)

```
%LOCALAPPDATA%\DaVinciTimeTracker\
├── davinci-timetracker.db          # SQLite database
├── davinci-timetracker.db-shm      # SQLite shared memory
├── davinci-timetracker.db-wal      # SQLite write-ahead log
└── logs\                           # Application logs
    └── davinci-tracker-YYYYMMDD.log
```

## 🎮 Usage

1. **Start the app** - Run `DaVinciTimeTracker.App.exe` or let it auto-start
2. **Check system tray** - Look for the app icon near the clock
3. **Open DaVinci Resolve** - Open a project and start working
4. **View stats** - Right-click tray icon → "Open Dashboard" or visit `http://localhost:5555`

### State Machine Behavior

The app uses a sophisticated state machine to track time accurately:

```
Project Opened → GraceStart (3 min) → Tracking → GraceEnd (10 min) → NotTracking
                      ↓                    ↓              ↓
                 If defocused         If defocused   If resumed
                   immediately         continues      returns to
                   ends session       in GraceEnd     Tracking
```

See **[ARCHITECTURE.md](ARCHITECTURE.md#state-machine)** for detailed state machine documentation.

## 🐛 Troubleshooting

### Common Issues

#### Python Not Found

```
Error: "Python script not found" or "Python not found"

Solutions:
1. Install Python 3.8+ from https://www.python.org/downloads/
2. Ensure "Add to PATH" is checked during installation
3. Or set environment variable:
   [System.Environment]::SetEnvironmentVariable('DAVINCI_TRACKER_PYTHON', 'C:\Path\To\python.exe', 'User')
```

#### DaVinci Resolve Studio Required

```
Error: DaVinci API returns null or errors

Solution: The FREE version of DaVinci Resolve does not support the scripting API.
You MUST have DaVinci Resolve Studio.
```

#### Port 5555 Already In Use

```
Error: "Failed to start web server"

Solution: Another app is using port 5555. Change port in Program.cs line 62 or close the conflicting app.
```

### View Logs

**Via Tray Menu:**

- Right-click tray icon → "View Latest Log"
- Right-click tray icon → "Open Logs Folder"

**Via PowerShell:**

```powershell
# View latest log (live tail)
Get-Content "$env:LOCALAPPDATA\DaVinciTimeTracker\logs\davinci-tracker-*.log" -Tail 50 -Wait

# Open in Notepad
notepad (Get-ChildItem "$env:LOCALAPPDATA\DaVinciTimeTracker\logs\*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName

# Open logs folder
explorer "$env:LOCALAPPDATA\DaVinciTimeTracker\logs"
```

## ⚙️ Configuration

### Environment Variables

| Variable                 | Purpose              | Default          |
| ------------------------ | -------------------- | ---------------- |
| `DAVINCI_TRACKER_PYTHON` | Custom Python path   | Auto-detected    |
| `PROGRAMDATA`            | DaVinci API location | `C:\ProgramData` |

### Grace Periods (Developers)

Edit `src/DaVinciTimeTracker.Core/Configuration/TrackingConfiguration.cs`:

```csharp
#if DEBUG
    public static readonly TimeSpan GraceStartDuration = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan GraceEndDuration = TimeSpan.FromSeconds(5);
#else
    public static readonly TimeSpan GraceStartDuration = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan GraceEndDuration = TimeSpan.FromMinutes(10);
#endif
```

## 🏗️ Building & Deployment

### For Maintainers

**Build and deploy a new version:**

```powershell
.\build-release.ps1
```

This will:

- Auto-generate version (`yyyyMMdd-HHmmss`)
- Build in Release mode
- Create ZIP package
- Deploy to network share (`F:\Linia\DaVinciTracker`)
- Copy installer scripts

See **[DELIVERY-PIPELINE-SUMMARY.md](DELIVERY-PIPELINE-SUMMARY.md)** for complete delivery process.

### For Developers

See **[DEVELOPMENT.md](DEVELOPMENT.md)** for:

- Development environment setup
- Project structure
- Build system (dotnet vs ben.ps1)
- Debugging techniques
- Code organization rules

## 📊 Technology Stack

| Layer          | Technology                                           |
| -------------- | ---------------------------------------------------- |
| **UI**         | Windows Forms (System Tray), HTML/CSS/JS (Dashboard) |
| **Backend**    | .NET 9, ASP.NET Core Minimal API                     |
| **Database**   | SQLite + Entity Framework Core                       |
| **Monitoring** | Windows API (P/Invoke), Python (DaVinci API)         |
| **Logging**    | Serilog                                              |

## 🚫 Known Limitations

1. **DaVinci Resolve Studio only** - Free version doesn't support scripting API
2. **Windows only** - Uses Windows-specific APIs (GetLastInputInfo, GetForegroundWindow)
3. **Single instance** - Only one app instance should run per user
4. **Port 5555** - Hardcoded (can be changed in code)
5. **"Untitled Project"** - Treated as "no project" and not tracked
6. **No remote access** - Dashboard is localhost only

## 📄 License

Internal company tool - [Specify License]

## 🤝 Contributing

This is an internal tool. For modifications:

1. Read **[DEVELOPMENT.md](DEVELOPMENT.md)** - Understand the project structure
2. Read **[ARCHITECTURE.md](ARCHITECTURE.md)** - Understand the state machine
3. Follow coding practices in `.cursor/rules/coding.mdc`
4. Test thoroughly in Debug mode (3s/5s grace periods)
5. Build release: `.\build-release.ps1`

## 📞 Support

For issues:

1. **Check logs** - Via tray menu or `%LOCALAPPDATA%\DaVinciTimeTracker\logs\`
2. **Verify requirements** - DaVinci Studio, Python, .NET 9
3. **Check documentation** - [ARCHITECTURE.md](ARCHITECTURE.md), [DEVELOPMENT.md](DEVELOPMENT.md)
4. **Contact maintainer** - [Your Contact Info]

## 🔍 For AI Agents

When working with this project:

1. **Start here**: Read this README first
2. **Understand architecture**: Read [ARCHITECTURE.md](ARCHITECTURE.md) - especially the state machine
3. **Check development guide**: [DEVELOPMENT.md](DEVELOPMENT.md) for building/debugging
4. **Follow coding rules**: See `.cursor/rules/` folder for C# conventions
5. **Key files to understand**:
   - `SessionManager.cs` - State machine implementation
   - `TimeTrackingService.cs` - Event router and orchestration
   - `DaVinciResolveMonitor.cs` - Project/focus detection
   - `ActivityMonitor.cs` - User activity tracking
   - `Program.cs` - Application entry point and web server setup

**Critical: The state machine is the heart of this app. Understand it before making changes.**
