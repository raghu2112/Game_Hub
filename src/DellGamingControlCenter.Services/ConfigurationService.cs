namespace DellGamingControlCenter.Services;

using System.Text.Json;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class ConfigurationService : IConfigurationService
{
    private readonly string _configFilePath;
    private readonly string _backupFilePath;
    private AppConfiguration _currentConfig = new();
    private readonly ILoggingService _logger;

    public ConfigurationService(ILoggingService logger)
    {
        _logger = logger;
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DellGamingControlCenter");
        Directory.CreateDirectory(appData);
        _configFilePath = Path.Combine(appData, "config.json");
        _backupFilePath = Path.Combine(appData, "config_backup.json");
        LoadConfig();
    }

    public AppConfiguration GetConfig()
    {
        return _currentConfig;
    }

    public void SaveConfig(AppConfiguration config)
    {
        _currentConfig = config;
        _currentConfig.LastModified = DateTime.UtcNow;
        try
        {
            var json = JsonSerializer.Serialize(_currentConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.Log($"Failed to save config: {ex.Message}", Core.Enums.LogSeverity.Error, "Configuration");
        }
    }

    public ConfigBackup CreateSystemBackup()
    {
        // For now, return empty. Needs implementation to read actual system states.
        var backup = new ConfigBackup { BackedUpAt = DateTime.UtcNow };
        try
        {
            var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_backupFilePath, json);
            _logger.Log("Created system backup.", Core.Enums.LogSeverity.Info, "Configuration");
        }
        catch (Exception ex)
        {
             _logger.Log($"Failed to save backup: {ex.Message}", Core.Enums.LogSeverity.Error, "Configuration");
        }
        return backup;
    }

    public bool RestoreSystemBackup(ConfigBackup backup)
    {
        _logger.Log("Restoring system backup...", Core.Enums.LogSeverity.Info, "Configuration");
        // Implement restoration of power plans, registry, etc.
        return true;
    }

    public IEnumerable<GameProfile> GetGameProfiles()
    {
        return _currentConfig.GameProfiles;
    }

    public void SaveGameProfile(GameProfile profile)
    {
        var existing = _currentConfig.GameProfiles.FirstOrDefault(p => p.GameId == profile.GameId);
        if (existing != null)
        {
            _currentConfig.GameProfiles.Remove(existing);
        }
        _currentConfig.GameProfiles.Add(profile);
        SaveConfig(_currentConfig);
        _logger.Log($"Saved game profile: {profile.GameName}", Core.Enums.LogSeverity.Success, "Configuration");
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                var loaded = JsonSerializer.Deserialize<AppConfiguration>(json);
                if (loaded != null)
                {
                    _currentConfig = loaded;
                }
            }
        }
        catch
        {
            _currentConfig = new AppConfiguration();
        }
    }
}
