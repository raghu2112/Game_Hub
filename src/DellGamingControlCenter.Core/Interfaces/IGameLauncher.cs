namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

public interface IGameLauncher
{
    /// <summary>Prepares, optimizes, and launches a game.</summary>
    Task<GameSession> LaunchGameAsync(GameProfile profile, CancellationToken cancellationToken);

    /// <summary>Locates the actual running game process after launch.</summary>
    Task<int?> FindGameProcessAsync(GameSession session, GameProfile profile, CancellationToken cancellationToken);

    /// <summary>Applies runtime optimizations (e.g. process priority) to the found process.</summary>
    Task<bool> ApplyRuntimeOptimizationsAsync(GameSession session, int processId);
}
