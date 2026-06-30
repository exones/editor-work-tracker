using DaVinciTimeTracker.Core.Native;
using DaVinciTimeTracker.Core.Resolve;
using DaVinciTimeTracker.Core.Utilities;
using Serilog;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DaVinciTimeTracker.Core.Diagnostics;

/// <summary>
/// Runs ordered diagnostic checks against the Python/DaVinci scripting bridge
/// and returns structured <see cref="CheckResult"/> items for the Troubleshooter UI.
///
/// Checks are numbered 1–11 in the plan; each produces one or more CheckMessages
/// and zero or more ResolutionOptions with copy-ready instructions.
/// </summary>
public sealed class ResolveDiagnosticsService
{
    private readonly IResolvePlatform _platform;
    private readonly ISystemActivityProvider _activity;
    private readonly ResolveScriptingClient _nodeToggleClient;
    private string _pythonPath;
    private readonly ILogger _logger;

    public ResolveDiagnosticsService(
        IResolvePlatform platform,
        ISystemActivityProvider activity,
        ResolveScriptingClient nodeToggleClient,
        string pythonPath,
        ILogger logger)
    {
        _platform = platform;
        _activity = activity;
        _nodeToggleClient = nodeToggleClient;
        _pythonPath = pythonPath;
        _logger = logger;
    }

    /// <summary>
    /// Updates the Python path after the resolver has selected a compatible interpreter.
    /// Called from Program.cs once Python detection completes.
    /// </summary>
    public void UpdatePythonPath(string pythonPath) => _pythonPath = pythonPath;

    public async Task<List<CheckResult>> RunAllAsync()
    {
        var results = new List<CheckResult>();

        results.Add(Check01_SystemInfo());
        results.Add(Check02_ResolveInstalled());
        results.Add(Check03_ResolveRunning());
        results.AddRange(Check04_05_PythonInterpreters());
        results.Add(Check06_EnvVars());
        results.Add(await Check07_FusionScriptAsync());
        results.Add(await Check08_EditionAndVersionAsync());
        results.Add(await Check09_EndToEndApiAsync());
        results.Add(await Check10_ExternalScriptingAsync());
        results.Add(await Check11_ProjectOpenAsync());

        return results;
    }

    public HealthSummary GetHealthSummary(List<CheckResult> results)
    {
        var fails = results.Count(r => r.Status == CheckStatus.Fail);
        var warns = results.Count(r => r.Status == CheckStatus.Warn);
        var level = fails > 0 ? HealthLevel.Red : warns > 0 ? HealthLevel.Amber : HealthLevel.Green;
        var summary = level switch
        {
            HealthLevel.Red   => $"{fails} issue(s) preventing DaVinci scripting",
            HealthLevel.Amber => $"{warns} warning(s) — scripting may be degraded",
            _                 => "DaVinci scripting bridge OK"
        };
        return new HealthSummary(level, summary, fails, warns);
    }

    // ── Check 1: System info (always Pass — informational) ───────────────────

    private static CheckResult Check01_SystemInfo()
    {
        var os = Environment.OSVersion.ToString();
        var dotnet = Environment.Version.ToString();
        var arch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
        return new CheckResult("system-info", "System Information", CheckStatus.Pass,
            [
                new CheckMessage(CheckStatus.Pass, $"OS: {os} ({arch})"),
                new CheckMessage(CheckStatus.Pass, $".NET runtime: {dotnet}"),
                new CheckMessage(CheckStatus.Pass, $"App version: {typeof(ResolveDiagnosticsService).Assembly.GetName().Version}")
            ], []);
    }

    // ── Check 2: Resolve installed ────────────────────────────────────────────

    private CheckResult Check02_ResolveInstalled()
    {
        var lib = _platform.GetFusionScriptLibPath();
        var api = _platform.GetScriptApiPath();

        if (lib != null && api != null)
            return new CheckResult("resolve-installed", "DaVinci Resolve Installed", CheckStatus.Pass,
                [
                    new CheckMessage(CheckStatus.Pass, $"fusionscript: {lib}"),
                    new CheckMessage(CheckStatus.Pass, $"Scripting API: {api}")
                ], []);

        var msgs = new List<CheckMessage>();
        if (lib == null) msgs.Add(new CheckMessage(CheckStatus.Fail, $"fusionscript not found at expected path: {_platform.GetResolveInstallDirectory()}"));
        if (api == null) msgs.Add(new CheckMessage(CheckStatus.Fail, "Scripting Modules folder not found"));

        return new CheckResult("resolve-installed", "DaVinci Resolve Installed", CheckStatus.Fail, msgs,
            [new ResolutionOption("Install DaVinci Resolve Studio",
                "Download DaVinci Resolve Studio from https://www.blackmagicdesign.com/products/davinciresolve",
                null, "https://www.blackmagicdesign.com/products/davinciresolve")]);
    }

