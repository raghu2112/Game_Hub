namespace DellGamingControlCenter.Services;

using System.Text.Json;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

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
            var tmpPath = _configFilePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _configFilePath, true);
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
        catch (Exception ex)
        {
            _logger.Log($"Failed to load config, using defaults. Error: {ex.Message}", Core.Enums.LogSeverity.Warning, "Configuration");
            _currentConfig = new AppConfiguration();
        }

        ValidateConfig(_currentConfig);
    }

    private void ValidateConfig(AppConfiguration config)
    {
        // 1. Numeric ranges
        if (config.MonitoringIntervalMs < 500 || config.MonitoringIntervalMs > 60000)
        {
            config.MonitoringIntervalMs = 2000;
        }

        // 2. Enums
        if (!Enum.IsDefined(typeof(Core.Enums.PerformanceMode), config.DefaultPerformanceMode))
        {
            config.DefaultPerformanceMode = Core.Enums.PerformanceMode.Balanced;
        }

        // 3. Thermal thresholds
        if (config.ThermalThresholds.WarmCelsius < 0 || config.ThermalThresholds.WarmCelsius > 150)
            config.ThermalThresholds.WarmCelsius = 65.0;
        if (config.ThermalThresholds.HotCelsius <= config.ThermalThresholds.WarmCelsius || config.ThermalThresholds.HotCelsius > 150)
            config.ThermalThresholds.HotCelsius = 80.0;
        if (config.ThermalThresholds.CriticalCelsius <= config.ThermalThresholds.HotCelsius || config.ThermalThresholds.CriticalCelsius > 150)
            config.ThermalThresholds.CriticalCelsius = 95.0;

        // 4. Game Profiles Validation
        var validProfiles = new List<GameProfile>();
        foreach (var profile in config.GameProfiles)
        {
            bool valid = true;

            // Profile identifier
            if (string.IsNullOrWhiteSpace(profile.GameId) || profile.GameId.Length > 50)
                profile.GameId = Guid.NewGuid().ToString("N")[..8];

            // Enum checks
            if (!Enum.IsDefined(typeof(Core.Enums.GpuPreference), profile.PreferredGpu))
                profile.PreferredGpu = Core.Enums.GpuPreference.Auto;
            if (!Enum.IsDefined(typeof(Core.Enums.PerformanceMode), profile.PerformanceMode))
                profile.PerformanceMode = Core.Enums.PerformanceMode.Balanced;
            if (!Enum.IsDefined(typeof(Core.Enums.ProcessPriorityClass), profile.ProcessPriority))
                profile.ProcessPriority = Core.Enums.ProcessPriorityClass.Normal;

            // Safe paths (just basic checks, prevent nulls)
            if (string.IsNullOrWhiteSpace(profile.ExecutablePath) || profile.ExecutablePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                _logger.Log($"Invalid executable path for profile {profile.GameName}", Core.Enums.LogSeverity.Warning, "Configuration");
                valid = false;
            }

            if (valid)
            {
                validProfiles.Add(profile);
            }
        }
        config.GameProfiles = validProfiles;

        // 5. Session History Validation
        if (config.SessionHistory == null) config.SessionHistory = new List<GameSession>();
        
        var validSessions = new List<GameSession>();
        var seenIds = new HashSet<string>();
        
        foreach(var s in config.SessionHistory)
        {
            if (string.IsNullOrWhiteSpace(s.SessionId) || string.IsNullOrWhiteSpace(s.GameId))
                continue;
                
            if (!seenIds.Add(s.SessionId))
                continue; // duplicate

            // Fix timestamps
            if (s.EndTime.HasValue && s.EndTime.Value < s.StartTime)
            {
                s.EndTime = s.StartTime;
            }

            // Fix NaN metrics
            if (double.IsNaN(s.AverageCpuUsage) || double.IsInfinity(s.AverageCpuUsage) || s.AverageCpuUsage < 0) s.AverageCpuUsage = 0;
            if (double.IsNaN(s.AverageRamUsageBytes) || double.IsInfinity(s.AverageRamUsageBytes) || s.AverageRamUsageBytes < 0) s.AverageRamUsageBytes = 0;
            if (double.IsNaN(s.PeakCpuUsage) || double.IsInfinity(s.PeakCpuUsage) || s.PeakCpuUsage < 0) s.PeakCpuUsage = 0;
            
            // Stale active session check
            if (s.CurrentState == GameSessionState.Running || 
                s.CurrentState == GameSessionState.Launching || 
                s.CurrentState == GameSessionState.WaitingForProcess || 
                s.CurrentState == GameSessionState.Preparing)
            {
                s.CurrentState = GameSessionState.Failed;
                s.FailureReason = "Interrupted by application restart";
                if (!s.EndTime.HasValue) s.EndTime = DateTime.UtcNow;
            }
            
            validSessions.Add(s);
        }
        
        // Take latest 100
        config.SessionHistory = validSessions.OrderByDescending(s => s.StartTime).Take(100).ToList();
    }
}
