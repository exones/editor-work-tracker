namespace DaVinciTimeTracker.Core.Models;

/// <summary>
/// Per-project metadata. ProjectName is the natural PK matching the denormalized
/// string used in ProjectSessions and ActivityEntries — no FK constraints needed.
/// Future fields: ClientName, Notes, InvoiceNumber, etc.
/// </summary>
public class Project
{
    public string ProjectName { get; set; } = string.Empty;
    public decimal? BilledAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}
