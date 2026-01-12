using DaVinciTimeTracker.Core.Configuration;
using DaVinciTimeTracker.Core.Models;
using Serilog;

namespace DaVinciTimeTracker.Core.Services;

public class SessionManager
{

    private readonly ILogger _logger;
    private ProjectSession? _currentSession;
    private TrackingState _state = TrackingState.NotTracking;
    private DateTime? _sessionStartTime;     // When GraceStart began
    private DateTime? _graceEndStartTime;    // When GraceEnd began

    public event EventHandler<ProjectSession>? SessionStarted;
    public event EventHandler<ProjectSession>? SessionEnded;

    public TrackingState CurrentState => _state;
    public string? CurrentProjectName => _currentSession?.ProjectName;
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
        _currentSession = new ProjectSession
        {
            Id = Guid.NewGuid(),
            ProjectName = projectName,
            StartTime = DateTime.UtcNow,
            FlushedEnd = null
        };

        _sessionStartTime = DateTime.UtcNow;
        _graceEndStartTime = null;
        _state = TrackingState.GraceStart;

        _logger.Information("Started session in GraceStart: {ProjectName}", projectName);
        SessionStarted?.Invoke(this, _currentSession);
    }

    private void TransitionToTracking()
    {
        if (_state != TrackingState.GraceStart)
        {
            _logger.Warning("TransitionToTracking called from invalid state: {State}", _state);
            return;
        }

        _state = TrackingState.Tracking;
        _logger.Information("Transitioned to Tracking ({Duration} elapsed)",
            TrackingConfiguration.GraceStartDuration);
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
