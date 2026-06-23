using DaVinciTimeTracker.Core.Models;
using DaVinciTimeTracker.Core.Monitors;
using DaVinciTimeTracker.Core.Services;
using Serilog;

namespace DaVinciTimeTracker.Core.Services;

/// <summary>
/// Tracks how long the user spends on each activity (DaVinci page or processing operation)
/// within a session, and classifies each segment as User or Processing.
///
/// Rotation triggers (close current entry, open new one):
///   - Page changes        (PageChanged event)
///   - Timeline changes    (TimelineChanged event)
///   - Render starts       (RenderingStarted — opens a Processing/render entry)
///   - Render stops        (RenderingStopped — opens a User/page entry)
///   - Session ends        (SessionEnded)
///
/// Deferred start: if GetCurrentPage() returns null at session start (DaVinci in background),
/// the first entry is held back until the first real page is detected and then opened
/// retroactively from the session StartTime.
/// </summary>
public sealed class ActivityTracker : IDisposable
{
    private readonly DaVinciResolveMonitor _monitor;
    private readonly TrackingContext       _context;
    private readonly SessionManager        _sessionManager;
    private readonly ILogger               _logger;

    private ActivityEntry?  _currentEntry;
    private ProjectSession? _pendingSession; // session waiting for first non-null page
    private bool _disposed;

    /// <summary>Fired when a segment ends. Caller persists the entry.</summary>
    public event Func<ActivityEntry, Task>? ActivityEnded;

    public ActivityEntry? CurrentEntry => _currentEntry;

