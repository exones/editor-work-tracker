using System.Diagnostics;
using System.Text;
using Serilog;

namespace DaVinciTimeTracker.Core.Resolve;

public class ResolveApiClient
{
    private readonly string _pythonPath;
    private readonly string _scriptPath;
    private readonly ILogger _logger;

    public ResolveApiClient(string pythonPath, string scriptPath, ILogger logger)
    {
        _pythonPath = pythonPath;
        _scriptPath = scriptPath;
        _logger = logger;
    }

    public async Task<string?> GetCurrentProjectNameAsync()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonPath,
                    Arguments = $"\"{_scriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !output.Contains("NO_PROJECT"))
            {
                var projectName = output.Trim();

                // Treat "Untitled Project" as no project open
                if (projectName.Equals("Untitled Project", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Debug("DaVinci opened without project (Untitled Project)");
                    return null;
                }

                _logger.Debug("DaVinci project detected: {ProjectName}", projectName);
                return projectName;
            }

            if (output.Contains("NO_PROJECT"))
            {
                _logger.Debug("No DaVinci project currently open");
                return null;
            }

            _logger.Warning("DaVinci API error: {Error}", error);
            return null;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to query DaVinci Resolve API");
            return null;
        }
    }
}
