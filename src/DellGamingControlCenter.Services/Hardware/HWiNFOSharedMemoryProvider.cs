namespace DellGamingControlCenter.Services.Hardware;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class HWiNFOSharedMemoryProvider : IHardwareTelemetryProvider
{
    private readonly ILoggingService _logger;
    public const string DefaultSharedMemoryPath = @"Global\HWiNFO_SENS_SM2";
    public const string DefaultSharedMemoryMutex = @"Global\HWiNFO_SM2_MUTEX";
    private const uint HeaderMagic = 0x53695748; // 'SiWH' / 'HWiS'

    private readonly string _sharedMemoryPath;
    private readonly string _sharedMemoryMutex;

    public string ProviderName => "HWiNFO64 Shared Memory";
    public bool IsAvailable { get; private set; }

    public HWiNFOSharedMemoryProvider(ILoggingService logger, string? sharedMemoryPath = null, string? sharedMemoryMutex = null)
    {
        _logger = logger;
        _sharedMemoryPath = sharedMemoryPath ?? DefaultSharedMemoryPath;
        _sharedMemoryMutex = sharedMemoryMutex ?? DefaultSharedMemoryMutex;
    }

    private MemoryMappedFile OpenSharedMemory()
    {
        try
        {
            return MemoryMappedFile.OpenExisting(_sharedMemoryPath, MemoryMappedFileRights.Read);
        }
        catch (FileNotFoundException) when (_sharedMemoryPath == DefaultSharedMemoryPath)
        {
            return MemoryMappedFile.OpenExisting(@"Local\HWiNFO_SENS_SM2", MemoryMappedFileRights.Read);
        }
    }

    private Mutex? OpenMutex()
    {
        if (Mutex.TryOpenExisting(_sharedMemoryMutex, out var m))
            return m;
        if (_sharedMemoryMutex == DefaultSharedMemoryMutex && Mutex.TryOpenExisting(@"Local\HWiNFO_SM2_MUTEX", out var localM))
            return localM;
        return null;
    }

    public Task InitializeAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                using var mmf = OpenSharedMemory();
                using var accessor = mmf.CreateViewAccessor(0, 4, MemoryMappedFileAccess.Read);
                uint magic = accessor.ReadUInt32(0);
                IsAvailable = (magic == HeaderMagic);
                if (IsAvailable)
                {
                    _logger.Log("HWiNFO Shared Memory detected and validated.", LogSeverity.Info, "Hardware");
                }
            }
            catch (FileNotFoundException)
            {
                IsAvailable = false;
                // Normal when HWiNFO is not running or Shared Memory is disabled
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                _logger.Log($"HWiNFO Shared Memory probe notice: {ex.Message}", LogSeverity.Info, "Hardware");
            }
        });
    }

    private sealed class SensorGroup
    {
        public uint Id { get; set; }
        public uint Instance { get; set; }
        public string Name { get; set; } = "";
    }

    public HardwareTelemetrySnapshot CaptureSnapshot()
    {
        var now = DateTime.UtcNow;
        var readings = new List<HardwareSensorReading>();
        bool isStale = false;
        string? staleReason = null;

        Mutex? mutex = null;
        try
        {
            mutex = OpenMutex();
            bool hasLock = false;
            if (mutex != null)
            {
                try { hasLock = mutex.WaitOne(25); } catch { }
            }

            try
            {
                using var mmf = OpenSharedMemory();
                using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

                uint magic = accessor.ReadUInt32(0);
                if (magic != HeaderMagic)
                {
                    IsAvailable = false;
                    return CreateEmptySnapshot(now, readings);
                }

                IsAvailable = true;
                long pollTime = accessor.ReadInt64(0x0C);
                DateTime pollUtc = (pollTime > 0)
                    ? DateTimeOffset.FromUnixTimeSeconds(pollTime).UtcDateTime
                    : now;
                isStale = (pollTime > 0) && (now - pollUtc).TotalSeconds > 10.0;
                staleReason = isStale
                    ? $"HWiNFO shared memory is stale (last update: {pollUtc:HH:mm:ss} UTC, {(int)(now - pollUtc).TotalSeconds}s ago)"
                    : null;

                uint sensorOffset = accessor.ReadUInt32(0x14);
                uint sensorSize = accessor.ReadUInt32(0x18);
                uint sensorCount = accessor.ReadUInt32(0x1C);
                uint entryOffset = accessor.ReadUInt32(0x20);
                uint entrySize = accessor.ReadUInt32(0x24);
                uint entryCount = accessor.ReadUInt32(0x28);

                // Sanity checks on offsets and sizes to prevent memory overflow
                if (sensorCount > 512 || entryCount > 4096 || sensorSize < 264 || entrySize < 316)
                {
                    return CreateEmptySnapshot(now, readings);
                }

                var sensorGroups = new List<SensorGroup>((int)sensorCount);
                for (uint i = 0; i < sensorCount; i++)
                {
                    long offset = sensorOffset + (i * sensorSize);
                    uint sId = accessor.ReadUInt32(offset);
                    uint sInstance = accessor.ReadUInt32(offset + 4);
                    string nameOrig = ReadNullTerminatedString(accessor, offset + 8, 128);
                    string nameUser = ReadNullTerminatedString(accessor, offset + 136, 128);
                    sensorGroups.Add(new SensorGroup
                    {
                        Id = sId,
                        Instance = sInstance,
                        Name = !string.IsNullOrWhiteSpace(nameUser) ? nameUser : nameOrig
                    });
                }

                // Read all telemetry entries
                for (uint j = 0; j < entryCount; j++)
                {
                    long offset = entryOffset + (j * entrySize);
                    uint sType = accessor.ReadUInt32(offset);
                    uint sIndex = accessor.ReadUInt32(offset + 4);
                    uint eId = accessor.ReadUInt32(offset + 8);
                    string nameOrig = ReadNullTerminatedString(accessor, offset + 12, 128);
                    string nameUser = ReadNullTerminatedString(accessor, offset + 140, 128);
                    string unit = ReadNullTerminatedString(accessor, offset + 268, 16);
                    double val = accessor.ReadDouble(offset + 284);
                    double valMin = accessor.ReadDouble(offset + 292);
                    double valMax = accessor.ReadDouble(offset + 300);

                    string parentSensor = (sIndex < sensorGroups.Count) ? sensorGroups[(int)sIndex].Name : "HWiNFO";
                    string displayName = !string.IsNullOrWhiteSpace(nameUser) ? nameUser : nameOrig;
                    var (compType, teleType) = ClassifySensor(parentSensor, displayName, sType);

                    readings.Add(new HardwareSensorReading
                    {
                        SensorId = $"hwinfo_{sIndex}_{eId}",
                        HardwareName = parentSensor,
                        ComponentType = compType,
                        SensorType = teleType,
                        SensorName = displayName,
                        Value = Math.Round(val, 3),
                        MinValue = Math.Round(valMin, 3),
                        MaxValue = Math.Round(valMax, 3),
                        Unit = unit.Trim(),
                        State = isStale ? SensorReadingState.Stale : SensorReadingState.Available,
                        DiagnosticReason = staleReason,
                        SourceProvider = ProviderName,
                        Timestamp = pollUtc
                    });
                }
            }
            finally
            {
                if (hasLock && mutex != null)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                }
            }
        }
        catch (FileNotFoundException)
        {
            IsAvailable = false;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            _logger.Log($"HWiNFO capture failed: {ex.Message}", LogSeverity.Warning, "Hardware");
        }
        finally
        {
            mutex?.Dispose();
        }

        if (readings.Count == 0)
        {
            return CreateEmptySnapshot(now, readings);
        }

        // Aggregate models from HWiNFO readings
        double? cpuTotal = null;
        var perCoreLoads = new List<double>();
        var perCoreClocks = new List<double>();
        var perCoreTemps = new List<double>();
        double? cpuPackageTemp = null;
        double? cpuPackagePower = null;
        double? cpuVid = null;
        bool? isCpuThrottling = null;

        double? amdGpuTemp = null, amdGpuClock = null, amdGpuMemClock = null, amdGpuUsage = null;
        int? amdGpuFan = null;
        int? systemFanRpm = null;

        long ramUsedBytes = 0, ramAvailBytes = 0, ramTotalBytes = 0;
        double? ramLoad = null;

        foreach (var r in readings)
        {
            string hn = r.HardwareName.ToLowerInvariant();
            string sn = r.SensorName.ToLowerInvariant();

            if (r.ComponentType == HardwareComponentType.Cpu)
            {
                if (r.SensorType == TelemetrySensorType.Temperature)
                {
                    if (sn.Contains("package") && r.Value.HasValue) cpuPackageTemp = r.Value.Value;
                    else if (sn.Contains("core") && !sn.Contains("distance") && r.Value.HasValue) perCoreTemps.Add(r.Value.Value);
                }
                else if (r.SensorType == TelemetrySensorType.Clock)
                {
                    if (sn.Contains("core") && r.Value.HasValue) perCoreClocks.Add(r.Value.Value);
                }
                else if (r.SensorType == TelemetrySensorType.Load)
                {
                    if (sn.Contains("total") && r.Value.HasValue) cpuTotal = r.Value.Value;
                    else if (sn.Contains("core") && r.Value.HasValue) perCoreLoads.Add(r.Value.Value);
                }
                else if (r.SensorType == TelemetrySensorType.Power && sn.Contains("package") && r.Value.HasValue)
                {
                    cpuPackagePower = r.Value.Value;
                }
                else if (r.SensorType == TelemetrySensorType.Voltage && (sn.Contains("vid") || sn.Contains("core") || sn.Contains("vcore")) && r.Value.HasValue)
                {
                    cpuVid = r.Value.Value;
                }
                else if (sn.Contains("throttl") || sn.Contains("prochot"))
                {
                    if (r.Value.HasValue) isCpuThrottling = r.Value.Value > 0;
                }
            }
            else if (r.ComponentType == HardwareComponentType.Gpu && (hn.Contains("amd") || hn.Contains("radeon")))
            {
                if (r.SensorType == TelemetrySensorType.Temperature && r.Value.HasValue) amdGpuTemp = r.Value.Value;
                else if (r.SensorType == TelemetrySensorType.Clock)
                {
                    if (sn.Contains("memory") && r.Value.HasValue) amdGpuMemClock = r.Value.Value;
                    else if (r.Value.HasValue) amdGpuClock = r.Value.Value;
                }
                else if (r.SensorType == TelemetrySensorType.Load && r.Value.HasValue) amdGpuUsage = r.Value.Value;
                else if (r.SensorType == TelemetrySensorType.Fan && r.Value.HasValue && r.Value.Value >= 0) amdGpuFan = (int)r.Value.Value;
            }
            else if (r.ComponentType == HardwareComponentType.Motherboard && (hn.Contains("dell") || hn.Contains("ec")))
            {
                if (r.SensorType == TelemetrySensorType.Temperature)
                {
                    if (sn.Contains("cpu") && r.Value.HasValue && !cpuPackageTemp.HasValue) cpuPackageTemp = r.Value.Value;
                    else if (sn.Contains("gpu") && r.Value.HasValue && !amdGpuTemp.HasValue) amdGpuTemp = r.Value.Value;
                }
                else if (r.SensorType == TelemetrySensorType.Fan && r.Value.HasValue && r.Value.Value >= 0)
                {
                    systemFanRpm = (int)r.Value.Value;
                }
            }
            else if (r.SensorType == TelemetrySensorType.Fan && r.Value.HasValue && r.Value.Value >= 0)
            {
                systemFanRpm = (int)r.Value.Value;
            }
            else if (r.ComponentType == HardwareComponentType.Memory)
            {
                if (sn.Contains("physical memory used") && r.Value.HasValue)
                    ramUsedBytes = (long)(r.Value.Value * 1024 * 1024);
                else if (sn.Contains("physical memory available") && r.Value.HasValue)
                    ramAvailBytes = (long)(r.Value.Value * 1024 * 1024);
                else if (sn.Contains("physical memory load") && r.Value.HasValue)
                    ramLoad = r.Value.Value;
            }
        }

        if (!cpuPackageTemp.HasValue && perCoreTemps.Count > 0)
        {
            cpuPackageTemp = Math.Round(perCoreTemps.Average(), 1);
        }

        ramTotalBytes = ramUsedBytes + ramAvailBytes;

        return new HardwareTelemetrySnapshot
        {
            Timestamp = now,
            ActiveProvider = ProviderName,
            DiscoveredSensors = readings,
            CpuMetrics = new CpuMetrics
            {
                UsagePercent = cpuTotal ?? -1,
                PerCoreUsage = perCoreLoads.ToArray(),
                CurrentClockMHz = perCoreClocks.Count > 0 ? perCoreClocks.Max() : 0,
                PerCoreClocksMHz = perCoreClocks.ToArray(),
                TemperatureCelsius = cpuPackageTemp,
                PerCoreTemperaturesCelsius = perCoreTemps.ToArray(),
                PackagePowerWatts = cpuPackagePower,
                CoreVoltageVolts = cpuVid,
                IsThermalThrottling = isCpuThrottling,
                TemperatureState = cpuPackageTemp.HasValue ? (isStale ? SensorReadingState.Stale : SensorReadingState.Available) : SensorReadingState.Unavailable,
                TemperatureDiagnosticReason = cpuPackageTemp.HasValue ? (isStale ? staleReason : null) : "No CPU temperature reading in HWiNFO",
                ClockState = perCoreClocks.Count > 0 ? (isStale ? SensorReadingState.Stale : SensorReadingState.Available) : SensorReadingState.Unavailable,
                ClockDiagnosticReason = perCoreClocks.Count > 0 ? (isStale ? staleReason : null) : "No live CPU clock readings found in HWiNFO",
                PowerState = cpuPackagePower.HasValue ? (isStale ? SensorReadingState.Stale : SensorReadingState.Available) : SensorReadingState.Unavailable,
                PowerDiagnosticReason = cpuPackagePower.HasValue ? (isStale ? staleReason : null) : "No CPU package power reading in HWiNFO",
                VoltageState = cpuVid.HasValue ? (isStale ? SensorReadingState.Stale : SensorReadingState.Available) : SensorReadingState.Unavailable,
                VoltageDiagnosticReason = cpuVid.HasValue ? (isStale ? staleReason : null) : "No CPU VID reading in HWiNFO",
                Timestamp = now
            },
            RamMetrics = new RamMetrics
            {
                TotalBytes = ramTotalBytes,
                AvailableBytes = ramAvailBytes,
                UsedBytes = ramUsedBytes,
                UsagePercent = ramLoad ?? (ramTotalBytes > 0 ? (double)ramUsedBytes / ramTotalBytes * 100 : 0),
                Timestamp = now
            },
            DiscreteGpuMetrics = new GpuMetrics
            {
                UsagePercent = amdGpuUsage,
                TemperatureCelsius = amdGpuTemp,
                CoreClockMHz = amdGpuClock,
                MemoryClockMHz = amdGpuMemClock,
                FanRpm = amdGpuFan,
                VramTotalBytes = 2048L * 1024 * 1024,
                TemperatureState = amdGpuTemp.HasValue ? (isStale ? SensorReadingState.Stale : SensorReadingState.Available) : SensorReadingState.Unavailable,
                TemperatureDiagnosticReason = amdGpuTemp.HasValue ? (isStale ? staleReason : null) : "AMD discrete GPU thermal sensor not present in HWiNFO",
                MemoryState = SensorReadingState.Unavailable,
                MemoryDiagnosticReason = "VRAM usage monitored via DXGI / LibreHardwareMonitor",
                Timestamp = now
            },
            ThermalMetrics = new ThermalMetrics
            {
                CpuTemperature = cpuPackageTemp,
                GpuTemperature = amdGpuTemp,
                FanRpm = systemFanRpm ?? amdGpuFan,
                IsCpuThrottling = isCpuThrottling,
                CpuTemperatureState = cpuPackageTemp.HasValue ? (isStale ? SensorReadingState.Stale : SensorReadingState.Available) : SensorReadingState.Unavailable,
                GpuTemperatureState = amdGpuTemp.HasValue ? (isStale ? SensorReadingState.Stale : SensorReadingState.Available) : SensorReadingState.Unavailable,
                FanRpmState = (systemFanRpm ?? amdGpuFan).HasValue ? (isStale ? SensorReadingState.Stale : SensorReadingState.Available) : SensorReadingState.Unsupported,
                DiagnosticReason = isStale ? staleReason : null,
                Timestamp = now
            }
        };
    }

    private HardwareTelemetrySnapshot CreateEmptySnapshot(DateTime now, List<HardwareSensorReading> readings)
    {
        return new HardwareTelemetrySnapshot
        {
            Timestamp = now,
            ActiveProvider = ProviderName,
            DiscoveredSensors = readings,
            CpuMetrics = new CpuMetrics { UsagePercent = -1, TemperatureState = SensorReadingState.Unavailable, TemperatureDiagnosticReason = "HWiNFO64 not running or Shared Memory not enabled" },
            ThermalMetrics = new ThermalMetrics { CpuTemperatureState = SensorReadingState.Unavailable, GpuTemperatureState = SensorReadingState.Unavailable, FanRpmState = SensorReadingState.Unavailable, DiagnosticReason = "HWiNFO64 not active" }
        };
    }

    private static (HardwareComponentType, TelemetrySensorType) ClassifySensor(string parent, string name, uint sType)
    {
        string pLower = parent.ToLowerInvariant();
        string nLower = name.ToLowerInvariant();

        var compType = HardwareComponentType.Other;
        if (pLower.Contains("cpu") || pLower.Contains("core") || pLower.Contains("intel core"))
            compType = HardwareComponentType.Cpu;
        else if (pLower.Contains("gpu") || pLower.Contains("radeon") || pLower.Contains("hd graphics"))
            compType = HardwareComponentType.Gpu;
        else if (pLower.Contains("memory") || nLower.Contains("physical memory"))
            compType = HardwareComponentType.Memory;
        else if (pLower.Contains("drive") || pLower.Contains("ssd") || pLower.Contains("hdd"))
            compType = HardwareComponentType.Storage;
        else if (pLower.Contains("dell") || pLower.Contains("motherboard") || pLower.Contains("ec"))
            compType = HardwareComponentType.Motherboard;

        var teleType = sType switch
        {
            1 => TelemetrySensorType.Temperature,
            2 => TelemetrySensorType.Voltage,
            3 => TelemetrySensorType.Fan,
            4 => TelemetrySensorType.Data,
            5 => TelemetrySensorType.Power,
            6 => TelemetrySensorType.Clock,
            7 => TelemetrySensorType.Load,
            _ => (nLower.Contains("temp") ? TelemetrySensorType.Temperature :
                 nLower.Contains("clock") ? TelemetrySensorType.Clock :
                 nLower.Contains("usage") || nLower.Contains("load") ? TelemetrySensorType.Load :
                 nLower.Contains("fan") ? TelemetrySensorType.Fan : TelemetrySensorType.Other)
        };

        return (compType, teleType);
    }

    private static string ReadNullTerminatedString(MemoryMappedViewAccessor accessor, long offset, int maxLen)
    {
        byte[] buffer = new byte[maxLen];
        accessor.ReadArray(offset, buffer, 0, maxLen);
        int nullIdx = Array.IndexOf(buffer, (byte)0);
        int len = nullIdx >= 0 ? nullIdx : maxLen;
        return Encoding.UTF8.GetString(buffer, 0, len).Trim();
    }

    public void Dispose()
    {
    }
}
