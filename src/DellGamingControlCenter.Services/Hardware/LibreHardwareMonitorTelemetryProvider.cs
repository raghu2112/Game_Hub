namespace DellGamingControlCenter.Services.Hardware;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using LibreHardwareMonitor.Hardware;

public class LibreHardwareMonitorTelemetryProvider : IHardwareTelemetryProvider
{
    private readonly ILoggingService _logger;
    private Computer? _computer;
    private readonly object _syncLock = new();
    private bool _isDisposed;
    private bool _isAdmin;

    public string ProviderName => "LibreHardwareMonitor";
    public bool IsAvailable { get; private set; }

    public LibreHardwareMonitorTelemetryProvider(ILoggingService logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync()
    {
        return Task.Run(() =>
        {
            lock (_syncLock)
            {
                try
                {
                    _isAdmin = OperatingSystem.IsWindows() && 
                        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

                    _computer = new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsMemoryEnabled = true,
                        IsMotherboardEnabled = true,
                        IsControllerEnabled = true,
                        IsStorageEnabled = true
                    };

                    _computer.Open();
                    IsAvailable = true;
                    _logger.Log($"LibreHardwareMonitor initialized successfully (Admin: {_isAdmin}).", LogSeverity.Info, "Hardware");
                }
                catch (Exception ex)
                {
                    IsAvailable = false;
                    _logger.Log($"LibreHardwareMonitor failed to initialize: {ex.Message}", LogSeverity.Warning, "Hardware");
                }
            }
        });
    }

