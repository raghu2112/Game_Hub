namespace DellGamingControlCenter.Services;

using System.IO;
using System.Text.Json;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

public class GameDiscoveryService : IGameDiscoveryService
{
    private readonly ILoggingService _logger;
    private readonly string _cacheFilePath;
    private List<DiscoveredGame> _cachedGames = new();
    private DateTime _lastScanTime = DateTime.MinValue;

    private readonly List<string> _searchPaths = new()
    {
        @"D:\",
        @"C:\Program Files (x86)\Steam\steamapps\common",
        @"C:\Program Files\Epic Games",
        @"C:\Program Files\Rockstar Games"
    };

    public GameDiscoveryService(ILoggingService logger)
    {
        _logger = logger;
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DellGamingControlCenter");
        _cacheFilePath = Path.Combine(appData, "game_cache.json");
        LoadCache();
    }

    public IEnumerable<DiscoveredGame> GetCachedGames()
    {
        return _cachedGames;
    }

    public async Task<IEnumerable<DiscoveredGame>> ScanForGamesAsync(bool forceRescan = false)
    {
        if (!forceRescan && _cachedGames.Any() && (DateTime.UtcNow - _lastScanTime).TotalHours < 12)
        {
            _logger.Log("Using cached games list.", LogSeverity.Info, "Games");
            return _cachedGames;
        }

        _logger.Log("Starting async game discovery scan...", LogSeverity.Info, "Games");
        var games = new List<DiscoveredGame>();

        var targetExecutables = new[] { 
            "gta-sa.exe", "gta-vc.exe", "GTAIV.exe", "speed.exe", "NFS13.exe", "NFS14.exe", 
            "eurotrucks2.exe", "FarmingSimulator2019Game.exe", "HMA.exe", "Car Mechanic Simulator 2021.exe", "RDR.exe",
            "Launcher.exe", "PlayGTAV.exe"
        };

        await Task.Run(() =>
        {
            foreach (var path in _searchPaths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        var directories = Directory.GetDirectories(path).Where(d => 
                            d.Contains("Game", StringComparison.OrdinalIgnoreCase) || 
                            d.Contains("GTA", StringComparison.OrdinalIgnoreCase) ||
                            d.Contains("Steam", StringComparison.OrdinalIgnoreCase) ||
                            d.Contains("Sim", StringComparison.OrdinalIgnoreCase) ||
                            d.Contains("Need-for-Speed", StringComparison.OrdinalIgnoreCase) ||
                            d.Contains("RDR", StringComparison.OrdinalIgnoreCase) ||
                            d.Contains("Hitman", StringComparison.OrdinalIgnoreCase));

                        foreach(var dir in directories)
                        {
                            var exes = Directory.GetFiles(dir, "*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3 })
                                .Where(f => targetExecutables.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));

                            foreach(var exe in exes)
                            {
                                var info = new FileInfo(exe);
                                if (!games.Any(g => string.Equals(g.ExecutablePath, info.FullName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    games.Add(new DiscoveredGame
                                    {
                                        Name = Path.GetFileNameWithoutExtension(info.Name),
                                        ExecutablePath = info.FullName,
                                        InstallDirectory = info.DirectoryName ?? "",
                                        ExecutableSizeBytes = info.Length,
                                        InstalledOnDriveType = info.FullName.StartsWith("D:", StringComparison.OrdinalIgnoreCase) ? StorageType.HDD : StorageType.SSD,
                                        DriveLetter = Path.GetPathRoot(info.FullName) ?? ""
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"Error scanning {path}: {ex.Message}", LogSeverity.Warning, "Games");
                    }
                }
            }
        });
        
        // Merge with existing cache (keep manual adds)
        foreach (var newGame in games)
        {
            if (!_cachedGames.Any(g => string.Equals(g.ExecutablePath, newGame.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                _cachedGames.Add(newGame);
            }
        }

        _lastScanTime = DateTime.UtcNow;
        SaveCache();
        
        _logger.Log($"Scan complete. Total {games.Count} discovered.", LogSeverity.Success, "Games");
        return _cachedGames;
    }

    public DiscoveredGame? AddGameManually(string executablePath)
    {
        if (File.Exists(executablePath))
        {
            if (_cachedGames.Any(g => string.Equals(g.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)))
            {
                return _cachedGames.First(g => string.Equals(g.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
            }

            var info = new FileInfo(executablePath);
            var game = new DiscoveredGame
            {
                Name = Path.GetFileNameWithoutExtension(info.Name),
                ExecutablePath = info.FullName,
                InstallDirectory = info.DirectoryName ?? "",
                ExecutableSizeBytes = info.Length,
                InstalledOnDriveType = info.FullName.StartsWith("D:", StringComparison.OrdinalIgnoreCase) ? StorageType.HDD : StorageType.SSD,
                DriveLetter = Path.GetPathRoot(info.FullName) ?? ""
            };
            
            _cachedGames.Add(game);
            SaveCache();
            _logger.Log($"Manually added game: {game.Name}", LogSeverity.Success, "Games");
            return game;
        }
        return null;
    }

    public bool RemoveGame(string gameId)
    {
        var game = _cachedGames.FirstOrDefault(g => g.Id == gameId);
        if (game != null)
        {
            _cachedGames.Remove(game);
            SaveCache();
            return true;
        }
        return false;
    }

    private void LoadCache()
    {
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                var json = File.ReadAllText(_cacheFilePath);
                var loaded = JsonSerializer.Deserialize<List<DiscoveredGame>>(json);
                if (loaded != null)
                {
                    _cachedGames = loaded;
                }
            }
        }
        catch { }
    }

    private void SaveCache()
    {
        try
        {
            var json = JsonSerializer.Serialize(_cachedGames, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.Log($"Failed to save game cache: {ex.Message}", LogSeverity.Error, "Games");
        }
    }
}
