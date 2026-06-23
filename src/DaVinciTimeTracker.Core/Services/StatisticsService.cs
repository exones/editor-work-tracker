using DaVinciTimeTracker.Core.Models;

namespace DaVinciTimeTracker.Core.Services;

public class StatisticsService
{
    public List<ProjectStatistics> CalculateStatistics(
        List<ProjectSession> sessions,
        List<ActivityEntry> activityEntries,
        string? currentProjectName,
        string? currentUserName = null)
    {
        // Pre-group activity entries by (ProjectName, UserName) for O(n) lookup
        var entriesByProjectUser = activityEntries
            .GroupBy(a => new { a.ProjectName, a.UserName })
            .ToDictionary(g => g.Key, g => g.ToList());

        var stats = sessions
            .GroupBy(s => new { s.ProjectName, s.UserName })
            .Select(g =>
            {
                // Session-based total is used as the denominator for percentages and
                // for TotalElapsedTime; it reflects true elapsed time including any
                // gaps where page/activity tracking was unavailable.
                var totalSeconds = g.Sum(s => CalculateSeconds(s));

                entriesByProjectUser.TryGetValue(g.Key, out var entries);
                entries ??= [];

                var workSeconds       = entries.Where(a => a.Kind == ActivityKind.User).Sum(a => CalculateActivitySeconds(a));
                var processingSeconds = entries.Where(a => a.Kind == ActivityKind.Processing).Sum(a => CalculateActivitySeconds(a));

                var breakdown = BuildActivityBreakdown(entries, totalSeconds);

                return new ProjectStatistics
                {
                    ProjectName         = g.Key.ProjectName,
                    UserName            = g.Key.UserName,
                    TotalActiveTime     = TimeSpanDto.FromTimeSpan(TimeSpan.FromSeconds(totalSeconds)),
                    TotalElapsedTime    = TimeSpanDto.FromTimeSpan(TimeSpan.FromSeconds(totalSeconds)),
                    TotalWorkTime       = TimeSpanDto.FromTimeSpan(TimeSpan.FromSeconds(workSeconds)),
                    TotalProcessingTime = TimeSpanDto.FromTimeSpan(TimeSpan.FromSeconds(processingSeconds)),
                    LastActivity        = g.Max(s => s.EndTime ?? s.StartTime),
                    SessionCount        = g.Count(),
                    IsCurrentlyTracking = g.Key.ProjectName == currentProjectName
                                       && g.Key.UserName    == currentUserName,
                    ActivityBreakdown   = breakdown
                };
            })
            .OrderByDescending(s => s.LastActivity)
            .ToList();

        return stats;
    }

    private static List<ActivityTimeStat> BuildActivityBreakdown(
        List<ActivityEntry> entries, long totalProjectSeconds)
    {
        if (totalProjectSeconds <= 0) return [];

        return entries
            .GroupBy(a => a.ActivityType)
            .Select(g =>
            {
                var totalSecs = g.Sum(a => CalculateActivitySeconds(a));
                var timelines = g
                    .Where(a => !string.IsNullOrWhiteSpace(a.TimelineName))
                    .Select(a => a.TimelineName!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(t => t)
                    .ToList();

                // All entries in a group share the same Kind (User or Processing),
                // so take the kind from the first entry.
                var kind = g.First().Kind;

                return new ActivityTimeStat
                {
                    ActivityType = g.Key,
                    Kind         = kind,
                    TotalTime    = TimeSpanDto.FromTimeSpan(TimeSpan.FromSeconds(totalSecs)),
                    Percentage   = Math.Round(totalSecs * 100.0 / totalProjectSeconds, 1),
                    Timelines    = timelines
                };
            })
            .Where(a => a.TotalTime.TotalSeconds > 0)
            .OrderByDescending(a => a.TotalTime.TotalSeconds)
            .ToList();
    }

    private static long CalculateSeconds(ProjectSession session)
    {
        var end = session.EndTime ?? session.FlushedEnd ?? DateTime.UtcNow;
        return (long)(end - session.StartTime).TotalSeconds;
    }

    private static long CalculateActivitySeconds(ActivityEntry entry)
    {
        var end = entry.EndTime ?? entry.FlushedEnd ?? DateTime.UtcNow;
        return (long)(end - entry.StartTime).TotalSeconds;
    }
}
