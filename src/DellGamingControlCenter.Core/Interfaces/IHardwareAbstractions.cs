namespace DellGamingControlCenter.Core.Interfaces;

using System.Collections.Generic;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

public interface IHardwareProfileProvider
{
    HardwareProfile GetProfile();
    CapabilityStatus GetCapabilityStatus(HardwareCapability capability);
}

public interface ICpuTelemetryService
{
    CpuInfo GetCpuInfo();
    CpuMetrics GetMetrics();
}

public interface IMemoryTelemetryService
{
    RamInfo GetRamInfo();
    RamMetrics GetMetrics();
}

public interface IGpuTelemetryService
{
    List<GpuInfo> GetGpuInfo();
    GpuMetrics GetMetrics();
}

public interface IStorageTelemetryService
{
    List<StorageDrive> GetDrives();
    List<StorageVolume> GetVolumes();
}

public interface IBatteryTelemetryService
{
    BatteryInfo? GetBatteryInfo();
    BatteryMetrics GetMetrics();
}

public interface IThermalTelemetryService
{
    ThermalMetrics GetMetrics();
}

public interface IGpuPreferenceController
{
    CapabilityStatus Status { get; }
    GpuPreference GetPreference(string executablePath);
    bool SetPreference(string executablePath, GpuPreference preference);
    bool RemovePreference(string executablePath);
}

public interface IProcessController
{
    List<ProcessInfo> GetProcesses();
    bool SetPriority(int processId, ProcessPriorityClass priority);
    ProcessPriorityClass GetPriority(int processId);
}

public interface IMemoryOptimizationController
{
    bool OptimizeMemory();
}

public interface IPowerPlanController
{
    string GetActivePowerSchemeGuid();
    System.Threading.Tasks.Task<bool> SetPowerSchemeAsync(string guid);
}

