using DaVinciTimeTracker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DaVinciTimeTracker.Data;

public class TimeTrackerDbContext : DbContext
{
    public DbSet<ProjectSession> ProjectSessions { get; set; }
    public DbSet<ActivityEntry> ActivityEntries { get; set; }

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
            entity.Property(e => e.UserName).IsRequired().HasDefaultValue("Unknown");
            entity.Property(e => e.StartTime).IsRequired();

            entity.HasIndex(e => e.UserName);
        });

        modelBuilder.Entity<ActivityEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProjectName).IsRequired();
            entity.Property(e => e.UserName).IsRequired();
            entity.Property(e => e.ActivityType).IsRequired();
            entity.Property(e => e.Kind).IsRequired()
                  .HasConversion<string>();
            entity.Property(e => e.StartTime).IsRequired();

            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => new { e.ProjectName, e.UserName });
        });
    }
}
