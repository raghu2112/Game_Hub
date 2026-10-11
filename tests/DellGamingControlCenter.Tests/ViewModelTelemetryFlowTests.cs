namespace DellGamingControlCenter.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Moq;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Services;
using DellGamingControlCenter.Services.Hardware;
using DellGamingControlCenter.App.ViewModels;

public class ViewModelTelemetryFlowTests
{
    private class FakeLoggingService : ILoggingService
    {
        public void Log(string message, LogSeverity severity = LogSeverity.Info, string category = "General", string? detail = null) { }
        public IEnumerable<ActivityLogEntry> GetRecentActivity(int maxEntries = 100) => new List<ActivityLogEntry>();
        public void ClearLog() { }
    }

    [Fact]
    public void DashboardViewModel_ReceivesSnapshot_UpdatesTelemetryPropertiesCorrectly()
    {
        var samplerMock = new Mock<IMetricsSampler>();
        var sessionManagerMock = new Mock<IGameSessionManager>();
        var hardwareProfileMock = new Mock<IHardwareProfileProvider>();
        var perfModeUseCaseMock = new Mock<IChangePerformanceModeUseCase>();
        var intelligenceMock = new Mock<IGamingIntelligenceEngine>();
        intelligenceMock.Setup(i => i.GetSystemInsights()).Returns(new List<GamingInsight>());

        var profile = new HardwareProfile
        {
            Cpu = new CpuInfo { Model = "Intel i3-7020U", BaseClockMHz = 2300, Cores = 2, Threads = 4 },
            Memory = new RamInfo { TotalBytes = 12L * 1024 * 1024 * 1024 },
            Gpus = new List<GpuInfo> { new GpuInfo { Name = "AMD Radeon 520", Type = GpuType.Discrete } }
        };
        hardwareProfileMock.Setup(p => p.GetProfile()).Returns(profile);
        perfModeUseCaseMock.Setup(p => p.GetCurrentMode()).Returns(PerformanceMode.Balanced);

        var appType = typeof(DellGamingControlCenter.App.App);
        appType.GetProperty("MetricsSampler", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, samplerMock.Object);
        appType.GetProperty("GameSessionManager", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, sessionManagerMock.Object);
        appType.GetProperty("HardwareProfileProvider", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, hardwareProfileMock.Object);
        appType.GetProperty("ChangePerformanceModeUseCase", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, perfModeUseCaseMock.Object);
        appType.GetProperty("GamingIntelligenceEngine", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, intelligenceMock.Object);

        var viewModel = new DashboardViewModel();

        // 1. Snapshot with live CPU, RAM, and discrete CPU temperature
        var snapshotLiveCpu = new DiagnosticSnapshot
        {
            CpuMetrics = new CpuMetrics { UsagePercent = 42.0, TemperatureCelsius = 68.0, TemperatureState = SensorReadingState.Available },
            RamMetrics = new RamMetrics { TotalBytes = 12L * 1024 * 1024 * 1024, UsedBytes = 6L * 1024 * 1024 * 1024 },
            GpuMetrics = new GpuMetrics { UsagePercent = 15.0 },
            ThermalMetrics = new ThermalMetrics { CpuTemperature = 68.0, CpuTemperatureState = SensorReadingState.Available },
            BatteryMetrics = new BatteryMetrics { ChargePercent = -1 }
        };

        // Invoke private OnSnapshotCaptured via reflection to execute logic synchronously without UI thread dependencies
        var method = typeof(DashboardViewModel).GetMethod("OnSnapshotCaptured", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        method.Invoke(viewModel, new object[] { this, snapshotLiveCpu });

        Assert.Equal("42%", viewModel.CpuUsage);
        Assert.Equal(42.0, viewModel.CpuPercent);
        Assert.Equal("68°C", viewModel.Temperature);
        Assert.Contains("6 GB used", viewModel.RamUsage);

        // 2. Snapshot where CPU temperature is Unavailable, but ACPI motherboard zone is 25°C
        var snapshotAcpiOnly = new DiagnosticSnapshot
        {
            CpuMetrics = new CpuMetrics { UsagePercent = 10.0, TemperatureState = SensorReadingState.Unavailable },
            RamMetrics = new RamMetrics { TotalBytes = 12L * 1024 * 1024 * 1024, UsedBytes = 4L * 1024 * 1024 * 1024 },
            GpuMetrics = new GpuMetrics { UsagePercent = 0.0 },
            ThermalMetrics = new ThermalMetrics { CpuTemperature = null, AmbientTemperature = 25.0, CpuTemperatureState = SensorReadingState.Unavailable },
            BatteryMetrics = new BatteryMetrics { ChargePercent = -1 }
        };

        method.Invoke(viewModel, new object[] { this, snapshotAcpiOnly });

        Assert.Equal("10%", viewModel.CpuUsage);
        Assert.Equal("25°C (ACPI)", viewModel.Temperature);

        // 3. Snapshot where both CPU and ACPI temperatures are Unavailable
        var snapshotUnavailable = new DiagnosticSnapshot
        {
            CpuMetrics = new CpuMetrics { UsagePercent = 5.0, TemperatureState = SensorReadingState.Unavailable },
            RamMetrics = new RamMetrics { TotalBytes = 12L * 1024 * 1024 * 1024, UsedBytes = 4L * 1024 * 1024 * 1024 },
            GpuMetrics = new GpuMetrics { UsagePercent = null },
            ThermalMetrics = new ThermalMetrics { CpuTemperature = null, AmbientTemperature = null, CpuTemperatureState = SensorReadingState.Unavailable },
            BatteryMetrics = new BatteryMetrics { ChargePercent = -1 }
        };

        method.Invoke(viewModel, new object[] { this, snapshotUnavailable });

        Assert.Equal("Unavailable", viewModel.Temperature);
    }
}
