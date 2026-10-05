namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Models;
using System.Threading;
using System.Threading.Tasks;

public interface ISessionMonitoringService
{
    /// <summary>Starts monitoring hardware and process metrics during a session.</summary>
    Task StartMonitoringAsync(GameSession session, CancellationToken cancellationToken);

    /// <summary>Stops monitoring and finalizes session metrics (peak usage, averages).</summary>
    Task StopMonitoringAsync(GameSession session);
}
