namespace DellGamingControlCenter.Core.Models;

using System;
using System.Collections.Generic;
using DellGamingControlCenter.Core.Enums;

public sealed class BaselineSnapshot
{
    public double CpuUsage { get; init; }
    public long RamUsageBytes { get; init; }
    public long AvailableRamBytes { get; init; }
    public double DiskActiveTime { get; init; }
    public string? PowerMode { get; init; }
    public bool GameModeEnabled { get; init; }
    public string? GpuPreference { get; init; }
    public int BackgroundProcessCount { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed class PerformanceEvent
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public LogSeverity Severity { get; init; } = LogSeverity.Info;
    public PerformanceEventType Type { get; init; } = PerformanceEventType.Unknown;
    public string Description { get; init; } = "";
    public string Evidence { get; init; } = "";
}

public enum PerformanceEventType
{
    Unknown,
    BottleneckDetected,
    ThermalWarning,
    MemoryPressure,
    StorageSpike,
    ProcessSpike,
    Stutter,
    PowerModeChanged,
    OptimizationApplied,
    OptimizationFailed,
    GameStarted,
    GameExited
}

public sealed class SessionReport
{
    public string SessionId { get; init; } = "";
    public TimeSpan Duration { get; init; }
    public int HealthScore { get; init; }
    
    // CPU
    public double AverageCpuUsage { get; init; }
    public double PeakCpuUsage { get; init; }
    
    // GPU
    public double AverageGpuUsage { get; init; }
    public double PeakGpuUsage { get; init; }
    
    // RAM
    public long AverageRamUsageBytes { get; init; }
    public long PeakRamUsageBytes { get; init; }
    
    // Storage
    public double AverageDiskActiveTime { get; init; }
    public double PeakDiskActiveTime { get; init; }
    
    // Thermals
    public double? PeakTemperature { get; init; }
    
    // FPS
    public FpsStatistics? FpsStats { get; set; }
    
    public List<PerformanceEvent> Events { get; init; } = [];
    public List<OptimizationRecommendation> Recommendations { get; init; } = [];
    public List<BottleneckResult> Bottlenecks { get; init; } = [];
}

public sealed class FpsSample
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public double Fps { get; init; }
    public double FrameTimeMs { get; init; }
}

public sealed class FpsStatistics
{
    public double AverageFPS { get; init; }
    public double MinFPS { get; init; }
    public double MaxFPS { get; init; }
    public double OnePercentLow { get; init; }
    public double ZeroPointOnePercentLow { get; init; }
    public double AverageFrameTimeMs { get; init; }
    public double FrameTimeVariance { get; init; }
}
