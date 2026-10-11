namespace DellGamingControlCenter.Services;

using System.Threading.Tasks;
using DellGamingControlCenter.Core.Interfaces;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using DellGamingControlCenter.Core.Models;

public class BenchmarkService : IBenchmarkService
{
    private readonly string _baseDir;

    public BenchmarkService()
    {
        // Installed runs live under Program Files — writable user data must go to
        // %LocalAppData%\DellGamingControlCenter (see review finding: UnauthorizedAccessException).
        _baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DellGamingControlCenter", "benchmarks");
        try
        {
            if (!Directory.Exists(_baseDir))
            {
                Directory.CreateDirectory(_baseDir);
            }
        }
        catch (Exception)
        {
            // Never fail startup over benchmark cache: degrade to a temp location instead.
            _baseDir = Path.Combine(Path.GetTempPath(), "GamerHub_benchmarks");
        }
    }

    /// <summary>Real hardware identity for benchmark metadata — no vendor branding, no hardcoding.</summary>
    private static string GetHardwareSnapshot()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                var manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "";
                var model = obj["Model"]?.ToString()?.Trim() ?? "";
                var system = string.Join(" ", new[] { manufacturer, model }.Where(s => s.Length > 0));
                return string.IsNullOrEmpty(system) ? "Unknown system" : system;
            }
        }
        catch
        {
            // WMI unavailable — keep the benchmark functional with a generic label.
        }
        return "Unknown system";
    }

    private string GetGameDir(string gameId)
    {
        var dir = Path.Combine(_baseDir, gameId);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    public Task<BenchmarkRun> StartBenchmarkAsync(GameProfile profile, BenchmarkType type, bool isBaseline)
    {
        var run = new BenchmarkRun
        {
            GameId = profile.GameId,
            GameName = profile.GameName,
            Type = type,
            IsBaseline = isBaseline,
            Timestamp = DateTime.UtcNow,
            OsVersion = Environment.OSVersion.VersionString,
            HardwareSnapshot = GetHardwareSnapshot(),
            OptimizationStateHash = $"Gpu:{profile.PreferredGpu},Power:{profile.PerformanceMode},Priority:{profile.ProcessPriority}"
        };

        return Task.FromResult(run);
    }

    public async Task<BenchmarkRun> StopBenchmarkAsync(BenchmarkRun currentRun, SessionReport report)
    {
        currentRun.Duration = report.Duration;
        
        currentRun.Result.HealthScore = report.HealthScore;
        currentRun.Result.Bottlenecks = report.Bottlenecks.Select(b => b.Type.ToString()).ToList();
        
        // Calculate statistics - in real life this uses the timeline events and snapshots
        currentRun.Result.CpuUsage = new MetricStatistics 
        { 
            IsAvailable = true, 
            Max = report.PeakCpuUsage 
        };
        currentRun.Result.RamUsage = new MetricStatistics 
        { 
            IsAvailable = true, 
            Max = report.PeakRamUsageBytes 
        };
        
        // Unavailable metrics
        currentRun.Result.GpuUsage = new MetricStatistics { IsAvailable = false };
        currentRun.Result.Fps = new MetricStatistics { IsAvailable = false };
        currentRun.Result.Temperature = new MetricStatistics { IsAvailable = false };
        currentRun.Result.Storage = new MetricStatistics { IsAvailable = false };

        // Save
        var dir = GetGameDir(currentRun.GameId);
        var file = Path.Combine(dir, $"{currentRun.BenchmarkId}.json");
        var json = JsonSerializer.Serialize(currentRun, new JsonSerializerOptions { WriteIndented = true });
        
        await File.WriteAllTextAsync(file, json);
        
        await CleanupOldBenchmarksAsync(currentRun.GameId);
        
        return currentRun;
    }

    public async Task<List<BenchmarkRun>> GetHistoryAsync(string gameId)
    {
        var dir = GetGameDir(gameId);
        var files = Directory.GetFiles(dir, "*.json");
        var list = new List<BenchmarkRun>();
        
        foreach(var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var run = JsonSerializer.Deserialize<BenchmarkRun>(json);
                if (run != null)
                {
                    list.Add(run);
                }
            }
            catch { /* Ignore corrupted */ }
        }
        
        return list.OrderByDescending(r => r.Timestamp).ToList();
    }

    public Task<BenchmarkComparison> CompareBenchmarksAsync(BenchmarkRun baseline, BenchmarkRun optimized)
    {
        var comp = new BenchmarkComparison
        {
            Baseline = baseline,
            Optimized = optimized
        };

        if (baseline.HardwareSnapshot != optimized.HardwareSnapshot)
        {
            comp.HardwareMismatchWarning = "Hardware configuration changed between benchmarks.";
        }
        
        if (baseline.Result.CpuUsage.IsAvailable && optimized.Result.CpuUsage.IsAvailable)
        {
            var diff = baseline.Result.CpuUsage.Average - optimized.Result.CpuUsage.Average;
            comp.Improvements["CpuAvg"] = diff;
        }

        if (baseline.Result.RamUsage.IsAvailable && optimized.Result.RamUsage.IsAvailable)
        {
            var diff = baseline.Result.RamUsage.Max - optimized.Result.RamUsage.Max;
            comp.Improvements["RamPeak"] = diff; // bytes saved
        }
        
        comp.Improvements["HealthScore"] = optimized.Result.HealthScore - baseline.Result.HealthScore;

        return Task.FromResult(comp);
    }

    public async Task ExportToCsvAsync(BenchmarkRun run, string path)
    {
        var csv = $"BenchmarkId,Game,Type,Baseline,Duration,CpuMax,RamMax,HealthScore\n";
        csv += $"{run.BenchmarkId},{run.GameName},{run.Type},{run.IsBaseline},{run.Duration},{run.Result.CpuUsage.Max},{run.Result.RamUsage.Max},{run.Result.HealthScore}\n";
        
        await File.WriteAllTextAsync(path, csv);
    }

    public async Task ExportToJsonAsync(BenchmarkRun run, string path)
    {
        var json = JsonSerializer.Serialize(run, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    public Task CleanupOldBenchmarksAsync(string gameId)
    {
        var dir = GetGameDir(gameId);
        var files = Directory.GetFiles(dir, "*.json").Select(f => new FileInfo(f)).OrderByDescending(f => f.CreationTime).ToList();
        
        if (files.Count > 100)
        {
            for (int i = 100; i < files.Count; i++)
            {
                try
                {
                    files[i].Delete();
                }
                catch { }
            }
        }
        
        return Task.CompletedTask;
    }
}
