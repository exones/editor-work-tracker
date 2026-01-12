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
    private readonly SessionManager _sessionManager;

    public ApiController(
        SessionRepository repository,
        StatisticsService statisticsService,
        SessionManager sessionManager)
    {
        _repository = repository;
        _statisticsService = statisticsService;
        _sessionManager = sessionManager;
    }

    [HttpGet("projects")]
    [Produces("application/json")]
    public async Task<IActionResult> GetProjects()
    {
        Response.Headers.ContentType = "application/json; charset=utf-8";
        var sessions = await _repository.GetAllSessionsAsync();
        var currentUserName = _sessionManager.CurrentUserName;
        var stats = _statisticsService.CalculateStatistics(sessions, _sessionManager.CurrentProjectName, currentUserName);

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
        return Ok(new
        {
            ProjectName = _sessionManager.CurrentProjectName,
            UserName = _sessionManager.CurrentUserName,
            State = _sessionManager.CurrentState.ToString(),
            IsTracking = _sessionManager.CurrentState != Core.Models.TrackingState.NotTracking
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
}
