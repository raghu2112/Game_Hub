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
    
    private GameSession? _activeSession;
    private readonly IMetricsSampler _metricsSampler;

    public SessionMonitoringService(ILoggingService logger, IConfigurationService config, IMetricsSampler metricsSampler)
    {
        _logger = logger;
        _config = config;
        _metricsSampler = metricsSampler;
    }

    public Task StartMonitoringAsync(GameSession session, CancellationToken cancellationToken)
    {
        _activeSession = session;
        _metricsSampler.SnapshotCaptured -= OnSnapshotCaptured; // Defensive unsubscribe
        _metricsSampler.SnapshotCaptured += OnSnapshotCaptured;
        _logger.Log("Session monitoring started using MetricsSampler.", LogSeverity.Info, "Monitor");
        return Task.CompletedTask;
    }

    public Task StopMonitoringAsync(GameSession session)
    {
        _metricsSampler.SnapshotCaptured -= OnSnapshotCaptured;
        _activeSession = null;
        _logger.Log("Session monitoring ended.", LogSeverity.Info, "Monitor");
        return Task.CompletedTask;
    }

    private void OnSnapshotCaptured(object? sender, DiagnosticSnapshot snapshot)
    {
        var session = _activeSession;
        if (session == null) return;

        // CPU Analytics
        if (snapshot.CpuMetrics.UsagePercent >= 0)
        {
            if (snapshot.CpuMetrics.UsagePercent > session.PeakCpuUsage) 
                session.PeakCpuUsage = snapshot.CpuMetrics.UsagePercent;
            
            session.TotalCpuUsage += snapshot.CpuMetrics.UsagePercent;
            session.CpuSampleCount++;
            session.AverageCpuUsage = session.TotalCpuUsage / session.CpuSampleCount;
        }

        // RAM Analytics
        if (snapshot.RamMetrics.UsedBytes > 0)
        {
            if (snapshot.RamMetrics.UsedBytes > session.PeakRamUsageBytes) 
                session.PeakRamUsageBytes = snapshot.RamMetrics.UsedBytes;
            
            session.TotalRamUsageBytes += snapshot.RamMetrics.UsedBytes;
            session.RamSampleCount++;
            session.AverageRamUsageBytes = session.TotalRamUsageBytes / session.RamSampleCount;
        }

        // GPU Analytics
        if (snapshot.GpuMetrics.UsagePercent.HasValue && snapshot.GpuMetrics.UsagePercent.Value >= 0)
        {
            if (snapshot.GpuMetrics.UsagePercent.Value > session.PeakGpuUsage)
                session.PeakGpuUsage = snapshot.GpuMetrics.UsagePercent.Value;
        }

        // Storage Analytics Extraction
        if (string.IsNullOrEmpty(session.GameStorageLocation))
        {
            try 
            {
                var pathRoot = System.IO.Path.GetPathRoot(session.ExecutablePath);
                if (!string.IsNullOrEmpty(pathRoot))
                {
                    session.GameStorageLocation = $"Local Drive ({pathRoot.TrimEnd('\\')})";
                }
                else
                {
                    session.GameStorageLocation = "Unknown Location";
                }
            }
            catch
            {
                session.GameStorageLocation = "Unknown Location";
            }
        }

        // Diagnostic Findings removed as snapshot does not contain findings directly
    }
}
