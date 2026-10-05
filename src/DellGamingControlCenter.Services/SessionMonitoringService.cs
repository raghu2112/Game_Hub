namespace DellGamingControlCenter.Services;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class SessionMonitoringService : ISessionMonitoringService
{
    private readonly ILoggingService _logger;
    private readonly IConfigurationService _config;
    
    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;

    public SessionMonitoringService(ILoggingService logger, IConfigurationService config)
    {
        _logger = logger;
        _config = config;
    }

    public Task StartMonitoringAsync(GameSession session, CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitoringTask = Task.Run(() => MonitorLoop(session, _cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopMonitoringAsync(GameSession session)
    {
        if (_cts != null)
        {
            _cts.Cancel();
        }
        
        if (_monitoringTask != null)
        {
            try { await _monitoringTask; } catch { /* Ignore cancellation */ }
        }
    }

    private async Task MonitorLoop(GameSession session, CancellationToken token)
    {
        int interval = _config.GetConfig().MonitoringIntervalMs;
        _logger.Log("Session monitoring started.", LogSeverity.Info, "Monitor");

        PerformanceCounter? cpuCounter = null;
        try
        {
            cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue(); // First call always returns 0
        }
        catch
        {
            _logger.Log("Could not initialize CPU performance counter.", LogSeverity.Warning, "Monitor");
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                // Process check
                var process = Process.GetProcessById(session.ProcessId);
                if (process.HasExited)
                {
                    _logger.Log($"Process {session.ProcessId} exited. Stopping monitoring.", LogSeverity.Info, "Monitor");
                    break;
                }

                // Sample CPU
                if (cpuCounter != null)
                {
                    double cpu = cpuCounter.NextValue();
                    if (cpu > session.PeakCpuUsage) session.PeakCpuUsage = cpu;
                }

                // Sample RAM working set
                long ram = process.WorkingSet64;
                if (ram > session.PeakRamUsageBytes) session.PeakRamUsageBytes = ram;

                // GPU & Temp requires specialized libraries (LibreHardwareMonitor) so we skip standard polling for safety
                // We'll leave PeakGpuUsage and Temp as is for now unless integrated with the hardware detector.
                
            }
            catch (ArgumentException)
            {
                // Process died
                break;
            }
            catch (Exception ex)
            {
                _logger.Log($"Monitor loop error: {ex.Message}", LogSeverity.Warning, "Monitor");
            }

            await Task.Delay(interval, token);
        }
        
        if (cpuCounter != null) cpuCounter.Dispose();
        _logger.Log("Session monitoring ended.", LogSeverity.Info, "Monitor");
    }
}
