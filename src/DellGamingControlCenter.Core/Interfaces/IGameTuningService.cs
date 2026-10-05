namespace DellGamingControlCenter.Core.Interfaces;

using System.Collections.Generic;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Models;

public interface IGameTuningService
{
    Task<GameTuningRecommendation> GenerateTuningRecommendationAsync(GameProfile profile);
    Task<GamePerformanceHistory> GetPerformanceHistoryAsync(string gameId);
}
