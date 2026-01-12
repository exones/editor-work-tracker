using DaVinciTimeTracker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DaVinciTimeTracker.Data.Repositories;

public class SessionRepository
{
    private readonly TimeTrackerDbContext _context;

    public SessionRepository(TimeTrackerDbContext context)
    {
        _context = context;
    }

    public async Task SaveSessionAsync(ProjectSession session)
    {
        var existingSession = await _context.ProjectSessions
            .FirstOrDefaultAsync(s => s.Id == session.Id);

        if (existingSession == null)
        {
            // New session - add it
            _context.ProjectSessions.Add(session);
        }
        else
        {
            // Existing session - update it
            _context.Entry(existingSession).CurrentValues.SetValues(session);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<List<ProjectSession>> GetAllSessionsAsync()
    {
        return await _context.ProjectSessions
            .ToListAsync();
    }

    public async Task<List<ProjectSession>> GetSessionsByProjectAsync(string projectName)
    {
        return await _context.ProjectSessions
            .Where(s => s.ProjectName == projectName)
            .OrderByDescending(s => s.StartTime)
            .ToListAsync();
    }

    public async Task<ProjectSession?> GetSessionByIdAsync(Guid id)
    {
        return await _context.ProjectSessions
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<int> DeleteProjectSessionsAsync(string projectName)
    {
        var sessionsToDelete = await _context.ProjectSessions
            .Where(s => s.ProjectName == projectName)
            .ToListAsync();

        var deleteCount = sessionsToDelete.Count;
        _context.ProjectSessions.RemoveRange(sessionsToDelete);
        await _context.SaveChangesAsync();

        return deleteCount;
    }
}
