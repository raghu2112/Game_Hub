namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Models;

/// <summary>
/// Service responsible for gathering standard Windows/hardware telemetry and specifications.
/// </summary>
public interface IHardwareDetector
{
    /// <summary>Returns a complete snapshot of all detectable hardware.</summary>
    HardwareSnapshot GetSnapshot();

    /// <summary>Retrieves real-time CPU metrics (usage, temp, etc).</summary>
    CpuMetrics GetCpuMetrics();

    /// <summary>Retrieves real-time RAM metrics.</summary>
    RamMetrics GetRamMetrics();

    /// <summary>Retrieves real-time GPU metrics.</summary>
    GpuMetrics GetGpuMetrics();

    /// <summary>Retrieves real-time thermal readings.</summary>
    ThermalMetrics GetThermalMetrics();

    /// <summary>Retrieves real-time battery status.</summary>
    BatteryMetrics GetBatteryMetrics();
}
