namespace DaVinciTimeTracker.Core.Models;

/// <summary>
/// Whether an activity involves active user work or autonomous machine processing.
/// Stored on each ActivityEntry instance (not derived from ActivityType) so future
/// ambiguous activities like capture can be classified at detection time.
/// </summary>
public enum ActivityKind
{
    /// <summary>User is actively working (edit, color, deliver setup, etc.).</summary>
    User,

    /// <summary>Machine is working autonomously (render, future: capture, export, etc.).</summary>
    Processing
}
