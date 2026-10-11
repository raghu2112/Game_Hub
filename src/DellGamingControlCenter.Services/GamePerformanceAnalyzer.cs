namespace DellGamingControlCenter.Services;

using System.Collections.Generic;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class GamePerformanceAnalyzer : IGamePerformanceAnalyzer
{
    private readonly ILoggingService _logger;
    private readonly ISystemHealthAnalyzer _healthAnalyzer;
    private readonly IFpsProvider _fpsProvider;

    public GamePerformanceAnalyzer(ILoggingService logger, ISystemHealthAnalyzer healthAnalyzer, IFpsProvider fpsProvider)
    {
        _logger = logger;
        _healthAnalyzer = healthAnalyzer;
        _fpsProvider = fpsProvider;
    }

    public Task<SessionReport> AnalyzeSessionAsync(GameSession session, IEnumerable<DiagnosticSnapshot> snapshots, IEnumerable<FpsSample> fpsSamples)
    {
        _logger.Log($"Analyzing session for {session.GameName}", Core.Enums.LogSeverity.Info, "PerformanceAnalyzer");

        var report = new SessionReport
        {
            SessionId = session.SessionId,
            Duration = (session.EndTime ?? DateTime.UtcNow) - session.StartTime,
            HealthScore = snapshots.Any() ? _healthAnalyzer.CalculateHealthScore(snapshots).OverallScore : 100,
            PeakCpuUsage = session.PeakCpuUsage,
            PeakGpuUsage = session.PeakGpuUsage,
            PeakRamUsageBytes = (long)session.PeakRamUsageBytes,
            AverageRamUsageBytes = (long)session.AverageRamUsageBytes,
            PeakRamUsagePercent = session.PeakRamUsageBytes / (12.0 * 1024 * 1024 * 1024) * 100,
            AverageRamUsagePercent = session.AverageRamUsageBytes / (12.0 * 1024 * 1024 * 1024) * 100,
            PeakTemperature = session.PeakTemperatureCelsius,
            Events = session.TimelineEvents
        };

        if (_fpsProvider.IsAvailable() && fpsSamples != null && fpsSamples.Any())
        {
            report.FpsStats = _fpsProvider.CalculateStatistics(fpsSamples);
        }

        return Task.FromResult(report);
    }

    public Task<string> CompareSessionsAsync(GameSession currentSession, GameSession previousSession)
    {
        if (currentSession.Report == null || previousSession.Report == null)
            return Task.FromResult("No reports available for comparison.");

        return Task.FromResult($"Comparison: GPU Peak changed from {previousSession.Report.PeakGpuUsage}% to {currentSession.Report.PeakGpuUsage}%");
    }
}
