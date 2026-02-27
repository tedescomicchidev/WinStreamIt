using System.Text.Json;
using SonosStream.Models;

namespace SonosStream.Services;

/// <summary>
/// Loads and saves application settings as JSON in the user's LocalApplicationData folder.
/// </summary>
public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings;

    public AppSettings Settings => _settings;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "SonosStream");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
        _settings = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt / unreadable — start fresh
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Best effort — non-fatal
        }
    }

    /// <summary>
    /// Replace the current settings object and save immediately.
    /// </summary>
    public void Update(AppSettings settings)
    {
        _settings = settings;
        Save();
    }
}
