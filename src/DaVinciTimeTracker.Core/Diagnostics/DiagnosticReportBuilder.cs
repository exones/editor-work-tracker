using DaVinciTimeTracker.Core.Utilities;
using System.Text;

namespace DaVinciTimeTracker.Core.Diagnostics;

/// <summary>
/// Builds a plain-text/markdown diagnostic report suitable for copy-paste or file export.
/// Contains: app info, all check results with messages and options, and recent relevant log lines.
/// Purely local — no data is sent anywhere.
/// </summary>
public static class DiagnosticReportBuilder
{
    private const int MaxLogLines = 50;

    private static readonly string[] LogKeywords =
    [
        "[resolve_api]", "[node_toggle_api]", "NodeToggle", "DaVinci",
        "Python", "fusionscript", "PythonDaemon", "ResolveApiClient",
        "sanity", "FAIL", "WARN", "ERR"
    ];

    public static string Build(List<CheckResult> results)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# DaVinci Time Tracker — Diagnostic Report");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // ── Summary ──────────────────────────────────────────────────────────
        var fails = results.Count(r => r.Status == CheckStatus.Fail);
        var warns = results.Count(r => r.Status == CheckStatus.Warn);
        sb.AppendLine($"## Summary: {(fails > 0 ? "❌ ISSUES FOUND" : warns > 0 ? "⚠ WARNINGS" : "✅ ALL OK")}");
        sb.AppendLine($"- Fails: {fails}  Warnings: {warns}  Passed: {results.Count(r => r.Status == CheckStatus.Pass)}");
        sb.AppendLine();

        // ── Checks ───────────────────────────────────────────────────────────
        sb.AppendLine("## Check Results");
        sb.AppendLine();
        foreach (var r in results)
        {
            var icon = r.Status switch
            {
                CheckStatus.Pass    => "✅",
                CheckStatus.Warn    => "⚠️",
                CheckStatus.Fail    => "❌",
                CheckStatus.Skipped => "⏭",
                _                   => "?"
            };
            sb.AppendLine($"### {icon} {r.Title}");
            foreach (var m in r.Messages)
                sb.AppendLine($"  [{m.Severity}] {m.Text}");
            if (r.Options.Count > 0)
            {
                sb.AppendLine("  **Resolution options:**");
                for (var i = 0; i < r.Options.Count; i++)
                {
                    sb.AppendLine($"  {i + 1}. {r.Options[i].Label}");
                    sb.AppendLine($"     {r.Options[i].Instructions}");
                }
            }
            sb.AppendLine();
        }

        // ── Recent relevant log lines ─────────────────────────────────────────
        sb.AppendLine("## Recent Log (relevant lines)");
        sb.AppendLine();
        try
        {
            var logDir = AppPaths.LogsDirectory;
            var logFile = Directory.GetFiles(logDir, "davinci-tracker-*.log")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .FirstOrDefault();

            if (logFile != null)
            {
                var relevant = File.ReadLines(logFile)
                    .Where(line => LogKeywords.Any(kw =>
                        line.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                    .TakeLast(MaxLogLines)
                    .ToList();

                if (relevant.Count > 0)
                    foreach (var line in relevant)
                        sb.AppendLine(line);
                else
                    sb.AppendLine("(no relevant log lines found)");
            }
            else
            {
                sb.AppendLine("(log file not found)");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"(error reading logs: {ex.Message})");
        }

        return sb.ToString();
    }
}
