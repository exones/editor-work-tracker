using Serilog;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DaVinciTimeTracker.Core.NodeToggle;

public class NodeToggleApiClient
{
    private string _pythonPath;
    private string _scriptPath;
    private readonly ILogger _logger;

    public NodeToggleApiClient(string pythonPath, string scriptPath, ILogger logger)
    {
        _pythonPath = pythonPath;
        _scriptPath = scriptPath;
        _logger = logger;
    }

    /// <summary>Updates the Python executable path after it has been resolved post-startup.</summary>
    public void SetPythonExecutable(string pythonPath, string scriptPath)
    {
        _pythonPath = pythonPath;
        _scriptPath = scriptPath;
        _logger.Information("NodeToggle: Python executable set → {Python}", pythonPath);
    }

    /// <summary>
    /// Executes a toggle for the given nodes.
    /// Returns (success, enabled) where enabled = true if nodes are now enabled.
    /// Returns (false, null) on failure.
    /// </summary>
    public async Task<(bool Success, bool? Enabled)> ExecuteToggleAsync(
        IEnumerable<NodeTarget> nodes,
        string action = "toggle")
    {
        var payload = new
        {
            nodes = nodes.Select(n => new
            {
                nodeId = n.NodeId,
                title = n.Title,
                level = n.Level.ToString()
            }),
            state = action
        };

        var json = JsonSerializer.Serialize(payload);
        _logger.Information("NodeToggle: spawning Python — action={Action}, nodes={Count}, python={Python}",
            action, payload.nodes.Count(), _pythonPath);

        if (string.IsNullOrEmpty(_pythonPath))
        {
            _logger.Warning("NodeToggle: Python path is not set — call SetPythonExecutable first");
            return (false, null);
        }
        if (!File.Exists(_scriptPath))
        {
            _logger.Warning("NodeToggle: script not found at {Script}", _scriptPath);
            return (false, null);
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonPath,
                    Arguments = $"\"{_scriptPath}\" {EscapeArgument(json)}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            stdout = stdout.Trim();

            // Always log stderr — it contains [node_toggle_api] diagnostic lines
            if (!string.IsNullOrWhiteSpace(stderr))
                _logger.Information("NodeToggle stderr:\n{Stderr}", stderr.Trim());

            if (process.ExitCode == 0 && stdout.StartsWith("OK:"))
            {
                var enabled = stdout == "OK:enabled";
                _logger.Information("NodeToggle: {Action} → {State}", action, stdout);
                return (true, enabled);
            }

            _logger.Warning("NodeToggle: FAILED (exit={Code}) stdout={Stdout}", process.ExitCode, stdout);
            LogActionableHint(stdout);
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "NodeToggleApiClient: exception executing toggle");
            return (false, null);
        }
    }

    /// <summary>
    /// Queries DaVinci Resolve for all nodes currently visible across all graph levels
    /// (Timeline, Clip, PreClip, PostClip) for the active clip/timeline.
    /// Returns an empty list if Resolve is not running or no project is open.
    /// </summary>
    public async Task<List<AvailableNode>> GetAvailableNodesAsync()
    {
        _logger.Information("NodeToggle: listing available nodes — python={Python}, script={Script}",
            _pythonPath, _scriptPath);

        if (string.IsNullOrEmpty(_pythonPath))
        {
            _logger.Warning("NodeToggle: Python path is not set — cannot list nodes");
            return [];
        }
        if (!File.Exists(_scriptPath))
        {
            _logger.Warning("NodeToggle: script not found at {Script}", _scriptPath);
            return [];
        }

        var payload = JsonSerializer.Serialize(new { nodes = Array.Empty<object>(), state = "list" });
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonPath,
                    Arguments = $"\"{_scriptPath}\" {EscapeArgument(payload)}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            stdout = stdout.Trim();

            if (!string.IsNullOrWhiteSpace(stderr))
                _logger.Information("NodeToggle list stderr:\n{Stderr}", stderr.Trim());

            if (stdout.StartsWith("NODES:"))
            {
                var json = stdout["NODES:".Length..];
                var nodes = JsonSerializer.Deserialize<List<AvailableNode>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? [];
                _logger.Information("NodeToggle: listed {Count} nodes", nodes.Count);
                return nodes;
            }

            _logger.Warning("NodeToggle list: unexpected output (exit={Code}): {Out}", process.ExitCode, stdout);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "NodeToggleApiClient: exception listing available nodes");
        }

        return [];
    }

    private void LogActionableHint(string stdout)
    {
        if (stdout.Contains("resolve_not_running"))
            _logger.Warning("  → DaVinci Resolve is not running — open Resolve first");
        else if (stdout.Contains("no_project"))
            _logger.Warning("  → No project is open in DaVinci Resolve");
        else if (stdout.Contains("no_timeline"))
            _logger.Warning("  → No timeline is active in DaVinci Resolve");
        else if (stdout.Contains("not_found") || stdout.Contains("all_nodes_failed"))
            _logger.Warning("  → One or more nodes were not found — check node IDs/titles in config");
        else if (stdout.Contains("modules_not_found"))
            _logger.Warning("  → DaVinci Resolve scripting modules not found — is DaVinci Studio installed?");
        else if (stdout.Contains("fusionscript"))
            _logger.Warning("  → fusionscript IPC failure — see Python resolver logs");
    }

    /// <summary>Escapes a string for use as a command-line argument (wraps in quotes, escapes internal quotes).</summary>
    private static string EscapeArgument(string arg)
    {
        // Escape backslashes before quotes, then wrap in double quotes
        arg = arg.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{arg}\"";
    }
}
