using DaVinciTimeTracker.Core.Services;

namespace DaVinciTimeTracker.Core.Configuration;

/// <summary>
/// Provides tracking duration constants read from UserSettingsService.
/// Call Configure() once at startup before any tracking begins.
/// Properties are read on every access, so changes to the underlying service
/// take effect on the next tick without restarting (grace periods are re-evaluated every 2s).
/// </summary>
public static class TrackingConfiguration
{
    private static UserSettingsService? _settings;

    /// <summary>Call once at startup to wire in the settings service.</summary>
    public static void Configure(UserSettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>How long the user must be focused + active before a session is confirmed.</summary>
    public static TimeSpan GraceStartDuration =>
        _settings is not null
            ? TimeSpan.FromSeconds(_settings.Current.GraceStartSeconds)
            : TimeSpan.FromSeconds(30);

    /// <summary>How long a session continues after the user loses focus before it ends.</summary>
    public static TimeSpan GraceEndDuration =>
        _settings is not null
            ? TimeSpan.FromMinutes(_settings.Current.GraceEndMinutes)
            : TimeSpan.FromMinutes(10);
}
