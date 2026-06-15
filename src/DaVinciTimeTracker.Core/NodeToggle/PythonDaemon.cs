using Serilog;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace DaVinciTimeTracker.Core.NodeToggle;

/// <summary>
/// Manages a persistent Python child process that runs a stdin/stdout JSON command loop.
///
/// Responsibilities:
///   - Start the process on first use (lazy)
///   - Restart automatically when the process crashes
///   - Serialise all stdin/stdout exchanges (one command at a time)
///   - Surface stderr lines as log entries
///   - Expose a simple SendAsync(json) → JsonNode? interface
///
/// The caller (NodeToggleApiClient) owns the command protocol; this class only
/// cares about process health and byte transport.
/// </summary>
public sealed class PythonDaemon : IDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────────
    public string PythonPath  { get; private set; } = "";
    public string ScriptPath  { get; private set; } = "";

    /// <summary>How long to wait for a response line before declaring the process hung.</summary>
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long to wait for the ready handshake line after starting the process.</summary>
    public TimeSpan StartupTimeout  { get; set; } = TimeSpan.FromSeconds(15);

    // ── State ─────────────────────────────────────────────────────────────────
    private Process?       _proc;
    private bool           _ready;           // process sent "ready":true on startup
    private DateTime       _lastRestartAt    = DateTime.MinValue;
    private int            _consecutiveFails;
    private readonly SemaphoreSlim _ioLock   = new(1, 1);
    private bool           _disposed;
    private readonly ILogger _logger;

    /// <summary>Fires when the process becomes ready (connected to DaVinci).</summary>
    public event Action?  BecameReady;

    /// <summary>Fires when the process exits unexpectedly.</summary>
    public event Action?  Crashed;

    public bool IsReady => _ready && _proc is { HasExited: false };

    // ── Constructor ───────────────────────────────────────────────────────────

    public PythonDaemon(ILogger logger) => _logger = logger;

    public void Configure(string pythonPath, string scriptPath)
    {
        if (PythonPath == pythonPath && ScriptPath == scriptPath) return;
        PythonPath  = pythonPath;
        ScriptPath  = scriptPath;
        _logger.Information("PythonDaemon: configured — {Python}", pythonPath);
        Restart("configuration changed");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a single-line JSON command and returns the single-line JSON response.
    /// Returns null on timeout, process crash, or if not yet ready.
    /// Thread-safe: concurrent callers queue behind a semaphore.
    /// </summary>
    public async Task<JsonNode?> SendAsync(string jsonLine)
    {
        await _ioLock.WaitAsync();
        try
        {
            await EnsureRunningAsync();
            if (!IsReady) return null;

            await _proc!.StandardInput.WriteLineAsync(jsonLine);

            using var cts = new CancellationTokenSource(ResponseTimeout);
            try
            {
                var line = await _proc.StandardOutput.ReadLineAsync(cts.Token);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _consecutiveFails = 0;
                    return JsonNode.Parse(line);
                }

                _logger.Warning("PythonDaemon: empty response");
                return null;
            }
            catch (OperationCanceledException)
            {
                _consecutiveFails++;
                _logger.Warning("PythonDaemon: response timed out after {Ms}ms (fail #{N})",
                    (int)ResponseTimeout.TotalMilliseconds, _consecutiveFails);
                Restart("response timeout");
                return null;
            }
        }
        catch (Exception ex)
        {
            _consecutiveFails++;
            _logger.Error(ex, "PythonDaemon: communication error");
            Restart("communication error");
            return null;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    // ── Process lifecycle ─────────────────────────────────────────────────────

    private async Task EnsureRunningAsync()
    {
        if (_proc is { HasExited: false } && _ready) return;

        // Backoff: don't restart more than once every 5 seconds
        var sinceLastRestart = DateTime.UtcNow - _lastRestartAt;
        if (sinceLastRestart < TimeSpan.FromSeconds(5))
        {
            _logger.Debug("PythonDaemon: backoff — {Secs:F1}s since last restart",
                sinceLastRestart.TotalSeconds);
            return;
        }

        if (string.IsNullOrEmpty(PythonPath) || string.IsNullOrEmpty(ScriptPath))
        {
            _logger.Debug("PythonDaemon: not configured yet");
            return;
        }
        if (!File.Exists(ScriptPath))
        {
            _logger.Warning("PythonDaemon: script not found at {Script}", ScriptPath);
            return;
        }

        KillCurrentProcess();
        _lastRestartAt = DateTime.UtcNow;

        _logger.Information("PythonDaemon: starting — {Python} \"{Script}\"", PythonPath, ScriptPath);

        _proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName  = PythonPath,
                Arguments = $"\"{ScriptPath}\"",
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute  = false,
                CreateNoWindow   = true,
                StandardInputEncoding  = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8,
            },
            EnableRaisingEvents = true
        };

        _proc.ErrorDataReceived += OnStderr;
        _proc.Exited += OnProcessExited;

        _proc.Start();
        _proc.BeginErrorReadLine();

        // Wait for the ready handshake line
        using var cts = new CancellationTokenSource(StartupTimeout);
        try
        {
            var startupLine = await _proc.StandardOutput.ReadLineAsync(cts.Token);
            if (!string.IsNullOrEmpty(startupLine))
            {
                var node = JsonNode.Parse(startupLine);
                _ready = node?["ready"]?.GetValue<bool>() == true;
                if (_ready)
                {
                    _logger.Information("PythonDaemon: ready");
                    BecameReady?.Invoke();
                }
                else
                {
                    _logger.Warning("PythonDaemon: startup issue — {Msg}",
                        node?["message"]?.GetValue<string>());
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("PythonDaemon: startup handshake timed out after {Ms}ms",
                (int)StartupTimeout.TotalMilliseconds);
            KillCurrentProcess();
        }
    }

    private void Restart(string reason)
    {
        _logger.Warning("PythonDaemon: restarting ({Reason})", reason);
        KillCurrentProcess();
    }

    private void KillCurrentProcess()
    {
        _ready = false;
        if (_proc is null) return;
        try
        {
            if (!_proc.HasExited)
            {
                _proc.StandardInput.Close(); // signals the Python loop to exit cleanly
                if (!_proc.WaitForExit(2000))
                    _proc.Kill(entireProcessTree: true);
            }
        }
        catch { /* ignore */ }
        finally
        {
            _proc.Dispose();
            _proc = null;
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnStderr(object _, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
            _logger.Information("NodeToggle py: {Line}", e.Data);
    }

    private void OnProcessExited(object? _, EventArgs __)
    {
        _ready = false;
        _logger.Warning("PythonDaemon: process exited — will restart on next command");
        Crashed?.Invoke();
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        KillCurrentProcess();
        _ioLock.Dispose();
    }
}
