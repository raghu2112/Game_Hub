namespace DellGamingControlCenter.Core.Models;

using DellGamingControlCenter.Core.Enums;

// ─── CPU ────────────────────────────────────────────────────────────────

public sealed class CpuInfo
{
    public string Manufacturer { get; init; } = "Unknown";
    public string Model { get; init; } = "Unknown";
    public string Architecture { get; init; } = "Unknown";
    public int Cores { get; init; }
    public int Threads { get; init; }
    public double BaseClockMHz { get; init; }
    public double MaxClockMHz { get; init; }
    public int L2CacheKB { get; init; }
    public int L3CacheKB { get; init; }
    public string Socket { get; init; } = "Unknown";
    public bool HasTurboBoost { get; init; }
    public bool PowerManagementSupported { get; init; }
}

public sealed class CpuMetrics
{
    public double UsagePercent { get; set; }
    public double CurrentClockMHz { get; init; }
    public double? TemperatureCelsius { get; init; }
    public double? PackagePowerWatts { get; init; }
    public double? CoreVoltageVolts { get; init; }
    public bool? IsThermalThrottling { get; init; }
    public double[] PerCoreUsage { get; init; } = [];
    public double[] PerCoreClocksMHz { get; init; } = [];
    public double[] PerCoreTemperaturesCelsius { get; init; } = [];
    public SensorReadingState TemperatureState { get; init; } = SensorReadingState.Unavailable;
    public string? TemperatureDiagnosticReason { get; init; }
    public SensorReadingState ClockState { get; init; } = SensorReadingState.Unavailable;
    public string? ClockDiagnosticReason { get; init; }
    public SensorReadingState PowerState { get; init; } = SensorReadingState.Unavailable;
    public string? PowerDiagnosticReason { get; init; }
    public SensorReadingState VoltageState { get; init; } = SensorReadingState.Unavailable;
    public string? VoltageDiagnosticReason { get; init; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// ─── GPU ────────────────────────────────────────────────────────────────

public sealed class GpuInfo
{
    public string Name { get; init; } = "Unknown";
    public string Manufacturer { get; init; } = "Unknown";
    public GpuType Type { get; init; } = GpuType.Unknown;
    public long VramBytes { get; init; }
    public string DriverVersion { get; init; } = "Unknown";
    public DateTime? DriverDate { get; init; }
    public string VideoProcessor { get; init; } = "Unknown";
    public string PnpDeviceId { get; init; } = "";
    public int? CurrentResolutionWidth { get; init; }
    public int? CurrentResolutionHeight { get; init; }
    public int? RefreshRate { get; init; }
    public string Status { get; init; } = "Unknown";
}

public sealed class GpuMetrics
{
    public double? UsagePercent { get; init; }
    public double? TemperatureCelsius { get; init; }
    public double? CoreClockMHz { get; init; }
    public double? MemoryClockMHz { get; init; }
    public double? PowerWatts { get; init; }
    public long? VramUsedBytes { get; init; }
    public long? VramAvailableBytes { get; init; }
    public long? VramTotalBytes { get; init; }
    public long? SharedMemoryUsedBytes { get; init; }
    public long? SharedMemoryAvailableBytes { get; init; }
    public long? SharedMemoryTotalBytes { get; init; }
    public int? FanRpm { get; init; }
    public SensorReadingState TemperatureState { get; init; } = SensorReadingState.Unavailable;
    public string? TemperatureDiagnosticReason { get; init; }
    public SensorReadingState MemoryState { get; init; } = SensorReadingState.Unavailable;
    public string? MemoryDiagnosticReason { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

// ─── RAM ────────────────────────────────────────────────────────────────

public sealed class RamModule
{
    public string BankLabel { get; init; } = "";
    public string DeviceLocator { get; init; } = "";
    public long CapacityBytes { get; init; }
    public int SpeedMHz { get; init; }
    public string Manufacturer { get; init; } = "Unknown";
    public string PartNumber { get; init; } = "";
}

public sealed class RamInfo
{
    public long TotalBytes { get; set; }
    public int ModuleCount { get; set; }
    public bool IsDualChannel { get; set; }
    public int SpeedMHz { get; set; }
    public List<RamModule> Modules { get; set; } = [];
}

public sealed class RamMetrics
{
    public long TotalBytes { get; init; }
    public long UsedBytes { get; init; }
    public long AvailableBytes { get; init; }
    public long CommitTotalBytes { get; init; }
    public long CommitLimitBytes { get; init; }
    public double UsagePercent { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

// ─── Storage ────────────────────────────────────────────────────────────

public sealed class StorageDrive
{
    public string Model { get; init; } = "Unknown";
    public StorageType Type { get; init; } = StorageType.Unknown;
    public string InterfaceType { get; init; } = "Unknown";
    public long SizeBytes { get; init; }
    public string Status { get; init; } = "Unknown";
    public string SerialNumber { get; init; } = "";  // Never display or upload
    public int Partitions { get; init; }
}

public sealed class StorageVolume
{
    public string DriveLetter { get; init; } = "";
    public string FileSystem { get; init; } = "";
    public long SizeBytes { get; init; }
    public long FreeSpaceBytes { get; init; }
    public string VolumeName { get; init; } = "";
    public bool IsSystemDrive { get; init; }
    public double UsagePercent => SizeBytes > 0 ? (1.0 - (double)FreeSpaceBytes / SizeBytes) * 100.0 : 0;
}

// ─── Battery ────────────────────────────────────────────────────────────

public sealed class BatteryInfo
{
    public string Manufacturer { get; init; } = "Unknown";
    public string DeviceId { get; init; } = "";
    public string Chemistry { get; init; } = "Unknown";
    public int DesignCapacityMWh { get; init; }
    public int FullChargeCapacityMWh { get; init; }
    public double WearPercent { get; init; }
    public int CycleCount { get; init; }
    public double DesignVoltage { get; init; }
}

public sealed class BatteryMetrics
{
    public int ChargePercent { get; init; }
    public bool IsAcConnected { get; init; }
    public bool IsCharging { get; init; }
    public int? EstimatedMinutesRemaining { get; init; }
    public double? TemperatureCelsius { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

// ─── Motherboard / System ───────────────────────────────────────────────

public sealed class SystemInfo
{
    public string Manufacturer { get; init; } = "Unknown";
    public string Model { get; init; } = "Unknown";
    public string SystemType { get; init; } = "Unknown";
    public string BiosVersion { get; init; } = "Unknown";
    public DateTime? BiosDate { get; init; }
    public string MotherboardProduct { get; init; } = "Unknown";
    public string MotherboardVersion { get; init; } = "Unknown";
    public string OsCaption { get; init; } = "Unknown";
    public string OsVersion { get; init; } = "Unknown";
    public string OsBuild { get; init; } = "Unknown";
    public string OsArchitecture { get; init; } = "Unknown";
}

// ─── Thermal ────────────────────────────────────────────────────────────

public sealed class ThermalMetrics
{
    public double? CpuTemperature { get; init; }
    public double? GpuTemperature { get; init; }
    public double? SsdTemperature { get; init; }
    public double? AmbientTemperature { get; init; }
    public int? FanRpm { get; init; }
    public ThermalState CpuThermalState { get; init; } = ThermalState.Unknown;
    public ThermalState GpuThermalState { get; init; } = ThermalState.Unknown;
    public bool? IsCpuThrottling { get; init; }
    public bool? IsGpuThrottling { get; init; }
    public SensorReadingState CpuTemperatureState { get; init; } = SensorReadingState.Unavailable;
    public SensorReadingState GpuTemperatureState { get; init; } = SensorReadingState.Unavailable;
    public SensorReadingState FanRpmState { get; init; } = SensorReadingState.Unavailable;
    public string? DiagnosticReason { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

// ─── Network ────────────────────────────────────────────────────────────

public sealed class NetworkAdapterInfo
{
    public string Name { get; init; } = "Unknown";
    public string MacAddress { get; init; } = "";
    public bool DhcpEnabled { get; init; }
    public string[] DnsServers { get; init; } = [];
    public string? DefaultGateway { get; init; }
}

// ─── Complete Hardware Snapshot ──────────────────────────────────────────

public sealed class HardwareSnapshot
{
    public SystemInfo System { get; init; } = new();
    public CpuInfo Cpu { get; init; } = new();
    public List<GpuInfo> Gpus { get; init; } = [];
    public RamInfo Ram { get; init; } = new();
    public List<StorageDrive> Drives { get; init; } = [];
    public List<StorageVolume> Volumes { get; init; } = [];
    public BatteryInfo? Battery { get; init; }
    public List<NetworkAdapterInfo> NetworkAdapters { get; init; } = [];
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
}

// ─── Universal Hardware Sensor Telemetry ────────────────────────────────

public sealed class HardwareSensorReading
{
    public string SensorId { get; init; } = "";
    public string HardwareName { get; init; } = "";
    public HardwareComponentType ComponentType { get; init; } = HardwareComponentType.Other;
    public TelemetrySensorType SensorType { get; init; } = TelemetrySensorType.Other;
    public string SensorName { get; init; } = "";
    public double? Value { get; init; }
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
    public string Unit { get; init; } = "";
    public SensorReadingState State { get; init; } = SensorReadingState.Unavailable;
    public string? DiagnosticReason { get; init; }
    public string SourceProvider { get; init; } = "";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed class HardwareTelemetrySnapshot
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ActiveProvider { get; init; } = "";
    public List<HardwareSensorReading> DiscoveredSensors { get; init; } = [];
    public CpuMetrics CpuMetrics { get; init; } = new();
    public RamMetrics RamMetrics { get; init; } = new();
    public GpuMetrics DiscreteGpuMetrics { get; init; } = new();
    public GpuMetrics IntegratedGpuMetrics { get; init; } = new();
    public ThermalMetrics ThermalMetrics { get; init; } = new();
}
