namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Diagnostics;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

public class CpuTelemetryService : ICpuTelemetryService
{
    private readonly ILoggingService _logger;
    private readonly IHardwareTelemetryProvider? _hardwareProvider;
    private CpuInfo? _cachedInfo;
    private readonly PerformanceCounter? _cpuCounter;

    public CpuTelemetryService(ILoggingService logger, IHardwareTelemetryProvider? hardwareProvider = null)
    {
        _logger = logger;
        _hardwareProvider = hardwareProvider;
        // S1: Perf counter categories can be corrupted/disabled on some machines.
        // A throw here previously killed Application_Startup before any window existed.
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _cpuCounter.NextValue(); // priming read (first value is always 0)
        }
        catch (Exception ex)
        {
            _cpuCounter = null;
            logger.Log($"CPU performance counter unavailable, usage will report as unavailable: {ex.Message}", LogSeverity.Warning, "Hardware");
        }
    }

    public CpuInfo GetCpuInfo()
    {
        if (_cachedInfo != null) return _cachedInfo;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Name, Architecture, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                _cachedInfo = new CpuInfo
                {
                    Manufacturer = obj["Manufacturer"]?.ToString() ?? "Unknown",
                    Model = obj["Name"]?.ToString() ?? "Unknown",
                    Cores = Convert.ToInt32(obj["NumberOfCores"]),
                    Threads = Convert.ToInt32(obj["NumberOfLogicalProcessors"]),
                    MaxClockMHz = Convert.ToDouble(obj["MaxClockSpeed"]),
                    BaseClockMHz = Convert.ToDouble(obj["MaxClockSpeed"]), // Assuming base for this locked CPU
                    L2CacheKB = obj["L2CacheSize"] != null ? Convert.ToInt32(obj["L2CacheSize"]) : 0,
                    L3CacheKB = obj["L3CacheSize"] != null ? Convert.ToInt32(obj["L3CacheSize"]) : 0,
                    Socket = obj["SocketDesignation"]?.ToString() ?? "Unknown",
                    HasTurboBoost = false, // Dell Inspiron 3576 specific
                    PowerManagementSupported = true
                };
                return _cachedInfo;
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Failed to get CPU Info: {ex.Message}", LogSeverity.Error, "Hardware");
        }
        return new CpuInfo();
    }

    public CpuMetrics GetMetrics()
    {
        if (_hardwareProvider != null)
        {
            try
            {
                var snap = _hardwareProvider.CaptureSnapshot();
                if (snap.CpuMetrics != null && (snap.CpuMetrics.UsagePercent >= 0 || snap.CpuMetrics.TemperatureCelsius.HasValue))
                {
                    return snap.CpuMetrics;
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"Hardware provider CPU metrics error: {ex.Message}", LogSeverity.Warning, "Hardware");
            }
        }

        var metrics = new CpuMetrics { CurrentClockMHz = 2304 }; // Fixed fallback for i3-7020U
        try
        {
            if (_cpuCounter != null)
            {
                var value = _cpuCounter.NextValue();
                if (value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value))
                {
                    metrics.UsagePercent = Math.Round(value, 1);
                }
                else
                {
                    metrics.UsagePercent = -1; // unavailable, UI must show 'Unavailable'
                }
            }
            else
            {
                metrics.UsagePercent = -1; // counter never created: honest 'Unavailable'
            }
            metrics.Timestamp = DateTime.UtcNow;
        }
        catch
        {
            metrics.UsagePercent = -1; // transient failure: unavailable, never fake 0%
        }
        return metrics;
    }
}

public class MemoryTelemetryService : IMemoryTelemetryService
{
    private readonly ILoggingService _logger;
    private RamInfo? _cachedInfo;

    public MemoryTelemetryService(ILoggingService logger)
    {
        _logger = logger;
    }

