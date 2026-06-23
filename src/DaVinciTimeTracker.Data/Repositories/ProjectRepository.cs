using DaVinciTimeTracker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DaVinciTimeTracker.Data.Repositories;

public class ProjectRepository
{
    private readonly TimeTrackerDbContext _context;

    public ProjectRepository(TimeTrackerDbContext context)
    {
        _context = context;
    }

    /// <summary>Creates a Projects row for the given project if one does not already exist.</summary>
    public async Task UpsertAsync(string projectName)
    {
        var existing = await _context.Projects.FirstOrDefaultAsync(p => p.ProjectName == projectName);
        if (existing is null)
        {
            _context.Projects.Add(new Project
            {
                ProjectName = projectName,
                CreatedAt   = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }
    }

    public async Task SetBilledAmountAsync(string projectName, decimal? amount)
    {
        var project = await _context.Projects.FirstOrDefaultAsync(p => p.ProjectName == projectName);
        if (project is null)
        {
            project = new Project { ProjectName = projectName, CreatedAt = DateTime.UtcNow };
            _context.Projects.Add(project);
        }
        project.BilledAmount = amount;
        await _context.SaveChangesAsync();
    }

    public async Task<List<Project>> GetAllAsync()
    {
        return await _context.Projects.ToListAsync();
    }
}
