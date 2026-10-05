namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

public interface IGameSessionManager
{
    /// <summary>Starts a complete orchestrated game session lifecycle.</summary>
    Task<GameSession> StartSessionAsync(GameProfile profile);

    /// <summary>Signals a manual stop of the automation and monitoring (without necessarily killing the game).</summary>
    Task StopSessionAsync(string sessionId);
    
    /// <summary>Called by the background watcher when the game process actually exits.</summary>
    Task HandleProcessExitAsync(string sessionId);
}
