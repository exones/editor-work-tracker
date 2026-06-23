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
    /// <summary>Time spent on User-kind activities (editor actively working).</summary>
    public TimeSpanDto TotalWorkTime { get; set; } = null!;
    /// <summary>Time spent on Processing-kind activities (render, etc.).</summary>
    public TimeSpanDto TotalProcessingTime { get; set; } = null!;
    /// <summary>Time breakdown per activity type, sorted by total time descending.</summary>
    public List<ActivityTimeStat> ActivityBreakdown { get; set; } = [];

    /// <summary>Total calculated cost across all activities. Null when no billing rates are configured.</summary>
    public decimal? TotalCost { get; set; }

    /// <summary>Currency code from billing settings (e.g. "CHF"). Null when no rates configured.</summary>
    public string? Currency { get; set; }

    /// <summary>Amount actually billed to the client. Null if not set. Project-level — same for all users.</summary>
    public decimal? BilledAmount { get; set; }
}

public class ActivityTimeStat
{
    /// <summary>
    /// Activity type: DaVinci page name for User activities ("color", "edit", "deliver", etc.)
    /// or operation name for Processing activities ("render", etc.).
    /// </summary>
    public string ActivityType { get; set; } = string.Empty;

    /// <summary>Whether this is a User or Processing activity.</summary>
    public ActivityKind Kind { get; set; }

    /// <summary>Total time on this activity type.</summary>
    public TimeSpanDto TotalTime { get; set; } = null!;

    /// <summary>0–100 share of total session time for this user/project.</summary>
    public double Percentage { get; set; }

    /// <summary>Distinct timeline names seen during this activity, for tooltip display.</summary>
    public List<string> Timelines { get; set; } = [];

    /// <summary>Calculated cost for this activity type. Null when no rate is configured for it.</summary>
    public decimal? Cost { get; set; }
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
