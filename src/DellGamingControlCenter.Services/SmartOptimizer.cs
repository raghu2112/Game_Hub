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
    private readonly IOptimizationEngine _baseOptimizationEngine;

    public SmartOptimizer(
        IDiagnosticEngine diagnosticEngine, 
        IRecommendationEngine recommendationEngine, 
        IMetricsSampler sampler,
        IOptimizationEngine baseOptimizationEngine)
    {
        _diagnosticEngine = diagnosticEngine;
        _recommendationEngine = recommendationEngine;
        _sampler = sampler;
        _baseOptimizationEngine = baseOptimizationEngine;
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

        var bottlenecks = await _diagnosticEngine.RunDiagnosticAnalysisAsync(history);
        var recommendations = _recommendationEngine.GenerateRecommendations(bottlenecks, profile);

        // Map recommendations to actual optimization plan actions
        var basePlan = _baseOptimizationEngine.GeneratePlan(profile);

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
