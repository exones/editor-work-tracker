using Serilog;
using System.Text.Json;
using System.Text.Json.Nodes;
using DaVinciTimeTracker.Core.NodeToggle;

namespace DaVinciTimeTracker.Core.Resolve;

/// <summary>
/// Thin protocol layer over PythonDaemon.
/// Single entry-point for all DaVinci Resolve scripting operations: node toggle/select,
/// background queries (list, diagnose, ping), and the lightweight status poll used by
/// DaVinciResolveMonitor every 2 seconds.
///
/// Three daemons are maintained by concern:
///   _toggleDaemon    — on/off/toggle: hotkey-triggered, must respond immediately
///   _keystrokeDaemon — select + any future keystroke injection: may hold for hundreds of ms
///   _backgroundDaemon — status, diagnose, list, ping: background queries that can wait
/// </summary>
public sealed class ResolveScriptingClient : IDisposable
{
    private readonly PythonDaemon _toggleDaemon;
    private readonly PythonDaemon _keystrokeDaemon;
    private readonly PythonDaemon _backgroundDaemon;
    private readonly ILogger _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public ResolveScriptingClient(ILogger logger)
    {
        _toggleDaemon     = new PythonDaemon(logger);
        _keystrokeDaemon  = new PythonDaemon(logger);
        _backgroundDaemon = new PythonDaemon(logger);
        _logger           = logger;
    }

    public void SetPythonExecutable(string pythonPath, string scriptPath)
    {
        _toggleDaemon.Configure(pythonPath, scriptPath);
        _keystrokeDaemon.Configure(pythonPath, scriptPath);
        _backgroundDaemon.Configure(pythonPath, scriptPath);
    }

    /// <summary>True when the background daemon is connected and ready to respond.</summary>
    public bool IsBackgroundDaemonReady => _backgroundDaemon.IsReady;

    // ── Status poll (replaces per-call process spawn) ─────────────────────────

    /// <summary>
    /// Lightweight poll: returns current project/page/timeline/rendering from the
    /// background daemon. Called every 2 s by DaVinciResolveMonitor.
    /// Returns ResolveStatus.Empty on daemon error or when no project is open.
    /// Also returns whether the daemon responded at all (out bridgeOk) so the
    /// monitor can update TrackingContext.ScriptingBridgeOk.
    /// </summary>
    public async Task<(ResolveStatus Status, bool BridgeOk)> GetCurrentStatusAsync()
    {
        var cmd  = JsonSerializer.Serialize(new { action = "status" });
        var resp = await _backgroundDaemon.SendAsync(cmd);

        if (resp is null) return (ResolveStatus.Empty, false);
        if (resp["status"]?.GetValue<string>() != "ok") return (ResolveStatus.Empty, true);

        var project   = resp["project"]?.GetValue<string>();
        var page      = resp["page"]?.GetValue<string>();
        var timeline  = resp["timeline"]?.GetValue<string>();
        var rendering = resp["rendering"]?.GetValue<bool>() ?? false;

        if (string.IsNullOrEmpty(page))     page     = null;
        if (string.IsNullOrEmpty(timeline)) timeline = null;

        if (string.IsNullOrEmpty(project) ||
            project.Equals("Untitled Project", StringComparison.OrdinalIgnoreCase))
            return (ResolveStatus.Empty, true);

        return (new ResolveStatus(project, page, timeline, rendering), true);
    }

    // ── Node commands ─────────────────────────────────────────────────────────

