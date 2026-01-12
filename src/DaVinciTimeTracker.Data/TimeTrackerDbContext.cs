using DaVinciTimeTracker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DaVinciTimeTracker.Data;

public class TimeTrackerDbContext : DbContext
{
    public DbSet<ProjectSession> ProjectSessions { get; set; }

    public TimeTrackerDbContext(DbContextOptions<TimeTrackerDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProjectName).IsRequired();
            entity.Property(e => e.StartTime).IsRequired();
        });
    }
}
