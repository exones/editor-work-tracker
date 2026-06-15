using DaVinciTimeTracker.Core.Models;

namespace DaVinciTimeTracker.Core.Services;

public class StatisticsService
{
    public List<ProjectStatistics> CalculateStatistics(
        List<ProjectSession> sessions,
        List<PageTimeEntry> pageEntries,
        string? currentProjectName,
        string? currentUserName = null)
    {
        // Pre-group page entries by (ProjectName, UserName) for O(n) lookup
        var pagesByProjectUser = pageEntries
            .GroupBy(p => new { p.ProjectName, p.UserName })
            .ToDictionary(g => g.Key, g => g.ToList());

        var stats = sessions
            .GroupBy(s => new { s.ProjectName, s.UserName })
            .Select(g =>
            {
                var totalSeconds = g.Sum(s => CalculateSeconds(s));

                var breakdown = pagesByProjectUser.TryGetValue(g.Key, out var entries)
                    ? BuildPageBreakdown(entries, totalSeconds)
                    : [];

                return new ProjectStatistics
                {
                    ProjectName      = g.Key.ProjectName,
                    UserName         = g.Key.UserName,
                    TotalActiveTime  = TimeSpanDto.FromTimeSpan(TimeSpan.FromSeconds(totalSeconds)),
                    TotalElapsedTime = TimeSpanDto.FromTimeSpan(TimeSpan.FromSeconds(totalSeconds)),
                    LastActivity     = g.Max(s => s.EndTime ?? s.StartTime),
                    SessionCount     = g.Count(),
                    IsCurrentlyTracking = g.Key.ProjectName == currentProjectName
                                       && g.Key.UserName    == currentUserName,
                    PageBreakdown    = breakdown
                };
            })
            .OrderByDescending(s => s.LastActivity)
            .ToList();

        return stats;
    }

    private static List<PageTimeStat> BuildPageBreakdown(
        List<PageTimeEntry> entries, long totalProjectSeconds)
    {
        if (totalProjectSeconds <= 0) return [];

        return entries
            .GroupBy(p => p.Page)
            .Select(g =>
            {
                var pageSeconds = g.Sum(p => CalculatePageSeconds(p));
                return new PageTimeStat
                {
                    Page       = g.Key,
                    TotalTime  = TimeSpanDto.FromTimeSpan(TimeSpan.FromSeconds(pageSeconds)),
                    Percentage = Math.Round(pageSeconds * 100.0 / totalProjectSeconds, 1)
                };
            })
            .Where(p => p.TotalTime.TotalSeconds > 0)
            .OrderByDescending(p => p.TotalTime.TotalSeconds)
            .ToList();
    }

    private static long CalculateSeconds(ProjectSession session)
    {
        var end = session.EndTime ?? session.FlushedEnd ?? DateTime.UtcNow;
        return (long)(end - session.StartTime).TotalSeconds;
    }

    private static long CalculatePageSeconds(PageTimeEntry entry)
    {
        var end = entry.EndTime ?? entry.FlushedEnd ?? DateTime.UtcNow;
        return (long)(end - entry.StartTime).TotalSeconds;
    }
}
