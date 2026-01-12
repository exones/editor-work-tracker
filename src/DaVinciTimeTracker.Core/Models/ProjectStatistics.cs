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
    public string? CurrentState { get; set; } // Added: GraceStart, Tracking, GraceEnd, NotTracking
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
