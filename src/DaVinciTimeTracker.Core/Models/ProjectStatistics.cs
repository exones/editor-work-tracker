namespace DaVinciTimeTracker.Core.Models;

public class ProjectStatistics
{
    public string ProjectName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public TimeSpanDto TotalActiveTime { get; set; } = null!;
    public TimeSpanDto TotalElapsedTime { get; set; } = null!;
    public DateTime LastActivity { get; set; }
    public int SessionCount { get; set; }
    public bool IsCurrentlyTracking { get; set; }
    public string? CurrentState { get; set; }
    /// <summary>Time spent actively working (not rendering).</summary>
    public TimeSpanDto TotalWorkTime { get; set; } = null!;
    /// <summary>Time spent waiting for renders.</summary>
    public TimeSpanDto TotalRenderTime { get; set; } = null!;
    /// <summary>Time spent per DaVinci page, sorted by total time descending.</summary>
    public List<PageTimeStat> PageBreakdown { get; set; } = [];
}

public class PageTimeStat
{
    /// <summary>Page name as returned by GetCurrentPage(): color, edit, cut, media, fusion, fairlight, deliver, photo</summary>
    public string Page { get; set; } = string.Empty;
    /// <summary>Total time on this page (active + render segments).</summary>
    public TimeSpanDto TotalTime { get; set; } = null!;
    /// <summary>Time actively working (IsRendering = false).</summary>
    public TimeSpanDto ActiveTime { get; set; } = null!;
    /// <summary>Time spent waiting for renders (IsRendering = true).</summary>
    public TimeSpanDto RenderTime { get; set; } = null!;
    /// <summary>0–100 share of total session time for this user/project.</summary>
    public double Percentage { get; set; }
    /// <summary>Distinct timeline names seen on this page, for tooltip display.</summary>
    public List<string> Timelines { get; set; } = [];
}

public class TimeSpanDto
{
    public long TotalSeconds { get; set; }
    public int Days { get; set; }
    public int Hours { get; set; }
    public int Minutes { get; set; }
    public int Seconds { get; set; }

    public static TimeSpanDto FromTimeSpan(TimeSpan timeSpan)
    {
        return new TimeSpanDto
        {
            TotalSeconds = (long)timeSpan.TotalSeconds,
            Days = timeSpan.Days,
            Hours = timeSpan.Hours,
            Minutes = timeSpan.Minutes,
            Seconds = timeSpan.Seconds
        };
    }
}
