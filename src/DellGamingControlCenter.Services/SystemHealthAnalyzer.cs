namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class SystemHealthAnalyzer : ISystemHealthAnalyzer
{
    public GamingHealthScore CalculateHealthScore(IEnumerable<DiagnosticSnapshot> history)
    {
        var snaps = history.ToList();
        if (snaps.Count == 0) return new GamingHealthScore { OverallScore = 100 };

        double avgCpu = snaps.Average(s => s.CpuMetrics.UsagePercent);
        double avgGpu = snaps.Average(s => s.GpuMetrics.UsagePercent ?? 0);
        double avgRam = snaps.Average(s => s.RamMetrics.TotalBytes > 0 ? (double)s.RamMetrics.UsedBytes / s.RamMetrics.TotalBytes : 0);
        
        int cpuScore = 100 - (int)(Math.Max(0, avgCpu - 50) * 2); // Drops fast after 50%
        int gpuScore = 100 - (int)(Math.Max(0, avgGpu - 80) * 3); // Drops fast after 80%
        int ramScore = 100 - (int)(Math.Max(0, avgRam - 0.70) * 200); // Drops fast after 70%
        
        cpuScore = Math.Clamp(cpuScore, 0, 100);
        gpuScore = Math.Clamp(gpuScore, 0, 100);
        ramScore = Math.Clamp(ramScore, 0, 100);

        var score = new GamingHealthScore
        {
            CpuScore = cpuScore,
            GpuScore = gpuScore,
            RamScore = ramScore,
            StorageScore = 100, // Placeholder until deep HDD analytics
            ThermalScore = 100, // Placeholder until thermals are active
            BackgroundScore = 100 // Placeholder
        };

        score.OverallScore = (score.CpuScore + score.GpuScore + score.RamScore + score.StorageScore + score.ThermalScore + score.BackgroundScore) / 6;

        if (score.GpuScore < 50) score.Deductions.Add("GPU sustained saturation");
        if (score.CpuScore < 50) score.Deductions.Add("CPU sustained saturation");
        if (score.RamScore < 50) score.Deductions.Add("High RAM pressure");

        return score;
    }
}
