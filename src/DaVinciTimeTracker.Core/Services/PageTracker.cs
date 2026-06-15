using DaVinciTimeTracker.Core.Models;
using DaVinciTimeTracker.Core.Monitors;
using Serilog;

namespace DaVinciTimeTracker.Core.Services;

/// <summary>
/// Tracks how long the user spends on each DaVinci Resolve page within a session.
///
/// Lifecycle:
///   - SessionStarted  → opens the first PageTimeEntry for the current page
///   - PageChanged     → closes the current entry, opens a new one
///   - SessionEnded    → closes the current entry
///
/// Only active while a session is running (Tracking / GraceEnd states).
/// GraceStart is excluded because SessionStarted hasn't fired yet.
///
/// FlushedEnd is updated by the caller (Program.cs periodic timer) for crash recovery.
/// </summary>
public sealed class PageTracker : IDisposable
{
    private readonly DaVinciResolveMonitor _monitor;
    private readonly SessionManager _sessionManager;
    private readonly ILogger _logger;

    private PageTimeEntry? _currentEntry;
    private ProjectSession? _pendingSession; // session waiting for first non-null page
    private bool _disposed;

    /// <summary>Fired when a page segment ends (page switch or session end). Caller persists the entry.</summary>
    public event Func<PageTimeEntry, Task>? PageSegmentEnded;

    public PageTimeEntry? CurrentEntry => _currentEntry;

    public PageTracker(DaVinciResolveMonitor monitor, SessionManager sessionManager, ILogger logger)
    {
        _monitor   = monitor;
        _sessionManager = sessionManager;
        _logger    = logger;

        _sessionManager.SessionStarted += OnSessionStarted;
        _sessionManager.SessionEnded   += OnSessionEnded;
        _monitor.PageChanged           += OnPageChanged;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnSessionStarted(object? sender, ProjectSession session)
    {
        var page = _monitor.CurrentPage;
        if (page != null)
        {
            _logger.Information("PageTracker: session started — opening entry for page '{Page}'", page);
            _pendingSession = null;
            OpenEntry(session, page);
        }
        else
        {
            // GetCurrentPage() returns null when DaVinci is minimised/background — wait for first real page
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
        // If we were waiting for the first page after session start, open retroactively
        if (_pendingSession is not null)
        {
            _logger.Information("PageTracker: first page '{Page}' detected — opening deferred entry from session start", newPage);
            var session = _pendingSession;
            _pendingSession = null;
            // StartTime retroactive to session start so total adds up correctly
            _currentEntry = new PageTimeEntry
            {
                Id          = Guid.NewGuid(),
                SessionId   = session.Id,
                ProjectName = session.ProjectName,
                UserName    = session.UserName,
                Page        = newPage,
                StartTime   = session.StartTime // retroactive
            };
            return;
        }

        if (_currentEntry is null) return; // no active session

        _logger.Debug("PageTracker: page changed to '{Page}' — rotating entry", newPage);
        CloseCurrentEntry();

        var activeSession = _sessionManager.GetCurrentSession();
        if (activeSession is not null)
            OpenEntry(activeSession, newPage);
    }

    // ── Entry management ──────────────────────────────────────────────────────

    private void OpenEntry(ProjectSession session, string page)
    {
        _currentEntry = new PageTimeEntry
        {
            Id          = Guid.NewGuid(),
            SessionId   = session.Id,
            ProjectName = session.ProjectName,
            UserName    = session.UserName,
            Page        = page,
            StartTime   = DateTime.UtcNow
        };
        _logger.Debug("PageTracker: opened entry {Id} for page '{Page}'", _currentEntry.Id, page);
    }

    private void CloseCurrentEntry()
    {
        if (_currentEntry is null) return;

        _currentEntry.EndTime = DateTime.UtcNow;
        var duration = _currentEntry.EndTime.Value - _currentEntry.StartTime;
        _logger.Debug("PageTracker: closed entry for '{Page}' — duration {Duration:mm\\:ss}",
            _currentEntry.Page, duration);

        var entry = _currentEntry;
        _currentEntry = null;

        _ = Task.Run(async () =>
        {
            try { await (PageSegmentEnded?.Invoke(entry) ?? Task.CompletedTask); }
            catch (Exception ex) { _logger.Error(ex, "PageTracker: error persisting page segment"); }
        });
    }

    /// <summary>
    /// Updates FlushedEnd on the open entry for crash recovery.
    /// Called by the periodic 30 s timer in Program.cs.
    /// </summary>
    public void FlushCurrentEntry()
    {
        if (_currentEntry is null) return;
        _currentEntry.FlushedEnd = DateTime.UtcNow;
        // Note: _pendingSession has no entry to flush yet — nothing to do
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sessionManager.SessionStarted -= OnSessionStarted;
        _sessionManager.SessionEnded   -= OnSessionEnded;
        _monitor.PageChanged           -= OnPageChanged;
    }
}
