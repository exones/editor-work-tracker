namespace DaVinciTimeTracker.Core.Models;

public class ProjectSession
{
    public Guid Id { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;  // Windows username
    public DateTime StartTime { get; set; }        // When session started
    public DateTime? EndTime { get; set; }         // When session ended (null = still tracking)
    public DateTime? FlushedEnd { get; set; }      // Last periodic save (crash recovery)

    // REMOVED: public int TotalActiveSeconds { get; set; }
    // REMOVED: public int TotalElapsedSeconds { get; set; }
    // REMOVED: public List<ActivityPeriod> ActivityPeriods { get; set; }
}
