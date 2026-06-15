using Serilog;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DaVinciTimeTracker.Core.NodeToggle;

/// <summary>
/// Thin protocol layer over PythonDaemon.
/// Knows the DaVinci node-toggle command format; delegates all process
/// management (start, restart, health) to PythonDaemon.
/// </summary>
public sealed class NodeToggleApiClient : IDisposable
{
    private readonly PythonDaemon _daemon;
    private readonly ILogger _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public NodeToggleApiClient(ILogger logger)
    {
        _daemon = new PythonDaemon(logger);
        _logger = logger;
    }

    public void SetPythonExecutable(string pythonPath, string scriptPath) =>
        _daemon.Configure(pythonPath, scriptPath);

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<List<AvailableNode>> GetAvailableNodesAsync()
    {
        _logger.Information("NodeToggle: requesting node list from DaVinci");
        var cmd = JsonSerializer.Serialize(new { action = "list" });
        var resp = await _daemon.SendAsync(cmd);

        if (resp?["status"]?.GetValue<string>() == "ok")
        {
            var nodes = resp["nodes"]?.Deserialize<List<AvailableNode>>(JsonOpts) ?? [];
            _logger.Information("NodeToggle: received {Count} nodes", nodes.Count);
            return nodes;
        }

        _logger.Warning("NodeToggle list failed: {Msg}", resp?["message"]?.GetValue<string>());
        return [];
    }

    public async Task<(bool Success, bool? Enabled)> ExecuteToggleAsync(
        IEnumerable<NodeTarget> nodes, string action)
    {
        var nodeList = nodes.ToList();
        _logger.Information("NodeToggle: {Action} × {Count} node(s)", action, nodeList.Count);

        var cmd = JsonSerializer.Serialize(new
        {
            action,
            nodes = nodeList.Select(n => new
            {
                nodeId = n.NodeId,
                title  = n.Title,
                level  = n.Level.ToString()
            })
        });

        var resp = await _daemon.SendAsync(cmd);

        if (resp?["status"]?.GetValue<string>() == "ok")
        {
            var enabled = resp["enabled"]?.GetValue<bool>();
            _logger.Information("NodeToggle: {Action} → {State}",
                action, enabled == true ? "enabled" : "disabled");
            return (true, enabled);
        }

        var msg = resp?["message"]?.GetValue<string>() ?? "(no response)";
        _logger.Warning("NodeToggle: {Action} failed — {Msg}", action, msg);
        LogActionableHint(msg);
        return (false, null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void LogActionableHint(string msg)
    {
        if (msg.Contains("resolve_not_running"))
            _logger.Warning("  → DaVinci Resolve is not running");
        else if (msg.Contains("no_project"))
            _logger.Warning("  → No project open in DaVinci Resolve");
        else if (msg.Contains("not_found") || msg.Contains("all_nodes_failed"))
            _logger.Warning("  → Node(s) not found — check IDs/titles in config");
        else if (msg.Contains("modules_not_found"))
            _logger.Warning("  → DaVinci Resolve scripting modules not found");
        else if (msg.Contains("fusionscript"))
            _logger.Warning("  → fusionscript IPC failure — check Python compatibility");
    }

    public void Dispose() => _daemon.Dispose();
}
