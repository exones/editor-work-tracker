using DaVinciTimeTracker.Core.Configuration;
using DaVinciTimeTracker.Core.Models;
using Serilog;

namespace DaVinciTimeTracker.Core.Services;

/// <summary>
/// Pure poll-based session state machine. All state decisions are driven by
/// TrackingSnapshot values read once per 2-second tick — no edge events, no
/// parameter threading. This eliminates the entire edge-suppression bug class.
///
/// State transitions:
///   NotTracking → GraceStart  when project + focus + active
///   GraceStart  → Tracking    when grace elapsed + conditions still valid
///   GraceStart  → NotTracking when grace elapsed + conditions invalid
///   Tracking    → GraceEnd    when !rendering AND (!focus OR !active)
///   GraceEnd    → Tracking    when focus + active
///   GraceEnd    → NotTracking when grace expired (unless rendering — clock resets)
/// </summary>
public class SessionManager
{
    private readonly ILogger _logger;
    private readonly string  _currentUserName;

    private ProjectSession? _currentSession;
    private TrackingState   _state = TrackingState.NotTracking;
    private DateTime?       _sessionStartTime;
    private DateTime?       _graceEndStartTime;
    private DateTime?       _lastActivityDuringGraceStart;

    public event EventHandler<ProjectSession>? SessionStarted;
    public event EventHandler<ProjectSession>? SessionEnded;

    public TrackingState CurrentState    => _state;
    public string?       CurrentProjectName => _currentSession?.ProjectName;
    public string        CurrentUserName => _currentUserName;
    public DateTime?     SessionStartTime => _sessionStartTime;

    public TimeSpan? GraceStartElapsedTime => _sessionStartTime.HasValue && _state == TrackingState.GraceStart
        ? DateTime.UtcNow - _sessionStartTime.Value : null;

    public TimeSpan? GraceEndElapsedTime => _graceEndStartTime.HasValue && _state == TrackingState.GraceEnd
        ? DateTime.UtcNow - _graceEndStartTime.Value : null;

    public SessionManager(ILogger logger)
    {
        _logger = logger;
        _currentUserName = Environment.UserName;
        _logger.Information("SessionManager initialized for user: {UserName}", _currentUserName);
    }

    public ProjectSession? GetCurrentSession() => _currentSession;

    // ── Single entry point: called every 2s by TimeTrackingService ────────────

