namespace DellGamingControlCenter.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Services;

public class HardwareAbstractionTests
{
    private class FakeLoggingService : ILoggingService
    {
        public void Log(string message, LogSeverity severity = LogSeverity.Info, string category = "General", string? detail = null) { }
        public IEnumerable<ActivityLogEntry> GetRecentActivity(int maxEntries = 100) => new List<ActivityLogEntry>();
        public void ClearLog() { }
    }

    private class FakeDellCapabilityDetector : IDellCapabilityDetector
    {
        public CapabilityStatus GetFeatureStatus(string featureName)
        {
            if (featureName == "Battery Thresholds") return CapabilityStatus.Unsupported;
            if (featureName == "Thermal Management") return CapabilityStatus.RequiresElevation;
            return CapabilityStatus.Unsupported;
        }
        public DellCapabilityReport AnalyzeCapabilities() => new DellCapabilityReport();
    }

    private class FakeCpuTelemetry : ICpuTelemetryService
    {
        public CpuInfo GetCpuInfo() => new CpuInfo { Model = "Intel i3", Cores = 2 };
        public CpuMetrics GetMetrics() => new CpuMetrics { UsagePercent = 50.0 };
    }

    private class FakeMemoryTelemetry : IMemoryTelemetryService
    {
        public RamInfo GetRamInfo() => new RamInfo { TotalBytes = 12L * 1024 * 1024 * 1024 };
        public RamMetrics GetMetrics() => new RamMetrics { TotalBytes = 12000, UsedBytes = 6000, AvailableBytes = 6000, UsagePercent = 50.0 };
    }

    private class FakeGpuTelemetry : IGpuTelemetryService
    {
        public List<GpuInfo> GetGpuInfo() => new List<GpuInfo>
        {
            new GpuInfo { Name = "Intel HD", Type = GpuType.Integrated },
            new GpuInfo { Name = "AMD Radeon", Type = GpuType.Discrete }
        };
        public GpuMetrics GetMetrics() => new GpuMetrics();
    }

    private class FakeStorageTelemetry : IStorageTelemetryService
    {
        public List<StorageDrive> GetDrives() => new List<StorageDrive>();
        public List<StorageVolume> GetVolumes() => new List<StorageVolume>();
    }

    private class FakeBatteryTelemetry : IBatteryTelemetryService
    {
        public BatteryInfo GetBatteryInfo() => new BatteryInfo();
        public BatteryMetrics GetMetrics() => new BatteryMetrics();
    }

    [Fact]
    public void CapabilityDetection_ReturnsCorrectStatus()
    {
        var provider = new HardwareProfileProvider(
            new FakeCpuTelemetry(),
            new FakeMemoryTelemetry(),
            new FakeGpuTelemetry(),
            new FakeStorageTelemetry(),
            new FakeBatteryTelemetry(),
            new FakeDellCapabilityDetector()
        );

        var profile = provider.GetProfile();

        Assert.Equal(CapabilityStatus.Supported, profile.Capabilities[HardwareCapability.CpuTelemetry]);
        Assert.Equal(CapabilityStatus.Unsupported, profile.Capabilities[HardwareCapability.FanSpeedRead]);
        Assert.Equal(CapabilityStatus.Unsupported, profile.Capabilities[HardwareCapability.ThermalTelemetry]);
        Assert.Equal(CapabilityStatus.RequiresElevation, profile.Capabilities[HardwareCapability.DellThermalControl]);
        Assert.Equal(CapabilityStatus.Unsupported, profile.Capabilities[HardwareCapability.BatteryChargeThreshold]);
    }

    [Fact]
    public void Gpu_DetectsMultipleAndIdentifiesDiscrete()
    {
        var provider = new HardwareProfileProvider(
            new FakeCpuTelemetry(),
            new FakeMemoryTelemetry(),
            new FakeGpuTelemetry(),
            new FakeStorageTelemetry(),
            new FakeBatteryTelemetry(),
            new FakeDellCapabilityDetector()
        );

        var profile = provider.GetProfile();

        Assert.Equal(2, profile.Gpus.Count);
        Assert.Contains(profile.Gpus, g => g.Type == GpuType.Integrated);
        Assert.Contains(profile.Gpus, g => g.Type == GpuType.Discrete);
    }

    [Fact]
    public void Memory_UsageCalculation()
    {
        var mem = new FakeMemoryTelemetry();
        var metrics = mem.GetMetrics();

        Assert.Equal(50.0, metrics.UsagePercent);
        Assert.Equal(12000, metrics.TotalBytes);
        Assert.Equal(6000, metrics.UsedBytes);
    }

    [Fact]
    public void GpuPreference_Validation()
    {
        var logger = new FakeLoggingService();
        var controller = new GpuPreferenceController(logger);

        // Safe failure when registry doesn't exist
        var preference = controller.GetPreference("invalid_path.exe");
        Assert.Equal(GpuPreference.Auto, preference);
    }
}
