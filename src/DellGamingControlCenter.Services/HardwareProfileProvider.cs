namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Generic;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

public class HardwareProfileProvider : IHardwareProfileProvider
{
    private readonly ICpuTelemetryService _cpuService;
    private readonly IMemoryTelemetryService _memoryService;
    private readonly IGpuTelemetryService _gpuService;
    private readonly IStorageTelemetryService _storageService;
    private readonly IBatteryTelemetryService _batteryService;
    private readonly IDellCapabilityDetector _dellDetector;
    private readonly IHardwareTelemetryProvider? _hardwareProvider;

    public HardwareProfileProvider(
        ICpuTelemetryService cpuService,
        IMemoryTelemetryService memoryService,
        IGpuTelemetryService gpuService,
        IStorageTelemetryService storageService,
        IBatteryTelemetryService batteryService,
        IDellCapabilityDetector dellDetector,
        IHardwareTelemetryProvider? hardwareProvider = null)
    {
        _cpuService = cpuService;
        _memoryService = memoryService;
        _gpuService = gpuService;
        _storageService = storageService;
        _batteryService = batteryService;
        _dellDetector = dellDetector;
        _hardwareProvider = hardwareProvider;
    }

    public List<HardwareSensorReading> GetSensorInventory()
    {
        return _hardwareProvider?.CaptureSnapshot()?.DiscoveredSensors ?? new List<HardwareSensorReading>();
    }

    public HardwareProfile GetProfile()
    {
        var profile = new HardwareProfile
        {
            Cpu = _cpuService.GetCpuInfo(),
            Gpus = _gpuService.GetGpuInfo(),
            Memory = _memoryService.GetRamInfo(),
            Storage = _storageService.GetDrives(),
            Battery = _batteryService.GetBatteryInfo()
        };

        // Populate Capabilities
        foreach (HardwareCapability cap in Enum.GetValues(typeof(HardwareCapability)))
        {
            profile.Capabilities[cap] = GetCapabilityStatus(cap);
        }

        return profile;
    }

    public CapabilityStatus GetCapabilityStatus(HardwareCapability capability)
    {
        return capability switch
        {
            HardwareCapability.CpuTelemetry => CapabilityStatus.Supported,
            HardwareCapability.MemoryTelemetry => CapabilityStatus.Supported,
            HardwareCapability.GpuTelemetry => CapabilityStatus.Supported,
            HardwareCapability.StorageTelemetry => CapabilityStatus.Supported,
            HardwareCapability.BatteryTelemetry => CapabilityStatus.Limited,
            HardwareCapability.PowerPlanControl => CapabilityStatus.Supported,
            HardwareCapability.GpuPreferenceControl => CapabilityStatus.Supported,
            HardwareCapability.ProcessPriorityControl => CapabilityStatus.Supported,
            
            // Dynamic check based on genuine telemetry provider detection
            HardwareCapability.ThermalTelemetry => GetThermalCapabilityStatus(),
            HardwareCapability.FanSpeedRead => GetFanSpeedCapabilityStatus(),

            // Control capabilities genuinely unsupported on Inspiron 3576
            HardwareCapability.FanSpeedControl => CapabilityStatus.Unsupported,
            HardwareCapability.FanCurveControl => CapabilityStatus.Unsupported,
            HardwareCapability.CpuFrequencyControl => CapabilityStatus.Unsupported,
            HardwareCapability.CpuVoltageControl => CapabilityStatus.Unsupported,
            HardwareCapability.GpuOverclocking => CapabilityStatus.Unsupported,
            HardwareCapability.GpuUndervolting => CapabilityStatus.Unsupported,
            
            // Dell specific
            HardwareCapability.BatteryChargeThreshold => _dellDetector.GetFeatureStatus("Battery Thresholds"),
            HardwareCapability.DellThermalControl => _dellDetector.GetFeatureStatus("Thermal Management"),
            HardwareCapability.DellBiosControl => CapabilityStatus.Unsupported,
            
            HardwareCapability.MemoryOptimization => CapabilityStatus.Unsupported,
            
            _ => CapabilityStatus.Unavailable
        };
    }

    private CapabilityStatus GetThermalCapabilityStatus()
    {
        if (_hardwareProvider != null)
        {
            try
            {
                var snap = _hardwareProvider.CaptureSnapshot();
                if (snap.ThermalMetrics?.CpuTemperature.HasValue == true || snap.ThermalMetrics?.AmbientTemperature.HasValue == true)
                {
                    return CapabilityStatus.Supported;
                }
                if (snap.ThermalMetrics?.CpuTemperatureState == SensorReadingState.Unavailable)
                {
                    return CapabilityStatus.RequiresElevation;
                }
            }
            catch { }
            return CapabilityStatus.RequiresElevation;
        }
        return CapabilityStatus.Unsupported;
    }

    private CapabilityStatus GetFanSpeedCapabilityStatus()
    {
        if (_hardwareProvider != null)
        {
            try
            {
                var snap = _hardwareProvider.CaptureSnapshot();
                if (snap.ThermalMetrics?.FanRpm.HasValue == true)
                {
                    return CapabilityStatus.Supported;
                }
            }
            catch { }
        }
        return CapabilityStatus.Unsupported;
    }
}
