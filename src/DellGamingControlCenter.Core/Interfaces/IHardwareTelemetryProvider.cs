namespace DellGamingControlCenter.Core.Interfaces;

using System;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Models;

/// <summary>
/// Authoritative contract for pluggable hardware telemetry providers
/// (e.g., Windows API, LibreHardwareMonitor, HWiNFO Shared Memory).
/// </summary>
public interface IHardwareTelemetryProvider : IDisposable
{
    /// <summary>Display name of the provider.</summary>
    string ProviderName { get; }

    /// <summary>Whether the provider is currently available on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>Initializes provider resources asynchronously.</summary>
    Task InitializeAsync();

    /// <summary>Captures a point-in-time telemetry snapshot without blocking the caller.</summary>
    HardwareTelemetrySnapshot CaptureSnapshot();
}
