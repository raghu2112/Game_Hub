namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

public interface IGameLauncher
{
    /// <summary>Prepares, optimizes, and launches a game.</summary>
    Task<GameSession> LaunchGameAsync(GameProfile profile, CancellationToken cancellationToken);

    Task<int?> FindGameProcessAsync(GameSession session, GameProfile profile, CancellationToken cancellationToken);
}
