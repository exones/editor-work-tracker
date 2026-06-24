using DaVinciTimeTracker.Core.Diagnostics;
using DaVinciTimeTracker.Core.Models;
using DaVinciTimeTracker.Core.NodeToggle;
using DaVinciTimeTracker.Core.Services;
using DaVinciTimeTracker.Core.Utilities;
using DaVinciTimeTracker.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace DaVinciTimeTracker.Web.Controllers;

[ApiController]
[Route("api")]
public class ApiController : ControllerBase
{
    private readonly SessionRepository        _repository;
    private readonly ProjectRepository        _projectRepository;
    private readonly StatisticsService        _statisticsService;
    private readonly SessionManager           _sessionManager;
    private readonly NodeToggleService        _nodeToggleService;
    private readonly TrackingContext          _trackingContext;
    private readonly UserSettingsService      _userSettingsService;
    private readonly ResolveDiagnosticsService _diagnosticsService;

    public ApiController(
        SessionRepository repository,
        ProjectRepository projectRepository,
        StatisticsService statisticsService,
        SessionManager sessionManager,
        NodeToggleService nodeToggleService,
        TrackingContext trackingContext,
        UserSettingsService userSettingsService,
        ResolveDiagnosticsService diagnosticsService)
    {
        _repository          = repository;
        _projectRepository   = projectRepository;
        _statisticsService   = statisticsService;
        _sessionManager      = sessionManager;
        _nodeToggleService   = nodeToggleService;
        _trackingContext     = trackingContext;
        _userSettingsService = userSettingsService;
        _diagnosticsService  = diagnosticsService;
    }

    [HttpGet("projects")]
    [Produces("application/json")]
    public async Task<IActionResult> GetProjects()
    {
        Response.Headers.ContentType = "application/json; charset=utf-8";
        var sessions         = await _repository.GetAllSessionsAsync();
        var activityEntries  = await _repository.GetAllActivitiesAsync();
        var projects         = await _projectRepository.GetAllAsync();
        var currentUserName  = _sessionManager.CurrentUserName;
        var billing          = _userSettingsService.Current.Billing;
        var stats = _statisticsService.CalculateStatistics(
            sessions, activityEntries, _sessionManager.CurrentProjectName,
            currentUserName, billing, projects);

        // Add current state information to the currently tracking project for current user
        foreach (var stat in stats)
        {
            if (stat.IsCurrentlyTracking && stat.UserName == currentUserName)
            {
                stat.CurrentState = _sessionManager.CurrentState.ToString();
            }
        }

        return Ok(stats);
    }

    [HttpGet("projects/{name}/sessions")]
    public async Task<IActionResult> GetProjectSessions(string name)
    {
        var sessions = await _repository.GetSessionsByProjectAsync(name);
        return Ok(sessions);
    }

    [HttpGet("current")]
    [Produces("application/json")]
    public IActionResult GetCurrentStatus()
    {
        Response.Headers.ContentType = "application/json; charset=utf-8";
        var snap = _trackingContext.Snapshot();
        return Ok(new
        {
            ProjectName      = _sessionManager.CurrentProjectName,
            UserName         = _sessionManager.CurrentUserName,
            State            = _sessionManager.CurrentState.ToString(),
            IsTracking       = _sessionManager.CurrentState != Core.Models.TrackingState.NotTracking,
            // Richer live state from TrackingContext
            IsResolveRunning = snap.IsResolveRunning,
            // Live project from context (non-null whenever a project is open, regardless of tracking state)
            LiveProject      = snap.Project,
            Page             = snap.Page,
            Timeline         = snap.Timeline,
            IsRendering      = snap.IsRendering,
            IsInFocus        = snap.IsInFocus,
            IsUserActive     = snap.IsUserActive,
            LastActivityChange = snap.LastActivityChange,
            LastFocusChange    = snap.LastFocusChange
        });
    }

