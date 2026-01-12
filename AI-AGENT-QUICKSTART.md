# AI Agent Quickstart

**For AI Agents/Assistants working on this project**

This is a quick reference guide to get you oriented quickly without reading 75KB of documentation.

## 30-Second Overview

**What**: Windows system tray app that automatically tracks time spent on DaVinci Resolve projects  
**How**: State machine with grace periods + DaVinci API polling + Windows activity monitoring  
**Tech**: .NET 9, WinForms, SQLite, EF Core, Python (for DaVinci API)

## Critical Files to Understand (Read These First)

| Priority | File | Purpose |
|----------|------|---------|
| **1** | `ARCHITECTURE.md` → State Machine section | **THE HEART** - Understand this before changing anything |
| **2** | `SessionManager.cs` | State machine implementation |
| **3** | `TimeTrackingService.cs` | Event router connecting monitors to state machine |
| **4** | `README.md` | User-facing features and quick reference |
| **5** | `DEVELOPMENT.md` | Build, debug, project structure |

## The State Machine (CRITICAL)

```
NotTracking → GraceStart (3min) → Tracking → GraceEnd (10min) → NotTracking
                 ↓ (defocus)        ↓ (defocus)     ↓ (resume)
              NotTracking         GraceEnd         Tracking
```

**Key Insight**: Two grace periods with different behaviors:
- **GraceStart**: "Not really working yet" - defocus = immediate end
- **GraceEnd**: "Already working" - defocus = 10-minute grace, keep tracking

**Why**: Prevents false tracking (accidental opens) while allowing short breaks (coffee, bathroom).

## Project Structure (5-Minute Version)

```
src/
├── App/                   # Windows Forms, system tray, web server host
├── Core/
│   ├── Services/
│   │   ├── SessionManager.cs          ⭐ STATE MACHINE
│   │   ├── TimeTrackingService.cs     Event router
│   │   └── StatisticsService.cs       Calculate totals
│   ├── Monitors/
│   │   ├── DaVinciResolveMonitor.cs   Polls DaVinci API
│   │   └── ActivityMonitor.cs         Windows idle detection
│   ├── Configuration/
│   │   └── TrackingConfiguration.cs   Grace period durations
│   └── Utilities/
│       ├── AppPaths.cs                Centralized paths
│       └── PythonPathResolver.cs      Auto-find Python
├── Data/                  # EF Core, SQLite, repositories
└── Web/                   # Dashboard (HTML/CSS/JS)
```

## Common Tasks

### Build & Run
```powershell
# Debug (3s/5s grace - fast testing)
dotnet run --project src\DaVinciTimeTracker.App\DaVinciTimeTracker.App.csproj -c Debug

# Release (3min/10min grace - production)
.\build-release.ps1
```

### Debug State Machine
Set breakpoints in `SessionManager.cs`:
- `CheckStateTransitions()` - Timer-driven transitions
- `HandleProjectChanged()` - Project events
- `StartSession()` / `EndCurrentSession()` - Session lifecycle

Watch variables:
- `_state` - Current state
- `_sessionStartTime` - GraceStart timestamp
- `_graceEndStartTime` - GraceEnd timestamp

### View Logs
```powershell
Get-Content "$env:LOCALAPPDATA\DaVinciTimeTracker\logs\*.log" -Tail 50 -Wait
```

## Coding Rules (Must Follow)

From `.cursor/rules/coding.mdc` and `.cursor/rules/csharp.mdc`:

1. **Never Nest** - Use early returns
2. **Explicit Names** - `hestonCalibrationSettings` not `calibSettings`
3. **Condition Variables** - Extract complex conditions:
   ```csharp
   var isSessionValid = isDaVinciInFocus && isUserActive && currentProject != null;
   if (isSessionValid) { ... }
   ```
4. **IDisposable** - Always dispose timers, processes, COM objects
5. **Structured Logging** - Use Serilog with `{PropertyName}` not `$"string"`

## Data Model

**One table**: `ProjectSessions`

| Column | Purpose |
|--------|---------|
| `StartTime` | Session start (UTC) |
| `EndTime` | Session end (UTC), NULL if active |
| `FlushedEnd` | Last save timestamp (crash recovery) |

