namespace DaVinciTimeTracker.Core.Configuration;

public static class TrackingConfiguration
{
#if DEBUG
    public static readonly TimeSpan GraceStartDuration = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan GraceEndDuration = TimeSpan.FromSeconds(5);
#else
    public static readonly TimeSpan GraceStartDuration = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan GraceEndDuration = TimeSpan.FromMinutes(10);
#endif
}
