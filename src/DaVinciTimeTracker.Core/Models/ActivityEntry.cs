namespace DaVinciTimeTracker.Core.Models;

/// <summary>
/// Records a continuous segment of a single activity within a session.
///
/// For User activities, ActivityType matches the DaVinci page name
/// (e.g. "edit", "color", "deliver", "media", "fusion", "fairlight", "cut", "photo").
/// For Processing activities, ActivityType is the operation name (e.g. "render").
///
/// Rotation triggers (close current entry, open new one):
///   - Page changes        (PageChanged event)
///   - Timeline changes    (TimelineChanged event)
///   - Render starts       (RenderingStarted — opens a new Processing/render entry)
///   - Render stops        (RenderingStopped — opens a new User/page entry)
///   - Session ends        (SessionEnded)
/// </summary>
public class ActivityEntry
{
    public Guid Id { get; set; }

    /// <summary>FK to ProjectSession.Id</summary>
    public Guid SessionId { get; set; }

    /// <summary>Denormalised for efficient per-project queries without a join.</summary>
    public string ProjectName { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// For User activities: DaVinci page name ("edit", "color", "deliver", etc.).
    /// For Processing activities: operation name ("render", etc.).
    /// </summary>
    public string ActivityType { get; set; } = string.Empty;

    /// <summary>Whether this segment represents user work or autonomous machine processing.</summary>
    public ActivityKind Kind { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    /// <summary>Last periodic flush time for crash recovery (mirrors ProjectSession.FlushedEnd).</summary>
    public DateTime? FlushedEnd { get; set; }

    /// <summary>Active timeline when this segment was recorded. Null for activities without a timeline context.</summary>
    public string? TimelineName { get; set; }
}
