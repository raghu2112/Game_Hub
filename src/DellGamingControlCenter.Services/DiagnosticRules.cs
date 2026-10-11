namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class HighMemoryUsageRule : IDiagnosticRule
{
    public IEnumerable<DiagnosticFinding> Evaluate(IEnumerable<DiagnosticSnapshot> history)
    {
        var recent = history.OrderByDescending(x => x.Timestamp).Take(5).ToList();
        if (recent.Count < 3) yield break;

        var usages = recent.Select(x => x.RamMetrics.UsagePercent).ToList();
        var avgUsage = usages.Average();

        if (avgUsage > 92)
        {
            yield return new DiagnosticFinding
            {
                Category = DiagnosticCategory.Memory,
                Severity = DiagnosticSeverity.Critical,
                Title = "Critical Memory Usage",
                Description = "System memory is almost entirely consumed.",
                ObservedValue = $"{avgUsage:F1}%",
                ReferenceRange = "< 85%",
                Confidence = DiagnosticConfidence.High,
                Evidence = "Memory usage exceeds 92% over multiple consecutive snapshots.",
                RecommendedAction = "Close heavy background applications immediately to prevent system instability."
            };
        }
        else if (avgUsage > 85)
        {
            yield return new DiagnosticFinding
            {
                Category = DiagnosticCategory.Memory,
                Severity = DiagnosticSeverity.Warning,
                Title = "High Memory Pressure",
                Description = "High memory usage detected.",
                ObservedValue = $"{avgUsage:F1}%",
                ReferenceRange = "< 85%",
                Confidence = DiagnosticConfidence.High,
                Evidence = "Memory usage exceeds 85% over multiple consecutive snapshots.",
                RecommendedAction = "Consider closing unnecessary applications before launching games."
            };
        }
        else if (avgUsage > 70)
        {
            yield return new DiagnosticFinding
            {
                Category = DiagnosticCategory.Memory,
                Severity = DiagnosticSeverity.Notice,
                Title = "Elevated Memory Usage",
                Description = "Memory usage is elevated but not critical.",
                ObservedValue = $"{avgUsage:F1}%",
                ReferenceRange = "< 70%",
                Confidence = DiagnosticConfidence.High,
                Evidence = "Memory usage exceeds 70%.",
                RecommendedAction = "No action required, but keep an eye on memory if performance drops."
            };
        }
    }
}

public class HighCpuUsageRule : IDiagnosticRule
{
    public IEnumerable<DiagnosticFinding> Evaluate(IEnumerable<DiagnosticSnapshot> history)
    {
        var recent = history.OrderByDescending(x => x.Timestamp).Take(5).ToList();
        if (recent.Count < 3) yield break;

        var cpuAvails = recent.Where(x => x.CpuMetrics.UsagePercent > 0).ToList();
        if (cpuAvails.Count < 3) yield break; // Need valid data

        var avgUsage = cpuAvails.Average(x => x.CpuMetrics.UsagePercent);

        if (avgUsage > 90)
        {
            yield return new DiagnosticFinding
            {
                Category = DiagnosticCategory.CPU,
                Severity = DiagnosticSeverity.Warning,
                Title = "Possible CPU Bottleneck",
                Description = "Sustained high CPU usage detected.",
                ObservedValue = $"{avgUsage:F1}%",
                ReferenceRange = "< 90%",
                Confidence = DiagnosticConfidence.Medium,
                Evidence = "CPU utilization exceeded 90% over a sustained observation window.",
                RecommendedAction = "Close background applications to free up CPU resources."
            };
        }
    }
}

public class GameOnHddRule : IDiagnosticRule
{
    public IEnumerable<DiagnosticFinding> Evaluate(IEnumerable<DiagnosticSnapshot> history)
    {
        var latest = history.OrderByDescending(x => x.Timestamp).FirstOrDefault();
        if (latest?.GameExecutablePath == null) yield break;

        var exePath = latest.GameExecutablePath;
        var hasHdd = latest.Hardware.Storage.Any(d => d.Type == Core.Enums.StorageType.HDD);
        
        if (hasHdd)
        {
            yield return new DiagnosticFinding
            {
                Category = DiagnosticCategory.Storage,
                Severity = DiagnosticSeverity.Notice,
                Title = "Game Installed on HDD",
                Description = "The active game is running from a mechanical hard drive.",
                ObservedValue = "HDD",
                ReferenceRange = "SSD",
                Confidence = DiagnosticConfidence.High,
                Evidence = $"Game executable is {exePath}. An HDD was detected on the system which can cause slow load times.",
                RecommendedAction = "If sufficient SSD space is available, consider moving frequently played games to the SSD to improve load times."
            };
        }
    }
}

