namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class RecommendationEngine : IRecommendationEngine
{
    public List<OptimizationRecommendation> GenerateRecommendations(IEnumerable<BottleneckResult> bottlenecks, GameProfile profile)
    {
        var recommendations = new List<OptimizationRecommendation>();
        
        // General GameProfile mismatch checks
        if (profile.PreferredGpu == GpuPreference.Auto)
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Title = "Set GPU Preference to Radeon 520",
                Reason = "Current game preference is not explicitly configured to use the discrete GPU.",
                Priority = PriorityLevel.High,
                ExpectedImpact = ImpactLevel.High,
                Risk = RiskLevel.Low,
                Source = "Hardware Policy",
                CorrelatedAction = new OptimizationAction { Type = "GpuPreference", Target = profile.ExecutablePath, Value = "HighPerformance" }
            });
        }
        
        if (profile.PerformanceMode != PerformanceMode.Gaming)
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Title = "Switch to Gaming Power Mode",
                Reason = $"Current mode is {profile.PerformanceMode}. Gaming mode prioritizes consistent performance.",
                Priority = PriorityLevel.Medium,
                ExpectedImpact = ImpactLevel.Medium,
                Risk = RiskLevel.Low,
                Source = "Power Policy",
                CorrelatedAction = new OptimizationAction { Type = "PowerMode", Target = "System", Value = "Gaming" }
            });
        }

        // Bottleneck-based recommendations
        foreach (var bottleneck in bottlenecks)
        {
            if (bottleneck.Type == BottleneckType.GPU)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Title = "Reduce GPU-heavy graphics settings",
                    Reason = bottleneck.Evidence,
                    Priority = bottleneck.Severity == Severity.Critical ? PriorityLevel.High : PriorityLevel.Medium,
                    ExpectedImpact = ImpactLevel.High,
                    Risk = RiskLevel.None,
                    Source = "Diagnostic Engine"
                });
            }
            else if (bottleneck.Type == BottleneckType.CPU)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Title = "Reduce CPU-sensitive settings (e.g. shadows, draw distance, physics)",
                    Reason = bottleneck.Evidence,
                    Priority = bottleneck.Severity == Severity.Critical ? PriorityLevel.High : PriorityLevel.Medium,
                    ExpectedImpact = ImpactLevel.Medium,
                    Risk = RiskLevel.None,
                    Source = "Diagnostic Engine"
                });
            }
            else if (bottleneck.Type == BottleneckType.Memory)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Title = "Close background applications",
                    Reason = bottleneck.Evidence,
                    Priority = PriorityLevel.High,
                    ExpectedImpact = ImpactLevel.High,
                    Risk = RiskLevel.Low,
                    Source = "Diagnostic Engine",
                    CorrelatedAction = new OptimizationAction { Type = "CloseBackgroundProcesses", Target = "System", Value = "Auto" }
                });
            }
        }

        return recommendations;
    }
}
