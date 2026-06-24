using DaVinciTimeTracker.Core.Services;
using Serilog;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DaVinciTimeTracker.Core.NodeToggle;

/// <summary>
/// Manages node toggle groups: load/save JSON config, file-watch for live-reload,
/// and execute toggles via the Python scripting API.
/// </summary>
public class NodeToggleService : IDisposable
{
    private readonly NodeToggleApiClient _apiClient;
    private readonly ILogger _logger;
    private readonly string _configPath;
    private UserSettingsService? _settingsService;
    private NodeToggleConfigFile _config = new();
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounceTimer;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private volatile bool _selfWriting; // suppress file-watcher reload during self-writes
    // Per-group execution locks: rapid hotkey presses are dropped while one is in flight
    private readonly Dictionary<string, SemaphoreSlim> _groupLocks = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Fired when the config file changes (after debounce), with the new group list.</summary>
    public event Action<List<ToggleGroup>>? ConfigChanged;

    /// <summary>Exposes the underlying API client so diagnostics can call SendDiagnoseAsync.</summary>
    public NodeToggleApiClient GetApiClient() => _apiClient;

    public NodeToggleService(string pythonPath, string scriptPath, string configPath, ILogger logger)
    {
        _apiClient = new NodeToggleApiClient(logger);
        if (!string.IsNullOrEmpty(pythonPath))
            _apiClient.SetPythonExecutable(pythonPath, scriptPath);
        _configPath = configPath;
        _logger = logger;

        LoadConfig();
        StartFileWatcher();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public List<ToggleGroup> GetAll() => [.. _config.Groups];

    /// <summary>Sets the UserSettingsService so select actions can read shortcut config.</summary>
    public void SetSettingsService(UserSettingsService settingsService) =>
        _settingsService = settingsService;

    /// <summary>
    /// Returns groups including their runtime enabled state for API responses.
    /// CurrentEnabled is [JsonIgnore] on the model (not persisted to file), so this method
    /// wraps each group with an anonymous projection that includes the live state.
    /// </summary>
    public IEnumerable<object> GetAllWithState() =>
        _config.Groups.Select(g => new
        {
            g.Id,
            g.Name,
            g.Hotkey,
            g.Nodes,
            actionType     = g.ActionType,       // Toggle | Select
            currentEnabled = g.CurrentEnabled    // runtime state (Toggle only)
        });

    public ToggleGroup? GetById(string id) =>
        _config.Groups.FirstOrDefault(g => g.Id == id);

    /// <summary>
    /// Persist the given list of groups and raise ConfigChanged so hotkeys re-register.
    /// Validates that each NodeTarget has at least one identifier.
    /// </summary>
    public void Save(List<ToggleGroup> groups)
    {
        foreach (var group in groups)
        {
            foreach (var node in group.Nodes.Where(n => !n.IsValid))
                throw new InvalidOperationException(
                    $"Node in group '{group.Name}' has no nodeId or title — at least one is required.");
        }

        _config.Groups = groups;
        WriteConfigFile();
        _logger.Information("NodeToggle config saved ({Count} groups)", groups.Count);
        ConfigChanged?.Invoke(GetAll());
    }

    private SemaphoreSlim GetGroupLock(string id)
    {
        if (!_groupLocks.TryGetValue(id, out var sem))
            _groupLocks[id] = sem = new SemaphoreSlim(1, 1);
        return sem;
    }

    /// <summary>
    /// Execute the action for the given group id, routing by ActionType.
    /// For Toggle: stateful on/off. For Select: navigates to the target node.
    /// </summary>
    public async Task<(bool Success, bool? Enabled)> ExecuteByIdAsync(string id)
    {
        var group = GetById(id);
        if (group?.ActionType == NodeActionType.Select)
        {
            var (ok, _) = await ExecuteSelectByIdAsync(id);
            return (ok, null);
        }
        return await ExecuteToggleByIdAsync(id);
    }

    /// <summary>Execute a node-select action by group id.</summary>
    public async Task<(bool Success, int? NodeIndex)> ExecuteSelectByIdAsync(string id)
    {
        var group = GetById(id);
        if (group is null)
        {
            _logger.Warning("NodeSelect: group id={Id} not found", id);
            return (false, null);
        }
        if (!group.Nodes.Any())
        {
            _logger.Warning("NodeSelect: group '{Name}' has no node configured", group.Name);
            return (false, null);
        }

        var sem = GetGroupLock(id);
        if (!sem.Wait(0))
        {
            _logger.Debug("NodeSelect: '{Name}' is busy — dropping rapid hotkey press", group.Name);
            return (false, null);
        }

        try
        {
            var appendShortcut = _settingsService?.Current.AppendNodeShortcut ?? "Alt+S";
            var nextShortcut   = _settingsService?.Current.NextNodeShortcut   ?? "Alt+Shift+Oem7";

            _logger.Information("NodeSelect: selecting node in '{Name}' (appendShortcut={A} nextShortcut={N})",
                group.Name, appendShortcut, nextShortcut);

            return await _apiClient.ExecuteSelectAsync(group.Nodes, appendShortcut, nextShortcut);
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Execute a toggle for the group with the given id.
    /// Sends an explicit "on"/"off" based on tracked state (GetNodeEnabled is unavailable in
    /// the DaVinci scripting API). Rapid calls are dropped while one execution is in flight
    /// to prevent concurrent Python processes from cancelling each other out.
    /// </summary>
    private async Task<(bool Success, bool? Enabled)> ExecuteToggleByIdAsync(string id)
    {
        var group = GetById(id);
        if (group is null)
        {
            _logger.Warning("NodeToggle: group id={Id} not found", id);
            return (false, null);
        }

        if (!group.Nodes.Any())
        {
            _logger.Warning("NodeToggle: group '{Name}' has no nodes configured", group.Name);
            return (false, null);
        }

        var sem = GetGroupLock(id);
        if (!sem.Wait(0)) // non-blocking: drop if already executing
        {
            _logger.Debug("NodeToggle: '{Name}' is busy — dropping rapid hotkey press", group.Name);
            return (false, null);
        }

        try
        {
            var targetEnabled = group.CurrentEnabled.HasValue ? !group.CurrentEnabled.Value : false;
            var action = targetEnabled ? "on" : "off";

            _logger.Information("NodeToggle: executing '{Name}' → {Action} ({Count} node(s))",
                group.Name, action, group.Nodes.Count);

            var (success, enabled) = await _apiClient.ExecuteToggleAsync(group.Nodes, action);
            if (success)
                group.CurrentEnabled = enabled;

            return (success, enabled);
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Updates the Python executable used for toggle/list operations.
    /// Call this after the Python resolver has selected a compatible interpreter.
    /// </summary>
    public void SetPythonExecutable(string pythonPath, string scriptPath) =>
        _apiClient.SetPythonExecutable(pythonPath, scriptPath);

    /// <summary>
    /// Queries DaVinci Resolve for all currently visible nodes across all graph levels.
    /// Returns an empty list if Resolve is not running or accessible.
    /// </summary>
    public Task<List<AvailableNode>> GetAvailableNodesAsync() =>
        _apiClient.GetAvailableNodesAsync();

    /// <summary>
    /// Execute an explicit "on"/"off"/toggle action for the group (used by Test button via API).
    /// For Select groups the action param is ignored and node navigation is performed instead.
    /// </summary>
    public async Task<(bool Success, bool? Enabled)> ExecuteByIdAsync(string id, string action)
    {
        var group = GetById(id);
        if (group is null) return (false, null);
        if (!group.Nodes.Any()) return (false, null);

        if (group.ActionType == NodeActionType.Select)
        {
            var (ok, _) = await ExecuteSelectByIdAsync(id);
            return (ok, null);
        }

        var sem = GetGroupLock(id);
        if (!sem.Wait(0))
        {
            _logger.Debug("NodeToggle: '{Name}' is busy — dropping request", group.Name);
            return (false, null);
        }
        try
        {
            var (success, enabled) = await _apiClient.ExecuteToggleAsync(group.Nodes, action);
            if (success)
                group.CurrentEnabled = enabled;
            return (success, enabled);
        }
        finally
        {
            sem.Release();
        }
    }

    // ── Config file I/O ───────────────────────────────────────────────────────

    private void LoadConfig()
    {
        if (!File.Exists(_configPath))
        {
            _config = new NodeToggleConfigFile();
            _logger.Debug("NodeToggle: no config file at {Path} — starting empty", _configPath);
            return;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            _config = JsonSerializer.Deserialize<NodeToggleConfigFile>(json, JsonOptions)
                      ?? new NodeToggleConfigFile();
            _logger.Information("NodeToggle: loaded {Count} group(s) from {Path}",
                _config.Groups.Count, _configPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "NodeToggle: failed to load config from {Path}", _configPath);
            _config = new NodeToggleConfigFile();
        }
    }

    private void WriteConfigFile()
    {
        try
        {
            _fileLock.Wait();
            _selfWriting = true;
            var json = JsonSerializer.Serialize(_config, JsonOptions);
            var dir = Path.GetDirectoryName(_configPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "NodeToggle: failed to write config to {Path}", _configPath);
        }
        finally
        {
            _fileLock.Release();
            // Clear the flag after a short delay so the watcher event (which fires
            // asynchronously) is still suppressed, but future external edits are picked up.
            Task.Delay(600).ContinueWith(_ => _selfWriting = false);
        }
    }

    // ── FileSystemWatcher (live config reload) ────────────────────────────────

    private void StartFileWatcher()
    {
        var dir = Path.GetDirectoryName(_configPath);
        var file = Path.GetFileName(_configPath);
        if (dir is null || !Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: reset timer on each rapid event, reload after 500 ms of quiet
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Timers.Timer(500) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) =>
        {
            if (_selfWriting)
            {
                _logger.Debug("NodeToggle: ignoring file-watcher event for self-written config");
                return;
            }
            _logger.Information("NodeToggle: config file changed externally, reloading...");
            LoadConfig();
            ConfigChanged?.Invoke(GetAll());
        };
        _debounceTimer.Start();
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
        _fileLock.Dispose();
        foreach (var sem in _groupLocks.Values) sem.Dispose();
        _groupLocks.Clear();
    }
}
