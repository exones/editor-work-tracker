using System.Timers;
using DaVinciTimeTracker.Core.Native;
using Serilog;
using Timer = System.Timers.Timer;

namespace DaVinciTimeTracker.Core.Monitors;

public class ActivityMonitor : IMonitor, IDisposable
{
    private readonly ILogger _logger;
    private readonly Timer _checkTimer;
    private readonly TimeSpan _inactivityThreshold;
    private bool _wasActive = true;
    private bool _disposed;

    public event EventHandler? UserBecameIdle;
    public event EventHandler? UserBecameActive;

    public ActivityMonitor(ILogger logger, int checkIntervalMs = 5000, int inactivityThresholdMinutes = 3)
    {
        _logger = logger;
        _checkTimer = new Timer(checkIntervalMs);
        _checkTimer.Elapsed += OnTimerElapsed;
        _inactivityThreshold = TimeSpan.FromMinutes(inactivityThresholdMinutes);
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        var isCurrentlyActive = IsUserActive();

        if (isCurrentlyActive && !_wasActive)
        {
            _logger.Information("User became active");
            UserBecameActive?.Invoke(this, EventArgs.Empty);
            _wasActive = true;
        }
        else if (!isCurrentlyActive && _wasActive)
        {
            _logger.Information("User became idle");
            UserBecameIdle?.Invoke(this, EventArgs.Empty);
            _wasActive = false;
        }
    }

    public bool IsUserActive()
    {
        var idleTime = WindowsApi.GetIdleTime();
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
        if (_disposed)
        {
            return;
        }

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
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    ~ActivityMonitor()
    {
        Dispose(disposing: false);
    }
}
