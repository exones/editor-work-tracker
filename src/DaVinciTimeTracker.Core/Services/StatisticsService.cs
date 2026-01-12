using DaVinciTimeTracker.Core.Models;

namespace DaVinciTimeTracker.Core.Services;

public class StatisticsService
{
    public List<ProjectStatistics> CalculateStatistics(
        List<ProjectSession> sessions,
        string? currentProjectName)
    {
        var stats = sessions
            .GroupBy(s => s.ProjectName)
            .Select(g => new ProjectStatistics
            {
                ProjectName = g.Key,
                TotalActiveTime = TimeSpanDto.FromTimeSpan(TimeSpan.FromSeconds(g.Sum(s => CalculateActiveSeconds(s)))),
                TotalElapsedTime = TimeSpanDto.FromTimeSpan(TimeSpan.FromSeconds(g.Sum(s => CalculateElapsedSeconds(s)))),
                LastActivity = g.Max(s => s.EndTime ?? s.StartTime),
                SessionCount = g.Count(),
                IsCurrentlyTracking = g.Key == currentProjectName
            })
            .OrderByDescending(s => s.LastActivity)
            .ToList();

        return stats;
    }

    private long CalculateActiveSeconds(ProjectSession session)
    {
        // Calculate session duration directly from timestamps
        // Use EndTime → FlushedEnd → UtcNow for open sessions (crash recovery)
        var endTime = session.EndTime ?? session.FlushedEnd ?? DateTime.UtcNow;
        var duration = (endTime - session.StartTime).TotalSeconds;

        return (long)duration;
    }

    private long CalculateElapsedSeconds(ProjectSession session)
    {
        // Same as active time (no distinction, all session time is work time)
        return CalculateActiveSeconds(session);
    }
}
