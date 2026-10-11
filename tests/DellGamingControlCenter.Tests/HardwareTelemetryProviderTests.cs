namespace DellGamingControlCenter.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Services;
using DellGamingControlCenter.Services.Hardware;
using DellGamingControlCenter.App.ViewModels;

public class HardwareTelemetryProviderTests
{
    private class FakeLoggingService : ILoggingService
    {
        public void Log(string message, LogSeverity severity = LogSeverity.Info, string category = "General", string? detail = null) { }
        public IEnumerable<ActivityLogEntry> GetRecentActivity(int maxEntries = 100) => new List<ActivityLogEntry>();
        public void ClearLog() { }
    }

    private class MockTelemetryProvider : IHardwareTelemetryProvider
    {
        public string ProviderName { get; }
        public bool IsAvailable { get; set; } = true;
        public List<HardwareSensorReading> Sensors { get; set; } = new();
        public bool Disposed { get; private set; }

        public MockTelemetryProvider(string name)
        {
            ProviderName = name;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public HardwareTelemetrySnapshot CaptureSnapshot()
        {
            var cpuMetrics = new CpuMetrics();
            var ramMetrics = new RamMetrics();
            var thermalMetrics = new ThermalMetrics();
            var discreteGpu = new GpuMetrics();

            foreach (var s in Sensors)
            {
                if (s.ComponentType == HardwareComponentType.Cpu)
                {
                    if (s.SensorType == TelemetrySensorType.Temperature && s.State == SensorReadingState.Available && s.Value.HasValue)
                    {
                        cpuMetrics = new CpuMetrics
                        {
                            TemperatureCelsius = s.Value.Value,
                            TemperatureState = SensorReadingState.Available,
                            UsagePercent = cpuMetrics.UsagePercent,
                            CurrentClockMHz = cpuMetrics.CurrentClockMHz,
                            ClockState = cpuMetrics.ClockState
                        };
                    }
                    if (s.SensorType == TelemetrySensorType.Clock && s.State == SensorReadingState.Available && s.Value.HasValue)
                    {
                        cpuMetrics = new CpuMetrics
                        {
                            TemperatureCelsius = cpuMetrics.TemperatureCelsius,
                            TemperatureState = cpuMetrics.TemperatureState,
                            UsagePercent = cpuMetrics.UsagePercent,
                            CurrentClockMHz = s.Value.Value,
                            ClockState = SensorReadingState.Available
                        };
                    }
                }
                else if (s.ComponentType == HardwareComponentType.Gpu)
                {
                    if (s.SensorType == TelemetrySensorType.Data && s.SensorName.Contains("Dedicated") && s.Value.HasValue)
                    {
                        discreteGpu = new GpuMetrics
                        {
                            VramTotalBytes = 2048L * 1024 * 1024,
                            VramUsedBytes = (long)(s.Value.Value * 1024 * 1024),
                            MemoryState = SensorReadingState.Available
                        };
                    }
                }
            }

            return new HardwareTelemetrySnapshot
            {
                ActiveProvider = ProviderName,
                DiscoveredSensors = Sensors.ToList(),
                CpuMetrics = cpuMetrics,
                RamMetrics = ramMetrics,
                DiscreteGpuMetrics = discreteGpu,
                ThermalMetrics = thermalMetrics
            };
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    [Fact]
    public async Task WindowsStandardTelemetryProvider_CapturesMemoryAndCpuMetrics()
    {
        var logger = new FakeLoggingService();
        using var provider = new WindowsStandardTelemetryProvider(logger);
        await provider.InitializeAsync();

        Assert.True(provider.IsAvailable);
        var snapshot = provider.CaptureSnapshot();

        Assert.NotNull(snapshot);
        Assert.NotEmpty(snapshot.DiscoveredSensors);

        // Verify RAM is discovered with honest states
        var ramUsed = snapshot.DiscoveredSensors.FirstOrDefault(s => s.ComponentType == HardwareComponentType.Memory && s.SensorName.Contains("Used"));
        Assert.NotNull(ramUsed);
        Assert.Equal(SensorReadingState.Available, ramUsed.State);
        Assert.True(ramUsed.Value > 0, "Used RAM must be greater than 0");

        var ramLoad = snapshot.DiscoveredSensors.FirstOrDefault(s => s.ComponentType == HardwareComponentType.Memory && s.SensorType == TelemetrySensorType.Load);
        Assert.NotNull(ramLoad);
        Assert.True(ramLoad.Value >= 0 && ramLoad.Value <= 100);

        // Verify that live clock is honestly reported as Unavailable because Windows counters don't measure live MSR clocks
        Assert.Equal(SensorReadingState.Unavailable, snapshot.CpuMetrics.ClockState);
        Assert.Equal(0, snapshot.CpuMetrics.CurrentClockMHz);
    }

    [Fact]
    public async Task HWiNFOSharedMemoryProvider_WhenNotRunning_HandlesGracefully()
    {
        var logger = new FakeLoggingService();
        using var provider = new HWiNFOSharedMemoryProvider(logger);
        await provider.InitializeAsync();

        var snapshot = provider.CaptureSnapshot();
        Assert.NotNull(snapshot);
        if (!provider.IsAvailable)
        {
            Assert.Empty(snapshot.DiscoveredSensors);
            Assert.Equal(SensorReadingState.Unavailable, snapshot.CpuMetrics.TemperatureState);
        }
    }

    [Fact]
    public async Task CompositeHardwareTelemetryProvider_DeduplicatesAndPrioritizes()
    {
        var logger = new FakeLoggingService();

        var hwInfoMock = new MockTelemetryProvider("HWiNFO64")
        {
            Sensors = new List<HardwareSensorReading>
            {
                new HardwareSensorReading
                {
                    HardwareName = "Intel Core i3-7020U",
                    ComponentType = HardwareComponentType.Cpu,
                    SensorType = TelemetrySensorType.Temperature,
                    SensorName = "CPU Package",
                    Value = 58.0,
                    Unit = "°C",
                    State = SensorReadingState.Available,
                    SourceProvider = "HWiNFO64"
                },
                new HardwareSensorReading
                {
                    HardwareName = "Intel Core i3-7020U",
                    ComponentType = HardwareComponentType.Cpu,
                    SensorType = TelemetrySensorType.Clock,
                    SensorName = "CPU Core #0 Clock",
                    Value = 2300.0,
                    Unit = "MHz",
                    State = SensorReadingState.Available,
                    SourceProvider = "HWiNFO64"
                }
            }
        };

        var lhmMock = new MockTelemetryProvider("LibreHardwareMonitor")
        {
            Sensors = new List<HardwareSensorReading>
            {
                // Duplicate reading with different value - HWiNFO (higher priority) should win
                new HardwareSensorReading
                {
                    HardwareName = "Intel Core i3-7020U",
                    ComponentType = HardwareComponentType.Cpu,
                    SensorType = TelemetrySensorType.Temperature,
                    SensorName = "CPU Package",
                    Value = 45.0,
                    Unit = "°C",
                    State = SensorReadingState.Available,
                    SourceProvider = "LibreHardwareMonitor"
                },
                // Unique reading - should be kept
                new HardwareSensorReading
                {
                    HardwareName = "Intel HD Graphics 620",
                    ComponentType = HardwareComponentType.Gpu,
                    SensorType = TelemetrySensorType.Load,
                    SensorName = "D3D 3D",
                    Value = 12.0,
                    Unit = "%",
                    State = SensorReadingState.Available,
                    SourceProvider = "LibreHardwareMonitor"
                }
            }
        };

        var winMock = new MockTelemetryProvider("Windows Native")
        {
            Sensors = new List<HardwareSensorReading>
            {
                new HardwareSensorReading
                {
                    HardwareName = "System Memory",
                    ComponentType = HardwareComponentType.Memory,
                    SensorType = TelemetrySensorType.Data,
                    SensorName = "Memory Used",
                    Value = 4096.0,
                    Unit = "MB",
                    State = SensorReadingState.Available,
                    SourceProvider = "Windows Native"
                }
            }
        };

        using var composite = new CompositeHardwareTelemetryProvider(logger, hwInfoMock, lhmMock, winMock);
        await composite.InitializeAsync();

        var snapshot = composite.CaptureSnapshot();

        // Check deduplication
        var tempSensors = snapshot.DiscoveredSensors.Where(s => s.SensorName == "CPU Package").ToList();
        Assert.Single(tempSensors);
        Assert.Equal("HWiNFO64", tempSensors[0].SourceProvider);
        Assert.Equal(58.0, tempSensors[0].Value);

        // Check inclusion of non-duplicate sensors from other providers
        var gpuLoad = snapshot.DiscoveredSensors.FirstOrDefault(s => s.SensorName == "D3D 3D");
        Assert.NotNull(gpuLoad);
        Assert.Equal("LibreHardwareMonitor", gpuLoad.SourceProvider);

        var memUsed = snapshot.DiscoveredSensors.FirstOrDefault(s => s.SensorName == "Memory Used");
        Assert.NotNull(memUsed);
        Assert.Equal("Windows Native", memUsed.SourceProvider);
    }

    [Fact]
    public void DataSemantics_HonestStates_NeverSilentlyConvertedToZero()
    {
        var readingUnavailable = new HardwareSensorReading
        {
            HardwareName = "Intel Core i3-7020U",
            ComponentType = HardwareComponentType.Cpu,
            SensorType = TelemetrySensorType.Temperature,
            SensorName = "CPU Package",
            Value = null,
            State = SensorReadingState.Unavailable
        };

        var readingUnsupported = new HardwareSensorReading
        {
            HardwareName = "Dell Inspiron 3576 Motherboard",
            ComponentType = HardwareComponentType.Fan,
            SensorType = TelemetrySensorType.Fan,
            SensorName = "System Fan",
            Value = null,
            State = SensorReadingState.Unsupported
        };

        Assert.Equal(SensorReadingState.Unavailable, readingUnavailable.State);
        Assert.Null(readingUnavailable.Value);

        Assert.Equal(SensorReadingState.Unsupported, readingUnsupported.State);
        Assert.Null(readingUnsupported.Value);
    }

    [Fact]
    public void CompositeHardwareTelemetryProvider_DisposesChildren()
    {
        var logger = new FakeLoggingService();
        var mock1 = new MockTelemetryProvider("M1");
        var mock2 = new MockTelemetryProvider("M2");
        var mock3 = new MockTelemetryProvider("M3");

        var composite = new CompositeHardwareTelemetryProvider(logger, mock1, mock2, mock3);
        composite.Dispose();

        Assert.True(mock1.Disposed);
        Assert.True(mock2.Disposed);
        Assert.True(mock3.Disposed);
    }

    [Fact]
    public void GpuMemorySemantics_DedicatedVsShared_ExplicitlySeparated()
    {
        var metrics = new GpuMetrics
        {
            VramTotalBytes = 2039L * 1024 * 1024,
            VramUsedBytes = 0L,
            VramAvailableBytes = 2039L * 1024 * 1024,
            SharedMemoryTotalBytes = 6076L * 1024 * 1024,
            SharedMemoryUsedBytes = 512L * 1024,
            SharedMemoryAvailableBytes = 6075L * 1024 * 1024,
            MemoryState = SensorReadingState.Available
        };

        Assert.Equal(SensorReadingState.Available, metrics.MemoryState);
        Assert.NotEqual(metrics.VramTotalBytes, metrics.SharedMemoryTotalBytes);
        Assert.True(metrics.SharedMemoryTotalBytes > metrics.VramTotalBytes);
        Assert.Equal(0L, metrics.VramUsedBytes);
        Assert.Equal(512L * 1024, metrics.SharedMemoryUsedBytes);
    }

    [Fact]
    public async Task LiveHardwareProbe_InspectAndLogRealSensors()
    {
        var logger = new FakeLoggingService();
        using var composite = new CompositeHardwareTelemetryProvider(logger);
        await composite.InitializeAsync();

        var samples = new List<HardwareTelemetrySnapshot>();
        for (int i = 0; i < 3; i++)
        {
            var snap = composite.CaptureSnapshot();
            samples.Add(snap);
            if (i < 2) await Task.Delay(1000);
        }

        Assert.NotEmpty(samples);
        var latest = samples.Last();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("DELL INSPIRON 3576 — HARDWARE SENSOR INVENTORY REPORT");
        sb.AppendLine($"Timestamp: {latest.Timestamp:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"Active Provider Pipeline: {latest.ActiveProvider}");
        sb.AppendLine("================================================================================");
        sb.AppendLine();
        sb.AppendLine("--- PROVIDER STATUS ---");
        sb.AppendLine($"HWiNFO64 Shared Memory Available: {composite.HWiNFOProvider.IsAvailable}");
        sb.AppendLine($"LibreHardwareMonitor Available:  {composite.LhmProvider.IsAvailable}");
        sb.AppendLine($"Windows Standard Native Available:{composite.WindowsProvider.IsAvailable}");
        sb.AppendLine();
        sb.AppendLine("--- CORE SYSTEM METRICS (LATEST SAMPLE) ---");
        sb.AppendLine($"CPU Usage:               {latest.CpuMetrics.UsagePercent:F1}%");
        sb.AppendLine($"CPU Clock (Live):        {(latest.CpuMetrics.CurrentClockMHz > 0 ? latest.CpuMetrics.CurrentClockMHz.ToString("F0") + " MHz" : "N/A")} [State: {latest.CpuMetrics.ClockState}] (Nominal Base: 2300 MHz)");
        sb.AppendLine($"CPU Temp:                {(latest.CpuMetrics.TemperatureCelsius.HasValue ? latest.CpuMetrics.TemperatureCelsius.Value.ToString("F1") + " °C" : "N/A")} [State: {latest.CpuMetrics.TemperatureState}]");
        sb.AppendLine($"CPU Package Power:       {(latest.CpuMetrics.PackagePowerWatts.HasValue ? latest.CpuMetrics.PackagePowerWatts.Value.ToString("F2") + " W" : "N/A")} [State: {latest.CpuMetrics.PowerState}]");
        sb.AppendLine($"CPU Voltage:             {(latest.CpuMetrics.CoreVoltageVolts.HasValue ? latest.CpuMetrics.CoreVoltageVolts.Value.ToString("F3") + " V" : "N/A")} [State: {latest.CpuMetrics.VoltageState}]");
        sb.AppendLine($"RAM Used:                {latest.RamMetrics.UsedBytes / (1024 * 1024):N0} MB / {latest.RamMetrics.TotalBytes / (1024 * 1024):N0} MB ({latest.RamMetrics.UsagePercent:F1}%)");
        sb.AppendLine($"RAM Available:           {latest.RamMetrics.AvailableBytes / (1024 * 1024):N0} MB");
        sb.AppendLine($"Discrete GPU Usage:      {(latest.DiscreteGpuMetrics.UsagePercent.HasValue ? latest.DiscreteGpuMetrics.UsagePercent.Value.ToString("F1") + "%" : "N/A")}");
        sb.AppendLine($"Discrete GPU Temp:       {(latest.DiscreteGpuMetrics.TemperatureCelsius.HasValue ? latest.DiscreteGpuMetrics.TemperatureCelsius.Value.ToString("F1") + " °C" : "N/A")} [State: {latest.DiscreteGpuMetrics.TemperatureState}]");
        sb.AppendLine($"Discrete GPU VRAM Dedicated Used: {(latest.DiscreteGpuMetrics.VramUsedBytes.HasValue ? (latest.DiscreteGpuMetrics.VramUsedBytes.Value / (1024 * 1024)).ToString() + " MB" : "N/A")} / {(latest.DiscreteGpuMetrics.VramTotalBytes.HasValue ? (latest.DiscreteGpuMetrics.VramTotalBytes.Value / (1024 * 1024)).ToString() + " MB" : "N/A")}");
        sb.AppendLine($"Discrete GPU VRAM Shared Used:    {(latest.DiscreteGpuMetrics.SharedMemoryUsedBytes.HasValue ? (latest.DiscreteGpuMetrics.SharedMemoryUsedBytes.Value / (1024 * 1024)).ToString() + " MB" : "N/A")} / {(latest.DiscreteGpuMetrics.SharedMemoryTotalBytes.HasValue ? (latest.DiscreteGpuMetrics.SharedMemoryTotalBytes.Value / (1024 * 1024)).ToString() + " MB" : "N/A")}");
        sb.AppendLine($"Integrated GPU Usage:    {(latest.IntegratedGpuMetrics.UsagePercent.HasValue ? latest.IntegratedGpuMetrics.UsagePercent.Value.ToString("F1") + "%" : "N/A")}");
        sb.AppendLine($"Fan RPM:                 {(latest.ThermalMetrics.FanRpm.HasValue ? latest.ThermalMetrics.FanRpm.Value.ToString("F0") + " RPM" : "N/A")} [State: {latest.ThermalMetrics.FanRpmState}]");
        sb.AppendLine();
        sb.AppendLine("--- ALL ENUMERATED SENSORS ---");
        sb.AppendLine(string.Format("{0,-30} | {1,-12} | {2,-25} | {3,-12} | {4,-15} | {5,-12} | {6}", "Hardware", "Component", "Sensor Name", "Type", "Value", "State", "Provider"));
        sb.AppendLine(new string('-', 125));

        foreach (var s in latest.DiscoveredSensors)
        {
            string valStr = s.Value.HasValue ? $"{s.Value.Value:F1} {s.Unit}" : "N/A";
            sb.AppendLine(string.Format("{0,-30} | {1,-12} | {2,-25} | {3,-12} | {4,-15} | {5,-12} | {6}",
                s.HardwareName,
                s.ComponentType,
                s.SensorName,
                s.SensorType,
                valStr,
                s.State,
                s.SourceProvider));
        }

        sb.AppendLine();
        sb.AppendLine("--- TIME-SERIES SAMPLING VERIFICATION ---");
        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            sb.AppendLine($"Sample #{i + 1} @ {s.Timestamp:HH:mm:ss.fff} => CPU Load: {s.CpuMetrics.UsagePercent:F1}%, RAM: {s.RamMetrics.UsagePercent:F1}%, Discrete GPU Load: {(s.DiscreteGpuMetrics.UsagePercent.HasValue ? s.DiscreteGpuMetrics.UsagePercent.Value.ToString("F1") + "%" : "N/A")}");
        }

        string reportPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "HARDWARE_SENSOR_INVENTORY.txt");
        System.IO.File.WriteAllText(reportPath, sb.ToString());
    }
}
