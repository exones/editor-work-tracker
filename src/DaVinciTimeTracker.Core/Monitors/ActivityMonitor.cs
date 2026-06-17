using System.Timers;
using DaVinciTimeTracker.Core.Native;
using DaVinciTimeTracker.Core.Services;
using Serilog;
using Timer = System.Timers.Timer;

namespace DaVinciTimeTracker.Core.Monitors;

/// <summary>
/// Polls OS idle time every 5 seconds and writes the current activity state
/// into TrackingContext. The SessionManager reducer reads it via snapshots.
/// </summary>
public class ActivityMonitor : IMonitor, IDisposable
{
    private readonly ILogger                 _logger;
    private readonly TrackingContext         _context;
    private readonly ISystemActivityProvider _systemActivity;
    private readonly Timer                   _checkTimer;
    private readonly TimeSpan                _inactivityThreshold;
    private bool _wasActive = true;
    private bool _disposed;

    public ActivityMonitor(
        TrackingContext context,
        ISystemActivityProvider systemActivity,
        ILogger logger,
        int checkIntervalMs = 5000,
        int inactivityThresholdMinutes = 1)
    {
        _context             = context;
        _systemActivity      = systemActivity;
        _logger              = logger;
        _checkTimer          = new Timer(checkIntervalMs);
        _checkTimer.Elapsed += OnTimerElapsed;
        _inactivityThreshold = TimeSpan.FromMinutes(inactivityThresholdMinutes);
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        var isCurrentlyActive = IsUserActive();
        _context.UpdateActivity(isCurrentlyActive);

        if (isCurrentlyActive && !_wasActive)
        {
            _logger.Information("User became active");
            _wasActive = true;
        }
        else if (!isCurrentlyActive && _wasActive)
        {
            _logger.Information("User became idle");
            _wasActive = false;
        }
    }

    public bool IsUserActive()
    {
        var idleTime = _systemActivity.GetIdleTime();
        return idleTime < _inactivityThreshold;
    }

    public void Start()
    {
        _logger.Information("Starting activity monitor");
        _checkTimer.Start();
    }

    public void Stop()
    {
        _logger.Information("Stopping activity monitor");
        _checkTimer.Stop();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _checkTimer.Stop();
            _checkTimer.Elapsed -= OnTimerElapsed;
            _checkTimer.Dispose();
            _logger.Information("Activity monitor disposed");
        }
        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~ActivityMonitor() => Dispose(false);
}
