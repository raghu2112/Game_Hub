namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class BottleneckAnalyzer : IBottleneckAnalyzer
{
    private readonly ILoggingService _logger;

    public BottleneckAnalyzer(ILoggingService logger)
    {
        _logger = logger;
    }

    public List<BottleneckResult> Analyze(IEnumerable<DiagnosticSnapshot> history)
    {
        var results = new List<BottleneckResult>();
        var snaps = history.ToList();
        
        if (snaps.Count < 5)
        {
            _logger.Log("Insufficient data for bottleneck analysis.", LogSeverity.Warning, "Diagnostics");
            return results;
        }

        double avgCpu = snaps.Average(s => s.CpuMetrics.UsagePercent);
        double avgGpu = snaps.Average(s => s.GpuMetrics.UsagePercent ?? 0);
        
        // Ensure availability
        bool cpuAvailable = true;
        bool gpuAvailable = snaps.Any(s => s.GpuMetrics.UsagePercent.HasValue);

        // 1. GPU Bottleneck (High confidence if GPU > 95% and CPU < 85%)
        if (gpuAvailable)
        {
            int sustainedGpuCount = snaps.Count(s => s.GpuMetrics.UsagePercent > 95);
            if (sustainedGpuCount > snaps.Count * 0.7) // 70% of time
            {
                results.Add(new BottleneckResult
                {
                    Type = BottleneckType.GPU,
                    Severity = Severity.High,
                    Confidence = cpuAvailable && avgCpu < 85 ? ConfidenceLevel.High : ConfidenceLevel.Medium,
                    Evidence = $"GPU utilization sustained >95% ({avgGpu:F1}% avg).",
                    Explanation = "Your graphics processor is working near full capacity. This suggests the GPU is currently limiting performance.",
                    Duration = TimeSpan.FromSeconds(snaps.Count)
                });
            }
        }

        // 2. CPU Bottleneck
        if (cpuAvailable)
        {
            int sustainedCpuCount = snaps.Count(s => s.CpuMetrics.UsagePercent > 90);
            if (sustainedCpuCount > snaps.Count * 0.7)
            {
                results.Add(new BottleneckResult
                {
                    Type = BottleneckType.CPU,
                    Severity = Severity.High,
                    Confidence = gpuAvailable && avgGpu < 80 ? ConfidenceLevel.High : ConfidenceLevel.Medium,
                    Evidence = $"CPU utilization sustained >90% ({avgCpu:F1}% avg).",
                    Explanation = "Your CPU is saturated, leaving little headroom for the game logic or feeding the GPU.",
                    Duration = TimeSpan.FromSeconds(snaps.Count)
                });
            }
        }

        // 3. RAM Pressure
        bool ramAvailable = true;
        if (ramAvailable)
        {
            double avgRamUsedPercent = snaps.Average(s => s.RamMetrics.TotalBytes > 0 ? (double)s.RamMetrics.UsedBytes / s.RamMetrics.TotalBytes : 0);
            if (avgRamUsedPercent > 0.85)
            {
                results.Add(new BottleneckResult
                {
                    Type = BottleneckType.Memory,
                    Severity = avgRamUsedPercent > 0.92 ? Severity.Critical : Severity.High,
                    Confidence = ConfidenceLevel.High,
                    Evidence = $"RAM utilization sustained at {(avgRamUsedPercent*100):F1}%.",
                    Explanation = "Available RAM fell below the configured threshold. High memory pressure causes system paging (stuttering).",
                    Duration = TimeSpan.FromSeconds(snaps.Count)
                });
            }
        }
        
        // 4. Thermals
        bool tempAvailable = snaps.Any(s => s.CpuMetrics.TemperatureCelsius.HasValue);
        if (tempAvailable)
        {
            double avgTemp = snaps.Average(s => s.CpuMetrics.TemperatureCelsius ?? 0);
            if (avgTemp > 85)
            {
                results.Add(new BottleneckResult
                {
                    Type = BottleneckType.Thermal,
                    Severity = Severity.High,
                    Confidence = ConfidenceLevel.High,
                    Evidence = $"Temperature sustained >85C ({avgTemp:F1}C avg).",
                    Explanation = "The system is running hot, which may lead to thermal throttling and frame drops.",
                    Duration = TimeSpan.FromSeconds(snaps.Count)
                });
            }
        }

        return results.OrderByDescending(r => r.Severity).ThenByDescending(r => r.Confidence).ToList();
    }
}
