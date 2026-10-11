namespace DellGamingControlCenter.Core.Interfaces;

using System.Collections.Generic;
using DellGamingControlCenter.Core.Models;

public interface IGamingIntelligenceEngine
{
    IEnumerable<GamingInsight> GetSystemInsights();
    IEnumerable<GamingInsight> GetGameInsights(string gameId);
    IEnumerable<GamingInsight> GetSessionInsights(GameSession session);
}
