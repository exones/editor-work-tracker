using System.Timers;
using DaVinciTimeTracker.Core.Native;
using DaVinciTimeTracker.Core.Resolve;
using DaVinciTimeTracker.Core.Services;
using Serilog;
using Timer = System.Timers.Timer;

namespace DaVinciTimeTracker.Core.Monitors;

public class DaVinciResolveMonitor : IMonitor, IDisposable
{
    private readonly ResolveApiClient       _apiClient;
    private readonly TrackingContext        _context;
    private readonly ISystemActivityProvider _systemActivity;
    private readonly ILogger                _logger;
    private readonly Timer                  _pollTimer;

    // Internal state for edge-event detection (page/timeline/render still use events for PageTracker)
    private string? _currentProject;
    private string? _currentPage;
    private string? _currentTimeline;
    private bool    _isRendering;
    private bool    _disposed;
    private bool    _wasProcessRunning;
    private bool    _sanityCheckPassed;

    // Properties still read by PageTracker directly
    public string? CurrentPage     => _currentPage;
    public string? CurrentTimeline => _currentTimeline;
    public bool    IsRendering     => _isRendering;

    // ── Edge events for PageTracker (segment boundaries) ─────────────────────
    public event EventHandler<string>? PageChanged;
    public event EventHandler<string>? TimelineChanged;
    public event EventHandler?         RenderingStarted;
    public event EventHandler?         RenderingStopped;

    public DaVinciResolveMonitor(
        ResolveApiClient apiClient,
        TrackingContext context,
        ISystemActivityProvider systemActivity,
        ILogger logger,
        int pollIntervalMs = 2000)
    {
        _apiClient      = apiClient;
        _context        = context;
        _systemActivity = systemActivity;
        _logger         = logger;
        _pollTimer      = new Timer(pollIntervalMs);
        _pollTimer.Elapsed += OnTimerElapsed;
    }

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        _pollTimer.Stop();
        try
        {
            await CheckProjectAsync();
            UpdateFocusContext();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in DaVinci Resolve monitor poll cycle");
        }
        finally
        {
            if (!_disposed) _pollTimer.Start();
        }
    }

    private async Task CheckProjectAsync()
    {
        var isProcessRunning = _systemActivity.IsDaVinciResolveRunning();

        if (!isProcessRunning)
        {
            if (_currentProject != null)
            {
                _logger.Information("DaVinci Resolve process not running — closing project: {ProjectName}", _currentProject);
                _currentProject  = null;
                _currentPage     = null;
                _currentTimeline = null;
                _isRendering     = false;
                _context.UpdateResolve(null, null, null, false, resolveRunning: false);
            }
            else
            {
                // Keep context in sync even when already null
                _context.UpdateResolve(null, null, null, false, resolveRunning: false);
            }
            _wasProcessRunning = false;
            _sanityCheckPassed = false;
            return;
        }

        if (!_wasProcessRunning)
        {
            _logger.Information("DaVinci Resolve process detected — running connection sanity check...");
            _wasProcessRunning = true;
            _sanityCheckPassed = await _apiClient.RunSanityCheckAsync();
            if (!_sanityCheckPassed)
            {
                _logger.Error("DaVinci API sanity check FAILED — tracking may not work correctly");
            }
        }

        var status      = await _apiClient.GetCurrentProjectNameAsync();
        var projectName = status.ProjectName;
        var page        = status.Page;
        var timeline    = status.Timeline;
        var isRendering = status.IsRendering;

        // Project change
        if (projectName != _currentProject)
        {
            if (projectName == null)
                _logger.Information("DaVinci project closed: {ProjectName}", _currentProject);
            else
                _logger.Information("DaVinci project changed to: {ProjectName}", projectName);

            _currentProject  = projectName;
            _currentPage     = null;
            _currentTimeline = null;
            _isRendering     = false;
        }

        // Always push current resolve state to context (Resolve is running at this point)
        _context.UpdateResolve(projectName, _currentPage, _currentTimeline, _isRendering, resolveRunning: true);

        if (_currentProject == null) return;

        // Page changes (null = DaVinci minimised, keep last known)
        if (page != null && page != _currentPage)
        {
            _logger.Debug("DaVinci page changed: {OldPage} → {NewPage}", _currentPage, page);
            _currentPage = page;
            _context.UpdateResolve(_currentProject, _currentPage, _currentTimeline, _isRendering, resolveRunning: true);
            PageChanged?.Invoke(this, page);
        }

        // Timeline changes (null = no timeline active, keep last known)
        if (timeline != null && timeline != _currentTimeline)
        {
            _logger.Debug("DaVinci timeline changed: {OldTimeline} → {NewTimeline}", _currentTimeline, timeline);
            _currentTimeline = timeline;
            _context.UpdateResolve(_currentProject, _currentPage, _currentTimeline, _isRendering, resolveRunning: true);
            TimelineChanged?.Invoke(this, timeline);
        }

        // Render state transitions
        if (isRendering && !_isRendering)
        {
            _logger.Information("DaVinci rendering started");
            _isRendering = true;
            _context.UpdateResolve(_currentProject, _currentPage, _currentTimeline, true, resolveRunning: true);
            RenderingStarted?.Invoke(this, EventArgs.Empty);
        }
        else if (!isRendering && _isRendering)
        {
            _logger.Information("DaVinci rendering stopped");
            _isRendering = false;
            _context.UpdateResolve(_currentProject, _currentPage, _currentTimeline, false, resolveRunning: true);
            RenderingStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateFocusContext()
    {
        if (_currentProject == null) return;
        var inFocus = _systemActivity.IsDaVinciInFocus();
        _context.UpdateFocus(inFocus);
    }

    public void Start()
    {
        _logger.Information("Starting DaVinci Resolve monitor");
        _logger.Information("Performing initial DaVinci state check...");
        Task.Run(async () =>
        {
            try
            {
                await CheckProjectAsync();
                UpdateFocusContext();
                _logger.Information("Initial state check complete");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during initial state check");
            }
        });
        _pollTimer.Start();
    }

    public void Stop()
    {
        _logger.Information("Stopping DaVinci Resolve monitor");
        _pollTimer.Stop();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _pollTimer.Stop();
            _pollTimer.Elapsed -= OnTimerElapsed;
            _pollTimer.Dispose();
            _logger.Information("DaVinci Resolve monitor disposed");
        }
        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~DaVinciResolveMonitor() => Dispose(false);
}
