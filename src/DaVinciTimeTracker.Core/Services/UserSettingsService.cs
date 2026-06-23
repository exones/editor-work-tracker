using DaVinciTimeTracker.Core.Models;
using Serilog;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DaVinciTimeTracker.Core.Services;

/// <summary>
/// Loads and persists user settings from a JSON file.
/// Follows the same pattern as NodeToggleService for file I/O.
/// </summary>
public class UserSettingsService
{
    private readonly string _path;
    private readonly ILogger _logger;
    private UserSettings _current;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public UserSettings Current => _current;

    public UserSettingsService(string path, ILogger logger)
    {
        _path   = path;
        _logger = logger;
        _current = Load();
    }

    /// <summary>Saves the given settings to disk and updates Current.</summary>
    public void Save(UserSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_path, json);
            _current = settings;
            _logger.Information("UserSettings saved to {Path}", _path);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save user settings to {Path}", _path);
            throw;
        }
    }

    private UserSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                _logger.Information("UserSettings file not found at {Path} — using defaults", _path);
                return new UserSettings();
            }

            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
            if (settings is null)
            {
                _logger.Warning("UserSettings deserialized to null — using defaults");
                return new UserSettings();
            }

            _logger.Information("UserSettings loaded from {Path}", _path);
            return settings;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load user settings from {Path} — using defaults", _path);
            return new UserSettings();
        }
    }
}