**No stored totals** - calculated on-the-fly from timestamps.

## Event Flow

```
DaVinci/Windows → Monitors (polling) → Events → TimeTrackingService (router) 
→ SessionManager (state machine) → Database (periodic saves) → Dashboard (queries)
```

## Common Pitfalls

1. **Don't bypass state machine** - All tracking logic goes through `SessionManager`
2. **Don't store totals in DB** - Calculate from timestamps
3. **Don't forget crash recovery** - `FlushedEnd` must be updated periodically
4. **Don't ignore grace periods** - They're critical for accurate tracking
5. **Don't use `ben.ps1`** - This project uses `dotnet` CLI directly

## File Locations

- **App install**: `%LOCALAPPDATA%\DaVinciTimeTracker\`
- **Database**: `%LOCALAPPDATA%\DaVinciTimeTracker\davinci-timetracker.db`
- **Logs**: `%LOCALAPPDATA%\DaVinciTimeTracker\logs\`
- **Python script**: Copied to app directory as `resolve_api.py`

## When to Read Full Docs

- **Changing state machine** → Read ARCHITECTURE.md state machine section thoroughly
- **Adding features** → Read DEVELOPMENT.md for patterns
- **Debugging crashes** → Read ARCHITECTURE.md crash recovery section
- **Deployment issues** → Read DEPLOYMENT.md
- **Build/release** → Read DELIVERY-PIPELINE-SUMMARY.md

## Key Design Decisions (Context)

1. **Why state machine?** - Captures complex behavior (grace periods, transitions) predictably
2. **Why %LOCALAPPDATA%?** - No admin rights, per-user data, survives updates
3. **Why Python?** - DaVinci API is Python-based, no .NET bindings
4. **Why SQLite?** - Zero config, single file, fast enough, works offline
5. **Why 3min/10min grace?** - Balance accuracy vs. usability (tested with users)

## Testing Checklist

- [ ] Open project → Should enter GraceStart
- [ ] Wait 3 min → Should enter Tracking (Debug: 3s)
- [ ] Defocus immediately after open → Should end (no tracking)
- [ ] Defocus while Tracking → Should enter GraceEnd
- [ ] Resume within grace → Should return to Tracking
- [ ] Wait past grace → Should end session
- [ ] Change projects → Should end old, start new
- [ ] Kill process → Restart → Orphaned sessions should close

## Emergency Fixes

### Python script missing
Check `.csproj` has:
```xml
<Content Include="..\DaVinciTimeTracker.Core\Resolve\resolve_api.py">
    <Link>resolve_api.py</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

### State machine stuck
Check logs for state transitions. Most likely:
- Timer not running
- Conditions in `CheckStateTransitions()` not met
- Event not being fired

### Database locked
Only one app instance should run. Kill duplicates:
```powershell
Get-Process -Name "*DaVinci*" | Stop-Process
```

## Documentation Structure

```
README.md                          ← Start here (user/dev overview)
  ├─→ ARCHITECTURE.md              ← Read this for technical design
  ├─→ DEVELOPMENT.md               ← Read this for coding/building
  ├─→ DEPLOYMENT.md                ← Read this for installation
  └─→ DELIVERY-PIPELINE-SUMMARY.md ← Read this for releases
```

## Next Steps

1. **Read** `ARCHITECTURE.md` state machine section (15 minutes)
2. **Read** `SessionManager.cs` implementation (10 minutes)
3. **Run** app in Debug mode and watch logs
4. **Test** a few scenarios from checklist above
5. **Now you're ready** to make changes!

## Questions to Ask Yourself Before Changing Code

- Does this affect the state machine? → Read ARCHITECTURE.md
- Do I need to add a new state? → Update state diagram, docs
- Am I changing grace periods? → Update TrackingConfiguration.cs
- Am I adding a monitor? → Follow IMonitor pattern, wire to TimeTrackingService
- Am I changing database schema? → Create EF Core migration
- Am I modifying paths? → Update AppPaths.cs
- Does this need testing? → Add manual test scenarios

## Contact

For questions or issues, contact: [Maintainer Info]

---

**Remember**: The state machine is the core. Understand it. Respect it. Don't bypass it.
