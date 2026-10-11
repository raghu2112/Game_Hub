namespace DellGamingControlCenter.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Moq;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Services;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.App.ViewModels;

public class BenchmarkTests : IDisposable
{
    private readonly BenchmarkService _benchmarkService;
    private readonly string _baseDir;

    public BenchmarkTests()
    {
        _benchmarkService = new BenchmarkService();
        // Must mirror BenchmarkService's storage location (moved to %LocalAppData% so the
        // installed app never writes to Program Files). Cleaned per-test so runs from
        // previous executions cannot accumulate and break count assertions.
        _baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DellGamingControlCenter", "benchmarks");
        if (Directory.Exists(_baseDir))
        {
            Directory.Delete(_baseDir, true);
        }
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

/// <summary>
/// Regression coverage for the benchmark-list entry refresh (pages are singletons:
/// profiles added in Game Center after startup previously never appeared here).
/// </summary>
public class BenchmarkViewModelRefreshTests
{
    private static void SetAppStatic(string name, object value)
    {
        typeof(DellGamingControlCenter.App.App)
            .GetProperty(name, BindingFlags.Public | BindingFlags.Static)
            ?.SetValue(null, value);
    }

    private static (BenchmarkViewModel Vm, List<GameProfile> Profiles) CreateViewModel()
    {
        var configMock = new Mock<IConfigurationService>();
        var profiles = new List<GameProfile>
        {
            new GameProfile { GameId = "b1", GameName = "First Game", ExecutablePath = @"C:\games\first.exe" }
        };
        // Returned by reference so tests can add profiles after construction.
        configMock.Setup(c => c.GetGameProfiles()).Returns(() => profiles);
        SetAppStatic("ConfigService", configMock.Object);

        var discoveryMock = new Mock<IGameDiscoveryService>();
        discoveryMock.Setup(d => d.GetCachedGames()).Returns(new List<DiscoveredGame>());

        var benchmarkMock = new Mock<IBenchmarkService>();
        benchmarkMock.Setup(b => b.GetHistoryAsync(It.IsAny<string>())).ReturnsAsync(new List<BenchmarkRun>());

        var tuningMock = new Mock<IGameTuningService>();
        tuningMock.Setup(t => t.GetPerformanceHistoryAsync(It.IsAny<string>()))
            .ReturnsAsync(new GamePerformanceHistory());

        var vm = new BenchmarkViewModel(
            benchmarkMock.Object,
            tuningMock.Object,
            discoveryMock.Object,
            Mock.Of<IGameSessionManager>());
        return (vm, profiles);
    }

    [Fact]
    public void RefreshGames_PicksUpProfilesAddedAfterStartup()
    {
        var (vm, profiles) = CreateViewModel();
        Assert.Contains(vm.DiscoveredGames, g => g.Id == "b1");

        profiles.Add(new GameProfile { GameId = "b2", GameName = "Second Game", ExecutablePath = @"E:\Games\Second Game\game.exe" });
        vm.RefreshGames();

        Assert.Contains(vm.DiscoveredGames, g => g.Id == "b2");
        // Selection stays on the first entry — a refresh must not yank the user's choice.
        Assert.Equal("b1", vm.SelectedGameId);
    }

    [Fact]
    public void RefreshGames_DoesNotMutateList_WhileRunIsActive()
    {
        var (vm, profiles) = CreateViewModel();
        vm.IsRunning = true;

        profiles.Add(new GameProfile { GameId = "b3", GameName = "Third Game", ExecutablePath = @"D:\Games\third.exe" });
        vm.RefreshGames();

        Assert.DoesNotContain(vm.DiscoveredGames, g => g.Id == "b3");
    }
}