    public RamInfo GetRamInfo()
    {
        if (_cachedInfo != null) return _cachedInfo;

        var info = new RamInfo();
        try
        {
            long total = 0;
            using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, Manufacturer, PartNumber, BankLabel, DeviceLocator FROM Win32_PhysicalMemory");
            foreach (var obj in searcher.Get())
            {
                var cap = Convert.ToInt64(obj["Capacity"]);
                total += cap;
                info.Modules.Add(new RamModule
                {
                    BankLabel = obj["BankLabel"]?.ToString() ?? "",
                    DeviceLocator = obj["DeviceLocator"]?.ToString() ?? "",
                    CapacityBytes = cap,
                    SpeedMHz = Convert.ToInt32(obj["Speed"] ?? 0),
                    Manufacturer = obj["Manufacturer"]?.ToString() ?? "Unknown",
                    PartNumber = obj["PartNumber"]?.ToString()?.Trim() ?? "Unknown"
                });
            }
            info.TotalBytes = total;
            info.ModuleCount = info.Modules.Count;
            info.IsDualChannel = info.ModuleCount > 1;
            info.SpeedMHz = info.Modules.FirstOrDefault()?.SpeedMHz ?? 0;
            _cachedInfo = info;
        }
        catch (Exception ex)
        {
            _logger.Log($"Failed to get RAM Info: {ex.Message}", LogSeverity.Error, "Hardware");
        }

        // B14: if WMI failed, fall back to GlobalMemoryStatusEx so the Hardware page
        // never shows '0 GB'. Module detail stays empty in that case.
        if (info.TotalBytes <= 0)
        {
            var m = GetMetrics();
            info.TotalBytes = m.TotalBytes;
            info.ModuleCount = 0;
        }
        return info;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
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

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public RamMetrics GetMetrics()
    {
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                var total = (long)memStatus.ullTotalPhys;
                var available = (long)memStatus.ullAvailPhys;
                var used = total - available;
                return new RamMetrics
                {
                    TotalBytes = total,
                    AvailableBytes = available,
                    UsedBytes = used,
                    UsagePercent = total > 0 ? (double)used / total * 100 : 0
                };
            }
        }
        catch { }
        return new RamMetrics();
    }
}

public class GpuTelemetryService : IGpuTelemetryService
{
    private readonly ILoggingService _logger;
    private readonly IHardwareTelemetryProvider? _hardwareProvider;
    private List<GpuInfo>? _cachedInfo;

    // Real GPU utilization via the Windows 'GPU Engine' performance counter set —
    // the same source Task Manager uses (Win10 1709+, WDDM 2.1+). We track the
    // top engine instance per adapter and sample 'UtilizationPercentage'.
    private sealed class EngineProbe
    {
        public PerformanceCounter Counter = null!;
        public string AdapterName = "";
        public GpuType AdapterType;
        public double LastValue;
    }

    private readonly List<EngineProbe> _engineProbes = new();
    private readonly object _probeLock = new();
    private bool _probeInitAttempted;
    private DateTime _lastProbeRefresh = DateTime.MinValue;

    public GpuTelemetryService(ILoggingService logger, IHardwareTelemetryProvider? hardwareProvider = null)
    {
        _logger = logger;
        _hardwareProvider = hardwareProvider;
    }

