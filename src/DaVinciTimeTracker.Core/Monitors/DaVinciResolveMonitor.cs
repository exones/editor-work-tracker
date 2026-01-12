using System.Timers;
using DaVinciTimeTracker.Core.Native;
using DaVinciTimeTracker.Core.Resolve;
using Serilog;
using Timer = System.Timers.Timer;

namespace DaVinciTimeTracker.Core.Monitors;

public class DaVinciResolveMonitor : IMonitor, IDisposable
{
    private readonly ResolveApiClient _apiClient;
    private readonly ILogger _logger;
    private readonly Timer _pollTimer;
    private string? _currentProject;
    private bool _wasInFocus = false;
    private bool _disposed;
    private bool _wasProcessRunning = false;
    private bool _sanityCheckPassed = false;

    public string? CurrentProject => _currentProject;

    public event EventHandler<string>? ProjectChanged;
    public event EventHandler? ProjectClosed;
    public event EventHandler? WindowFocusLost;
    public event EventHandler? WindowFocusGained;

    public DaVinciResolveMonitor(ResolveApiClient apiClient, ILogger logger, int pollIntervalMs = 2000)
    {
        _apiClient = apiClient;
        _logger = logger;
        _pollTimer = new Timer(pollIntervalMs);
        _pollTimer.Elapsed += OnTimerElapsed;
    }

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        _pollTimer.Stop();
        try
        {
            await CheckProjectAsync();
            CheckWindowFocus();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in DaVinci Resolve monitor poll cycle");
        }
        finally
        {
            if (!_disposed)
            {
                _pollTimer.Start();
            }
        }
    }

    private async Task CheckProjectAsync()
    {
        // OPTIMIZATION: Check if DaVinci Resolve process is running before calling Python API
        var isProcessRunning = WindowsApi.IsDaVinciResolveRunning();
        
        if (!isProcessRunning)
        {
            // If DaVinci was open and now it's closed
            if (_currentProject != null)
            {
                _logger.Information("DaVinci Resolve process not running - closing project: {ProjectName}", _currentProject);
                ProjectClosed?.Invoke(this, EventArgs.Empty);
                _currentProject = null;
                _wasInFocus = false;
            }
            
            // Reset state when process stops
            _wasProcessRunning = false;
            _sanityCheckPassed = false;
            
            return; // Skip expensive Python API call
        }

        // DaVinci process just started - run sanity check ONCE
        if (isProcessRunning && !_wasProcessRunning)
        {
            _logger.Information("DaVinci Resolve process detected - running connection sanity check...");
            _wasProcessRunning = true;
            _sanityCheckPassed = await _apiClient.RunSanityCheckAsync();
            
            if (!_sanityCheckPassed)
            {
                _logger.Error("DaVinci API sanity check FAILED - tracking may not work correctly");
                _logger.Error("Please review the diagnostic messages above and fix the issues");
            }
        }

        // Process is running, now check project via Python API
        var projectName = await _apiClient.GetCurrentProjectNameAsync();

        if (projectName != _currentProject)
        {
            if (projectName == null && _currentProject != null)
            {
                // Project closed
                _logger.Information("DaVinci project closed: {ProjectName}", _currentProject);
                ProjectClosed?.Invoke(this, EventArgs.Empty);
            }
            else if (projectName != null)
            {
                // Project opened or changed
                _logger.Information("DaVinci project changed to: {ProjectName}", projectName);
                ProjectChanged?.Invoke(this, projectName);
            }

            _currentProject = projectName;
        }
    }

    private void CheckWindowFocus()
    {
        if (_currentProject == null)
        {
            return;
        }

        var isInFocus = WindowsApi.IsDaVinciResolveInFocus();

        if (isInFocus && !_wasInFocus)
        {
            // DaVinci gained focus
            _logger.Information("DaVinci window gained focus");
            WindowFocusGained?.Invoke(this, EventArgs.Empty);
            _wasInFocus = true;
        }
        else if (!isInFocus && _wasInFocus)
        {
            // DaVinci lost focus
            var foregroundProcess = WindowsApi.GetForegroundProcessName();
            _logger.Information("DaVinci window lost focus (switched to: {Process})",
                foregroundProcess ?? "unknown");
            WindowFocusLost?.Invoke(this, EventArgs.Empty);
            _wasInFocus = false;
        }
    }

    public void Start()
    {
        _logger.Information("Starting DaVinci Resolve monitor");
        
        // Immediately check current state (don't wait for first timer tick)
        _logger.Information("Performing initial DaVinci state check...");
        Task.Run(async () =>
        {
            try
            {
                await CheckProjectAsync();
                CheckWindowFocus();
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
        if (_disposed)
        {
            return;
        }

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
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    ~DaVinciResolveMonitor()
    {
        Dispose(disposing: false);
    }
}
