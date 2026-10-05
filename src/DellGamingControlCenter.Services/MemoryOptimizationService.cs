namespace DellGamingControlCenter.Services;

using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class MemoryOptimizationService : IMemoryOptimizationService
{
    private readonly ILoggingService _logger;

    public MemoryOptimizationService(ILoggingService logger)
    {
        _logger = logger;
    }

    public async Task<MemoryAnalysisResult> AnalyzeMemoryAsync()
    {
        return await Task.Run(() =>
        {
            long totalRam = 0;
            long freeRam = 0;
            
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (ManagementObject queryObj in searcher.Get())
                {
                    totalRam = Convert.ToInt64(queryObj["TotalVisibleMemorySize"]) * 1024; // KB to Bytes
                    freeRam = Convert.ToInt64(queryObj["FreePhysicalMemory"]) * 1024;
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"WMI memory query failed: {ex.Message}", LogSeverity.Warning, "Memory");
            }

            // Fallback total if WMI fails (12GB typical for this laptop)
            if (totalRam == 0) totalRam = 12L * 1024 * 1024 * 1024;

            long usedRam = totalRam - freeRam;
            double usedPercent = (double)usedRam / totalRam;
            
            MemoryPressureLevel pressure = MemoryPressureLevel.Normal;
            if (usedPercent > 0.90) pressure = MemoryPressureLevel.Critical;
            else if (usedPercent > 0.80) pressure = MemoryPressureLevel.High;
            else if (usedPercent > 0.65) pressure = MemoryPressureLevel.Elevated;

            var result = new MemoryAnalysisResult
            {
                TotalRamBytes = totalRam,
                AvailableRamBytes = freeRam,
                UsedRamBytes = usedRam,
                PressureLevel = pressure
            };

            var topProcs = Process.GetProcesses()
                .GroupBy(p => { try { return p.ProcessName; } catch { return "Unknown"; } })
                .Select(g => new { Name = g.Key, Memory = g.Sum(p => { try { return p.WorkingSet64; } catch { return 0L; } }) })
                .OrderByDescending(x => x.Memory)
                .Take(5)
                .ToDictionary(x => x.Name, x => x.Memory);

            foreach(var kvp in topProcs)
            {
                if (!result.TopProcessesBytes.ContainsKey(kvp.Key))
                {
                    result.TopProcessesBytes.Add(kvp.Key, kvp.Value);
                }
            }

            return result;
        });
    }

    public Task<bool> TryCleanupStandbyListAsync()
    {
        // Standby list cleanup requires P/Invoke to NtSetSystemInformation or external tool like RAMMap.
        // For safety and staying within standard APIs without external binaries, we will log that it requires elevation.
        _logger.Log("Standby list cleanup requested. This is an advanced feature requiring elevation and undocumented APIs.", LogSeverity.Warning, "Memory");
        return Task.FromResult(false); // Refuse to execute undocumented APIs
    }
}