public class IntegratedGpuPreferenceRule : IDiagnosticRule
{
    public IEnumerable<DiagnosticFinding> Evaluate(IEnumerable<DiagnosticSnapshot> history)
    {
        var latest = history.OrderByDescending(x => x.Timestamp).FirstOrDefault();
        if (latest?.GameExecutablePath == null) yield break;

        if (latest.ActiveGpuPreference == Core.Enums.GpuPreference.Integrated)
        {
            yield return new DiagnosticFinding
            {
                Category = DiagnosticCategory.GPU,
                Severity = DiagnosticSeverity.Warning,
                Title = "Integrated GPU Selected",
                Description = "Game is configured to use the integrated GPU.",
                ObservedValue = "Integrated",
                ReferenceRange = "HighPerformance",
                Confidence = DiagnosticConfidence.High,
                Evidence = "Windows GPU preference for the game executable is explicitly set to Integrated.",
                RecommendedAction = "Consider switching the GPU preference to High Performance (Radeon 520) for this game."
            };
        }
    }
}

public class PowerPlanMismatchRule : IDiagnosticRule
{
    public IEnumerable<DiagnosticFinding> Evaluate(IEnumerable<DiagnosticSnapshot> history)
    {
        var latest = history.OrderByDescending(x => x.Timestamp).FirstOrDefault();
        if (latest?.GameExecutablePath == null) yield break;
        if (string.IsNullOrEmpty(latest.ActivePowerSchemeGuid)) yield break;

        // Assuming High Performance plan guid is known (e.g. 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c)
        // If not, we can just say if it's not the game mode's target plan. 
        // For now, if Game is active but plan is power saver (a1841308-3541-4fab-bc81-f71556f20b4a).
        if (latest.ActivePowerSchemeGuid.Equals("a1841308-3541-4fab-bc81-f71556f20b4a", StringComparison.OrdinalIgnoreCase))
        {
            yield return new DiagnosticFinding
            {
                Category = DiagnosticCategory.Power,
                Severity = DiagnosticSeverity.Warning,
                Title = "Power Saver Plan Active",
                Description = "A game is active but Windows is in Power Saver mode.",
                ObservedValue = "Power Saver",
                ReferenceRange = "Balanced or High Performance",
                Confidence = DiagnosticConfidence.High,
                Evidence = "The active power scheme GUID matches the Windows Power Saver plan.",
                RecommendedAction = "Switch to a gaming or high-performance power plan."
            };
        }
    }
}

public class BackgroundResourceRule : IDiagnosticRule
{
    public IEnumerable<DiagnosticFinding> Evaluate(IEnumerable<DiagnosticSnapshot> history)
    {
        var latest = history.OrderByDescending(x => x.Timestamp).FirstOrDefault();
        if (latest == null) yield break;

        var heavyProcs = latest.Processes
            .Where(p => p.WorkingSetBytes > 1024 * 1024 * 500) // > 500 MB
            .Where(p => latest.GameExecutablePath == null || !latest.GameExecutablePath.Contains(p.Name))
            .ToList();

        foreach (var proc in heavyProcs)
        {
            yield return new DiagnosticFinding
            {
                Category = DiagnosticCategory.Process,
                Severity = DiagnosticSeverity.Notice,
                Title = "High-Resource Background Process",
                Description = $"Potential background resource consumer: {proc.Name}",
                ObservedValue = $"{proc.WorkingSetBytes / 1024 / 1024} MB",
                ReferenceRange = "< 500 MB",
                Confidence = DiagnosticConfidence.High,
                Evidence = $"Process {proc.Name} is consuming significant RAM.",
                RecommendedAction = "Close if unrelated to gaming."
            };
        }
    }
}
