namespace DaVinciTimeTracker.Core.Diagnostics;

public enum CheckStatus { Pass, Warn, Fail, Skipped }

/// <summary>One observation line inside a check (a check may have several).</summary>
public record CheckMessage(CheckStatus Severity, string Text);

/// <summary>
/// One resolution path the user can act on.
/// Multiple options let the user choose their preferred fix
/// (e.g. "install Python 3.12" vs "pin existing interpreter via env var").
/// AutoFixId is non-null when the app can apply it automatically via
/// POST /api/diagnostics/apply-fix/{AutoFixId}.
/// </summary>
public record ResolutionOption(
    string Label,          // e.g. "Install Python 3.12 (recommended)"
    string Instructions,   // copy-ready text: command, Preferences path, env var, etc.
    string? AutoFixId,     // non-null → app can apply this automatically
    string? DocUrl         // optional deep-link to BMD docs or internal help
);

/// <summary>
/// Overall result for one diagnostic check.
/// Status = worst severity across all Messages.
/// </summary>
public record CheckResult(
    string Id,
    string Title,
    CheckStatus Status,                       // worst of Messages[].Severity
    IReadOnlyList<CheckMessage> Messages,     // one or more observations
    IReadOnlyList<ResolutionOption> Options   // zero or more resolution paths
)
{
    /// <summary>Convenience: single-message, no options.</summary>
    public static CheckResult Single(string id, string title, CheckStatus status, string text) =>
        new(id, title, status,
            [new CheckMessage(status, text)],
            []);

    /// <summary>Convenience: single-message with one option.</summary>
    public static CheckResult WithFix(string id, string title, CheckStatus status, string text,
        string fixLabel, string fixInstructions, string? autoFixId = null) =>
        new(id, title, status,
            [new CheckMessage(status, text)],
            [new ResolutionOption(fixLabel, fixInstructions, autoFixId, null)]);
}

/// <summary>Lightweight health summary returned by GET /api/health.</summary>
public record HealthSummary(
    HealthLevel Level,
    string Summary,
    int FailCount,
    int WarnCount
);

public enum HealthLevel { Green, Amber, Red }