    public async Task<List<AvailableNode>> GetAvailableNodesAsync()
    {
        _logger.Information("NodeToggle: requesting node list from DaVinci");
        var cmd  = JsonSerializer.Serialize(new { action = "list" });
        var resp = await _backgroundDaemon.SendAsync(cmd);

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

        var resp = await _toggleDaemon.SendAsync(cmd);

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

    public async Task<(bool Success, int? NodeIndex)> ExecuteSelectAsync(
        IEnumerable<NodeTarget> nodes, string appendShortcut, string nextShortcut)
    {
        var nodeList = nodes.ToList();
        _logger.Information("NodeSelect: navigating to node in group ({Count} node(s))", nodeList.Count);

        if (!TryParseShortcutToVkDict(appendShortcut, out var appendKey))
        {
            _logger.Warning("NodeSelect: cannot parse appendNodeShortcut '{S}' — check Settings", appendShortcut);
            return (false, null);
        }
        _logger.Information("NodeSelect: appendShortcut '{S}' → {Key}", appendShortcut, JsonSerializer.Serialize(appendKey));

        if (!TryParseShortcutToVkDict(nextShortcut, out var nextKey))
        {
            _logger.Warning("NodeSelect: cannot parse nextNodeShortcut '{S}' — check Settings", nextShortcut);
            return (false, null);
        }
        _logger.Information("NodeSelect: nextShortcut '{S}' → {Key}", nextShortcut, JsonSerializer.Serialize(nextKey));

        var cmd = JsonSerializer.Serialize(new
        {
            action = "select",
            nodes  = nodeList.Select(n => new { nodeId = n.NodeId, title = n.Title, level = n.Level.ToString() }),
            appendNodeKey = appendKey,
            nextNodeKey   = nextKey
        });

        _logger.Information("NodeSelect: sending command: {Cmd}", cmd);
        var resp = await _keystrokeDaemon.SendAsync(cmd);
        _logger.Information("NodeSelect: daemon response: {Resp}", resp?.ToJsonString());

        if (resp?["status"]?.GetValue<string>() == "ok")
        {
            var nodeIndex  = resp["nodeIndex"]?.GetValue<int>();
            var totalNodes = resp["totalNodes"]?.GetValue<int>();
            _logger.Information("NodeSelect: success — navigated to node {Index}/{Total}", nodeIndex, totalNodes);
            return (true, nodeIndex);
        }

        var msg = resp?["message"]?.GetValue<string>() ?? "(no response)";
        _logger.Warning("NodeSelect: failed — {Msg}", msg);
        return (false, null);
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the daemon's diagnose action and returns the raw JSON node,
    /// or null if the daemon is not connected or returns an error.
    /// Used by ResolveDiagnosticsService (on-demand only).
    /// </summary>
    public async Task<JsonNode?> SendDiagnoseAsync()
    {
        var cmd = JsonSerializer.Serialize(new { action = "diagnose" });
        return await _backgroundDaemon.SendAsync(cmd);
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

    /// <summary>
    /// Parses a shortcut string like "Alt+S" or "Alt+Shift+Oem7" into a VK payload
    /// for the Python keybd_event handler. Works without Windows Forms dependency.
    /// </summary>
    private static bool TryParseShortcutToVkDict(string shortcut, out object? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(shortcut)) return false;

        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var keyPart  = parts[^1];
        var modParts = parts[..^1];

        if (!s_keyNameToVk.TryGetValue(keyPart.ToUpperInvariant(), out var vk) || vk == 0)
            return false;

        var mods = modParts
            .Select(m => s_modNameToVk.TryGetValue(m.ToUpperInvariant(), out var mv) ? mv : (int?)null)
            .Where(m => m.HasValue)
            .Select(m => m!.Value)
            .Distinct()
            .ToList();

        result = mods.Count switch
        {
            0 => new { vk, mod = (int?)null, mod2 = (int?)null },
            1 => new { vk, mod = (int?)mods[0], mod2 = (int?)null },
            _ => new { vk, mod = (int?)mods[0], mod2 = (int?)mods[1] }
        };

        return true;
    }

    private static readonly Dictionary<string, int> s_modNameToVk = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ALT"]     = 0x12,
        ["SHIFT"]   = 0x10,
        ["CTRL"]    = 0x11,
        ["CONTROL"] = 0x11,
        ["WIN"]     = 0x5B,
        ["WINDOWS"] = 0x5B,
    };

    private static readonly Dictionary<string, int> s_keyNameToVk = BuildKeyMap();

    private static Dictionary<string, int> BuildKeyMap()
    {
        var m = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var c = 'A'; c <= 'Z'; c++)
            m[c.ToString()] = 0x41 + (c - 'A');

        for (var d = '0'; d <= '9'; d++)
            m[d.ToString()] = 0x30 + (d - '0');

        for (var i = 1; i <= 24; i++)
            m[$"F{i}"] = 0x6F + i;

        m["OEM1"]      = 0xBA;
        m["OEMPLUS"]   = 0xBB;
        m["OEMCOMMA"]  = 0xBC;
        m["OEMMINUS"]  = 0xBD;
        m["OEMPERIOD"] = 0xBE;
        m["OEM2"]      = 0xBF;
        m["OEM3"]      = 0xC0;
        m["OEM4"]      = 0xDB;
        m["OEM5"]      = 0xDC;
        m["OEM6"]      = 0xDD;
        m["OEM7"]      = 0xDE;

        m["BACK"]   = 0x08;
        m["TAB"]    = 0x09;
        m["RETURN"] = 0x0D;
        m["ESCAPE"] = 0x1B;
        m["SPACE"]  = 0x20;
        m["LEFT"]   = 0x25;
        m["UP"]     = 0x26;
        m["RIGHT"]  = 0x27;
        m["DOWN"]   = 0x28;
        m["DELETE"] = 0x2E;
        m["HOME"]   = 0x24;
        m["END"]    = 0x23;
        m["PRIOR"]  = 0x21;
        m["NEXT"]   = 0x22;

        for (var i = 0; i <= 9; i++)
            m[$"NUMPAD{i}"] = 0x60 + i;

        return m;
    }

    public void Dispose()
    {
        _toggleDaemon.Dispose();
        _keystrokeDaemon.Dispose();
        _backgroundDaemon.Dispose();
    }
}

/// <summary>Parsed status from the background daemon — one poll result.</summary>
public record ResolveStatus(
    string? ProjectName,
    string? Page,
    string? Timeline,
    bool    IsRendering)
{
    public static readonly ResolveStatus Empty = new(null, null, null, false);
}
