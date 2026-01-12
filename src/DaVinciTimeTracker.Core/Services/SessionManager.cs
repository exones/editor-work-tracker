using DaVinciTimeTracker.Core.Configuration;
using DaVinciTimeTracker.Core.Models;
using Serilog;

namespace DaVinciTimeTracker.Core.Services;

public class SessionManager
{

    private readonly ILogger _logger;
    private readonly string _currentUserName;
    private ProjectSession? _currentSession;
    private TrackingState _state = TrackingState.NotTracking;
    private DateTime? _sessionStartTime;     // When GraceStart began
    private DateTime? _graceEndStartTime;    // When GraceEnd began

    public event EventHandler<ProjectSession>? SessionStarted;
    public event EventHandler<ProjectSession>? SessionEnded;

    public TrackingState CurrentState => _state;
    public string? CurrentProjectName => _currentSession?.ProjectName;
    public string CurrentUserName => _currentUserName;
    public DateTime? SessionStartTime => _sessionStartTime;

    public TimeSpan? GraceStartElapsedTime => _sessionStartTime.HasValue && _state == TrackingState.GraceStart
        ? DateTime.UtcNow - _sessionStartTime.Value
        : null;

    public TimeSpan? GraceEndElapsedTime => _graceEndStartTime.HasValue && _state == TrackingState.GraceEnd
        ? DateTime.UtcNow - _graceEndStartTime.Value
        : null;

    public SessionManager(ILogger logger)
    {
        _logger = logger;
        _currentUserName = Environment.UserName;
        _logger.Information("SessionManager initialized for user: {UserName}", _currentUserName);
    }

    public ProjectSession? GetCurrentSession()
    {
        return _currentSession;
    }

    // ============ PUBLIC EVENT HANDLERS ============

    public void HandleProjectChanged(string projectName)
    {
        _logger.Information("HandleProjectChanged: {ProjectName}, CurrentState: {State}", projectName, _state);

        // End old session if exists
        if (_currentSession != null)
        {
            EndCurrentSession();
        }

        // Start new session in GraceStart state
        StartSession(projectName);
    }

    public void HandleProjectClosed()
    {
        _logger.Information("HandleProjectClosed, CurrentState: {State}", _state);
        ProcessEndTrigger();
    }

    public void HandleFocusLost()
    {
        _logger.Information("HandleFocusLost, CurrentState: {State}", _state);
        ProcessEndTrigger();
    }

    public void HandleFocusGained(string? projectName, bool isUserActive)
    {
        _logger.Information("HandleFocusGained, Project: {Project}, Active: {Active}, CurrentState: {State}",
            projectName, isUserActive, _state);

        switch (_state)
        {
            case TrackingState.NotTracking:
                // Start new session if conditions are met
                if (projectName != null && isUserActive)
                {
                    StartSession(projectName);
                }
                break;
            case TrackingState.GraceEnd:
                // Exit grace period, return to Tracking
                if (isUserActive)
                {
                    ExitGracePeriod();
                }
                break;
        }
    }

    public void HandleUserIdle()
    {
        _logger.Information("HandleUserIdle, CurrentState: {State}", _state);
        ProcessEndTrigger();
    }

    public void HandleUserActive()
    {
        _logger.Information("HandleUserActive, CurrentState: {State}", _state);

        switch (_state)
        {
            case TrackingState.GraceEnd:
                // Exit grace period, return to Tracking
                ExitGracePeriod();
                break;
        }
    }

    // Called periodically by TimeTrackingService timer
    public void CheckStateTransitions(bool isDaVinciInFocus, bool isUserActive, string? currentProject)
    {
        switch (_state)
        {
            case TrackingState.GraceStart:
                // Check if grace period elapsed
                if (_sessionStartTime.HasValue)
                {
                    var graceStartDuration = DateTime.UtcNow - _sessionStartTime.Value;
                    if (graceStartDuration >= TrackingConfiguration.GraceStartDuration)
                    {
                        // Verify conditions are still valid before transitioning
                        if (isDaVinciInFocus && isUserActive && currentProject != null)
                        {
                            TransitionToTracking();
                        }
                        else
                        {
                            _logger.Information("GraceStart expired but conditions not valid (Focus: {Focus}, Active: {Active}, Project: {Project}) - ending session",
                                isDaVinciInFocus, isUserActive, currentProject != null);
                            EndCurrentSession();
                        }
                    }
                }
                break;

            case TrackingState.GraceEnd:
                // Check if grace period elapsed
                if (_graceEndStartTime.HasValue)
                {
                    var graceEndDuration = DateTime.UtcNow - _graceEndStartTime.Value;
                    if (graceEndDuration >= TrackingConfiguration.GraceEndDuration)
                    {
                        _logger.Information("Grace period expired ({Duration}) - ending session",
                            TrackingConfiguration.GraceEndDuration);
                        EndCurrentSession();
                    }
                }
                break;
        }
    }