    /// <summary>
    /// Evaluates the current world-state snapshot and advances the session state machine.
    /// This is the only public method that changes state; it replaces all the old
    /// Handle* methods and CheckStateTransitions.
    /// </summary>
    public void Tick(TrackingSnapshot s)
    {
        // STEP 1 — project identity change only ENDS the current session.
        // Starting is gated by the NotTracking branch below (focus + active required),
        // which avoids churning throwaway GraceStarts while the user is away.
        if (_currentSession != null && s.Project != _currentSession.ProjectName)
        {
            _logger.Information("Tick: project changed from '{Old}' to '{New}' — ending current session",
                _currentSession.ProjectName, s.Project);
            EndCurrentSession(); // sets state = NotTracking
        }

        // STEP 2 — evaluate the (possibly just-updated) state against the snapshot.
        var now = DateTime.UtcNow;

        switch (_state)
        {
            case TrackingState.NotTracking:
                if (s.Project != null)
                {
                    if (s.IsRendering)
                    {
                        // Render in progress → start immediately and bypass GraceStart.
                        // A render is unambiguous intentional work; no focus/activity gate needed.
                        _logger.Information("Rendering detected — starting session immediately (bypassing GraceStart)");
                        StartSession(s.Project);
                        _lastActivityDuringGraceStart = now; // satisfy the activity guard
                        TransitionToTracking();
                    }
                    else if (s.IsInFocus && s.IsUserActive)
                    {
                        StartSession(s.Project);
                    }
                }
                break;

            case TrackingState.GraceStart:
                if (s.IsUserActive)
                    _lastActivityDuringGraceStart = now;

                // Render starting during GraceStart → no reason to wait, transition immediately
                if (s.IsRendering && _lastActivityDuringGraceStart == null)
                    _lastActivityDuringGraceStart = now;

                if (_sessionStartTime.HasValue)
                {
                    var elapsed = now - _sessionStartTime.Value;
                    bool hadActivity = _lastActivityDuringGraceStart.HasValue;

                    if (s.IsRendering && hadActivity)
                    {
                        // Skip the remainder of GraceStart — render is confirmation enough
                        _logger.Information("Rendering started during GraceStart — skipping grace period");
                        TransitionToTracking();
                    }
                    else if (elapsed >= TrackingConfiguration.GraceStartDuration)
                    {
                        if (s.IsInFocus && s.Project != null && hadActivity)
                        {
                            TransitionToTracking();
                        }
                        else
                        {
                            _logger.Information(
                                "GraceStart expired — conditions not valid " +
                                "(Focus: {Focus}, Project: {Project}, HadActivity: {HadActivity}) — ending session",
                                s.IsInFocus, s.Project != null, hadActivity);
                            EndCurrentSession();
                        }
                    }
                }
                break;

            case TrackingState.Tracking:
                // Rendering keeps the session in Tracking regardless of focus/activity.
                if (!s.IsRendering && (!s.IsInFocus || !s.IsUserActive))
                    EnterGracePeriod();
                break;

            case TrackingState.GraceEnd:
                if (s.IsInFocus && s.IsUserActive)
                {
                    ExitGracePeriod();
                }
                else if (_graceEndStartTime.HasValue)
                {
                    var graceElapsed = now - _graceEndStartTime.Value;
                    if (graceElapsed >= TrackingConfiguration.GraceEndDuration)
                    {
                        if (s.IsRendering)
                        {
                            // Hold the clock: render keeps session alive even in GraceEnd
                            _graceEndStartTime = now;
                            _logger.Debug("GraceEnd clock reset — rendering in progress");
                        }
                        else
                        {
                            _logger.Information("Grace period expired ({Duration}) — ending session",
                                TrackingConfiguration.GraceEndDuration);
                            EndCurrentSession();
                        }
                    }
                }
                break;
        }
    }

    // ── Internal state transitions (unchanged from original) ──────────────────

    private void StartSession(string projectName)
    {
        _currentSession = new ProjectSession
        {
            Id          = Guid.NewGuid(),
            ProjectName = projectName,
            UserName    = _currentUserName,
            StartTime   = DateTime.MinValue,
            FlushedEnd  = null
        };

        _sessionStartTime             = DateTime.UtcNow;
        _graceEndStartTime            = null;
        _lastActivityDuringGraceStart = null;
        _state = TrackingState.GraceStart;

        _logger.Information("Entered GraceStart for project: {ProjectName}, user: {UserName} (not tracking yet)",
            projectName, _currentUserName);
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
            _logger.Error("TransitionToTracking: session or start time is null");
            return;
        }

        _currentSession.StartTime = _sessionStartTime.Value;
        _state = TrackingState.Tracking;

        _logger.Information("Transitioned to Tracking for project: {ProjectName}",
            _currentSession.ProjectName);

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
        _logger.Information("Entered grace period ({Duration}) — time continues to accumulate",
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
        _logger.Information("Exited grace period — resumed normal tracking");
    }

    private void EndCurrentSession()
    {
        if (_currentSession == null) return;

        var wasInGraceStart = _state == TrackingState.GraceStart;

        if (wasInGraceStart)
        {
            _logger.Information("Exited during GraceStart for project: {ProjectName} — no session recorded",
                _currentSession.ProjectName);
            _currentSession               = null;
            _sessionStartTime             = null;
            _graceEndStartTime            = null;
            _lastActivityDuringGraceStart = null;
            _state = TrackingState.NotTracking;
            return;
        }

        _currentSession.EndTime = DateTime.UtcNow;
        _logger.Information("Ended session: {ProjectName} (Duration: {Duration})",
            _currentSession.ProjectName,
            _currentSession.EndTime.Value - _currentSession.StartTime);

        SessionEnded?.Invoke(this, _currentSession);

        _currentSession               = null;
        _sessionStartTime             = null;
        _graceEndStartTime            = null;
        _lastActivityDuringGraceStart = null;
        _state = TrackingState.NotTracking;
    }
}
