using DaVinciTimeTracker.Core.Monitors;
using Serilog;
using System.Timers;
using Timer = System.Timers.Timer;

namespace DaVinciTimeTracker.Core.Services;

/// <summary>
/// Orchestrates the tracking components. On each 2-second tick it snapshots the
/// TrackingContext and feeds it to the SessionManager reducer. No edge-event
/// subscriptions, no parameter threading.
/// </summary>
public class TimeTrackingService : IDisposable
{
    private readonly DaVinciResolveMonitor _resolveMonitor;
    private readonly ActivityMonitor       _activityMonitor;
    private readonly SessionManager        _sessionManager;
    private readonly TrackingContext       _context;
    private readonly ILogger               _logger;
    private readonly Timer                 _tickTimer;
    private bool _disposed;

    public TimeTrackingService(
        DaVinciResolveMonitor resolveMonitor,
        ActivityMonitor activityMonitor,
        SessionManager sessionManager,
        TrackingContext context,
        ILogger logger)
    {
        _resolveMonitor  = resolveMonitor;
        _activityMonitor = activityMonitor;
        _sessionManager  = sessionManager;
        _context         = context;
        _logger          = logger;
        _tickTimer       = new Timer(2000);
        _tickTimer.Elapsed += OnTick;
    }

    public void Start()
    {
        _logger.Information("Starting Time Tracking Service");
        _resolveMonitor.Start();
        _activityMonitor.Start();
        _tickTimer.Start();
    }

    public void Stop()
    {
        _logger.Information("Stopping Time Tracking Service");
        _tickTimer.Stop();
        _resolveMonitor.Stop();
        _activityMonitor.Stop();

        // Force a final tick with the last known snapshot to end any open session
        _sessionManager.Tick(TrackingSnapshot.Closed);
    }

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        _sessionManager.Tick(_context.Snapshot());
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _tickTimer.Stop();
            _tickTimer.Elapsed -= OnTick;
            _tickTimer.Dispose();
            _resolveMonitor.Dispose();
            _activityMonitor.Dispose();
            _logger.Information("Time Tracking Service disposed");
        }
        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~TimeTrackingService() => Dispose(false);
}
