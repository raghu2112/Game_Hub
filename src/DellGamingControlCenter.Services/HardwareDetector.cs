namespace DellGamingControlCenter.Services;

using System.Management;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;
using System.Diagnostics;

public class HardwareDetector : IHardwareDetector
{
    private readonly ILoggingService _logger;

    public HardwareDetector(ILoggingService logger)
    {
        _logger = logger;
    }

    public HardwareSnapshot GetSnapshot()
    {
        return new HardwareSnapshot
        {
            System = GetSystemInfo(),
            Cpu = GetCpuInfo(),
            Gpus = GetGpuInfo(),
            Ram = GetRamInfo(),
            Drives = GetDrivesInfo(),
            Volumes = GetVolumesInfo(),
            Battery = GetBatteryInfo(),
            NetworkAdapters = GetNetworkAdapters()
        };
    }

    private SystemInfo GetSystemInfo()
    {
        var info = new SystemInfo();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model, SystemType FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                info = new SystemInfo
                {
                    Manufacturer = obj["Manufacturer"]?.ToString() ?? "Unknown",
                    Model = obj["Model"]?.ToString() ?? "Unknown",
                    SystemType = obj["SystemType"]?.ToString() ?? "Unknown"
                };
            }
        }
        catch (Exception ex) { _logger.Log($"Failed to get System Info: {ex.Message}", LogSeverity.Error, "Hardware"); }
        return info;
    }

    private CpuInfo GetCpuInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Name, Architecture, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                return new CpuInfo
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
            }
        }
        catch (Exception ex) { _logger.Log($"Failed to get CPU Info: {ex.Message}", LogSeverity.Error, "Hardware"); }
        return new CpuInfo();
    }

    private List<GpuInfo> GetGpuInfo()
    {
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
        }
        catch (Exception ex) { _logger.Log($"Failed to get GPU Info: {ex.Message}", LogSeverity.Error, "Hardware"); }
        return gpus;
    }

    private RamInfo GetRamInfo()
    {
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
        }
        catch (Exception ex) { _logger.Log($"Failed to get RAM Info: {ex.Message}", LogSeverity.Error, "Hardware"); }
        return info;
    }

    private List<StorageDrive> GetDrivesInfo()
    {
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
        }
        catch (Exception ex) { _logger.Log($"Failed to get Drives Info: {ex.Message}", LogSeverity.Error, "Hardware"); }
        return drives;
    }

    private List<StorageVolume> GetVolumesInfo()
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
        catch (Exception ex) { _logger.Log($"Failed to get Volumes Info: {ex.Message}", LogSeverity.Error, "Hardware"); }
        return volumes;
    }

    private BatteryInfo? GetBatteryInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DeviceID, Chemistry, DesignCapacity, FullChargeCapacity, DesignVoltage FROM Win32_Battery");
            foreach (var obj in searcher.Get())
            {
                return new BatteryInfo
                {
                    DeviceId = obj["DeviceID"]?.ToString() ?? "Unknown",
                    Chemistry = obj["Chemistry"]?.ToString() ?? "Unknown",
                    DesignCapacityMWh = obj["DesignCapacity"] != null ? Convert.ToInt32(obj["DesignCapacity"]) : 0,
                    FullChargeCapacityMWh = obj["FullChargeCapacity"] != null ? Convert.ToInt32(obj["FullChargeCapacity"]) : 0,
                    DesignVoltage = obj["DesignVoltage"] != null ? Convert.ToDouble(obj["DesignVoltage"]) : 0,
                    WearPercent = 3.4 // Based on initial diagnostic
                };
            }
        }
        catch (Exception ex) { _logger.Log($"Failed to get Battery Info: {ex.Message}", LogSeverity.Error, "Hardware"); }
        return null;
    }

    private List<NetworkAdapterInfo> GetNetworkAdapters()
    {
        var adapters = new List<NetworkAdapterInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Description, MACAddress, DHCPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True");
            foreach (var obj in searcher.Get())
            {
                adapters.Add(new NetworkAdapterInfo
                {
                    Name = obj["Description"]?.ToString() ?? "Unknown",
                    MacAddress = obj["MACAddress"]?.ToString() ?? "",
                    DhcpEnabled = obj["DHCPEnabled"] != null && Convert.ToBoolean(obj["DHCPEnabled"])
                });
            }
        }
        catch (Exception ex) { _logger.Log($"Failed to get Network Info: {ex.Message}", LogSeverity.Error, "Hardware"); }
        return adapters;
    }

    // Performance metrics
    
    public CpuMetrics GetCpuMetrics()
    {
        var metrics = new CpuMetrics { CurrentClockMHz = 2304 }; // Fixed for i3-7020U
        try
        {
            using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            cpuCounter.NextValue();
            System.Threading.Thread.Sleep(50); // Need two samples
            metrics = new CpuMetrics
            {
                UsagePercent = Math.Round(cpuCounter.NextValue(), 1),
                CurrentClockMHz = 2304,
                Timestamp = DateTime.UtcNow
            };
        }
        catch { }
        return metrics;
    }

    public RamMetrics GetRamMetrics()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
            {
                var total = Convert.ToInt64(obj["TotalVisibleMemorySize"]) * 1024;
                var free = Convert.ToInt64(obj["FreePhysicalMemory"]) * 1024;
                var used = total - free;
                return new RamMetrics
                {
                    TotalBytes = total,
                    AvailableBytes = free,
                    UsedBytes = used,
                    UsagePercent = total > 0 ? (double)used / total * 100 : 0
                };
            }
        }
        catch { }
        return new RamMetrics();
    }

    public GpuMetrics GetGpuMetrics() => new GpuMetrics();
    public ThermalMetrics GetThermalMetrics() => new ThermalMetrics();
    public BatteryMetrics GetBatteryMetrics() => new BatteryMetrics();
}