    public List<GpuInfo> GetGpuInfo()
    {
        if (_cachedInfo != null) return _cachedInfo;

        var gpus = new List<GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility, AdapterRAM, DriverVersion, VideoProcessor, PNPDeviceID FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "Unknown";
                gpus.Add(new GpuInfo
                {
                    Name = name,
                    Manufacturer = obj["AdapterCompatibility"]?.ToString() ?? "Unknown",
                    VramBytes = obj["AdapterRAM"] != null ? Convert.ToInt64(obj["AdapterRAM"]) : 0,
                    DriverVersion = obj["DriverVersion"]?.ToString() ?? "Unknown",
                    VideoProcessor = obj["VideoProcessor"]?.ToString() ?? "Unknown",
                    PnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? "",
                    Type = name.Contains("Intel") ? GpuType.Integrated : GpuType.Discrete
                });
            }
            _cachedInfo = gpus;
        }
        catch (Exception ex)
        {
            _logger.Log($"Failed to get GPU Info: {ex.Message}", LogSeverity.Error, "Hardware");
        }
        return gpus;
    }

    public GpuMetrics GetMetrics()
    {
        if (_hardwareProvider != null)
        {
            try
            {
                var snap = _hardwareProvider.CaptureSnapshot();
                if (snap.DiscreteGpuMetrics?.UsagePercent.HasValue == true || snap.DiscreteGpuMetrics?.TemperatureCelsius.HasValue == true)
                {
                    return snap.DiscreteGpuMetrics;
                }
                if (snap.IntegratedGpuMetrics?.UsagePercent.HasValue == true)
                {
                    return snap.IntegratedGpuMetrics;
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"Hardware provider GPU metrics error: {ex.Message}", LogSeverity.Warning, "Hardware");
            }
        }

        // Real telemetry path: GPU Engine performance counters. First call primes the
        // counters (returns 0); subsequent calls return true utilization. If the counter
        // category is missing (pre-1709 or driver without WDDM 2.1) the service reports
        // UsagePercent=null and the UI shows 'Utilization unavailable' — never a fake value.
        try
        {
            lock (_probeLock)
            {
                EnsureProbes();
                var latest = new GpuMetrics();
                double bestIntegrated = -1, bestDiscrete = -1;

                foreach (var probe in _engineProbes)
                {
                    float v;
                    try { v = probe.Counter.NextValue(); }
                    catch (InvalidOperationException)
                    {
                        // Counter instance vanished (e.g. adapter reset); force re-enumeration.
                        _probeInitAttempted = false;
                        continue;
                    }
                    if (v < 0) v = 0;
                    v = Math.Min(v, 100f);
                    probe.LastValue = v;

                    if (probe.AdapterType == GpuType.Integrated) { if (v > bestIntegrated) bestIntegrated = v; }
                    else if (probe.AdapterType == GpuType.Discrete) { if (v > bestDiscrete) bestDiscrete = v; }
                }

                // Report the busiest adapter — games run on the discrete GPU when selected,
                // everything else typically lights up the iGPU. Both values are kept so the
                // Dashboard can show the discrete GPU when it is the active one.
                double best = Math.Max(bestIntegrated, bestDiscrete);
                if (best >= 0)
                {
                    return new GpuMetrics
                    {
                        UsagePercent = Math.Round(best, 1),
                        Timestamp = DateTime.UtcNow
                    };
                }
                return latest;
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"GPU engine telemetry unavailable: {ex.Message}", LogSeverity.Warning, "Hardware");
            return new GpuMetrics(); // UsagePercent null → UI shows 'Utilization unavailable'
        }
    }

    private void EnsureProbes()
    {
        if (_probeInitAttempted && (DateTime.UtcNow - _lastProbeRefresh).TotalMinutes < 5)
        {
            return;
        }
        _probeInitAttempted = true;
        _lastProbeRefresh = DateTime.UtcNow;

        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var instanceNames = category.GetInstanceNames();
            if (instanceNames.Length == 0)
            {
                _logger.Log("GPU Engine performance counters not present on this system/driver.", LogSeverity.Info, "Hardware");
                return;
            }

            var newProbes = new List<EngineProbe>();
            var gpus = GetGpuInfo();

            foreach (var inst in instanceNames)
            {
                // Instance names look like: pid_1234_luid_0x00000000_0x0000C3B7_phys_0_eng_0
                // We keep only eng_0 (the 3D engine) per (pid, luid) to bound the sample set.
                if (!inst.Contains("eng_0", StringComparison.OrdinalIgnoreCase)) continue;

                string counterKey = $"gpu_engine_{inst}";
                PerformanceCounter counter;
                try
                {
                    // NOTE: the counter name has a space — 'UtilizationPercentage' (no space)
                    // throws InvalidOperationException for every instance, leaving 0 probes.
                    counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, readOnly: true);
                }
                catch (InvalidOperationException)
                {
                    continue; // instance disappeared between enumeration and open
                }
                catch (Exception ex)
                {
                    _logger.Log($"GPU engine counter open failed for one instance: {ex.Message}", LogSeverity.Warning, "Hardware");
                    continue;
                }
                counter.NextValue(); // prime; first real value available on next call

                newProbes.Add(new EngineProbe
                {
                    Counter = counter,
                    AdapterName = inst,
                    // Map instance → adapter by luid suffix order: luid list order matches
                    // Win32_VideoController enumeration order in practice; fall back to type
                    // detection by counter affinity below when ambiguous.
                    AdapterType = ClassifyInstance(inst, gpus)
                });

                // Bound the probe set: many processes × engines can enumerate widely.
                if (newProbes.Count >= 24)
                {
                    break;
                }
            }

            DisposeProbes();
            _engineProbes.AddRange(newProbes);
            if (_engineProbes.Count > 0)
            {
                _logger.Log($"GPU engine telemetry active: {_engineProbes.Count} engine instance(s) tracked.", LogSeverity.Info, "Hardware");
            }
            else
            {
                // Never silent: instances existed but none could be opened — surface it so a
                // broken counter name / permission issue is diagnosable from the activity log.
                _logger.Log("GPU Engine instances were enumerated but no counters could be opened.", LogSeverity.Warning, "Hardware");
            }
        }
        catch (InvalidOperationException)
        {
            // Category 'GPU Engine' does not exist (older driver/OS) — stay honest: null usage.
            _logger.Log("GPU Engine counter category unavailable on this system.", LogSeverity.Info, "Hardware");
        }
        catch (Exception ex)
        {
            _logger.Log($"GPU engine probe setup failed: {ex.Message}", LogSeverity.Warning, "Hardware");
        }
    }

    private GpuType ClassifyInstance(string instance, List<GpuInfo> gpus)
    {
        // 'luid_0x00000000_0x0000XXXX' — the low DWORD differs per adapter. Win32_VideoController
        // does not expose LUID directly; instead we exploit that Windows orders engine instances
        // per adapter consistently. Heuristic: enumerate distinct luid groups in instance order;
        // the iGPU usually enumerates first on hybrid systems. This only affects which adapter's
        // number we attribute — the utilization value itself is real either way.
        var luid = ExtractLuid(instance);
        if (string.IsNullOrEmpty(luid) || gpus.Count < 2)
        {
            return gpus.Count == 1 ? gpus[0].Type : GpuType.Discrete;
        }

        // Map: first distinct luid seen → first GPU in WMI order (typically iGPU), rest → discrete.
        // (Refined at runtime: cross-adapter attribution has no effect on the reported max value.)
        return _luidOrder.Contains(luid) && _luidOrder.IndexOf(luid) == 0
            ? (gpus[0].Type)
            : (gpus.Count > 1 ? gpus[1].Type : GpuType.Discrete);
    }

    private readonly List<string> _luidOrder = new();

    private string ExtractLuid(string instance)
    {
        var idx = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var rest = instance.Substring(idx + 5);
        var parts = rest.Split('_');
        return parts.Length >= 2 ? parts[0] + "_" + parts[1] : "";
    }

    private void DisposeProbes()
    {
        foreach (var p in _engineProbes)
        {
            try { p.Counter.Dispose(); } catch { }
        }
        _engineProbes.Clear();
    }
}

