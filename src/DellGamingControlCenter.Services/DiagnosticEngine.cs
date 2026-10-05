namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using System.Management;
using System.Linq;

public class DiagnosticEngine : IDiagnosticEngine
{
    private readonly ILoggingService _logger;
    private readonly IBottleneckAnalyzer _bottleneckAnalyzer;
    private PerformanceCounter? _cpuCounter;

    public DiagnosticEngine(ILoggingService logger, IBottleneckAnalyzer bottleneckAnalyzer)
    {
        _logger = logger;
        _bottleneckAnalyzer = bottleneckAnalyzer;
        
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
        }
        catch { }
    }

    public async Task<DiagnosticSnapshot> CaptureSnapshotAsync()
    {
        return await Task.Run(() =>
        {
            var snapshot = new DiagnosticSnapshot
            {
                Timestamp = DateTime.UtcNow
            };

            // CPU
            if (_cpuCounter != null)
            {
                try
                {
                    snapshot.CpuUtilization = _cpuCounter.NextValue();
                    snapshot.CpuAvailability = MetricAvailability.Measured;
                }
                catch
                {
                    snapshot.CpuAvailability = MetricAvailability.Unavailable;
                }
            }

            // RAM
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (ManagementObject queryObj in searcher.Get())
                {
                    long totalKb = Convert.ToInt64(queryObj["TotalVisibleMemorySize"]);
                    long freeKb = Convert.ToInt64(queryObj["FreePhysicalMemory"]);
                    snapshot.TotalRamBytes = totalKb * 1024;
                    snapshot.AvailableRamBytes = freeKb * 1024;
                    snapshot.UsedRamBytes = snapshot.TotalRamBytes - snapshot.AvailableRamBytes;
                }
                snapshot.RamAvailability = MetricAvailability.Measured;
            }
            catch
            {
                snapshot.RamAvailability = MetricAvailability.Unavailable;
            }
            
            // Storage omitted to avoid heavy WMI calls during gameplay on 5400RPM HDD.
            snapshot.DiskActiveTimePercent = 0;
            snapshot.StorageAvailability = MetricAvailability.Unavailable;

            // GPU & Thermals (Would need LibreHardwareMonitor, set unavailable for raw C# without native DLL)
            snapshot.GpuAvailability = MetricAvailability.Unavailable;
            snapshot.ThermalAvailability = MetricAvailability.Unavailable;

            return snapshot;
        });
    }

    public Task<List<BottleneckResult>> RunDiagnosticAnalysisAsync(IEnumerable<DiagnosticSnapshot> history)
    {
        return Task.Run(() => _bottleneckAnalyzer.Analyze(history));
    }
}