    public ActivityTracker(DaVinciResolveMonitor monitor, TrackingContext context, SessionManager sessionManager, ILogger logger)
    {
        _monitor        = monitor;
        _context        = context;
        _sessionManager = sessionManager;
        _logger         = logger;

        _sessionManager.SessionStarted  += OnSessionStarted;
        _sessionManager.SessionEnded    += OnSessionEnded;
        _monitor.PageChanged            += OnPageChanged;
        _monitor.TimelineChanged        += OnTimelineChanged;
        _monitor.RenderingStarted       += OnRenderingStarted;
        _monitor.RenderingStopped       += OnRenderingStopped;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnSessionStarted(object? sender, ProjectSession session)
    {
        var snap = _context.Snapshot();
        var page = snap.Page;

        if (page != null && !snap.IsRendering)
        {
            // Normal case: DaVinci is focused, page is known, not rendering
            _pendingSession = null;
            _logger.Information("ActivityTracker: session started — page '{Page}' timeline '{Timeline}'",
                page, snap.Timeline);
            OpenEntry(session, page, snap.Timeline, ActivityKind.User);
        }
        else if (snap.IsRendering)
        {
            // Render in progress (page may be null — GetCurrentPage returns null during render).
            // Open a render/Processing entry retroactively from session start.
            _pendingSession = null;
            _logger.Information("ActivityTracker: session started during render — opening render entry retroactively");
            _currentEntry = new ActivityEntry
            {
                Id           = Guid.NewGuid(),
                SessionId    = session.Id,
                ProjectName  = session.ProjectName,
                UserName     = session.UserName,
                ActivityType = "render",
                Kind         = ActivityKind.Processing,
                TimelineName = snap.Timeline,
                StartTime    = session.StartTime  // retroactive to session start
            };
        }
        else
        {
            // Page unknown, not rendering — defer until first PageChanged fires
            _logger.Information("ActivityTracker: session started, page unknown (DaVinci in background) — deferring first entry");
            _pendingSession = session;
        }
    }

    private void OnSessionEnded(object? sender, ProjectSession session)
    {
        _logger.Information("ActivityTracker: session ended — closing entry");
        _pendingSession = null;
        CloseCurrentEntry();
    }

    private void OnPageChanged(object? sender, string newPage)
    {
        if (TryResolvePending(newPage)) return;
        if (_currentEntry is null) return;

        _logger.Debug("ActivityTracker: page changed to '{Page}' — rotating entry", newPage);
        CloseCurrentEntry();

        var snap = _context.Snapshot();
        var s    = _sessionManager.GetCurrentSession();
        if (s is not null)
            OpenEntry(s, newPage, snap.Timeline, ActivityKind.User);
    }

    private void OnTimelineChanged(object? sender, string newTimeline)
    {
        if (_currentEntry is null && _pendingSession is null) return;
        if (_currentEntry is null) return;

        _logger.Debug("ActivityTracker: timeline changed to '{Timeline}' — rotating entry", newTimeline);
        var snap             = _context.Snapshot();
        var currentType      = _currentEntry.ActivityType;
        var currentKind      = _currentEntry.Kind;
        CloseCurrentEntry();

        var s = _sessionManager.GetCurrentSession();
        if (s is not null)
            OpenEntry(s, currentType, newTimeline, currentKind);
    }

    private void OnRenderingStarted(object? sender, EventArgs e)
    {
        // Special case: session started while DaVinci was rendering in background.
        // PageChanged never fires during render, so _pendingSession never resolves.
        // Open the render entry retroactively now.
        if (_pendingSession is not null)
        {
            var session = _pendingSession;
            var snap    = _context.Snapshot();
            _pendingSession = null;
            _logger.Information("ActivityTracker: rendering started with pending session — opening deferred render entry");
            _currentEntry = new ActivityEntry
            {
                Id           = Guid.NewGuid(),
                SessionId    = session.Id,
                ProjectName  = session.ProjectName,
                UserName     = session.UserName,
                ActivityType = "render",
                Kind         = ActivityKind.Processing,
                TimelineName = snap.Timeline,
                StartTime    = session.StartTime  // retroactive to session start
            };
            return;
        }

        if (_currentEntry is null) return;

        _logger.Information("ActivityTracker: rendering started on '{ActivityType}' — rotating to render entry",
            _currentEntry.ActivityType);
        var timeline = _currentEntry.TimelineName;
        CloseCurrentEntry();

        var s = _sessionManager.GetCurrentSession();
        if (s is not null)
            OpenEntry(s, "render", timeline, ActivityKind.Processing);
    }

    private void OnRenderingStopped(object? sender, EventArgs e)
    {
        if (_currentEntry is null) return;

        _logger.Information("ActivityTracker: rendering stopped — rotating back to user activity");
        var timeline = _currentEntry.TimelineName;
        CloseCurrentEntry();

        // Resume on the last known page; fall back to "unknown" if context has no page.
        var snap = _context.Snapshot();
        var page = snap.Page ?? "unknown";

        var s = _sessionManager.GetCurrentSession();
        if (s is not null)
            OpenEntry(s, page, timeline, ActivityKind.User);
    }

    // ── Deferred-start helper ─────────────────────────────────────────────────

    /// <summary>
    /// If a session is pending its first page detection, open the entry retroactively
    /// from session start time and return true. Caller should early-return after this.
    /// </summary>
    private bool TryResolvePending(string newPage)
    {
        if (_pendingSession is null) return false;

        _logger.Information("ActivityTracker: first page '{Page}' detected — opening deferred entry from session start", newPage);
        var session = _pendingSession;
        var snap    = _context.Snapshot();
        _pendingSession = null;

        _currentEntry = new ActivityEntry
        {
            Id           = Guid.NewGuid(),
            SessionId    = session.Id,
            ProjectName  = session.ProjectName,
            UserName     = session.UserName,
            ActivityType = newPage,
            Kind         = snap.IsRendering ? ActivityKind.Processing : ActivityKind.User,
            TimelineName = snap.Timeline,
            StartTime    = session.StartTime  // retroactive to session start
        };
        return true;
    }

    // ── Entry management ──────────────────────────────────────────────────────

    private void OpenEntry(ProjectSession session, string activityType, string? timeline, ActivityKind kind)
    {
        _currentEntry = new ActivityEntry
        {
            Id           = Guid.NewGuid(),
            SessionId    = session.Id,
            ProjectName  = session.ProjectName,
            UserName     = session.UserName,
            ActivityType = activityType,
            Kind         = kind,
            TimelineName = timeline,
            StartTime    = DateTime.UtcNow
        };
        _logger.Debug("ActivityTracker: opened entry {Id} activityType='{ActivityType}' kind={Kind} timeline='{Timeline}'",
            _currentEntry.Id, activityType, kind, timeline);
    }

    private void CloseCurrentEntry()
    {
        if (_currentEntry is null) return;

        _currentEntry.EndTime = DateTime.UtcNow;
        var duration = _currentEntry.EndTime.Value - _currentEntry.StartTime;
        _logger.Debug("ActivityTracker: closed entry activityType='{ActivityType}' kind={Kind} timeline='{Timeline}' duration={Duration:mm\\:ss}",
            _currentEntry.ActivityType, _currentEntry.Kind, _currentEntry.TimelineName, duration);

        var entry = _currentEntry;
        _currentEntry = null;

        _ = Task.Run(async () =>
        {
            try { await (ActivityEnded?.Invoke(entry) ?? Task.CompletedTask); }
            catch (Exception ex) { _logger.Error(ex, "ActivityTracker: error persisting activity segment"); }
        });
    }

    /// <summary>Updates FlushedEnd on the open entry for crash recovery (called by 30s timer in Program.cs).</summary>
    public void FlushCurrentEntry()
    {
        if (_currentEntry is null) return;
        _currentEntry.FlushedEnd = DateTime.UtcNow;
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sessionManager.SessionStarted  -= OnSessionStarted;
        _sessionManager.SessionEnded    -= OnSessionEnded;
        _monitor.PageChanged            -= OnPageChanged;
        _monitor.TimelineChanged        -= OnTimelineChanged;
        _monitor.RenderingStarted       -= OnRenderingStarted;
        _monitor.RenderingStopped       -= OnRenderingStopped;
    }
}
