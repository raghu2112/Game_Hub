namespace DellGamingControlCenter.Services;

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Interfaces;

public class GameTuningService : IGameTuningService
{
    private readonly IBenchmarkService _benchmarkService;
    private readonly IConfigurationService _configService;

    public GameTuningService(IBenchmarkService benchmarkService, IConfigurationService configService)
    {
        _benchmarkService = benchmarkService;
        _configService = configService;
    }

    public async Task<GameTuningRecommendation> GenerateTuningRecommendationAsync(GameProfile profile)
    {
        var history = await GetPerformanceHistoryAsync(profile.GameId);
        var benchmarks = await _benchmarkService.GetHistoryAsync(profile.GameId);
        
        var rec = new GameTuningRecommendation
        {
            GameId = profile.GameId
        };
        
        if (history.SessionCount == 0 && history.BenchmarkCount == 0)
        {
            rec.RecommendedPowerMode = "Balanced"; // Default safe
            rec.RecommendedGpuPreference = "Default";
            rec.Confidence = "Low";
            rec.Reason = "Based on hardware capability only (no previous sessions).";
            return rec;
        }

        // We have data. Do we have benchmarks?
        if (benchmarks.Any())
        {
            // Analyze benchmarks
            var bestBenchmark = benchmarks.OrderByDescending(b => b.Result.HealthScore).First();
            rec.Reason = $"Based on {benchmarks.Count} previous benchmarks.";
            
            // Reconstruct the optimization state that produced this best score.
            // (In a real implementation we would parse OptimizationStateHash)
            if (bestBenchmark.Result.HealthScore > 80)
            {
                rec.RecommendedPowerMode = "BestPerformance";
                rec.RecommendedGpuPreference = "HighPerformance";
            }
            
            rec.Confidence = benchmarks.Count >= 3 ? "High" : "Medium";
            
            // Graphics Recommendations
            rec.GraphicsRecommendations = new List<string>
            {
                "Texture Quality: Low / Medium",
                "Shadow Quality: Low",
                "Ambient Occlusion: Off",
                "Anti-Aliasing: Low",
                "Resolution: 1280x720 or 1600x900 depending on benchmark"
            };
        }
        else
        {
            // Rely on session history
            rec.Reason = $"Observed in {history.SessionCount} previous sessions.";
            rec.Confidence = "Medium";
            
            if (history.PrimaryBottleneck == "CPU")
            {
                rec.RecommendedPowerMode = "BestPerformance";
                rec.RecommendedPriority = "AboveNormal";
            }
        }
        
        return rec;
    }

    public async Task<GamePerformanceHistory> GetPerformanceHistoryAsync(string gameId)
    {
        var sessions = _configService.GetConfig().SessionHistory.Where(s => s.GameId == gameId).ToList();
        var benchmarks = await _benchmarkService.GetHistoryAsync(gameId);
        
        var history = new GamePerformanceHistory
        {
            SessionCount = sessions.Count,
            BenchmarkCount = benchmarks.Count
        };
        
        if (sessions.Any())
        {
            var validScores = sessions.Where(s => s.Report != null).Select(s => s.Report!.HealthScore).ToList();
            if (validScores.Any())
            {
                history.BestHealthScore = validScores.Max();
                history.WorstHealthScore = validScores.Min();
                history.AverageHealthScore = (int)validScores.Average();
            }
            
            var bottlenecks = sessions.Where(s => s.Report != null)
                .SelectMany(s => s.Report!.Bottlenecks)
                .GroupBy(b => b.Type)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
                
            if (bottlenecks != null)
            {
                history.PrimaryBottleneck = bottlenecks.Key.ToString();
                history.BottleneckConfidence = bottlenecks.Count() > 3 ? "High" : "Medium";
            }
        }
        
        return history;
    }
}
