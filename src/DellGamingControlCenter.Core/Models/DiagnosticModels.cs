namespace DellGamingControlCenter.Core.Models;

using System;
using System.Collections.Generic;
using DellGamingControlCenter.Core.Enums;

public sealed class DiagnosticSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // CPU
    public double CpuUtilization { get; set; }
    public double GameProcessCpuUtilization { get; set; }
    public MetricAvailability CpuAvailability { get; set; } = MetricAvailability.Measured;

    // GPU
    public double GpuUtilization { get; set; }
    public MetricAvailability GpuAvailability { get; set; } = MetricAvailability.Unavailable;

    // RAM
    public long TotalRamBytes { get; set; }
    public long UsedRamBytes { get; set; }
    public long AvailableRamBytes { get; set; }
    public MetricAvailability RamAvailability { get; set; } = MetricAvailability.Measured;

    // Storage
    public double DiskActiveTimePercent { get; set; }
    public MetricAvailability StorageAvailability { get; set; } = MetricAvailability.Measured;

    // Thermals
    public double CpuTemperature { get; set; }
    public MetricAvailability ThermalAvailability { get; set; } = MetricAvailability.Unavailable;

    // Process
    public int BackgroundProcessCount { get; set; }
}

public sealed class BottleneckResult
{
    public BottleneckType Type { get; set; } = BottleneckType.None;
    public Severity Severity { get; set; } = Severity.Low;
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Low;
    public string Evidence { get; set; } = "";
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;
    public string Explanation { get; set; } = "";
    public List<OptimizationRecommendation> Recommendations { get; set; } = new();
}

public sealed class OptimizationRecommendation
{
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public PriorityLevel Priority { get; set; } = PriorityLevel.Low; // Normal, High, Low
    public ImpactLevel ExpectedImpact { get; set; } = ImpactLevel.Unknown;
    public RiskLevel Risk { get; set; } = RiskLevel.None;
    public string Source { get; set; } = "";
    public OptimizationAction? CorrelatedAction { get; set; }
}

public enum PriorityLevel
{
    Low,
    Medium,
    High
}

public sealed class GameSettingsRecommendation
{
    public string Setting { get; set; } = "";
    public string CurrentValue { get; set; } = "";
    public string RecommendedValue { get; set; } = "";
    public string Reason { get; set; } = "";
    public ImpactLevel Impact { get; set; } = ImpactLevel.Medium;
    public RiskLevel Risk { get; set; } = RiskLevel.None;
    public string Source { get; set; } = "";
}

public sealed class GamingHealthScore
{
    public int OverallScore { get; set; }
    public int GpuScore { get; set; }
    public int CpuScore { get; set; }
    public int RamScore { get; set; }
    public int StorageScore { get; set; }
    public int ThermalScore { get; set; }
    public int BackgroundScore { get; set; }
    public List<string> Deductions { get; set; } = new();
}
