using DaVinciTimeTracker.Core.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DaVinciTimeTracker.Data;

public class TimeTrackerDbContextFactory : IDesignTimeDbContextFactory<TimeTrackerDbContext>
{
    public TimeTrackerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TimeTrackerDbContext>();
        optionsBuilder.UseSqlite(AppPaths.DatabaseConnectionString);

        return new TimeTrackerDbContext(optionsBuilder.Options);
    }
}
