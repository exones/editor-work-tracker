namespace DaVinciTimeTracker.Core.Models;

/// <summary>
/// Records how long the user spent on a specific DaVinci Resolve page
/// (color, edit, cut, media, fusion, fairlight, deliver, photo) within a session.
/// One entry is created per continuous page visit; switching pages closes the current
/// entry and opens a new one.
/// </summary>
public class PageTimeEntry
{
    public Guid Id { get; set; }

    /// <summary>FK to ProjectSession.Id</summary>
    public Guid SessionId { get; set; }

    /// <summary>Denormalised for efficient per-project queries without a join.</summary>
    public string ProjectName { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// DaVinci page name as returned by resolve.GetCurrentPage():
    /// "media", "cut", "edit", "fusion", "color", "fairlight", "deliver", "photo"
    /// </summary>
    public string Page { get; set; } = string.Empty;

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    /// <summary>Last periodic flush time for crash recovery (mirrors ProjectSession.FlushedEnd).</summary>
    public DateTime? FlushedEnd { get; set; }
}
