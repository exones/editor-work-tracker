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

    // ── ProjectSession ────────────────────────────────────────────────────────

    public async Task SaveSessionAsync(ProjectSession session)
    {
        var existing = await _context.ProjectSessions.FirstOrDefaultAsync(s => s.Id == session.Id);
        if (existing == null)
            _context.ProjectSessions.Add(session);
        else
            _context.Entry(existing).CurrentValues.SetValues(session);

        await _context.SaveChangesAsync();
    }

    public async Task<List<ProjectSession>> GetAllSessionsAsync()
    {
        return await _context.ProjectSessions.ToListAsync();
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
        return await _context.ProjectSessions.FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<int> DeleteProjectSessionsAsync(string projectName)
    {
        var sessionsToDelete = await _context.ProjectSessions
            .Where(s => s.ProjectName == projectName)
            .ToListAsync();

        _context.ProjectSessions.RemoveRange(sessionsToDelete);

        // Also delete associated activity entries
        var activityEntries = await _context.ActivityEntries
            .Where(a => a.ProjectName == projectName)
            .ToListAsync();
        _context.ActivityEntries.RemoveRange(activityEntries);

        // Also delete the Projects metadata row
        var project = await _context.Projects.FirstOrDefaultAsync(p => p.ProjectName == projectName);
        if (project != null) _context.Projects.Remove(project);

        await _context.SaveChangesAsync();
        return sessionsToDelete.Count;
    }

    // ── ActivityEntry ─────────────────────────────────────────────────────────

    public async Task SaveActivityAsync(ActivityEntry entry)
    {
        var existing = await _context.ActivityEntries.FirstOrDefaultAsync(a => a.Id == entry.Id);
        if (existing == null)
            _context.ActivityEntries.Add(entry);
        else
            _context.Entry(existing).CurrentValues.SetValues(entry);

        await _context.SaveChangesAsync();
    }

    public async Task<List<ActivityEntry>> GetActivitiesByProjectAsync(string projectName)
    {
        return await _context.ActivityEntries
            .Where(a => a.ProjectName == projectName)
            .ToListAsync();
    }

    public async Task<List<ActivityEntry>> GetAllActivitiesAsync()
    {
        return await _context.ActivityEntries.ToListAsync();
    }

    /// <summary>
    /// Crash recovery: close any open activity entries for the given user.
    /// Synchronous — called during startup before async infrastructure is running.
    /// </summary>
    public void FinaliseOpenActivities(string userName, TimeTrackerDbContext db)
    {
        var open = db.ActivityEntries
            .Where(a => a.EndTime == null && a.UserName == userName)
            .ToList();

        foreach (var entry in open)
            entry.EndTime = entry.FlushedEnd ?? DateTime.UtcNow;

        if (open.Count > 0)
            db.SaveChanges();
    }
}
