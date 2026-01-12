using DaVinciTimeTracker.Core.Monitors;
using Serilog;
using System.Timers;
using Timer = System.Timers.Timer;

namespace DaVinciTimeTracker.Core.Services;

public class TimeTrackingService : IDisposable
{
    private readonly DaVinciResolveMonitor _resolveMonitor;
    private readonly ActivityMonitor _activityMonitor;
    private readonly SessionManager _sessionManager;
    private readonly ILogger _logger;
    private readonly Timer _stateCheckTimer;
    private bool _disposed;

    public TimeTrackingService(
        DaVinciResolveMonitor resolveMonitor,
        ActivityMonitor activityMonitor,
        SessionManager sessionManager,
        ILogger logger)
    {
        _resolveMonitor = resolveMonitor;
        _activityMonitor = activityMonitor;
        _sessionManager = sessionManager;
        _logger = logger;
        _stateCheckTimer = new Timer(2000); // Check every 2 seconds
        _stateCheckTimer.Elapsed += OnStateCheckTimerElapsed;
    }

    public void Start()
    {
        _logger.Information("Starting Time Tracking Service");

        _resolveMonitor.ProjectChanged += OnProjectChanged;
        _resolveMonitor.ProjectClosed += OnProjectClosed;
        _resolveMonitor.WindowFocusLost += OnWindowFocusLost;
        _resolveMonitor.WindowFocusGained += OnWindowFocusGained;
        _activityMonitor.UserBecameIdle += OnUserIdle;
        _activityMonitor.UserBecameActive += OnUserActive;

        _resolveMonitor.Start();
        _activityMonitor.Start();
        _stateCheckTimer.Start();
    }

    public void Stop()
    {
        _logger.Information("Stopping Time Tracking Service");

        _stateCheckTimer.Stop();
        _resolveMonitor.Stop();
        _activityMonitor.Stop();

        _sessionManager.HandleProjectClosed();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _stateCheckTimer.Stop();
            _stateCheckTimer.Elapsed -= OnStateCheckTimerElapsed;
            _stateCheckTimer.Dispose();
            _resolveMonitor.Dispose();
            _activityMonitor.Dispose();
            _logger.Information("Time Tracking Service disposed");
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    ~TimeTrackingService()
    {
        Dispose(disposing: false);
    }

    private void OnStateCheckTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        // Pass current conditions to state machine for verification
        _sessionManager.CheckStateTransitions(
            Core.Native.WindowsApi.IsDaVinciResolveInFocus(),
            _activityMonitor.IsUserActive(),
            _resolveMonitor.CurrentProject
        );
    }

    private void OnProjectChanged(object? sender, string projectName)
    {
        _sessionManager.HandleProjectChanged(projectName);
    }

    private void OnProjectClosed(object? sender, EventArgs e)
    {
        _sessionManager.HandleProjectClosed();
    }

    private void OnWindowFocusLost(object? sender, EventArgs e)
    {
        _sessionManager.HandleFocusLost();
    }

    private void OnWindowFocusGained(object? sender, EventArgs e)
    {
        _sessionManager.HandleFocusGained(
            _resolveMonitor.CurrentProject,
            _activityMonitor.IsUserActive());
    }

    private void OnUserIdle(object? sender, EventArgs e)
    {
        _sessionManager.HandleUserIdle();
    }

    private void OnUserActive(object? sender, EventArgs e)
    {
        _sessionManager.HandleUserActive();
    }
}
