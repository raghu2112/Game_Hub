namespace DellGamingControlCenter.Core.Interfaces;

using System.Collections.Generic;
using DellGamingControlCenter.Core.Models;

public interface IRecommendationEngine
{
    /// <summary>Generates actionable recommendations based on identified bottlenecks.</summary>
    List<OptimizationRecommendation> GenerateRecommendations(IEnumerable<DiagnosticFinding> bottlenecks, GameProfile profile);
}
