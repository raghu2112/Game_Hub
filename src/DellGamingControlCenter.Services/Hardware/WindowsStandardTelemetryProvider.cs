namespace DellGamingControlCenter.Services.Hardware;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class WindowsStandardTelemetryProvider : IHardwareTelemetryProvider
{
    private readonly ILoggingService _logger;
    private PerformanceCounter? _cpuTotalCounter;
    private readonly List<PerformanceCounter> _cpuCoreCounters = new();
    private readonly List<PerformanceCounter> _gpuCounters = new();
    private bool _isDisposed;

    public string ProviderName => "Windows Standard API / Performance Counters";
    public bool IsAvailable { get; private set; } = true;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public WindowsStandardTelemetryProvider(ILoggingService logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                _cpuTotalCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                _cpuTotalCounter.NextValue();

                int logicalCores = Environment.ProcessorCount;
                for (int i = 0; i < logicalCores; i++)
                {
                    try
                    {
                        var counter = new PerformanceCounter("Processor", "% Processor Time", i.ToString(), true);
                        counter.NextValue();
                        _cpuCoreCounters.Add(counter);
                    }
                    catch
                    {
                        // Instance might not be accessible
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"Windows CPU counters initialization failed: {ex.Message}", LogSeverity.Warning, "Hardware");
            }

            InitGpuCounters();
        });
    }

    private void InitGpuCounters()
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var instanceNames = category.GetInstanceNames();
            foreach (var inst in instanceNames)
            {
                if (!inst.Contains("eng_0", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true);
                    c.NextValue();
                    _gpuCounters.Add(c);
                    if (_gpuCounters.Count >= 16) break;
                }
                catch { }
            }
        }
        catch { }
    }

    public HardwareTelemetrySnapshot CaptureSnapshot()
    {
        var sensors = new List<HardwareSensorReading>();
        var now = DateTime.UtcNow;

        // 1. CPU Load
        double cpuTotal = -1;
        if (_cpuTotalCounter != null)
        {
            try
            {
                float v = _cpuTotalCounter.NextValue();
                if (v >= 0 && !float.IsNaN(v) && !float.IsInfinity(v))
                {
                    cpuTotal = Math.Round(Math.Min(v, 100f), 1);
                    sensors.Add(new HardwareSensorReading
                    {
                        SensorId = "win_cpu_total_load",
                        HardwareName = "Processor",
                        ComponentType = HardwareComponentType.Cpu,
                        SensorType = TelemetrySensorType.Load,
                        SensorName = "CPU Total Usage",
                        Value = cpuTotal,
                        Unit = "%",
                        State = SensorReadingState.Available,
                        SourceProvider = ProviderName,
                        Timestamp = now
                    });
                }
            }
            catch { }
        }

        var perCore = new List<double>();
        for (int i = 0; i < _cpuCoreCounters.Count; i++)
        {
            try
            {
                float v = _cpuCoreCounters[i].NextValue();
                if (v >= 0 && !float.IsNaN(v))
                {
                    double rounded = Math.Round(Math.Min(v, 100f), 1);
                    perCore.Add(rounded);
                    sensors.Add(new HardwareSensorReading
                    {
                        SensorId = $"win_cpu_core_{i}_load",
                        HardwareName = "Processor",
                        ComponentType = HardwareComponentType.Cpu,
                        SensorType = TelemetrySensorType.Load,
                        SensorName = $"CPU Core #{i} Usage",
                        Value = rounded,
                        Unit = "%",
                        State = SensorReadingState.Available,
                        SourceProvider = ProviderName,
                        Timestamp = now
                    });
                }
            }
            catch { }
        }

        // 2. RAM
        long totalRam = 0, usedRam = 0, availRam = 0;
        double ramPercent = 0;
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            totalRam = (long)memStatus.ullTotalPhys;
            availRam = (long)memStatus.ullAvailPhys;
            usedRam = totalRam - availRam;
            ramPercent = totalRam > 0 ? (double)usedRam / totalRam * 100 : 0;

            sensors.Add(new HardwareSensorReading
            {
                SensorId = "win_ram_load",
                HardwareName = "Memory",
                ComponentType = HardwareComponentType.Memory,
                SensorType = TelemetrySensorType.Load,
                SensorName = "Memory Load",
                Value = Math.Round(ramPercent, 1),
                Unit = "%",
                State = SensorReadingState.Available,
                SourceProvider = ProviderName,
                Timestamp = now
            });
            sensors.Add(new HardwareSensorReading
            {
                SensorId = "win_ram_used",
                HardwareName = "Memory",
                ComponentType = HardwareComponentType.Memory,
                SensorType = TelemetrySensorType.Data,
                SensorName = "Memory Used",
                Value = Math.Round(usedRam / (1024.0 * 1024 * 1024), 2),
                Unit = "GB",
                State = SensorReadingState.Available,
                SourceProvider = ProviderName,
                Timestamp = now
            });
        }

        // 3. GPU Load
        double maxGpuUsage = -1;
        foreach (var c in _gpuCounters)
        {
            try
            {
                float v = c.NextValue();
                if (v >= 0 && !float.IsNaN(v))
                {
                    if (v > maxGpuUsage) maxGpuUsage = v;
                }
            }
            catch { }
        }
        if (maxGpuUsage >= 0)
        {
            maxGpuUsage = Math.Round(Math.Min(maxGpuUsage, 100.0), 1);
            sensors.Add(new HardwareSensorReading
            {
                SensorId = "win_gpu_3d_load",
                HardwareName = "GPU Engine",
                ComponentType = HardwareComponentType.Gpu,
                SensorType = TelemetrySensorType.Load,
                SensorName = "GPU 3D Engine Usage",
                Value = maxGpuUsage,
                Unit = "%",
                State = SensorReadingState.Available,
                SourceProvider = ProviderName,
                Timestamp = now
            });
        }

        // 4. ACPI Thermal Zone
        double? acpiTempCelsius = null;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, HighPrecisionTemperature, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
            foreach (var obj in searcher.Get())
            {
                var hp = obj["HighPrecisionTemperature"];
                if (hp != null)
                {
                    double dKelvin = Convert.ToDouble(hp);
                    if (dKelvin > 2000 && dKelvin < 4000)
                    {
                        acpiTempCelsius = Math.Round((dKelvin - 2732) / 10.0, 1);
                    }
                }
                else if (obj["Temperature"] != null)
                {
                    double kelvin = Convert.ToDouble(obj["Temperature"]);
                    if (kelvin > 200 && kelvin < 400)
                    {
                        acpiTempCelsius = Math.Round(kelvin - 273.15, 1);
                    }
                }

                if (acpiTempCelsius.HasValue)
                {
                    var name = obj["Name"]?.ToString() ?? "ACPI Thermal Zone";
                    sensors.Add(new HardwareSensorReading
                    {
                        SensorId = "win_acpi_thermal_zone",
                        HardwareName = name,
                        ComponentType = HardwareComponentType.Motherboard,
                        SensorType = TelemetrySensorType.Temperature,
                        SensorName = "ACPI Thermal Zone",
                        Value = acpiTempCelsius.Value,
                        Unit = "°C",
                        State = SensorReadingState.Available,
                        SourceProvider = ProviderName,
                        Timestamp = now
                    });
                    break;
                }
            }
        }
        catch { }

        // Core temp state from Windows API standpoint is Unavailable without elevation/kernel driver
        sensors.Add(new HardwareSensorReading
        {
            SensorId = "win_cpu_core_temp",
            HardwareName = "Intel Core i3-7020U",
            ComponentType = HardwareComponentType.Cpu,
            SensorType = TelemetrySensorType.Temperature,
            SensorName = "CPU Core Temperature",
            Value = null,
            Unit = "°C",
            State = SensorReadingState.Unavailable,
            DiagnosticReason = "Requires administrator elevation or kernel driver to access MSR_IA32_THERM_STATUS",
            SourceProvider = ProviderName,
            Timestamp = now
        });

        return new HardwareTelemetrySnapshot
        {
            Timestamp = now,
            ActiveProvider = ProviderName,
            DiscoveredSensors = sensors,
            CpuMetrics = new CpuMetrics
            {
                UsagePercent = cpuTotal,
                PerCoreUsage = perCore.ToArray(),
                CurrentClockMHz = 0,
                TemperatureCelsius = null,
                TemperatureState = SensorReadingState.Unavailable,
                TemperatureDiagnosticReason = "Direct CPU core temperature requires administrator elevation or HWiNFO64 shared memory",
                ClockState = SensorReadingState.Unavailable,
                ClockDiagnosticReason = "Windows Performance Counters do not expose live CPU frequency (nominal base is 2300 MHz)",
                PowerState = SensorReadingState.Unavailable,
                PowerDiagnosticReason = "Power consumption MSRs require driver elevation or HWiNFO64",
                VoltageState = SensorReadingState.Unavailable,
                VoltageDiagnosticReason = "Core voltage requires driver elevation or HWiNFO64",
                Timestamp = now
            },
            RamMetrics = new RamMetrics
            {
                TotalBytes = totalRam,
                AvailableBytes = availRam,
                UsedBytes = usedRam,
                UsagePercent = ramPercent,
                Timestamp = now
            },
            DiscreteGpuMetrics = new GpuMetrics
            {
                UsagePercent = maxGpuUsage >= 0 ? maxGpuUsage : null,
                TemperatureState = SensorReadingState.Unavailable,
                TemperatureDiagnosticReason = "Discrete GPU sensor requires activation or HWiNFO64 integration",
                MemoryState = SensorReadingState.Unavailable,
                MemoryDiagnosticReason = "Dedicated VRAM allocation requires DXGI / LibreHardwareMonitor",
                Timestamp = now
            },
            ThermalMetrics = new ThermalMetrics
            {
                AmbientTemperature = acpiTempCelsius,
                CpuTemperature = null,
                CpuTemperatureState = SensorReadingState.Unavailable,
                GpuTemperatureState = SensorReadingState.Unavailable,
                FanRpmState = SensorReadingState.Unavailable,
                DiagnosticReason = "Direct CPU core temperature requires administrator elevation or HWiNFO64 shared memory",
                Timestamp = now
            }
        };
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try { _cpuTotalCounter?.Dispose(); } catch { }
        foreach (var c in _cpuCoreCounters) { try { c.Dispose(); } catch { } }
        foreach (var c in _gpuCounters) { try { c.Dispose(); } catch { } }
        _cpuCoreCounters.Clear();
        _gpuCounters.Clear();
    }
}
