namespace DellGamingControlCenter.Core.Interfaces;

using System.Threading.Tasks;
using System.Collections.Generic;
using DellGamingControlCenter.Core.Models;

/// <summary>
/// Service responsible for discovering installed games across multiple launchers and directories.
/// </summary>
public interface IGameDiscoveryService
{
    /// <summary>Scans common locations and user-defined directories for games asynchronously.</summary>
    Task<IEnumerable<DiscoveredGame>> ScanForGamesAsync(bool forceRescan = false);

    /// <summary>Returns currently cached games.</summary>
    IEnumerable<DiscoveredGame> GetCachedGames();

    /// <summary>Adds a specific game executable manually.</summary>
    DiscoveredGame? AddGameManually(string executablePath);

    /// <summary>Removes a game from the known list.</summary>
    bool RemoveGame(string gameId);
}