    // ── Check 3: Resolve process running ─────────────────────────────────────

    private CheckResult Check03_ResolveRunning()
    {
        var running = _activity.IsDaVinciResolveRunning();
        return running
            ? CheckResult.Single("resolve-running", "DaVinci Resolve Running", CheckStatus.Pass, "DaVinci Resolve process detected")
            : CheckResult.WithFix("resolve-running", "DaVinci Resolve Running", CheckStatus.Warn,
                "DaVinci Resolve is not running (some checks will be skipped)",
                "Start DaVinci Resolve", "Open DaVinci Resolve Studio, load a project, then re-run diagnostics");
    }

    // ── Check 4+5: Python interpreters + compatibility ────────────────────────

    private List<CheckResult> Check04_05_PythonInterpreters()
    {
        var candidates = PythonPathResolver.CollectAndProbe(_logger);

        // Check 4: found interpreters
        CheckResult check4;
        if (candidates.Count == 0)
        {
            check4 = CheckResult.WithFix("python-found", "Python Interpreter Found", CheckStatus.Fail,
                "No compatible Python interpreter found (need 64-bit, ≥ 3.6)",
                "Install Python 3.12 (recommended)",
                "winget install Python.Python.3.12  -or-  https://www.python.org/downloads/",
                null);
        }
        else
        {
            var msgs = candidates.Select(c =>
                new CheckMessage(c.CompatibilityNote != null ? CheckStatus.Warn : CheckStatus.Pass,
                    c.DisplayLabel + (c.CompatibilityNote != null ? $" — {c.CompatibilityNote}" : "")))
                .ToList();
            check4 = new CheckResult("python-found", "Python Interpreter(s) Found", CheckStatus.Pass, msgs, []);
        }

        // Check 5: selected interpreter compatibility
        CheckResult check5;
        if (!string.IsNullOrEmpty(_pythonPath))
        {
            var selected = candidates.FirstOrDefault(c =>
                string.Equals(c.Path, _pythonPath, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                check5 = CheckResult.Single("python-compat", "Selected Python Compatibility", CheckStatus.Warn,
                    $"Selected Python not in probed list: {_pythonPath}");
            }
            else if (!selected.MeetsMinimumRequirements)
            {
                check5 = CheckResult.WithFix("python-compat", "Selected Python Compatibility", CheckStatus.Fail,
                    $"{selected.DisplayLabel} — does not meet minimum requirements (64-bit ≥ 3.6)",
                    "Install Python 3.12 (recommended)",
                    "winget install Python.Python.3.12");
            }
            else
            {
                var best = candidates.FirstOrDefault();
                var msgs = new List<CheckMessage>
                {
                    new(CheckStatus.Pass, $"Selected: {selected.DisplayLabel}")
                };
                var opts = new List<ResolutionOption>();
                if (best != null && !string.Equals(best.Path, _pythonPath, StringComparison.OrdinalIgnoreCase))
                {
                    msgs.Add(new CheckMessage(CheckStatus.Warn,
                        $"A higher-ranked interpreter is available: {best.DisplayLabel}"));
                    opts.Add(new ResolutionOption("Pin to preferred interpreter",
                        $"set DAVINCI_TRACKER_PYTHON={best.Path}",
                        $"pin-python:{best.Path}", null));
                }
                var worst = selected.CompatibilityNote != null ? CheckStatus.Warn : CheckStatus.Pass;
                check5 = new CheckResult("python-compat", "Selected Python Compatibility", worst, msgs, opts);
            }
        }
        else
        {
            check5 = CheckResult.Single("python-compat", "Selected Python Compatibility", CheckStatus.Skipped,
                "No Python selected yet");
        }

        return [check4, check5];
    }

    // ── Check 6: Environment variables ───────────────────────────────────────

    private CheckResult Check06_EnvVars()
    {
        var lib = _platform.GetFusionScriptLibPath();
        var api = _platform.GetScriptApiPath();
        var modules = _platform.GetScriptingModulesPath();
        var pythonHome = !string.IsNullOrEmpty(_pythonPath)
            ? Path.GetDirectoryName(_pythonPath) : null;

        var msgs = new List<CheckMessage>
        {
            new(lib != null ? CheckStatus.Pass : CheckStatus.Fail,
                $"RESOLVE_SCRIPT_LIB: {lib ?? "(not found)"}"),
            new(api != null ? CheckStatus.Pass : CheckStatus.Warn,
                $"RESOLVE_SCRIPT_API: {api ?? "(not found)"}"),
            new(modules != null ? CheckStatus.Pass : CheckStatus.Warn,
                $"PYTHONPATH (Modules): {modules ?? "(not found)"}"),
            new(pythonHome != null ? CheckStatus.Pass : CheckStatus.Warn,
                $"PYTHONHOME (will inject): {pythonHome ?? "(no interpreter selected)"}"),
        };

        var worst = msgs.Max(m => m.Severity);
        return new CheckResult("env-vars", "Environment Variables", worst, msgs, []);
    }

    // ── Check 7: fusionscript load probe ─────────────────────────────────────

    private async Task<CheckResult> Check07_FusionScriptAsync()
    {
        var lib = _platform.GetFusionScriptLibPath();
        if (string.IsNullOrEmpty(_pythonPath) || lib == null)
            return CheckResult.Single("fusionscript", "fusionscript Load Test", CheckStatus.Skipped,
                "Skipped — Python or Resolve library not found");

        var result = await Task.Run(() =>
            PythonPathResolver.TestFusionScriptCompatibility(_pythonPath, lib, _logger));

        return result switch
        {
            PythonPathResolver.FusionScriptTestResult.Compatible =>
                CheckResult.Single("fusionscript", "fusionscript Load Test", CheckStatus.Pass,
                    "fusionscript loaded successfully"),
            PythonPathResolver.FusionScriptTestResult.Incompatible =>
                new CheckResult("fusionscript", "fusionscript Load Test", CheckStatus.Fail,
                    [new CheckMessage(CheckStatus.Fail, "fusionscript ABI/IPC incompatible with selected Python")],
                    [
                        new ResolutionOption("Use PyManager or Windows Store Python",
                            "set DAVINCI_TRACKER_PYTHON=C:\\Program Files\\PyManager\\python.exe", "pin-python-pymanager", null),
                        new ResolutionOption("Install Python 3.12 (recommended)",
                            "winget install Python.Python.3.12", null, null)
                    ]),
            _ => CheckResult.Single("fusionscript", "fusionscript Load Test", CheckStatus.Warn,
                "Test inconclusive — DaVinci Resolve may not be running or not fully started")
        };
    }

    // ── Check 8: Edition + version (authoritative via daemon diagnose) ────────

    private async Task<CheckResult> Check08_EditionAndVersionAsync()
    {
        var diag = await RunDiagnoseAsync();
        if (diag is null)
            return CheckResult.Single("resolve-edition", "DaVinci Resolve Edition & Version", CheckStatus.Warn,
                "Could not connect to Resolve scripting — ensure Resolve is running with external scripting enabled");

        var productName = diag["product_name"]?.GetValue<string>() ?? "Unknown";
        var versionStr  = diag["version_string"]?.GetValue<string>() ?? "Unknown";
        var isStudio    = diag["is_studio"]?.GetValue<bool>() ?? false;

        if (!isStudio)
            return new CheckResult("resolve-edition", "DaVinci Resolve Edition & Version", CheckStatus.Fail,
                [
                    new CheckMessage(CheckStatus.Pass, $"Product: {productName} {versionStr}"),
                    new CheckMessage(CheckStatus.Fail, "Free version detected — external scripting requires DaVinci Resolve Studio")
                ],
                [new ResolutionOption("Upgrade to DaVinci Resolve Studio",
                    "Purchase DaVinci Resolve Studio from https://www.blackmagicdesign.com/store",
                    null, "https://www.blackmagicdesign.com/store")]);

        return new CheckResult("resolve-edition", "DaVinci Resolve Edition & Version", CheckStatus.Pass,
            [
                new CheckMessage(CheckStatus.Pass, $"Product: {productName}"),
                new CheckMessage(CheckStatus.Pass, $"Version: {versionStr}")
            ], []);
    }

    // ── Check 9: End-to-end API ───────────────────────────────────────────────

    private async Task<CheckResult> Check09_EndToEndApiAsync()
    {
        var diag = await RunDiagnoseAsync();
        if (diag is null)
            return CheckResult.Single("api-e2e", "End-to-End API", CheckStatus.Warn,
                "Skipped — daemon not connected");

        var scriptappOk = diag["scriptapp_ok"]?.GetValue<bool>() ?? false;
        var msgs = new List<CheckMessage>
        {
            new(scriptappOk ? CheckStatus.Pass : CheckStatus.Fail, $"scriptapp('Resolve'): {(scriptappOk ? "OK" : "returned None")}"),
        };

        if (scriptappOk)
        {
            var page = diag["current_page"]?.GetValue<string>();
            msgs.Add(new CheckMessage(CheckStatus.Pass, $"GetCurrentPage(): {page ?? "None (Resolve minimised — normal)"}"));
        }

        var worst = msgs.Max(m => m.Severity);
        var opts = worst == CheckStatus.Fail
            ? new List<ResolutionOption>
            {
                new("Enable External Scripting",
                    "In DaVinci Resolve: Preferences → System → General → External scripting using → Local",
                    null, null),
                new("Console self-check",
                    "In Resolve: Workspace → Console (Py3) → run: import sys; print(sys.version)",
                    null, null)
            }
            : new List<ResolutionOption>();

        return new CheckResult("api-e2e", "End-to-End API", worst, msgs, opts);
    }

    // ── Check 10: External scripting permission (inferred) ────────────────────

    private async Task<CheckResult> Check10_ExternalScriptingAsync()
    {
        if (!_activity.IsDaVinciResolveRunning())
            return CheckResult.Single("ext-scripting", "External Scripting Permission", CheckStatus.Skipped,
                "Skipped — Resolve not running");

        var diag = await RunDiagnoseAsync();
        var scriptappOk = diag?["scriptapp_ok"]?.GetValue<bool>() ?? false;

        if (scriptappOk)
            return CheckResult.Single("ext-scripting", "External Scripting Permission", CheckStatus.Pass,
                "scriptapp connected — external scripting is enabled");

        // Inferred: process is up but scriptapp returns None
        return new CheckResult("ext-scripting", "External Scripting Permission", CheckStatus.Fail,
            [new CheckMessage(CheckStatus.Fail,
                "DaVinci is running but scriptapp returns None — external scripting is likely disabled or set to Console-only")],
            [
                new ResolutionOption("Enable Local scripting (recommended)",
                    "DaVinci Resolve → Preferences → System → General → External scripting using → Local\nRestart Resolve after changing.",
                    null, null),
                new ResolutionOption("Console self-check to confirm Python version",
                    "In Resolve: Workspace → Console (Py3) → run: import sys; print(sys.version)\nThis shows the exact Python Resolve is using.",
                    null, null)
            ]);
    }

    // ── Check 11: Project open + page (informational) ─────────────────────────

    private async Task<CheckResult> Check11_ProjectOpenAsync()
    {
        var diag = await RunDiagnoseAsync();
        if (diag is null)
            return CheckResult.Single("project-open", "Project & Page Status", CheckStatus.Skipped,
                "Skipped — daemon not connected");

        var projectOpen = diag["project_open"]?.GetValue<bool>() ?? false;
        var page = diag["current_page"]?.GetValue<string>();

        var msgs = new List<CheckMessage>
        {
            new(projectOpen ? CheckStatus.Pass : CheckStatus.Warn,
                projectOpen ? "Project is open" : "No project open — open a project in Resolve to enable tracking"),
            new(CheckStatus.Pass,
                page != null
                    ? $"Current page: {page}"
                    : "Current page: None (expected when Resolve is minimised or running headless — not an error)")
        };
        var worst = msgs.Max(m => m.Severity);
        return new CheckResult("project-open", "Project & Page Status", worst, msgs, []);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<JsonNode?> RunDiagnoseAsync()
    {
        try
        {
            var resp = await _nodeToggleClient.SendDiagnoseAsync();
            if (resp?["status"]?.GetValue<string>() == "ok") return resp;
        }
        catch (Exception ex) { _logger.Debug("Diagnostics: daemon diagnose failed — {Err}", ex.Message); }
        return null;
    }
}
