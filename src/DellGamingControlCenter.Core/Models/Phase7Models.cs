namespace DellGamingControlCenter.Core.Models;

using System;
using System.Collections.Generic;

public enum BenchmarkType
{
    Quick,
    Standard,
    Extended
}

public class BenchmarkRun
{
    public string BenchmarkId { get; set; } = Guid.NewGuid().ToString();
    public string GameId { get; set; } = "";
    public string GameName { get; set; } = "";
    public BenchmarkType Type { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }
    public bool IsBaseline { get; set; }
    
    public string OsVersion { get; set; } = "";
    public string HardwareSnapshot { get; set; } = "";
    public string GpuDriverVersion { get; set; } = "Unavailable";
    
    public string OptimizationStateHash { get; set; } = "";
    
    public BenchmarkResult Result { get; set; } = new BenchmarkResult();
}

public class BenchmarkResult
{
    public int HealthScore { get; set; }
    
    public MetricStatistics CpuUsage { get; set; } = new MetricStatistics();
    public MetricStatistics RamUsage { get; set; } = new MetricStatistics();
    public MetricStatistics GpuUsage { get; set; } = new MetricStatistics();
    public MetricStatistics Fps { get; set; } = new MetricStatistics();
    public MetricStatistics Storage { get; set; } = new MetricStatistics();
    public MetricStatistics Temperature { get; set; } = new MetricStatistics();
    
    public List<string> Bottlenecks { get; set; } = new List<string>();
}

public class MetricStatistics
{
    public bool IsAvailable { get; set; }
    public double Average { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Median { get; set; }
    public double P95 { get; set; }
    public double P99 { get; set; }
}

public class BenchmarkComparison
{
    public BenchmarkRun Baseline { get; set; } = null!;
    public BenchmarkRun Optimized { get; set; } = null!;
    public string HardwareMismatchWarning { get; set; } = "";
    
    public Dictionary<string, double> Improvements { get; set; } = new Dictionary<string, double>();
}

public class GameTuningRecommendation
{
    public string GameId { get; set; } = "";
    public string RecommendedPowerMode { get; set; } = "Balanced";
    public string RecommendedGpuPreference { get; set; } = "Default";
    public string RecommendedPriority { get; set; } = "Normal";
    public string BackgroundManagement { get; set; } = "Recommended";
    public string Confidence { get; set; } = "Low"; // Low, Medium, High
    public string Reason { get; set; } = "";
    public string RiskLevel { get; set; } = "Low";
    
    public List<string> GraphicsRecommendations { get; set; } = new List<string>();
}

public class GamePerformanceHistory
{
    public int SessionCount { get; set; }
    public int BenchmarkCount { get; set; }
    public int BestHealthScore { get; set; }
    public int WorstHealthScore { get; set; }
    public int AverageHealthScore { get; set; }
    public string PrimaryBottleneck { get; set; } = "None";
    public string BottleneckConfidence { get; set; } = "Low";
}
