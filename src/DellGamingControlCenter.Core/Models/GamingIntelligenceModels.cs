namespace DellGamingControlCenter.Core.Models;

using System;
using DellGamingControlCenter.Core.Enums;

public enum InsightType
{
    Fact,
    DerivedMetric,
    Inference
}

public enum InsightScope
{
    System,
    Game,
    Session
}

public class GamingInsight
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Detail { get; init; } = "";
    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Info;
    public DiagnosticCategory Category { get; init; } = DiagnosticCategory.System;
    public DiagnosticConfidence Confidence { get; init; } = DiagnosticConfidence.High;
    public InsightType Type { get; init; } = InsightType.Fact;
    public InsightScope Scope { get; init; } = InsightScope.System;
    
    // Evidence string explaining the logic
    public string Evidence { get; init; } = "";
    
    // Actionable recommendation string
    public string Recommendation { get; init; } = "";
    
    // Associated entities
    public string? RelatedGameId { get; init; }
    public string? RelatedSessionId { get; init; }
}

public class OptimizationRecommendation
{
    public string Title { get; init; } = "";
    public string Reason { get; init; } = "";
    public PriorityLevel Priority { get; init; }
    public ImpactLevel ExpectedImpact { get; init; }
    public RiskLevel Risk { get; init; }
    public string Source { get; init; } = "";
    public OptimizationAction? CorrelatedAction { get; init; }
}

public class BottleneckResult
{
    public BottleneckType Type { get; init; }
    public Severity Severity { get; init; }
    public ConfidenceLevel Confidence { get; init; }
    public string Evidence { get; init; } = "";
    public string Explanation { get; init; } = "";
    public TimeSpan Duration { get; init; }
}

public class GamingHealthScore
{
    public int OverallScore { get; set; }
    public int CpuScore { get; set; }
    public int GpuScore { get; set; }
    public int RamScore { get; set; }
    public int StorageScore { get; set; }
    public int ThermalScore { get; set; }
    public int BackgroundScore { get; set; }
    public System.Collections.Generic.List<string> Deductions { get; set; } = new();
}
