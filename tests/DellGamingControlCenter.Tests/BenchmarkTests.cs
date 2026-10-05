namespace DellGamingControlCenter.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Services;
using DellGamingControlCenter.Core.Interfaces;

public class BenchmarkTests : IDisposable
{
    private readonly BenchmarkService _benchmarkService;
    private readonly string _baseDir;

    public BenchmarkTests()
    {
        _benchmarkService = new BenchmarkService();
        _baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "benchmarks");
    }
    
    public void Dispose()
    {
        if (Directory.Exists(_baseDir))
        {
            Directory.Delete(_baseDir, true);
        }
    }

    [Fact]
    public async Task StartBenchmark_CreatesBaseline()
    {
        var profile = new GameProfile { GameId = "test-123", GameName = "Test Game" };
        var run = await _benchmarkService.StartBenchmarkAsync(profile, BenchmarkType.Standard, true);
        
        Assert.NotNull(run);
        Assert.True(run.IsBaseline);
        Assert.Equal("test-123", run.GameId);
    }

    [Fact]
    public async Task StopBenchmark_CalculatesStatsAndSaves()
    {
        var profile = new GameProfile { GameId = "test-123", GameName = "Test Game" };
        var run = await _benchmarkService.StartBenchmarkAsync(profile, BenchmarkType.Standard, true);
        
        var report = new SessionReport 
        { 
            HealthScore = 85,
            PeakCpuUsage = 90,
            PeakRamUsageBytes = 8000000000
        };
        
        var result = await _benchmarkService.StopBenchmarkAsync(run, report);
        
        Assert.Equal(85, result.Result.HealthScore);
        Assert.True(result.Result.CpuUsage.IsAvailable);
        Assert.Equal(90, result.Result.CpuUsage.Max);
        Assert.False(result.Result.GpuUsage.IsAvailable); // Unavailable metric
        
        var history = await _benchmarkService.GetHistoryAsync("test-123");
        Assert.Single(history);
    }

    [Fact]
    public async Task CompareBenchmarks_DetectsImprovements()
    {
        var baseline = new BenchmarkRun { HardwareSnapshot = "HW1" };
        baseline.Result.CpuUsage = new MetricStatistics { IsAvailable = true, Average = 80 };
        baseline.Result.HealthScore = 70;
        
        var optimized = new BenchmarkRun { HardwareSnapshot = "HW1" };
        optimized.Result.CpuUsage = new MetricStatistics { IsAvailable = true, Average = 60 };
        optimized.Result.HealthScore = 80;
        
        var comparison = await _benchmarkService.CompareBenchmarksAsync(baseline, optimized);
        
        Assert.Empty(comparison.HardwareMismatchWarning);
        Assert.Equal(20, comparison.Improvements["CpuAvg"]);
        Assert.Equal(10, comparison.Improvements["HealthScore"]);
    }
    
    [Fact]
    public async Task CompareBenchmarks_DetectsHardwareMismatch()
    {
        var baseline = new BenchmarkRun { HardwareSnapshot = "HW1" };
        var optimized = new BenchmarkRun { HardwareSnapshot = "HW2" };
        
        var comparison = await _benchmarkService.CompareBenchmarksAsync(baseline, optimized);
        
        Assert.NotEmpty(comparison.HardwareMismatchWarning);
    }
}