    [HttpDelete("projects/{name}")]
    public async Task<IActionResult> DeleteProject(string name)
    {
        try
        {
            var deletedCount = await _repository.DeleteProjectSessionsAsync(name);
            return Ok(new { success = true, deletedSessions = deletedCount, message = $"Deleted {deletedCount} session(s)" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // ── Settings endpoints ────────────────────────────────────────────────────

    [HttpGet("settings")]
    [Produces("application/json")]
    public IActionResult GetSettings()
    {
        return Ok(_userSettingsService.Current);
    }

    [HttpPost("settings")]
    public IActionResult SaveSettings([FromBody] UserSettings settings)
    {
        try
        {
            _userSettingsService.Save(settings);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // ── Project billing endpoints ─────────────────────────────────────────────

    [HttpPost("projects/{name}/billing")]
    public async Task<IActionResult> SetProjectBilling(string name, [FromBody] SetBillingRequest body)
    {
        try
        {
            await _projectRepository.SetBilledAmountAsync(name, body.Amount);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("projects/{name}/billing")]
    public async Task<IActionResult> ClearProjectBilling(string name)
    {
        try
        {
            await _projectRepository.SetBilledAmountAsync(name, null);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // ── Node toggle endpoints ─────────────────────────────────────────────────

    /// <summary>
    /// Returns all nodes currently visible in DaVinci Resolve (active timeline / clip).
    /// Used by the dashboard to populate node picker dropdowns.
    /// Returns an empty array if Resolve is not running.
    /// </summary>
    [HttpGet("node-toggles/available-nodes")]
    public async Task<IActionResult> GetAvailableNodes()
    {
        var nodes = await _nodeToggleService.GetAvailableNodesAsync();
        return Ok(nodes);
    }

    [HttpGet("node-toggles")]
    public IActionResult GetNodeToggles()
    {
        // Project to include runtime CurrentEnabled state (excluded from JSON config by [JsonIgnore])
        var groups = _nodeToggleService.GetAllWithState();
        return Ok(groups);
    }

    [HttpPost("node-toggles")]
    public IActionResult CreateNodeToggle([FromBody] ToggleGroup group)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(group.Id))
                group.Id = Guid.NewGuid().ToString("N")[..8];

            var groups = _nodeToggleService.GetAll();
            if (groups.Any(g => g.Id == group.Id))
                return Conflict(new { success = false, message = $"Group id '{group.Id}' already exists." });

            groups.Add(group);
            _nodeToggleService.Save(groups);
            return Ok(new { success = true, group });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpPut("node-toggles/{id}")]
    public IActionResult UpdateNodeToggle(string id, [FromBody] ToggleGroup group)
    {
        try
        {
            group.Id = id;
            var groups = _nodeToggleService.GetAll();
            var idx = groups.FindIndex(g => g.Id == id);
            if (idx < 0)
                return NotFound(new { success = false, message = $"Group '{id}' not found." });

            groups[idx] = group;
            _nodeToggleService.Save(groups);
            return Ok(new { success = true, group });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("node-toggles/{id}")]
    public IActionResult DeleteNodeToggle(string id)
    {
        try
        {
            var groups = _nodeToggleService.GetAll();
            var removed = groups.RemoveAll(g => g.Id == id);
            if (removed == 0)
                return NotFound(new { success = false, message = $"Group '{id}' not found." });

            _nodeToggleService.Save(groups);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>Resets the in-process state assumption without touching DaVinci.</summary>
    [HttpPost("node-toggles/{id}/reset-state")]
    public IActionResult ResetNodeToggleState(string id, [FromQuery] bool assumeEnabled = true)
    {
        var group = _nodeToggleService.GetById(id);
        if (group is null) return NotFound(new { success = false });
        group.CurrentEnabled = assumeEnabled;
        return Ok(new { success = true, currentEnabled = group.CurrentEnabled });
    }

    [HttpPost("node-toggles/{id}/execute")]
    public async Task<IActionResult> ExecuteNodeToggle(string id, [FromQuery] string action = "toggle")
    {
        try
        {
            var group = _nodeToggleService.GetById(id);

            // Select action — navigate to the target node
            if (group?.ActionType == Core.NodeToggle.NodeActionType.Select)
            {
                var (selectOk, nodeIndex) = await _nodeToggleService.ExecuteSelectByIdAsync(id);
                if (selectOk)
                    return Ok(new { success = true, nodeIndex });
                return Ok(new { success = false, message = "Select failed — check application logs for details." });
            }

            // Toggle action — stateful on/off (same as hotkey path)
            var (success, enabled) = action is "on" or "off"
                ? await _nodeToggleService.ExecuteByIdAsync(id, action)
                : await _nodeToggleService.ExecuteByIdAsync(id);

            if (success)
                return Ok(new { success = true, enabled });

            return Ok(new { success = false, message = "Toggle failed — check application logs for details." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    /// <summary>Lightweight health pill for the dashboard header.</summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        var results = await _diagnosticsService.RunAllAsync();
        var summary = _diagnosticsService.GetHealthSummary(results);
        return Ok(summary);
    }

    /// <summary>Full structured diagnostic check results for the Troubleshooter tab.</summary>
    [HttpGet("diagnostics")]
    public async Task<IActionResult> GetDiagnostics()
    {
        var results = await _diagnosticsService.RunAllAsync();
        return Ok(results);
    }

    /// <summary>Plain-text exportable report (copy to clipboard or save to file).</summary>
    [HttpGet("diagnostics/report")]
    [Produces("text/plain")]
    public async Task<IActionResult> GetDiagnosticReport()
    {
        var results = await _diagnosticsService.RunAllAsync();
        var report  = DiagnosticReportBuilder.Build(results);
        return Content(report, "text/plain");
    }

    /// <summary>Apply a safe auto-fix by ID (e.g. pin DAVINCI_TRACKER_PYTHON).</summary>
    [HttpPost("diagnostics/apply-fix/{autoFixId}")]
    public IActionResult ApplyFix(string autoFixId)
    {
        try
        {
            if (autoFixId.StartsWith("pin-python:", StringComparison.OrdinalIgnoreCase))
            {
                var pythonPath = autoFixId["pin-python:".Length..];
                if (!System.IO.File.Exists(pythonPath))
                    return BadRequest(new { success = false, message = $"Python not found at: {pythonPath}" });

                Environment.SetEnvironmentVariable("DAVINCI_TRACKER_PYTHON", pythonPath,
                    EnvironmentVariableTarget.User);
                return Ok(new { success = true, message = $"Pinned DAVINCI_TRACKER_PYTHON={pythonPath} (restart app to apply)" });
            }

            return BadRequest(new { success = false, message = $"Unknown fix id: {autoFixId}" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
}

public record SetBillingRequest(decimal? Amount);
