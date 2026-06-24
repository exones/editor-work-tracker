namespace DaVinciTimeTracker.Core.Models;

public class UserSettings
{
    // ── Tracking ──────────────────────────────────────────────────────────────

    /// <summary>Seconds of focus+activity required before a new session is confirmed. Default 30s.</summary>
    public int GraceStartSeconds { get; set; } = 30;

    /// <summary>Minutes a session continues after losing focus before it ends. Default 10min.</summary>
    public int GraceEndMinutes { get; set; } = 10;

    /// <summary>Minutes of no keyboard/mouse input before the user is considered idle. Default 1min.</summary>
    public int InactivityThresholdMinutes { get; set; } = 1;

    // ── Node Actions ──────────────────────────────────────────────────────────

    /// <summary>
    /// DaVinci 'Add Serial Node' keyboard shortcut (Color page).
    /// Used by the Select action to create a temp anchor node.
    /// Default is the DaVinci Windows default: Alt+S.
    /// </summary>
    public string AppendNodeShortcut { get; set; } = "Alt+S";

    /// <summary>
    /// DaVinci 'Next Node' keyboard shortcut (Color page).
    /// Used by the Select action to navigate to the target node.
    /// Windows default: Alt+Shift+Oem7 (Alt+Shift+').
    /// </summary>
    public string NextNodeShortcut { get; set; } = "Alt+Shift+Oem7";

    // ── Billing ───────────────────────────────────────────────────────────────

    public BillingSettings Billing { get; set; } = new();
}

public class BillingSettings
{
    /// <summary>ISO 4217 currency code (e.g. "USD", "EUR", "CHF"). Shown on cost displays.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Default hourly rate for User-kind activities when no per-type override is set.</summary>
    public decimal DefaultUserRatePerHour { get; set; } = 0;

    /// <summary>Default hourly rate for Processing-kind activities when no per-type override is set.</summary>
    public decimal DefaultProcessingRatePerHour { get; set; } = 0;

    /// <summary>
    /// Per-ActivityType rate overrides. Key = ActivityType string ("edit", "color", "render", etc.).
    /// When absent for a given type, the Kind default above applies.
    /// </summary>
    public Dictionary<string, decimal> ActivityTypeRates { get; set; } = new();

    /// <summary>Returns true when at least one non-zero rate is configured.</summary>
    public bool HasAnyRate =>
        DefaultUserRatePerHour > 0 ||
        DefaultProcessingRatePerHour > 0 ||
        ActivityTypeRates.Values.Any(r => r > 0);

    /// <summary>Looks up the effective hourly rate for an activity.</summary>
    public decimal GetRate(string activityType, ActivityKind kind)
    {
        if (ActivityTypeRates.TryGetValue(activityType, out var r)) return r;
        return kind == ActivityKind.User ? DefaultUserRatePerHour : DefaultProcessingRatePerHour;
    }
}
