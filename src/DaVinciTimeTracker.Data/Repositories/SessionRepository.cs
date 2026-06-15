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

        // Also delete associated page time entries
        var pageEntries = await _context.PageTimeEntries
            .Where(p => p.ProjectName == projectName)
            .ToListAsync();
        _context.PageTimeEntries.RemoveRange(pageEntries);

        await _context.SaveChangesAsync();
        return sessionsToDelete.Count;
    }

    // ── PageTimeEntry ─────────────────────────────────────────────────────────

    public async Task SavePageEntryAsync(PageTimeEntry entry)
    {
        var existing = await _context.PageTimeEntries.FirstOrDefaultAsync(p => p.Id == entry.Id);
        if (existing == null)
            _context.PageTimeEntries.Add(entry);
        else
            _context.Entry(existing).CurrentValues.SetValues(entry);

        await _context.SaveChangesAsync();
    }

    public async Task<List<PageTimeEntry>> GetPageEntriesByProjectAsync(string projectName)
    {
        return await _context.PageTimeEntries
            .Where(p => p.ProjectName == projectName)
            .ToListAsync();
    }

    public async Task<List<PageTimeEntry>> GetAllPageEntriesAsync()
    {
        return await _context.PageTimeEntries.ToListAsync();
    }

    /// <summary>
    /// Crash recovery: close any open page entries for the given user.
    /// Synchronous — called during startup before async infrastructure is running.
    /// </summary>
    public void FinaliseOpenPageEntries(string userName, TimeTrackerDbContext db)
    {
        var open = db.PageTimeEntries
            .Where(p => p.EndTime == null && p.UserName == userName)
            .ToList();

        foreach (var entry in open)
            entry.EndTime = entry.FlushedEnd ?? DateTime.UtcNow;

        if (open.Count > 0)
            db.SaveChanges();
    }
}
