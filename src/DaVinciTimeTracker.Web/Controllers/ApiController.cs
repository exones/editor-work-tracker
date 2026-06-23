using DaVinciTimeTracker.Core.NodeToggle;
using DaVinciTimeTracker.Core.Services;
using DaVinciTimeTracker.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace DaVinciTimeTracker.Web.Controllers;

[ApiController]
[Route("api")]
public class ApiController : ControllerBase
{
    private readonly SessionRepository _repository;
    private readonly StatisticsService _statisticsService;
    private readonly SessionManager    _sessionManager;
    private readonly NodeToggleService _nodeToggleService;
    private readonly TrackingContext   _trackingContext;

    public ApiController(
        SessionRepository repository,
        StatisticsService statisticsService,
        SessionManager sessionManager,
        NodeToggleService nodeToggleService,
        TrackingContext trackingContext)
    {
        _repository      = repository;
        _statisticsService = statisticsService;
        _sessionManager  = sessionManager;
        _nodeToggleService = nodeToggleService;
        _trackingContext  = trackingContext;
    }

    [HttpGet("projects")]
    [Produces("application/json")]
    public async Task<IActionResult> GetProjects()
    {
        Response.Headers.ContentType = "application/json; charset=utf-8";
        var sessions         = await _repository.GetAllSessionsAsync();
        var activityEntries  = await _repository.GetAllActivitiesAsync();
        var currentUserName  = _sessionManager.CurrentUserName;
        var stats = _statisticsService.CalculateStatistics(sessions, activityEntries, _sessionManager.CurrentProjectName, currentUserName);

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
            // "toggle" uses the stateful path (same as hotkey) so state is tracked correctly
            // across Test presses and hotkey presses. GetNodeEnabled is not available in the
            // DaVinci scripting API, so state must be tracked in-process.
            var (success, enabled) = action is "on" or "off"
                ? await _nodeToggleService.ExecuteByIdAsync(id, action)
                : await _nodeToggleService.ExecuteByIdAsync(id);       // stateful toggle

            if (success)
                return Ok(new { success = true, enabled });

            return Ok(new { success = false, message = "Toggle failed — check application logs for details." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
}