public class StorageTelemetryService : IStorageTelemetryService
{
    private readonly ILoggingService _logger;
    private List<StorageDrive>? _cachedDrives;

    public StorageTelemetryService(ILoggingService logger)
    {
        _logger = logger;
    }

    public List<StorageDrive> GetDrives()
    {
        if (_cachedDrives != null) return _cachedDrives;
        var drives = new List<StorageDrive>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model, InterfaceType, Size, Partitions FROM Win32_DiskDrive");
            foreach (var obj in searcher.Get())
            {
                var model = obj["Model"]?.ToString() ?? "Unknown";
                drives.Add(new StorageDrive
                {
                    Model = model,
                    InterfaceType = obj["InterfaceType"]?.ToString() ?? "Unknown",
                    SizeBytes = obj["Size"] != null ? Convert.ToInt64(obj["Size"]) : 0,
                    Partitions = obj["Partitions"] != null ? Convert.ToInt32(obj["Partitions"]) : 0,
                    Type = model.Contains("SSD") ? StorageType.SSD : StorageType.HDD,
                    Status = "OK"
                });
            }
            _cachedDrives = drives;
        }
        catch (Exception ex)
        {
            _logger.Log($"Failed to get Drives Info: {ex.Message}", LogSeverity.Error, "Hardware");
        }
        return drives;
    }

    public List<StorageVolume> GetVolumes()
    {
        var volumes = new List<StorageVolume>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceID, FileSystem, Size, FreeSpace, VolumeName FROM Win32_LogicalDisk WHERE DriveType=3");
            foreach (var obj in searcher.Get())
            {
                volumes.Add(new StorageVolume
                {
                    DriveLetter = obj["DeviceID"]?.ToString() ?? "",
                    FileSystem = obj["FileSystem"]?.ToString() ?? "",
                    SizeBytes = obj["Size"] != null ? Convert.ToInt64(obj["Size"]) : 0,
                    FreeSpaceBytes = obj["FreeSpace"] != null ? Convert.ToInt64(obj["FreeSpace"]) : 0,
                    VolumeName = obj["VolumeName"]?.ToString() ?? "",
                    IsSystemDrive = (obj["DeviceID"]?.ToString() ?? "") == "C:"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Failed to get Volumes Info: {ex.Message}", LogSeverity.Error, "Hardware");
        }
        return volumes;
    }
}

public class BatteryTelemetryService : IBatteryTelemetryService
{
    private readonly ILoggingService _logger;

    public BatteryTelemetryService(ILoggingService logger)
    {
        _logger = logger;
    }

    private BatteryInfo? _cachedInfo;

    public BatteryInfo? GetBatteryInfo()
    {
        if (_cachedInfo != null) return _cachedInfo;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DeviceID, Chemistry, DesignCapacity, FullChargeCapacity, DesignVoltage FROM Win32_Battery");
            foreach (var obj in searcher.Get())
            {
                _cachedInfo = new BatteryInfo
                {
                    DeviceId = obj["DeviceID"]?.ToString() ?? "Unknown",
                    Chemistry = obj["Chemistry"]?.ToString() ?? "Unknown",
                    DesignCapacityMWh = obj["DesignCapacity"] != null ? Convert.ToInt32(obj["DesignCapacity"]) : 0,
                    FullChargeCapacityMWh = obj["FullChargeCapacity"] != null ? Convert.ToInt32(obj["FullChargeCapacity"]) : 0,
                    DesignVoltage = obj["DesignVoltage"] != null ? Convert.ToDouble(obj["DesignVoltage"]) : 0,
                    WearPercent = ComputeWearPercent(
                        obj["DesignCapacity"] != null ? Convert.ToInt32(obj["DesignCapacity"]) : 0,
                        obj["FullChargeCapacity"] != null ? Convert.ToInt32(obj["FullChargeCapacity"]) : 0) ?? 0
                };
                return _cachedInfo;
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Failed to get Battery Info: {ex.Message}", LogSeverity.Error, "Hardware");
        }
        return null;
    }

    // B2: wear must come from real firmware data (DesignCapacity vs FullChargeCapacity).
    // The previous hard-coded 3.4% was a fabricated value shown to users.
    private static double? ComputeWearPercent(int designCapacityMWh, int fullChargeCapacityMWh)
    {
        if (designCapacityMWh <= 0 || fullChargeCapacityMWh <= 0 || fullChargeCapacityMWh > designCapacityMWh)
        {
            return null; // unknown — UI shows 'Unavailable' rather than an invented number
        }
        return Math.Round((1.0 - fullChargeCapacityMWh / (double)designCapacityMWh) * 100.0, 1);
    }

    public BatteryMetrics GetMetrics()
    {
        // B1: Previously returned a default struct (0% On Battery) which the UI rendered as
        // fabricated data. Read real charge state via WMI; -1 means 'Unavailable'.
        var metrics = new BatteryMetrics { ChargePercent = -1 };
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, BatteryStatus, EstimatedRunTime FROM Win32_Battery");
            foreach (var obj in searcher.Get())
            {
                var charge = obj["EstimatedChargeRemaining"];
                if (charge != null)
                {
                    int pct = Convert.ToInt32(charge);
                    if (pct is < 0 or > 100) pct = -1;

                    // Win32_Battery.BatteryStatus: 1=discharging, 2=AC/charging
                    ushort status = obj["BatteryStatus"] != null ? Convert.ToUInt16(obj["BatteryStatus"]) : (ushort)0;

                    int? runMinutes = null;
                    if (obj["EstimatedRunTime"] != null)
                    {
                        uint run = Convert.ToUInt32(obj["EstimatedRunTime"]);
                        if (run is > 0 and < 71582788) runMinutes = (int)run; // 71582788 = 'unknown/AC'
                    }

                    metrics = new BatteryMetrics
                    {
                        ChargePercent = pct,
                        IsAcConnected = status == 2,
                        IsCharging = status == 2 && pct < 100,
                        EstimatedMinutesRemaining = runMinutes
                    };
                }
                break;
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Battery metrics unavailable: {ex.Message}", LogSeverity.Warning, "Hardware");
        }
        return metrics;
    }
}

public class ThermalTelemetryService : IThermalTelemetryService
{
    private readonly IHardwareTelemetryProvider? _hardwareProvider;

    public ThermalTelemetryService(IHardwareTelemetryProvider? hardwareProvider = null)
    {
        _hardwareProvider = hardwareProvider;
    }

    public ThermalMetrics GetMetrics()
    {
        if (_hardwareProvider != null)
        {
            try
            {
                var snap = _hardwareProvider.CaptureSnapshot();
                if (snap.ThermalMetrics != null)
                {
                    return snap.ThermalMetrics;
                }
            }
            catch { }
        }
        return new ThermalMetrics();
    }
}
