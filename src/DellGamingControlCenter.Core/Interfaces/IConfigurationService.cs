namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

/// <summary>
/// Central configuration management and backup.
/// </summary>
public interface IConfigurationService
{
    /// <summary>Gets the current application configuration.</summary>
    AppConfiguration GetConfig();

    /// <summary>Saves the application configuration.</summary>
    void SaveConfig(AppConfiguration config);

    /// <summary>Creates a backup of system state before making optimizations.</summary>
    ConfigBackup CreateSystemBackup();

    /// <summary>Restores a previous system state.</summary>
    bool RestoreSystemBackup(ConfigBackup backup);

    /// <summary>Returns the active game profiles.</summary>
    IEnumerable<GameProfile> GetGameProfiles();

    /// <summary>Saves or updates a game profile.</summary>
    void SaveGameProfile(GameProfile profile);
}