    // ============ INTERNAL STATE TRANSITIONS ============

    private void ProcessEndTrigger()
    {
        switch (_state)
        {
            case TrackingState.GraceStart:
                // Immediate end (no grace protection)
                EndCurrentSession();
                break;
            case TrackingState.Tracking:
                // Enter grace period
                EnterGracePeriod();
                break;
            case TrackingState.GraceEnd:
                // Already in grace, do nothing
                break;
        }
    }

    private void StartSession(string projectName)
    {
        // Don't create ProjectSession yet - wait until GraceStart completes
        // Store the project name temporarily
        _currentSession = new ProjectSession
        {
            Id = Guid.NewGuid(),
            ProjectName = projectName,
            UserName = _currentUserName,
            StartTime = DateTime.MinValue, // Placeholder - will be set in TransitionToTracking
            FlushedEnd = null
        };

        _sessionStartTime = DateTime.UtcNow;
        _graceEndStartTime = null;
        _state = TrackingState.GraceStart;

        _logger.Information("Entered GraceStart for project: {ProjectName}, user: {UserName} (not tracking yet)",
            projectName, _currentUserName);
        // Don't fire SessionStarted yet - wait until we transition to Tracking
    }

    private void TransitionToTracking()
    {
        if (_state != TrackingState.GraceStart)
        {
            _logger.Warning("TransitionToTracking called from invalid state: {State}", _state);
            return;
        }

        if (_currentSession == null || _sessionStartTime == null)
        {
            _logger.Error("TransitionToTracking called but session or start time is null");
            return;
        }

        // NOW we officially start tracking - set the actual StartTime retroactively to when GraceStart began
        _currentSession.StartTime = _sessionStartTime.Value;
        _state = TrackingState.Tracking;

        _logger.Information("Transitioned to Tracking for project: {ProjectName} ({Duration} elapsed, tracking starts from GraceStart time)",
            _currentSession.ProjectName,
            TrackingConfiguration.GraceStartDuration);

        // Fire SessionStarted event NOW (not during GraceStart)
        SessionStarted?.Invoke(this, _currentSession);
    }

    private void EnterGracePeriod()
    {
        if (_state != TrackingState.Tracking)
        {
            _logger.Warning("EnterGracePeriod called from invalid state: {State}", _state);
            return;
        }

        _graceEndStartTime = DateTime.UtcNow;
        _state = TrackingState.GraceEnd;
        _logger.Information("Entered grace period ({Duration}) - time continues to accumulate",
            TrackingConfiguration.GraceEndDuration);
    }

    private void ExitGracePeriod()
    {
        if (_state != TrackingState.GraceEnd)
        {
            _logger.Warning("ExitGracePeriod called from invalid state: {State}", _state);
            return;
        }

        _graceEndStartTime = null;
        _state = TrackingState.Tracking;
        _logger.Information("Exited grace period - resumed normal tracking");
    }

    private void EndCurrentSession()
    {
        if (_currentSession == null)
        {
            return;
        }

        // Check if we're ending during GraceStart (before actual tracking began)
        var wasInGraceStart = _state == TrackingState.GraceStart;

        if (wasInGraceStart)
        {
            // Exiting during GraceStart - no tracking should occur
            _logger.Information("Exited during GraceStart for project: {ProjectName} - no session recorded",
                _currentSession.ProjectName);

            // Don't fire SessionEnded event - session never truly started
            _currentSession = null;
            _sessionStartTime = null;
            _graceEndStartTime = null;
            _state = TrackingState.NotTracking;
            return;
        }

        // Normal session end (was in Tracking or GraceEnd state)
        _currentSession.EndTime = DateTime.UtcNow;

        _logger.Information("Ended session: {ProjectName} (Duration: {Duration})",
            _currentSession.ProjectName,
            _currentSession.EndTime.Value - _currentSession.StartTime);

        SessionEnded?.Invoke(this, _currentSession);

        _currentSession = null;
        _sessionStartTime = null;
        _graceEndStartTime = null;
        _state = TrackingState.NotTracking;
    }
}