    public HardwareTelemetrySnapshot CaptureSnapshot()
    {
        var now = DateTime.UtcNow;
        var readings = new List<HardwareSensorReading>();

        if (!IsAvailable || _computer == null)
        {
            return new HardwareTelemetrySnapshot
            {
                Timestamp = now,
                ActiveProvider = ProviderName,
                DiscoveredSensors = readings
            };
        }

        lock (_syncLock)
        {
            try
            {
                // Temporary accumulators for aggregated models
                double? cpuTotalLoad = null;
                var perCoreLoads = new List<double>();
                var perCoreClocks = new List<double>();
                var perCoreTemps = new List<double>();
                double? cpuPackageTemp = null;
                double? cpuPackagePower = null;
                double? cpuCoreVoltage = null;
                double maxCpuClock = 0;

                long ramTotalBytes = 0, ramUsedBytes = 0, ramAvailBytes = 0;
                double? ramLoadPercent = null;

                double? amdGpuLoad = null, amdGpuTemp = null, amdGpuClock = null, amdGpuMemClock = null, amdGpuPower = null;
                long? amdGpuVramUsed = null, amdGpuVramAvail = null, amdGpuVramTotal = null;
                long? amdGpuSharedUsed = null, amdGpuSharedAvail = null, amdGpuSharedTotal = null;
                int? amdGpuFanRpm = null;

                double? intelGpuLoad = null, intelGpuPower = null;
                long? intelSharedMemTotal = null, intelSharedMemUsed = null;

                int? fanRpm = null;

                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                    ProcessHardwareSensors(hardware, readings, now);

                    foreach (var sub in hardware.SubHardware)
                    {
                        sub.Update();
                        ProcessHardwareSensors(sub, readings, now);
                    }

                    // Extract metrics for aggregated models
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        foreach (var s in hardware.Sensors)
                        {
                            if (s.SensorType == SensorType.Load)
                            {
                                if (s.Name.Equals("CPU Total", StringComparison.OrdinalIgnoreCase))
                                    cpuTotalLoad = s.Value.HasValue ? Math.Round(s.Value.Value, 1) : null;
                                else if (s.Name.Contains("Core #") && s.Name.Contains("Thread #") && s.Value.HasValue)
                                    perCoreLoads.Add(Math.Round(s.Value.Value, 1));
                            }
                            else if (s.SensorType == SensorType.Temperature)
                            {
                                if (s.Name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    cpuPackageTemp = Math.Round(s.Value.Value, 1);
                                else if (s.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase) && !s.Name.Contains("Distance") && s.Value.HasValue)
                                    perCoreTemps.Add(Math.Round(s.Value.Value, 1));
                            }
                            else if (s.SensorType == SensorType.Clock)
                            {
                                if (s.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                {
                                    perCoreClocks.Add(Math.Round(s.Value.Value, 0));
                                    if (s.Value.Value > maxCpuClock) maxCpuClock = s.Value.Value;
                                }
                            }
                            else if (s.SensorType == SensorType.Power)
                            {
                                if (s.Name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue && (s.Value.Value > 0.05f || _isAdmin))
                                    cpuPackagePower = Math.Round(s.Value.Value, 2);
                            }
                            else if (s.SensorType == SensorType.Voltage)
                            {
                                if (s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue && (s.Value.Value > 0.1f || _isAdmin))
                                    cpuCoreVoltage = Math.Round(s.Value.Value, 3);
                            }
                        }
                    }
                    else if (hardware.HardwareType == HardwareType.Memory)
                    {
                        if (hardware.Name.Equals("Total Memory", StringComparison.OrdinalIgnoreCase) || hardware.Identifier.ToString().Contains("/ram"))
                        {
                            foreach (var s in hardware.Sensors)
                            {
                                if (s.SensorType == SensorType.Data && s.Name.Equals("Memory Used", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    ramUsedBytes = (long)(s.Value.Value * 1024 * 1024 * 1024);
                                else if (s.SensorType == SensorType.Data && s.Name.Equals("Memory Available", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    ramAvailBytes = (long)(s.Value.Value * 1024 * 1024 * 1024);
                                else if (s.SensorType == SensorType.Load && s.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    ramLoadPercent = Math.Round(s.Value.Value, 1);
                            }
                            ramTotalBytes = ramUsedBytes + ramAvailBytes;
                        }
                    }
                    else if (hardware.HardwareType == HardwareType.GpuAmd)
                    {
                        foreach (var s in hardware.Sensors)
                        {
                            if (s.SensorType == SensorType.Load && (s.Name.Equals("D3D 3D", StringComparison.OrdinalIgnoreCase) || s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)))
                            {
                                if (s.Value.HasValue && (amdGpuLoad == null || s.Value.Value > amdGpuLoad))
                                    amdGpuLoad = Math.Round(s.Value.Value, 1);
                            }
                            else if (s.SensorType == SensorType.Temperature && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue && s.Value.Value > 0)
                            {
                                amdGpuTemp = Math.Round(s.Value.Value, 1);
                            }
                            else if (s.SensorType == SensorType.Clock)
                            {
                                if (s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    amdGpuClock = Math.Round(s.Value.Value, 0);
                                else if (s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    amdGpuMemClock = Math.Round(s.Value.Value, 0);
                            }
                            else if (s.SensorType == SensorType.SmallData || s.SensorType == SensorType.Data)
                            {
                                if (s.Name.Equals("D3D Dedicated Memory Total", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    amdGpuVramTotal = (long)(s.Value.Value * 1024 * 1024);
                                else if (s.Name.Equals("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    amdGpuVramUsed = (long)(s.Value.Value * 1024 * 1024);
                                else if (s.Name.Equals("D3D Dedicated Memory Free", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    amdGpuVramAvail = (long)(s.Value.Value * 1024 * 1024);
                                else if (s.Name.Equals("D3D Shared Memory Total", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    amdGpuSharedTotal = (long)(s.Value.Value * 1024 * 1024);
                                else if (s.Name.Equals("D3D Shared Memory Used", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    amdGpuSharedUsed = (long)(s.Value.Value * 1024 * 1024);
                                else if (s.Name.Equals("D3D Shared Memory Free", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    amdGpuSharedAvail = (long)(s.Value.Value * 1024 * 1024);
                            }
                            else if (s.SensorType == SensorType.Fan && s.Value.HasValue && s.Value.Value > 0)
                            {
                                amdGpuFanRpm = (int)s.Value.Value;
                            }
                        }
                    }
                    else if (hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        foreach (var s in hardware.Sensors)
                        {
                            if (s.SensorType == SensorType.Load && s.Name.Equals("D3D 3D", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                intelGpuLoad = Math.Round(s.Value.Value, 1);
                            else if (s.SensorType == SensorType.Power && s.Value.HasValue && s.Value.Value > 0)
                                intelGpuPower = Math.Round(s.Value.Value, 2);
                            else if (s.SensorType == SensorType.SmallData || s.SensorType == SensorType.Data)
                            {
                                if (s.Name.Equals("D3D Shared Memory Total", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    intelSharedMemTotal = (long)(s.Value.Value * 1024 * 1024);
                                else if (s.Name.Equals("D3D Shared Memory Used", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)
                                    intelSharedMemUsed = (long)(s.Value.Value * 1024 * 1024);
                            }
                        }
                    }
                    else if (hardware.HardwareType == HardwareType.Motherboard || hardware.HardwareType == HardwareType.SuperIO)
                    {
                        foreach (var s in hardware.Sensors)
                        {
                            if (s.SensorType == SensorType.Fan && s.Value.HasValue && s.Value.Value > 0)
                                fanRpm = (int)s.Value.Value;
                        }
                    }
                }

                // If package temp wasn't found but core temps exist, use average
                if (!cpuPackageTemp.HasValue && perCoreTemps.Count > 0)
                {
                    cpuPackageTemp = Math.Round(perCoreTemps.Average(), 1);
                }

                return new HardwareTelemetrySnapshot
                {
                    Timestamp = now,
                    ActiveProvider = ProviderName,
                    DiscoveredSensors = readings,
                    CpuMetrics = new CpuMetrics
                    {
                        UsagePercent = cpuTotalLoad ?? -1,
                        PerCoreUsage = perCoreLoads.ToArray(),
                        CurrentClockMHz = maxCpuClock > 0 ? maxCpuClock : 0,
                        PerCoreClocksMHz = perCoreClocks.ToArray(),
                        TemperatureCelsius = cpuPackageTemp,
                        PerCoreTemperaturesCelsius = perCoreTemps.ToArray(),
                        PackagePowerWatts = cpuPackagePower,
                        CoreVoltageVolts = cpuCoreVoltage,
                        TemperatureState = cpuPackageTemp.HasValue ? SensorReadingState.Available :
                            (!_isAdmin ? SensorReadingState.Unavailable : SensorReadingState.Unsupported),
                        TemperatureDiagnosticReason = cpuPackageTemp.HasValue ? null :
                            (!_isAdmin ? "Requires administrator elevation to load ring-0 driver (WinRing0) or running HWiNFO64" : "Sensor not exposed by CPU"),
                        ClockState = maxCpuClock > 0 ? SensorReadingState.Available : SensorReadingState.Unavailable,
                        ClockDiagnosticReason = maxCpuClock > 0 ? null :
                            (!_isAdmin ? "Requires administrator elevation (WinRing0) or HWiNFO64 to read live CPU clock registers (nominal base is 2300 MHz)" : "Clock registers not exposed"),
                        PowerState = cpuPackagePower.HasValue ? SensorReadingState.Available : SensorReadingState.Unavailable,
                        PowerDiagnosticReason = cpuPackagePower.HasValue ? null :
                            (!_isAdmin ? "Requires administrator elevation (WinRing0) to read MSR energy status (0x611) or HWiNFO64" : "Energy registers not exposed"),
                        VoltageState = cpuCoreVoltage.HasValue ? SensorReadingState.Available : SensorReadingState.Unavailable,
                        VoltageDiagnosticReason = cpuCoreVoltage.HasValue ? null :
                            (!_isAdmin ? "Requires administrator elevation (WinRing0) to read CPU VID registers or HWiNFO64" : "Voltage registers not exposed"),
                        Timestamp = now
                    },
                    RamMetrics = new RamMetrics
                    {
                        TotalBytes = ramTotalBytes,
                        AvailableBytes = ramAvailBytes,
                        UsedBytes = ramUsedBytes,
                        UsagePercent = ramLoadPercent ?? (ramTotalBytes > 0 ? (double)ramUsedBytes / ramTotalBytes * 100 : 0),
                        Timestamp = now
                    },
                    DiscreteGpuMetrics = new GpuMetrics
                    {
                        UsagePercent = amdGpuLoad,
                        TemperatureCelsius = amdGpuTemp,
                        CoreClockMHz = amdGpuClock,
                        MemoryClockMHz = amdGpuMemClock,
                        PowerWatts = amdGpuPower,
                        VramUsedBytes = amdGpuVramUsed,
                        VramAvailableBytes = amdGpuVramAvail,
                        VramTotalBytes = amdGpuVramTotal ?? (2048L * 1024 * 1024),
                        SharedMemoryUsedBytes = amdGpuSharedUsed,
                        SharedMemoryAvailableBytes = amdGpuSharedAvail,
                        SharedMemoryTotalBytes = amdGpuSharedTotal,
                        FanRpm = amdGpuFanRpm,
                        TemperatureState = amdGpuTemp.HasValue ? SensorReadingState.Available : SensorReadingState.Unavailable,
                        TemperatureDiagnosticReason = amdGpuTemp.HasValue ? null : "AMD Radeon 520 in PCIe D3cold power-saving sleep",
                        MemoryState = amdGpuVramTotal.HasValue ? SensorReadingState.Available : SensorReadingState.Unavailable,
                        MemoryDiagnosticReason = amdGpuVramTotal.HasValue ? null : "DXGI memory telemetry unavailable",
                        Timestamp = now
                    },
                    IntegratedGpuMetrics = new GpuMetrics
                    {
                        UsagePercent = intelGpuLoad,
                        PowerWatts = intelGpuPower,
                        VramUsedBytes = intelSharedMemUsed,
                        VramTotalBytes = intelSharedMemTotal,
                        TemperatureState = SensorReadingState.Unavailable,
                        TemperatureDiagnosticReason = "Integrated graphics shares CPU package thermal envelope",
                        Timestamp = now
                    },
                    ThermalMetrics = new ThermalMetrics
                    {
                        CpuTemperature = cpuPackageTemp,
                        GpuTemperature = amdGpuTemp,
                        FanRpm = fanRpm,
                        CpuTemperatureState = cpuPackageTemp.HasValue ? SensorReadingState.Available :
                            (!_isAdmin ? SensorReadingState.Unavailable : SensorReadingState.Unsupported),
                        GpuTemperatureState = amdGpuTemp.HasValue ? SensorReadingState.Available : SensorReadingState.Unavailable,
                        FanRpmState = fanRpm.HasValue ? SensorReadingState.Available : SensorReadingState.Unsupported,
                        DiagnosticReason = cpuPackageTemp.HasValue ? null :
                            (!_isAdmin ? "Requires administrator elevation to load ring-0 driver (WinRing0)" : null),
                        Timestamp = now
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.Log($"Error during LibreHardwareMonitor sampling: {ex.Message}", LogSeverity.Warning, "Hardware");
                return new HardwareTelemetrySnapshot
                {
                    Timestamp = now,
                    ActiveProvider = ProviderName,
                    DiscoveredSensors = readings
                };
            }
        }
    }

    private void ProcessHardwareSensors(IHardware hardware, List<HardwareSensorReading> readings, DateTime now)
    {
        var compType = MapComponentType(hardware.HardwareType);

        foreach (var sensor in hardware.Sensors)
        {
            var sensorType = MapSensorType(sensor.SensorType);
            var state = SensorReadingState.Available;
            string? diagReason = null;

            if (!sensor.Value.HasValue)
            {
                if (sensorType == TelemetrySensorType.Temperature || sensorType == TelemetrySensorType.Clock)
                {
                    state = SensorReadingState.Unavailable;
                    diagReason = !_isAdmin
                        ? "Requires administrator elevation to load ring-0 driver (WinRing0)"
                        : "Hardware driver returned null reading";
                }
                else
                {
                    state = SensorReadingState.Unavailable;
                    diagReason = "Sensor value currently null";
                }
            }
            else if (sensorType == TelemetrySensorType.Temperature && sensor.Value.Value <= 0.0f)
            {
                state = SensorReadingState.Unavailable;
                diagReason = "AMD Radeon 520 in PCIe D3cold power-saving sleep (GPU powered off)";
            }
            else if (sensorType == TelemetrySensorType.Power && sensor.Value.Value <= 0.05f && !_isAdmin)
            {
                state = SensorReadingState.Unavailable;
                diagReason = "Requires administrator elevation to read MSR energy status";
            }
            else if (sensorType == TelemetrySensorType.Fan && sensor.Value.Value <= 0.0f && hardware.HardwareType == HardwareType.GpuAmd)
            {
                state = SensorReadingState.Unavailable;
                diagReason = "Discrete GPU fan inactive (GPU in standby)";
            }

            readings.Add(new HardwareSensorReading
            {
                SensorId = sensor.Identifier.ToString(),
                HardwareName = hardware.Name,
                ComponentType = compType,
                SensorType = sensorType,
                SensorName = sensor.Name,
                Value = (state == SensorReadingState.Available && sensor.Value.HasValue) ? Math.Round(sensor.Value.Value, 2) : null,
                MinValue = sensor.Min.HasValue ? Math.Round(sensor.Min.Value, 2) : null,
                MaxValue = sensor.Max.HasValue ? Math.Round(sensor.Max.Value, 2) : null,
                Unit = GetUnitString(sensor.SensorType),
                State = state,
                DiagnosticReason = diagReason,
                SourceProvider = ProviderName,
                Timestamp = now
            });
        }
    }

    private static HardwareComponentType MapComponentType(HardwareType type) => type switch
    {
        HardwareType.Cpu => HardwareComponentType.Cpu,
        HardwareType.GpuAmd => HardwareComponentType.Gpu,
        HardwareType.GpuIntel => HardwareComponentType.Gpu,
        HardwareType.GpuNvidia => HardwareComponentType.Gpu,
        HardwareType.Memory => HardwareComponentType.Memory,
        HardwareType.Storage => HardwareComponentType.Storage,
        HardwareType.Motherboard => HardwareComponentType.Motherboard,
        HardwareType.SuperIO => HardwareComponentType.Motherboard,
        HardwareType.Battery => HardwareComponentType.Battery,
        _ => HardwareComponentType.Other
    };

    private static TelemetrySensorType MapSensorType(SensorType type) => type switch
    {
        SensorType.Voltage => TelemetrySensorType.Voltage,
        SensorType.Clock => TelemetrySensorType.Clock,
        SensorType.Temperature => TelemetrySensorType.Temperature,
        SensorType.Load => TelemetrySensorType.Load,
        SensorType.Fan => TelemetrySensorType.Fan,
        SensorType.Power => TelemetrySensorType.Power,
        SensorType.Data => TelemetrySensorType.Data,
        SensorType.SmallData => TelemetrySensorType.Data,
        SensorType.Factor => TelemetrySensorType.Factor,
        SensorType.Control => TelemetrySensorType.Control,
        _ => TelemetrySensorType.Other
    };

    private static string GetUnitString(SensorType type) => type switch
    {
        SensorType.Voltage => "V",
        SensorType.Clock => "MHz",
        SensorType.Temperature => "°C",
        SensorType.Load => "%",
        SensorType.Fan => "RPM",
        SensorType.Power => "W",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Factor => "",
        SensorType.Control => "%",
        _ => ""
    };

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        lock (_syncLock)
        {
            try
            {
                _computer?.Close();
                _computer = null;
            }
            catch { }
        }
    }
}
