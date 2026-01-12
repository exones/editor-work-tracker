# Architecture Guide

Complete technical architecture documentation for DaVinci Time Tracker.

## Table of Contents

- [System Overview](#system-overview)
- [Component Architecture](#component-architecture)
- [State Machine (Critical)](#state-machine-critical)
- [Data Flow](#data-flow)
- [Database Schema](#database-schema)
- [Technology Stack](#technology-stack)
- [Key Design Decisions](#key-design-decisions)

## System Overview

```
┌─────────────────────────────────────────────────────────────┐
│                      USER INTERACTION                        │
│  ┌────────────┐              ┌──────────────────┐          │
│  │ System Tray│◄─────────────┤  Web Dashboard   │          │
│  │   (Forms)  │              │ (localhost:5555) │          │
│  └────────────┘              └──────────────────┘          │
└────────────┬────────────────────────┬────────────────────────┘
             │                        │
             ▼                        ▼
┌────────────────────────────────────────────────────────────┐
│                   APPLICATION CORE                          │
│  ┌──────────────────────────────────────────────────────┐ │
│  │              Time Tracking Service                    │ │
│  │          (Event Router & Orchestrator)                │ │
│  └──────────────────────────────────────────────────────┘ │
│             │                    │                          │
│             ▼                    ▼                          │
│  ┌─────────────────┐   ┌──────────────────┐              │
│  │  Session Manager│   │   Statistics     │              │
│  │  (State Machine)│   │     Service      │              │
│  └─────────────────┘   └──────────────────┘              │
│             ▲                    │                          │
└─────────────┼────────────────────┼──────────────────────────┘
              │                    │
              │                    ▼
┌─────────────┼───────────────────────────────────────────────┐
│  MONITORING │         DATA PERSISTENCE                       │
│  ┌──────────┴─────┐    ┌────────────────┐                  │
│  │ DaVinci Monitor│    │ SQLite Database│                  │
│  │  (Python API)  │    │  (EF Core ORM) │                  │
│  └────────────────┘    └────────────────┘                  │
│  ┌────────────────┐                                         │
│  │ Activity Monitor│                                         │
│  │ (Windows API)  │                                         │
│  └────────────────┘                                         │
└─────────────────────────────────────────────────────────────┘
```

## Component Architecture

### Core Components

#### 1. TimeTrackingService

**Purpose**: Main orchestrator that connects all monitoring and tracking components.

**Responsibilities**:

- Routes events from monitors to SessionManager
- Manages periodic state checks (every 1 second)
- Implements `IDisposable` for resource cleanup

**Key Events Handled**:

- `ProjectChanged` → `SessionManager.HandleProjectChanged()`
- `ProjectClosed` → `SessionManager.HandleProjectClosed()`
- `WindowFocusGained/Lost` → `SessionManager.HandleFocusGained/Lost()`
- `UserActive/Idle` → `SessionManager.HandleUserActive/Idle()`

**Location**: `src/DaVinciTimeTracker.Core/Services/TimeTrackingService.cs`

#### 2. SessionManager

**Purpose**: Implements the state machine that determines when to track time.

**This is the HEART of the application.**

**Responsibilities**:

- Manages tracking states (`NotTracking`, `GraceStart`, `Tracking`, `GraceEnd`)
- Creates and ends `ProjectSession` entities
- Implements grace period logic
- Fires events for session start/end

**Critical Fields**:

```csharp
private ProjectSession? _currentSession;
private TrackingState _state = TrackingState.NotTracking;
private DateTime? _sessionStartTime;      // When GraceStart began
private DateTime? _graceEndStartTime;     // When GraceEnd began
```

**Location**: `src/DaVinciTimeTracker.Core/Services/SessionManager.cs`

#### 3. DaVinciResolveMonitor

**Purpose**: Monitors DaVinci Resolve for project and window focus changes.

**Responsibilities**:

- Polls DaVinci Resolve API every 2 seconds
- Detects project changes
- Detects window focus changes
- Fires events (no state management)

**How it Works**:

1. Runs Python script (`resolve_api.py`) to call DaVinci API
2. Compares current state with previous state
3. Fires events only on changes

**Special Handling**:

- "Untitled Project" → treated as `null` (no project)
- Window detection → Uses `WindowsApi.GetForegroundProcessName()`

**Location**: `src/DaVinciTimeTracker.Core/Monitors/DaVinciResolveMonitor.cs`

#### 4. ActivityMonitor

**Purpose**: Monitors user activity using Windows API.

**Responsibilities**:

- Checks idle time every 1 second using `GetLastInputInfo`
- Fires `UserIdle` event after 1 minute of inactivity
- Fires `UserActive` event when activity resumes

**Windows API Used**:

- `GetLastInputInfo` - Gets last input time from keyboard/mouse

**Location**: `src/DaVinciTimeTracker.Core/Monitors/ActivityMonitor.cs`

#### 5. StatisticsService

**Purpose**: Calculates aggregated statistics from database.

**Responsibilities**:

- Queries `ProjectSession` entities
- Calculates total active time (on-the-fly from timestamps)
- Calculates total elapsed time (on-the-fly from timestamps)
- Groups by project
- Returns `ProjectStatistics` DTOs

**Important**: No stored totals - everything calculated from session timestamps.

**Location**: `src/DaVinciTimeTracker.Core/Services/StatisticsService.cs`

## State Machine (Critical)

### States

```
┌──────────────┐
│ NotTracking  │  No active session
└──────┬───────┘
       │ HandleProjectChanged()
       ▼
┌──────────────┐
│  GraceStart  │  3-minute grace period before tracking starts
└──────┬───────┘
       │ After 3 minutes (if conditions valid)
       ▼
┌──────────────┐
│   Tracking   │  Actively tracking time
└──────┬───────┘
       │ HandleFocusLost() or HandleUserIdle()
       ▼
┌──────────────┐
│   GraceEnd   │  10-minute grace period (continues tracking)
└──────┬───────┘
       │ After 10 minutes OR HandleFocusGained()/HandleUserActive()
       ▼
Either NotTracking (ended) or Tracking (resumed)
```

### State Transitions

#### Starting a Session

```
User opens DaVinci project
  ↓
DaVinciResolveMonitor detects project change
  ↓
TimeTrackingService.OnProjectChanged()
  ↓
SessionManager.HandleProjectChanged(projectName)
  ↓
SessionManager.StartSession()
  ↓
State = GraceStart
SessionStartTime = Now
Creates new ProjectSession (EndTime = null)
Fires SessionStarted event
```

#### GraceStart → Tracking

```
Every 1 second, CheckStateTransitions() is called
  ↓
If in GraceStart AND elapsed >= 3 minutes:
  ↓
  Verify conditions still valid:
    - DaVinci in focus?
    - User active?
    - Project still open?
  ↓
  If YES → TransitionToTracking()
  If NO  → EndCurrentSession()
```

**Why the condition check?** Prevents a race condition where:

1. Project opened
2. Immediately defocused
3. 3 minutes pass
4. Incorrectly transitions to Tracking

#### Tracking → GraceEnd

```
User defocuses DaVinci OR user becomes idle
  ↓
HandleFocusLost() or HandleUserIdle()
  ↓
SessionManager.ProcessEndTrigger()
  ↓
State = Tracking? → EnterGracePeriod()
  ↓
State = GraceEnd
GraceEndStartTime = Now
Time continues to accumulate
```

#### GraceEnd → Tracking (Resume)

```
User refocuses DaVinci OR user becomes active
  ↓
HandleFocusGained() or HandleUserActive()
  ↓
SessionManager.ExitGracePeriod()
  ↓
State = Tracking
GraceEndStartTime = null
```

#### GraceEnd → NotTracking (Timeout)

```
Every 1 second, CheckStateTransitions() is called
  ↓
If in GraceEnd AND elapsed >= 10 minutes:
  ↓
  EndCurrentSession()
  ↓
  ProjectSession.EndTime = Now
  Fires SessionEnded event
  State = NotTracking
```

### Special Cases

#### Immediate End from GraceStart

If defocus/idle happens during GraceStart (first 3 minutes):

```
State = GraceStart
  ↓
HandleFocusLost() or HandleUserIdle()
  ↓
ProcessEndTrigger() → Immediate EndCurrentSession()
  ↓
State = NotTracking
```

**Rationale**: If user switches away immediately, they're not actually working on this project.

#### Project Change

```
Currently tracking Project A
  ↓
User opens Project B
  ↓
HandleProjectChanged("Project B")
  ↓
EndCurrentSession() for Project A
  ↓
StartSession("Project B")
  ↓
State = GraceStart for Project B
```

**Key**: Sessions are per-project. Changing projects ends the old session and starts a new one.

#### DaVinci Close

Treated the same as focus loss:

```
DaVinci closes
  ↓
HandleProjectClosed()
  ↓
ProcessEndTrigger()
  ↓
If Tracking → GraceEnd (10-minute grace)
If GraceStart → NotTracking (immediate end)
```

## Data Flow

### Tracking Flow

```
┌─────────────┐     Every 2s      ┌──────────────────┐
│   DaVinci   │◄──────────────────┤ DaVinci Monitor  │
│   Resolve   │                   │  (Python API)    │
└─────────────┘                   └────────┬─────────┘
                                           │ Event: ProjectChanged
                                           ▼
┌─────────────┐     Every 1s      ┌──────────────────┐
│   Windows   │◄──────────────────┤ Activity Monitor │
│   (GetLastInputInfo)  │         │  (Windows API)   │
└─────────────┘                   └────────┬─────────┘
                                           │ Event: UserActive/Idle
                                           ▼
                                 ┌──────────────────────┐
                                 │ TimeTrackingService  │
                                 │   (Event Router)     │
                                 └──────────┬───────────┘
                                            │ Routes events
                                            ▼
                                 ┌──────────────────────┐
                                 │   SessionManager     │
                                 │   (State Machine)    │
                                 └──────────┬───────────┘
                                            │ SessionStarted/Ended
                                            ▼
┌─────────────────────────────────────────────────────────────┐
│                  Periodic Save (every 30s)                   │
│  - Updates ProjectSession.FlushedEnd = Now                   │
│  - Saves to database (upsert)                                │
└────────────────────────────┬────────────────────────────────┘
                             ▼
                  ┌────────────────────┐
                  │  SQLite Database   │
                  │  ProjectSessions   │
                  └────────────────────┘
```

### Dashboard Query Flow

```
Browser requests http://localhost:5555/api/statistics
  ↓
ApiController.GetStatistics()
  ↓
StatisticsService.GetStatisticsAsync()
  ↓
Queries database:
  - Groups by ProjectName
  - Calculates TotalActiveSeconds from (EndTime ?? Now) - StartTime
  - Calculates TotalElapsedSeconds the same way
  - Counts sessions
  - Gets latest session.StartTime as LastActivity
  ↓
Returns List<ProjectStatistics>
  ↓
JSON response to browser
  ↓
dashboard.js renders UI
```

## Database Schema

### ProjectSessions Table

**Entity**: `ProjectSession` (`src/DaVinciTimeTracker.Core/Models/ProjectSession.cs`)

| Column        | Type             | Nullable | Description                                   |
| ------------- | ---------------- | -------- | --------------------------------------------- |
| `Id`          | UNIQUEIDENTIFIER | NO       | Primary key (GUID)                            |
| `ProjectName` | NVARCHAR(500)    | NO       | Name of DaVinci project                       |
| `StartTime`   | DATETIME         | NO       | Session start (UTC)                           |
| `EndTime`     | DATETIME         | YES      | Session end (UTC), NULL if active             |
| `FlushedEnd`  | DATETIME         | YES      | Last periodic save timestamp (crash recovery) |

**Indexes**:

- Primary key on `Id`
- Index on `ProjectName` (for grouping queries)

**Important Fields**:

- **FlushedEnd**: Updated every 30 seconds while session is active. Used for crash recovery to close orphaned sessions on startup.
- **EndTime**: Set only when session truly ends (transitions to `NotTracking`).

**Time Calculations** (done on-the-fly):

```csharp
// Active time = elapsed time (simplified for this app)
var endTime = session.EndTime ?? DateTime.UtcNow;
var activeSeconds = (endTime - session.StartTime).TotalSeconds;
```

### Database Lifecycle

**Startup**:

1. Apply EF Core migrations
2. **Crash recovery**: Find sessions where `EndTime == null`
3. Set `EndTime = FlushedEnd ?? DateTime.UtcNow` for orphaned sessions

**Runtime**:

- Every 30 seconds: Update `FlushedEnd` for active sessions
- On session end: Set `EndTime` and save

**Shutdown**:

- `finally` block ensures final save before exit

## Technology Stack

### Backend

| Component         | Technology               | Purpose                 |
| ----------------- | ------------------------ | ----------------------- |
| **Framework**     | .NET 9                   | Core runtime            |
| **UI Framework**  | Windows Forms            | System tray application |
| **Web Framework** | ASP.NET Core Minimal API | Dashboard backend       |
| **ORM**           | Entity Framework Core 9  | Database access         |
| **Database**      | SQLite                   | Local data storage      |
| **Logging**       | Serilog                  | Structured logging      |

### Frontend

| Component   | Technology                | Purpose              |
| ----------- | ------------------------- | -------------------- |
| **UI**      | HTML5 + CSS3 + JavaScript | Dashboard interface  |
| **HTTP**    | Fetch API                 | API calls            |
| **Styling** | Custom CSS                | Modern card-based UI |

### External Integration

| Component       | Technology  | Purpose                     |
| --------------- | ----------- | --------------------------- |
| **DaVinci API** | Python 3.8+ | Resolve scripting API       |
| **Windows API** | P/Invoke    | User activity, window focus |
| **COM Interop** | Dynamic COM | Startup shortcuts           |

### Build & Deployment

| Component        | Technology    | Purpose             |
| ---------------- | ------------- | ------------------- |
| **Build**        | dotnet CLI    | Compilation         |
| **Packaging**    | PowerShell 7  | Automated builds    |
| **Distribution** | Network share | Internal deployment |

## Key Design Decisions

### 1. Why a State Machine?

**Problem**: Simple boolean flags don't capture the complexity of tracking behavior.

**Solution**: Explicit states with clear transitions make the logic predictable and testable.

**Benefits**:

- Easy to reason about: "What happens when X occurs in state Y?"
- Easy to extend: Add new states without breaking existing logic
- Easy to debug: Logs show exact state transitions

### 2. Why Grace Periods?

**GraceStart (3 minutes)**: Prevents accidental tracking if you quickly switch away.

- Example: Open project, realize it's wrong, close it → No tracking

**GraceEnd (10 minutes)**: Allows short breaks without ending the session.

- Example: Coffee break, bathroom → Session continues

**Trade-off**: Some users might want longer/shorter periods. Solution: Configurable in code (future: UI configuration).

### 3. Why Separate GraceStart from GraceEnd?

**GraceStart**: "Haven't really started yet" - no grace on exit.

**GraceEnd**: "Already working" - give grace on exit.

**Reasoning**: Different mental models for starting vs. pausing work.

### 4. Why No Stored Totals?

**Decision**: Calculate totals on-the-fly from session timestamps.

**Rationale**:

- Single source of truth (timestamps)
- No risk of totals getting out of sync
- Crash recovery updates `EndTime`, totals automatically correct

**Trade-off**: Slightly more CPU for queries, but database is small so negligible.

### 5. Why SQLite?

**Decision**: Use SQLite instead of SQL Server or cloud database.

**Rationale**:

- Zero configuration for users
- Single-file database (easy backup)
- Fast enough for this use case
- Works offline

**Limitation**: Single concurrent writer (but app is single-instance anyway).

### 6. Why %LOCALAPPDATA%?

**Decision**: Store user data in `%LOCALAPPDATA%\DaVinciTimeTracker\`.

**Rationale**:

- Windows standard for user-specific app data
- No admin rights required
- Per-user isolation
- Survives application updates

**Alternative Considered**: Store in same folder as .exe → Rejected because Program Files is read-only.

### 7. Why Python for DaVinci API?

**Decision**: Use Python subprocess to call DaVinci API.

**Rationale**:

- DaVinci's scripting API is Python-based
- No .NET bindings available
- Subprocess is simple and reliable

**Trade-off**: Requires Python on user's machine, but DaVinci Studio users typically have it.

### 8. Why Periodic State Checks?

**Decision**: Timer every 1 second to check state transitions.

**Rationale**:

- Ensures grace periods expire even if no events occur
- Centralized transition logic
- Predictable timing

**Alternative Considered**: Event-driven only → Rejected because grace period expiration needs timer anyway.

### 9. Why Upsert Instead of Insert + Update?

**Decision**: Use upsert pattern in `SessionRepository.SaveSessionAsync()`.

**Rationale**:

- Handles both new sessions and periodic updates
- Prevents duplicate sessions
- Simpler API (one method for both cases)

### 10. Why Crash Recovery with FlushedEnd?

**Problem**: If app crashes, active sessions remain with `EndTime == null`.

**Solution**: Periodic `FlushedEnd` updates + startup recovery.

**How it Works**:

1. Every 30s: `FlushedEnd = Now`
2. On startup: Find `EndTime == null` → Set `EndTime = FlushedEnd ?? Now`

**Result**: Maximum 30 seconds of "lost" time in case of crash.

## Performance Considerations

### Polling Intervals

| Monitor       | Interval   | Rationale                                          |
| ------------- | ---------- | -------------------------------------------------- |
| DaVinci       | 2 seconds  | API is slow, no need for faster polling            |
| Activity      | 1 second   | Responsive idle detection                          |
| State Check   | 1 second   | Grace period accuracy                              |
| Periodic Save | 30 seconds | Balance between write frequency and crash recovery |

### Database Performance

- **Indexes**: On `ProjectName` for grouping queries
- **WAL mode**: SQLite Write-Ahead Logging for better concurrency
- **Single connection**: EF Core manages connection pooling

### Resource Usage

- **CPU**: Negligible (mostly waiting on timers)
- **Memory**: ~50-100 MB
- **Disk**: Database grows slowly (few KB per session)
- **Network**: None (except localhost:5555)

## Error Handling

### Principles

1. **Fail gracefully**: App should continue running even if monitors fail
2. **Log everything**: Serilog captures all errors
3. **User-friendly errors**: Show actionable messages (e.g., "Python not found")
4. **Crash recovery**: Database cleanup on startup

### Error Scenarios

| Scenario          | Handling                                          |
| ----------------- | ------------------------------------------------- |
| Python not found  | Log warning, show error dialog with download link |
| DaVinci API fails | Log error, continue running (retry on next poll)  |
| Database locked   | Retry with exponential backoff                    |
| Port 5555 in use  | Fatal error, show message, exit                   |
| Crash             | Startup recovery closes orphaned sessions         |

## Testing Considerations

### Debug Mode

**Grace Periods**:

- GraceStart: 3 seconds (instead of 3 minutes)
- GraceEnd: 5 seconds (instead of 10 minutes)

**Purpose**: Fast testing without waiting minutes.

**Configuration**: `src/DaVinciTimeTracker.Core/Configuration/TrackingConfiguration.cs`

### Manual Testing Scenarios

1. **Happy path**: Open project → Wait 3 min → Work → Close
2. **Quick switch**: Open project → Immediately defocus
3. **Coffee break**: Work → Defocus 5 min → Resume
4. **Long break**: Work → Defocus 15 min → Should end
5. **Project change**: Work on A → Switch to B
6. **Crash recovery**: Kill process → Restart → Check orphaned sessions closed

## Security Considerations

### Local-Only

- **Dashboard**: `localhost:5555` only (no remote access)
- **Database**: Local SQLite file (no network exposure)
- **No authentication**: Not needed for local-only tool

### Data Privacy

- **PII**: Only project names are stored (user decides what to name projects)
- **Backup**: Users can backup/delete `%LOCALAPPDATA%\DaVinciTimeTracker\`

## Future Enhancements (Potential)

1. **Configurable grace periods** - UI for adjusting durations
2. **Multiple DaVinci instances** - Track multiple projects simultaneously
3. **Export data** - CSV/Excel export of sessions
4. **Remote dashboard** - Access dashboard from other devices (security implications)
5. **Tagging** - User-defined tags for projects
6. **Goals** - Set time goals per project
7. **Notifications** - Alert when reaching time goals
8. **Integrations** - Export to time tracking services

## Debugging Tips

### Enable Verbose Logging

Edit `Program.cs`:

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()  // Change to Debug
    .WriteTo.File(...)
    .CreateLogger();
```

### Watch State Transitions

```powershell
Get-Content "$env:LOCALAPPDATA\DaVinciTimeTracker\logs\*.log" -Tail 50 -Wait | Select-String "State|Transition|Grace"
```

### Query Database

```powershell
# Install sqlite3
# Query active sessions
sqlite3 "$env:LOCALAPPDATA\DaVinciTimeTracker\davinci-timetracker.db" "SELECT * FROM ProjectSessions WHERE EndTime IS NULL;"
```

## Conclusion

The architecture is designed for:

- **Reliability**: Crash recovery, graceful error handling
- **Accuracy**: State machine with grace periods captures real work sessions
- **Simplicity**: Clear separation of concerns, minimal dependencies
- **Maintainability**: Well-documented state transitions, comprehensive logging

**Most Important**: Understand the state machine before making changes. It's the core logic that makes this app useful.
