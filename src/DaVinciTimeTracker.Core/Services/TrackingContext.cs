namespace DaVinciTimeTracker.Core.Services;

/// <summary>
/// Thread-safe store for all live world-state that drives the session state machine.
/// Updated by DaVinciResolveMonitor and ActivityMonitor; read by SessionManager (via snapshot)
/// and exposed as richer status to the API.
/// </summary>
public sealed class TrackingContext
{
    private readonly object _lock = new();

    // ── Resolve state (written by DaVinciResolveMonitor) ─────────────────────
    public bool     IsResolveRunning   { get; private set; }
    public bool     ScriptingBridgeOk  { get; private set; }
    public string?  Project            { get; private set; }
    public string?  Page               { get; private set; }
    public string?  Timeline           { get; private set; }
    public bool     IsRendering        { get; private set; }

    // ── OS activity state (written by ActivityMonitor + DaVinciResolveMonitor) ──
    public bool     IsInFocus    { get; private set; }
    public bool     IsUserActive { get; private set; }

    // ── Timestamps ────────────────────────────────────────────────────────────
    public DateTime LastUpdated        { get; private set; } = DateTime.UtcNow;
    public DateTime LastFocusChange    { get; private set; } = DateTime.UtcNow;
    public DateTime LastActivityChange { get; private set; } = DateTime.UtcNow;

    // ── Writes ────────────────────────────────────────────────────────────────

    public void UpdateResolve(string? project, string? page, string? timeline, bool rendering, bool resolveRunning, bool scriptingBridgeOk = false)
    {
        lock (_lock)
        {
            IsResolveRunning  = resolveRunning;
            ScriptingBridgeOk = scriptingBridgeOk;
            Project           = project;
            Page              = page;
            Timeline          = timeline;
            IsRendering       = rendering;
            LastUpdated       = DateTime.UtcNow;
        }
    }

    public void UpdateFocus(bool inFocus)
    {
        lock (_lock)
        {
            if (inFocus == IsInFocus) return;
            IsInFocus       = inFocus;
            LastFocusChange = DateTime.UtcNow;
            LastUpdated     = DateTime.UtcNow;
        }
    }

    public void UpdateActivity(bool active)
    {
        lock (_lock)
        {
            if (active == IsUserActive) return;
            IsUserActive        = active;
            LastActivityChange  = DateTime.UtcNow;
            LastUpdated         = DateTime.UtcNow;
        }
    }

    // ── Consistent snapshot for the reducer ──────────────────────────────────

    public TrackingSnapshot Snapshot()
    {
        lock (_lock)
            return new TrackingSnapshot(
                IsResolveRunning, ScriptingBridgeOk, Project, Page, Timeline,
                IsRendering, IsInFocus, IsUserActive,
                LastActivityChange, LastFocusChange);
    }
}

/// <summary>Immutable point-in-time view of TrackingContext, consumed by SessionManager.Tick.</summary>
public record TrackingSnapshot(
    bool     IsResolveRunning,
    bool     ScriptingBridgeOk,
    string?  Project,
    string?  Page,
    string?  Timeline,
    bool     IsRendering,
    bool     IsInFocus,
    bool     IsUserActive,
    DateTime LastActivityChange,
    DateTime LastFocusChange)
{
    /// <summary>Sentinel used on shutdown: no project, no focus, no activity — ends any open session.</summary>
    public static readonly TrackingSnapshot Closed = new(
        false, false, null, null, null, false, false, false,
        DateTime.UtcNow, DateTime.UtcNow);
}
