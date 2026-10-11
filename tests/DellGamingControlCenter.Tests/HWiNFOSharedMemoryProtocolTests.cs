namespace DellGamingControlCenter.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Services.Hardware;
using DellGamingControlCenter.App.ViewModels;

public class HWiNFOSharedMemoryProtocolTests
{
    private class FakeLoggingService : ILoggingService
    {
        public void Log(string message, LogSeverity severity = LogSeverity.Info, string category = "General", string? detail = null) { }
        public IEnumerable<ActivityLogEntry> GetRecentActivity(int maxEntries = 100) => new List<ActivityLogEntry>();
        public void ClearLog() { }
    }

    private static void WriteFixedAscii(MemoryMappedViewAccessor acc, long offset, string text, int maxLen)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        int writeLen = Math.Min(bytes.Length, maxLen - 1);
        acc.WriteArray(offset, bytes, 0, writeLen);
        acc.Write(offset + writeLen, (byte)0);
    }

    private sealed class SensorGroupDef
    {
        public uint Id { get; set; }
        public uint Instance { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class ReadingDef
    {
        public uint Type { get; set; }
        public uint GroupIndex { get; set; }
        public uint ReadingId { get; set; }
        public string Name { get; set; } = "";
        public string Unit { get; set; } = "";
        public double Value { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
    }

    private static MemoryMappedFile CreateMockHWiNFOMemory(
        string mapName,
        long pollTimeSeconds,
        List<SensorGroupDef> groups,
        List<ReadingDef> readings,
        uint magic = 0x53695748)
    {
        long fileSize = 65536;
        var mmf = MemoryMappedFile.CreateNew(mapName, fileSize);
        using var acc = mmf.CreateViewAccessor(0, fileSize, MemoryMappedFileAccess.ReadWrite);

        // Header
        acc.Write(0x00, magic);
        acc.Write(0x04, 1u); // Version
        acc.Write(0x08, 0u); // Revision
        acc.Write(0x0C, pollTimeSeconds);

        uint sensorOffset = 0x100;
        uint sensorSize = 264;
        uint sensorCount = (uint)groups.Count;
        uint entryOffset = sensorOffset + (sensorCount * sensorSize);
        uint entrySize = 316;
        uint entryCount = (uint)readings.Count;

        acc.Write(0x14, sensorOffset);
        acc.Write(0x18, sensorSize);
        acc.Write(0x1C, sensorCount);
        acc.Write(0x20, entryOffset);
        acc.Write(0x24, entrySize);
        acc.Write(0x28, entryCount);

        // Sensor Groups
        for (int i = 0; i < groups.Count; i++)
        {
            long off = sensorOffset + (i * sensorSize);
            acc.Write(off, groups[i].Id);
            acc.Write(off + 4, groups[i].Instance);
            WriteFixedAscii(acc, off + 8, groups[i].Name, 128);
            WriteFixedAscii(acc, off + 136, groups[i].Name, 128);
        }

        // Reading Elements
        for (int j = 0; j < readings.Count; j++)
        {
            long off = entryOffset + (j * entrySize);
            acc.Write(off, readings[j].Type);
            acc.Write(off + 4, readings[j].GroupIndex);
            acc.Write(off + 8, readings[j].ReadingId);
            WriteFixedAscii(acc, off + 12, readings[j].Name, 128);
            WriteFixedAscii(acc, off + 140, readings[j].Name, 128);
            WriteFixedAscii(acc, off + 268, readings[j].Unit, 16);
            acc.Write(off + 284, readings[j].Value);
            acc.Write(off + 292, readings[j].Min);
            acc.Write(off + 300, readings[j].Max);
            acc.Write(off + 308, readings[j].Value); // Avg
        }

        return mmf;
    }

    private static (List<SensorGroupDef>, List<ReadingDef>) CreateStandardInspironSensorSet()
    {
        var groups = new List<SensorGroupDef>
        {
            new() { Id = 1, Instance = 0, Name = "Intel Core i3-7020U" },
            new() { Id = 2, Instance = 0, Name = "AMD Radeon 520" },
            new() { Id = 3, Instance = 0, Name = "Dell EC" },
            new() { Id = 4, Instance = 0, Name = "System Memory" }
        };

        var readings = new List<ReadingDef>
        {
            // CPU readings
            new() { GroupIndex = 0, Type = 1, ReadingId = 1, Name = "CPU Package", Unit = "°C", Value = 54.5, Min = 45.0, Max = 68.0 },
            new() { GroupIndex = 0, Type = 1, ReadingId = 2, Name = "Core #0", Unit = "°C", Value = 52.0, Min = 44.0, Max = 67.0 },
            new() { GroupIndex = 0, Type = 1, ReadingId = 3, Name = "Core #1", Unit = "°C", Value = 53.0, Min = 44.0, Max = 66.0 },
            new() { GroupIndex = 0, Type = 6, ReadingId = 4, Name = "Core #0 Clock", Unit = "MHz", Value = 2295.0, Min = 798.0, Max = 2300.0 },
            new() { GroupIndex = 0, Type = 6, ReadingId = 5, Name = "Core #1 Clock", Unit = "MHz", Value = 2295.0, Min = 798.0, Max = 2300.0 },
            new() { GroupIndex = 0, Type = 7, ReadingId = 6, Name = "Total CPU Usage", Unit = "%", Value = 34.5, Min = 1.0, Max = 100.0 },
            new() { GroupIndex = 0, Type = 5, ReadingId = 7, Name = "CPU Package Power", Unit = "W", Value = 7.85, Min = 2.10, Max = 15.0 },
            new() { GroupIndex = 0, Type = 2, ReadingId = 8, Name = "Core #0 VID", Unit = "V", Value = 0.815, Min = 0.650, Max = 1.050 },
            new() { GroupIndex = 0, Type = 8, ReadingId = 9, Name = "Core Thermal Throttling", Unit = "", Value = 0.0, Min = 0.0, Max = 1.0 },

            // AMD Radeon 520 readings
            new() { GroupIndex = 1, Type = 1, ReadingId = 10, Name = "GPU Temperature", Unit = "°C", Value = 48.0, Min = 40.0, Max = 65.0 },
            new() { GroupIndex = 1, Type = 6, ReadingId = 11, Name = "GPU Core Clock", Unit = "MHz", Value = 600.0, Min = 300.0, Max = 1030.0 },
            new() { GroupIndex = 1, Type = 6, ReadingId = 12, Name = "GPU Memory Clock", Unit = "MHz", Value = 1000.0, Min = 300.0, Max = 1000.0 },
            new() { GroupIndex = 1, Type = 7, ReadingId = 13, Name = "GPU Utilization", Unit = "%", Value = 15.0, Min = 0.0, Max = 100.0 },

            // Dell EC fan reading
            new() { GroupIndex = 2, Type = 3, ReadingId = 14, Name = "Fan", Unit = "RPM", Value = 2450.0, Min = 0.0, Max = 4500.0 },

            // System Memory readings
            new() { GroupIndex = 3, Type = 4, ReadingId = 15, Name = "Physical Memory Used", Unit = "MB", Value = 8192.0, Min = 4000.0, Max = 12000.0 },
            new() { GroupIndex = 3, Type = 4, ReadingId = 16, Name = "Physical Memory Available", Unit = "MB", Value = 4096.0, Min = 100.0, Max = 8000.0 },
            new() { GroupIndex = 3, Type = 7, ReadingId = 17, Name = "Physical Memory Load", Unit = "%", Value = 66.7, Min = 20.0, Max = 95.0 }
        };

        return (groups, readings);
    }

    [Fact]
    public async Task StructureAndDataParsing_AllSensorsCorrectlyMapped()
    {
        string mapName = "Local\\TEST_HWINFO_STRUCT_" + Guid.NewGuid().ToString("N");
        string mutexName = "Local\\TEST_HWINFO_MUTEX_" + Guid.NewGuid().ToString("N");
        long nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (groups, readings) = CreateStandardInspironSensorSet();

        using var mmf = CreateMockHWiNFOMemory(mapName, nowSeconds, groups, readings);
        var logger = new FakeLoggingService();
        var provider = new HWiNFOSharedMemoryProvider(logger, mapName, mutexName);

        await provider.InitializeAsync();
        Assert.True(provider.IsAvailable);

        var snap = provider.CaptureSnapshot();
        Assert.NotNull(snap);
        Assert.Equal("HWiNFO64 Shared Memory", snap.ActiveProvider);
        Assert.Equal(17, snap.DiscoveredSensors.Count);

        // CPU
        Assert.Equal(54.5, snap.CpuMetrics.TemperatureCelsius);
        Assert.Equal(SensorReadingState.Available, snap.CpuMetrics.TemperatureState);
        Assert.Equal(2295.0, snap.CpuMetrics.CurrentClockMHz);
        Assert.Equal(SensorReadingState.Available, snap.CpuMetrics.ClockState);
        Assert.Equal(7.85, snap.CpuMetrics.PackagePowerWatts);
        Assert.Equal(SensorReadingState.Available, snap.CpuMetrics.PowerState);
        Assert.Equal(0.815, snap.CpuMetrics.CoreVoltageVolts);
        Assert.Equal(SensorReadingState.Available, snap.CpuMetrics.VoltageState);
        Assert.False(snap.CpuMetrics.IsThermalThrottling);

        // GPU
        Assert.Equal(48.0, snap.DiscreteGpuMetrics.TemperatureCelsius);
        Assert.Equal(SensorReadingState.Available, snap.DiscreteGpuMetrics.TemperatureState);
        Assert.Equal(600.0, snap.DiscreteGpuMetrics.CoreClockMHz);
        Assert.Equal(1000.0, snap.DiscreteGpuMetrics.MemoryClockMHz);
        Assert.Equal(15.0, snap.DiscreteGpuMetrics.UsagePercent);

        // Fan & Thermals
        Assert.Equal(54.5, snap.ThermalMetrics.CpuTemperature);
        Assert.Equal(48.0, snap.ThermalMetrics.GpuTemperature);
        Assert.Equal(2450, snap.ThermalMetrics.FanRpm);
        Assert.Equal(SensorReadingState.Available, snap.ThermalMetrics.FanRpmState);

        // RAM
        Assert.Equal(8192L * 1024 * 1024, snap.RamMetrics.UsedBytes);
        Assert.Equal(4096L * 1024 * 1024, snap.RamMetrics.AvailableBytes);
        Assert.Equal(66.7, snap.RamMetrics.UsagePercent);
    }

    [Fact]
    public async Task TelemetryPipeline_FlowsThroughCompositeToViewModels()
    {
        string mapName = "Local\\TEST_HWINFO_PIPE_" + Guid.NewGuid().ToString("N");
        string mutexName = "Local\\TEST_HWINFO_PIPE_MUTEX_" + Guid.NewGuid().ToString("N");
        long nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (groups, readings) = CreateStandardInspironSensorSet();

        using var mmf = CreateMockHWiNFOMemory(mapName, nowSeconds, groups, readings);
        var logger = new FakeLoggingService();
        var hwinfoProvider = new HWiNFOSharedMemoryProvider(logger, mapName, mutexName);
        var lhmProvider = new LibreHardwareMonitorTelemetryProvider(logger);
        var winProvider = new WindowsStandardTelemetryProvider(logger);

        using var composite = new CompositeHardwareTelemetryProvider(logger, hwinfoProvider, lhmProvider, winProvider);
        await composite.InitializeAsync();

        var snap = composite.CaptureSnapshot();
        Assert.NotNull(snap);
        Assert.Equal(54.5, snap.CpuMetrics.TemperatureCelsius);
        Assert.Equal(SensorReadingState.Available, snap.CpuMetrics.TemperatureState);
        Assert.Equal(48.0, snap.DiscreteGpuMetrics.TemperatureCelsius);
        Assert.Equal(SensorReadingState.Available, snap.DiscreteGpuMetrics.TemperatureState);
        Assert.Equal(2450, snap.ThermalMetrics.FanRpm);
        Assert.Equal(SensorReadingState.Available, snap.ThermalMetrics.FanRpmState);
        Assert.Equal(7.85, snap.CpuMetrics.PackagePowerWatts);
        Assert.Equal(0.815, snap.CpuMetrics.CoreVoltageVolts);

        // Convert to DiagnosticSnapshot (as MetricsSampler does from HardwareTelemetrySnapshot)
        var diag = new DiagnosticSnapshot
        {
            Timestamp = snap.Timestamp,
            CpuMetrics = snap.CpuMetrics,
            RamMetrics = snap.RamMetrics,
            GpuMetrics = snap.DiscreteGpuMetrics,
            ThermalMetrics = snap.ThermalMetrics,
            BatteryMetrics = new BatteryMetrics { ChargePercent = -1 }
        };

        // Configure App static dependencies for DashboardViewModel
        var samplerMock = new Moq.Mock<IMetricsSampler>();
        var sessionManagerMock = new Moq.Mock<IGameSessionManager>();
        var hardwareProfileMock = new Moq.Mock<IHardwareProfileProvider>();
        var perfModeUseCaseMock = new Moq.Mock<IChangePerformanceModeUseCase>();
        var intelligenceMock = new Moq.Mock<IGamingIntelligenceEngine>();
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
        appType.GetProperty("MetricsSampler", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.SetValue(null, samplerMock.Object);
        appType.GetProperty("GameSessionManager", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.SetValue(null, sessionManagerMock.Object);
        appType.GetProperty("HardwareProfileProvider", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.SetValue(null, hardwareProfileMock.Object);
        appType.GetProperty("ChangePerformanceModeUseCase", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.SetValue(null, perfModeUseCaseMock.Object);
        appType.GetProperty("GamingIntelligenceEngine", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.SetValue(null, intelligenceMock.Object);

        // Propagate to DashboardViewModel via reflection OnSnapshotCaptured
        var dashVm = new DashboardViewModel();
        var method = typeof(DashboardViewModel).GetMethod("OnSnapshotCaptured", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        method.Invoke(dashVm, new object[] { this, diag });

        Assert.Equal("54°C", dashVm.Temperature);
        Assert.Equal("34%", dashVm.CpuUsage);
        Assert.Equal(34.5, dashVm.CpuPercent);
        Assert.Contains("GB used", dashVm.RamUsage);
    }

    [Fact]
    public async Task StaleDataDetection_MarkedStaleWhenPollTimeOlderThan10Seconds()
    {
        string mapName = "Local\\TEST_HWINFO_STALE_" + Guid.NewGuid().ToString("N");
        string mutexName = "Local\\TEST_HWINFO_STALE_MUTEX_" + Guid.NewGuid().ToString("N");
        // Set timestamp 30 seconds ago
        long staleSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 30;
        var (groups, readings) = CreateStandardInspironSensorSet();

        using var mmf = CreateMockHWiNFOMemory(mapName, staleSeconds, groups, readings);
        var logger = new FakeLoggingService();
        var provider = new HWiNFOSharedMemoryProvider(logger, mapName, mutexName);

        await provider.InitializeAsync();
        Assert.True(provider.IsAvailable);

        var snap = provider.CaptureSnapshot();
        Assert.NotNull(snap);
        Assert.Equal(SensorReadingState.Stale, snap.CpuMetrics.TemperatureState);
        Assert.NotNull(snap.CpuMetrics.TemperatureDiagnosticReason);
        Assert.Contains("HWiNFO shared memory is stale", snap.CpuMetrics.TemperatureDiagnosticReason);

        Assert.Equal(SensorReadingState.Stale, snap.CpuMetrics.ClockState);
        Assert.Equal(SensorReadingState.Stale, snap.CpuMetrics.PowerState);
        Assert.Equal(SensorReadingState.Stale, snap.CpuMetrics.VoltageState);
        Assert.Equal(SensorReadingState.Stale, snap.DiscreteGpuMetrics.TemperatureState);
        Assert.Equal(SensorReadingState.Stale, snap.ThermalMetrics.FanRpmState);

        foreach (var sensor in snap.DiscoveredSensors)
        {
            Assert.Equal(SensorReadingState.Stale, sensor.State);
            Assert.Contains("HWiNFO shared memory is stale", sensor.DiagnosticReason);
        }
    }

    [Fact]
    public async Task MissingSharedMemory_HandledGracefullyWithoutExceptions()
    {
        string nonExistentName = "Local\\NON_EXISTENT_MAP_" + Guid.NewGuid().ToString("N");
        var logger = new FakeLoggingService();
        var provider = new HWiNFOSharedMemoryProvider(logger, nonExistentName, "Local\\NON_EXISTENT_MUTEX");

        await provider.InitializeAsync();
        Assert.False(provider.IsAvailable);

        var snap = provider.CaptureSnapshot();
        Assert.NotNull(snap);
        Assert.False(provider.IsAvailable);
        Assert.Empty(snap.DiscoveredSensors);
        Assert.Equal(SensorReadingState.Unavailable, snap.CpuMetrics.TemperatureState);
        Assert.Equal(SensorReadingState.Unavailable, snap.ThermalMetrics.FanRpmState);
    }

    [Fact]
    public void LifecycleAndRecovery_ShutdownAndRestartCycles()
    {
        string mapName = "Local\\TEST_HWINFO_LIFECYCLE_" + Guid.NewGuid().ToString("N");
        string mutexName = "Local\\TEST_HWINFO_LIFECYCLE_MUTEX_" + Guid.NewGuid().ToString("N");
        var logger = new FakeLoggingService();
        var provider = new HWiNFOSharedMemoryProvider(logger, mapName, mutexName);

        // Phase 1: Shared memory absent (HWiNFO not yet running)
        var snap1 = provider.CaptureSnapshot();
        Assert.False(provider.IsAvailable);
        Assert.Empty(snap1.DiscoveredSensors);

        // Phase 2: HWiNFO starts up (Shared memory created)
        var (groups, readings) = CreateStandardInspironSensorSet();
        long nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var mmf = CreateMockHWiNFOMemory(mapName, nowSeconds, groups, readings);

        var snap2 = provider.CaptureSnapshot();
        Assert.True(provider.IsAvailable);
        Assert.Equal(17, snap2.DiscoveredSensors.Count);
        Assert.Equal(54.5, snap2.CpuMetrics.TemperatureCelsius);

        // Phase 3: HWiNFO shuts down (Shared memory closed)
        mmf.Dispose();

        var snap3 = provider.CaptureSnapshot();
        Assert.False(provider.IsAvailable);
        Assert.Empty(snap3.DiscoveredSensors);

        // Phase 4: HWiNFO restarts (Shared memory recreated)
        long restartSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var mmf2 = CreateMockHWiNFOMemory(mapName, restartSeconds, groups, readings);

        var snap4 = provider.CaptureSnapshot();
        Assert.True(provider.IsAvailable);
        Assert.Equal(17, snap4.DiscoveredSensors.Count);
        Assert.Equal(54.5, snap4.CpuMetrics.TemperatureCelsius);
    }

    [Fact]
    public void MutexSynchronization_HonorsMutexLocking()
    {
        string mapName = "Local\\TEST_HWINFO_MUTEX_SYNC_" + Guid.NewGuid().ToString("N");
        string mutexName = "Local\\TEST_HWINFO_MUTEX_SYNC_MUTEX_" + Guid.NewGuid().ToString("N");
        long nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var (groups, readings) = CreateStandardInspironSensorSet();

        using var mmf = CreateMockHWiNFOMemory(mapName, nowSeconds, groups, readings);
        using var mutex = new Mutex(true, mutexName, out bool createdNew); // Acquired by this thread

        var logger = new FakeLoggingService();
        var provider = new HWiNFOSharedMemoryProvider(logger, mapName, mutexName);

        // Reading should not hang when mutex is held; it has a 25ms timeout and safely proceeds or finishes
        var snap = provider.CaptureSnapshot();
        Assert.NotNull(snap);

        mutex.ReleaseMutex();
    }
}
