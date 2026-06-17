using DaVinciTimeTracker.Core.Models;
using DaVinciTimeTracker.Core.Monitors;
using DaVinciTimeTracker.Core.Services;
using Serilog;

namespace DaVinciTimeTracker.Core.Services;

/// <summary>
/// Tracks how long the user spends on each DaVinci Resolve page + timeline within a session,
/// and tags segments where DaVinci is actively rendering.
///
/// Rotation triggers (close current entry, open new one):
///   - Page changes      (PageChanged event)
///   - Timeline changes  (TimelineChanged event)
///   - Render starts     (RenderingStarted — same page/timeline, IsRendering = true)
///   - Render stops      (RenderingStopped — same page/timeline, IsRendering = false)
///   - Session ends      (SessionEnded)
///
/// Deferred start: if GetCurrentPage() returns null at session start (DaVinci in background),
/// the first entry is held back until the first real page is detected and then opened
/// retroactively from the session StartTime.
/// </summary>
public sealed class PageTracker : IDisposable
{
    private readonly DaVinciResolveMonitor _monitor;
    private readonly TrackingContext       _context;
    private readonly SessionManager        _sessionManager;
    private readonly ILogger               _logger;

    private PageTimeEntry?  _currentEntry;
    private ProjectSession? _pendingSession; // session waiting for first non-null page
    private bool _disposed;

    /// <summary>Fired when a segment ends. Caller persists the entry.</summary>
    public event Func<PageTimeEntry, Task>? PageSegmentEnded;

    public PageTimeEntry? CurrentEntry => _currentEntry;

    public PageTracker(DaVinciResolveMonitor monitor, TrackingContext context, SessionManager sessionManager, ILogger logger)
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
        if (page != null)
        {
            _pendingSession = null;
            _logger.Information("PageTracker: session started — page '{Page}' timeline '{Timeline}'",
                page, snap.Timeline);
            OpenEntry(session, page, snap.Timeline, snap.IsRendering);
        }
        else
        {
            _logger.Information("PageTracker: session started, page unknown (DaVinci in background) — deferring first entry");
            _pendingSession = session;
        }
    }

    private void OnSessionEnded(object? sender, ProjectSession session)
    {
        _logger.Information("PageTracker: session ended — closing entry");
        _pendingSession = null;
        CloseCurrentEntry();
    }

    private void OnPageChanged(object? sender, string newPage)
    {
        if (TryResolvePending(newPage)) return;
        if (_currentEntry is null) return;

        _logger.Debug("PageTracker: page changed to '{Page}' — rotating entry", newPage);
        CloseCurrentEntry();

        var snap = _context.Snapshot();
        var s    = _sessionManager.GetCurrentSession();
        if (s is not null)
            OpenEntry(s, newPage, snap.Timeline, snap.IsRendering);
    }

    private void OnTimelineChanged(object? sender, string newTimeline)
    {
        if (_currentEntry is null && _pendingSession is null) return;
        if (_currentEntry is null) return;

        _logger.Debug("PageTracker: timeline changed to '{Timeline}' — rotating entry", newTimeline);
        var snap = _context.Snapshot();
        CloseCurrentEntry();

        var s = _sessionManager.GetCurrentSession();
        if (s is not null)
            OpenEntry(s, snap.Page ?? _currentEntry?.Page ?? "unknown", newTimeline, snap.IsRendering);
    }

    private void OnRenderingStarted(object? sender, EventArgs e)
    {
        if (_currentEntry is null) return;

        _logger.Information("PageTracker: rendering started on page '{Page}' — rotating to render entry",
            _currentEntry.Page);
        var page     = _currentEntry.Page;
        var timeline = _currentEntry.TimelineName;
        CloseCurrentEntry();

        var s = _sessionManager.GetCurrentSession();
        if (s is not null)
            OpenEntry(s, page, timeline, isRendering: true);
    }

    private void OnRenderingStopped(object? sender, EventArgs e)
    {
        if (_currentEntry is null) return;

        _logger.Information("PageTracker: rendering stopped — rotating back to active entry");
        var page     = _currentEntry.Page;
        var timeline = _currentEntry.TimelineName;
        CloseCurrentEntry();

        var s = _sessionManager.GetCurrentSession();
        if (s is not null)
            OpenEntry(s, page, timeline, isRendering: false);
    }

    // ── Deferred-start helper ─────────────────────────────────────────────────

    /// <summary>
    /// If a session is pending its first page detection, open the entry retroactively
    /// from session start time and return true. Caller should early-return after this.
    /// </summary>
    private bool TryResolvePending(string newPage)
    {
        if (_pendingSession is null) return false;

        _logger.Information("PageTracker: first page '{Page}' detected — opening deferred entry from session start", newPage);
        var session = _pendingSession;
        var snap    = _context.Snapshot();
        _pendingSession = null;

        _currentEntry = new PageTimeEntry
        {
            Id           = Guid.NewGuid(),
            SessionId    = session.Id,
            ProjectName  = session.ProjectName,
            UserName     = session.UserName,
            Page         = newPage,
            TimelineName = snap.Timeline,
            IsRendering  = snap.IsRendering,
            StartTime    = session.StartTime  // retroactive to session start
        };
        return true;
    }

    // ── Entry management ──────────────────────────────────────────────────────

    private void OpenEntry(ProjectSession session, string page, string? timeline, bool isRendering)
    {
        _currentEntry = new PageTimeEntry
        {
            Id           = Guid.NewGuid(),
            SessionId    = session.Id,
            ProjectName  = session.ProjectName,
            UserName     = session.UserName,
            Page         = page,
            TimelineName = timeline,
            IsRendering  = isRendering,
            StartTime    = DateTime.UtcNow
        };
        _logger.Debug("PageTracker: opened entry {Id} page='{Page}' timeline='{Timeline}' rendering={Rendering}",
            _currentEntry.Id, page, timeline, isRendering);
    }

    private void CloseCurrentEntry()
    {
        if (_currentEntry is null) return;

        _currentEntry.EndTime = DateTime.UtcNow;
        var duration = _currentEntry.EndTime.Value - _currentEntry.StartTime;
        _logger.Debug("PageTracker: closed entry page='{Page}' timeline='{Timeline}' rendering={Rendering} duration={Duration:mm\\:ss}",
            _currentEntry.Page, _currentEntry.TimelineName, _currentEntry.IsRendering, duration);

        var entry = _currentEntry;
        _currentEntry = null;

        _ = Task.Run(async () =>
        {
            try { await (PageSegmentEnded?.Invoke(entry) ?? Task.CompletedTask); }
            catch (Exception ex) { _logger.Error(ex, "PageTracker: error persisting page segment"); }
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
