namespace DellGamingControlCenter.Services;

using System.Threading.Tasks;
using System.Linq;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class SmartOptimizer : ISmartOptimizer
{
    private readonly IDiagnosticEngine _diagnosticEngine;
    private readonly IRecommendationEngine _recommendationEngine;
    private readonly IMetricsSampler _sampler;

    public SmartOptimizer(
        IDiagnosticEngine diagnosticEngine, 
        IRecommendationEngine recommendationEngine, 
        IMetricsSampler sampler)
    {
        _diagnosticEngine = diagnosticEngine;
        _recommendationEngine = recommendationEngine;
        _sampler = sampler;
    }

    public async Task<OptimizationPlan> GenerateSmartPlanAsync(GameProfile profile)
    {
        // Capture baseline
        var snapshot = await _diagnosticEngine.CaptureSnapshotAsync();
        
        // Analyze recent history if available
        var history = _sampler.GetHistory().ToList();
        if (history.Count == 0)
        {
            history.Add(snapshot);
        }

        var report = await _diagnosticEngine.RunDiagnosticAnalysisAsync(history);
        var recommendations = _recommendationEngine.GenerateRecommendations(report.Findings, profile);

        // Map recommendations to actual optimization plan actions
        var basePlan = new OptimizationPlan
        {
            GameId = profile.GameId,
            GameName = profile.GameName
        };

        if (profile.PreferredGpu != Core.Enums.GpuPreference.Auto)
        {
            basePlan.Actions.Add(new OptimizationAction { Type = "GpuPreference", Target = profile.ExecutablePath, Value = profile.PreferredGpu.ToString() });
        }
        if (profile.ProcessPriority != Core.Enums.ProcessPriorityClass.Normal)
        {
            basePlan.Actions.Add(new OptimizationAction { Type = "ProcessPriority", Target = profile.ExecutablePath, Value = profile.ProcessPriority.ToString() });
        }

        foreach (var rec in recommendations)
        {
            if (rec.CorrelatedAction != null && !basePlan.Actions.Any(a => a.Type == rec.CorrelatedAction.Type))
            {
                var action = new OptimizationAction
                {
                    Type = rec.CorrelatedAction.Type,
                    Target = rec.CorrelatedAction.Target,
                    Value = rec.CorrelatedAction.Value,
                    Description = rec.Title,
                    EstimatedBenefit = rec.Reason,
                    RiskLevel = rec.Risk.ToString()
                };
                basePlan.Actions.Add(action);
            }
        }

        return basePlan;
    }
}
