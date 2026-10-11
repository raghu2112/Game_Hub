namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class GamingIntelligenceEngine : IGamingIntelligenceEngine
{
    private readonly IConfigurationService _configService;
    private readonly IMetricsSampler _metricsSampler;

    public GamingIntelligenceEngine(IConfigurationService configService, IMetricsSampler metricsSampler)
    {
        _configService = configService;
        _metricsSampler = metricsSampler;
    }

    public IEnumerable<GamingInsight> GetSystemInsights()
    {
        var insights = new List<GamingInsight>();
        var config = _configService.GetConfig();

        // 1. GPU Telemetry Limitation (Fact)
        insights.Add(new GamingInsight
        {
            Id = "Sys_GpuTelemetryLimited",
            Title = "GPU telemetry is limited on this device",
            Summary = "The AMD Radeon 520 provides utilization metrics but no thermal sensors.",
            Detail = "Because this device lacks GPU temperature sensors, thermal throttling detection relies exclusively on CPU package temperature or clock speed heuristics.",
            Severity = DiagnosticSeverity.Info,
            Category = DiagnosticCategory.GPU,
            Confidence = DiagnosticConfidence.High,
            Type = InsightType.Fact,
            Scope = InsightScope.System,
            Evidence = "Hardware profile capability matrix indicates GPU temperature: Unsupported.",
            Recommendation = "Do not rely on software to monitor GPU thermals; rely on external ventilation."
        });

        // 2. HDD Installed Games (Derived Metric)
        int hddGames = config.GameProfiles.Count(p => p.ExecutablePath?.StartsWith("D:", StringComparison.OrdinalIgnoreCase) == true);
        if (hddGames > 0)
        {
            insights.Add(new GamingInsight
            {
                Id = "Sys_GamesOnHdd",
                Title = $"{hddGames} game(s) installed on mechanical drive",
                Summary = "Games on the D: drive may experience longer loading times.",
                Detail = "Mechanical hard drives (5400 RPM) significantly reduce loading speeds and can cause stuttering if the game streams assets dynamically.",
                Severity = DiagnosticSeverity.Notice,
                Category = DiagnosticCategory.Storage,
                Confidence = DiagnosticConfidence.High,
                Type = InsightType.DerivedMetric,
                Scope = InsightScope.System,
                Evidence = $"{hddGames} profiles located on D: (HDD).",
                Recommendation = "Consider moving heavily played or slow-loading games to the C: drive (SSD)."
            });
        }

        // 3. System-wide Memory Pressure (Derived Metric / Inference)
        var recentSessions = config.SessionHistory.OrderByDescending(s => s.EndTime).Take(10).ToList();
        if (recentSessions.Count >= 3)
        {
            int highRamSessions = recentSessions.Count(s => s.Report?.AverageRamUsagePercent >= 80);
            if ((double)highRamSessions / recentSessions.Count >= 0.5)
            {
                insights.Add(new GamingInsight
                {
                    Id = "Sys_MemoryPressure",
                    Title = "System memory pressure is recurring across games",
                    Summary = "Frequent high memory usage observed in recent gaming sessions.",
                    Detail = "System memory is frequently reaching critical levels, which can lead to page file swapping and noticeable performance drops.",
                    Severity = DiagnosticSeverity.Warning,
                    Category = DiagnosticCategory.Memory,
                    Confidence = DiagnosticConfidence.High,
                    Type = InsightType.DerivedMetric,
                    Scope = InsightScope.System,
                    Evidence = $"{highRamSessions} out of {recentSessions.Count} recent sessions exceeded 80% RAM utilization.",
                    Recommendation = "Close background applications and browsers before starting games."
                });
            }
        }

        // Live diagnostics removed since Findings are not on DiagnosticSnapshot

        return insights;
    }

    public IEnumerable<GamingInsight> GetGameInsights(string gameId)
    {
        var insights = new List<GamingInsight>();
        var config = _configService.GetConfig();
        var profile = config.GameProfiles.FirstOrDefault(p => p.GameId == gameId);
        
        if (profile == null) return insights;

        // 1. Game on HDD (Fact)
        if (profile.ExecutablePath?.StartsWith("D:", StringComparison.OrdinalIgnoreCase) == true)
        {
            insights.Add(new GamingInsight
            {
                Id = $"Game_Hdd_{gameId}",
                Title = "This game is installed on a mechanical drive",
                Summary = "Slower load times may be experienced.",
                Detail = "The D: drive is a 5400 RPM mechanical disk. Asset streaming and level loads will be slower compared to the primary SSD.",
                Severity = DiagnosticSeverity.Notice,
                Category = DiagnosticCategory.Storage,
                Confidence = DiagnosticConfidence.High,
                Type = InsightType.Fact,
                Scope = InsightScope.Game,
                RelatedGameId = gameId,
                Evidence = $"Storage: Local Drive (D:) for {profile.GameName}",
                Recommendation = "If faster game loading is important, consider installing the game on the C: SSD."
            });
        }

        // 2. Repeated Session Pattern (Derived Metric)
        var gameSessions = config.SessionHistory
            .Where(s => s.GameId == gameId && s.Report != null)
            .OrderByDescending(s => s.EndTime)
            .Take(10)
            .ToList();

        if (gameSessions.Count >= 3)
        {
            int memoryPressureCount = gameSessions.Count(s => s.Report!.AverageRamUsagePercent >= 80);
            if ((double)memoryPressureCount / gameSessions.Count >= 0.5)
            {
                insights.Add(new GamingInsight
                {
                    Id = $"Game_MemPressure_{gameId}",
                    Title = "Repeated memory pressure detected",
                    Summary = "This game consistently pushes memory limits.",
                    Detail = "Memory utilization exceeded 80% on average in most recent sessions, leaving little headroom for Windows operations.",
                    Severity = DiagnosticSeverity.Warning,
                    Category = DiagnosticCategory.Memory,
                    Confidence = DiagnosticConfidence.High,
                    Type = InsightType.DerivedMetric,
                    Scope = InsightScope.Game,
                    RelatedGameId = gameId,
                    Evidence = $"{memoryPressureCount} / {gameSessions.Count} recent sessions exceeded 80% average memory threshold.",
                    Recommendation = "Consider lowering texture quality or closing background processes."
                });
            }

            // Check Policy Effectiveness History
            int policySuccess = gameSessions.Count(s => s.OptimizationSuccessful && s.RestorationSuccessful);
            if (policySuccess == gameSessions.Count)
            {
                insights.Add(new GamingInsight
                {
                    Id = $"Game_PolicySuccess_{gameId}",
                    Title = "Gaming profile applying consistently",
                    Summary = "Profile policies are reliably applying and restoring.",
                    Detail = "Over the last several sessions, GamerHub has successfully enforced and restored power and priority policies without failure.",
                    Severity = DiagnosticSeverity.Info,
                    Category = DiagnosticCategory.System,
                    Confidence = DiagnosticConfidence.High,
                    Type = InsightType.DerivedMetric,
                    Scope = InsightScope.Game,
                    RelatedGameId = gameId,
                    Evidence = $"Profile applied successfully in {policySuccess}/{gameSessions.Count} sessions.",
                    Recommendation = "No action required."
                });
            }
            else if (policySuccess == 0)
            {
                insights.Add(new GamingInsight
                {
                    Id = $"Game_PolicyFail_{gameId}",
                    Title = "Profile policies failing to apply",
                    Summary = "Requested optimizations are frequently blocked or unsupported.",
                    Detail = "GamerHub is unable to consistently apply the requested GPU preference or Power Plan for this game.",
                    Severity = DiagnosticSeverity.Warning,
                    Category = DiagnosticCategory.System,
                    Confidence = DiagnosticConfidence.High,
                    Type = InsightType.DerivedMetric,
                    Scope = InsightScope.Game,
                    RelatedGameId = gameId,
                    Evidence = $"Profile application failed in {gameSessions.Count - policySuccess} recent sessions.",
                    Recommendation = "Review the Game Profile settings and ensure you have administrative permissions."
                });
            }
        }

        return insights;
    }

    public IEnumerable<GamingInsight> GetSessionInsights(GameSession session)
    {
        var insights = new List<GamingInsight>();

        // 1. High Memory Pressure (Fact)
        if (session.Report != null && session.Report.AverageRamUsagePercent >= 80)
        {
            insights.Add(new GamingInsight
            {
                Id = $"Session_MemPressure_{session.SessionId}",
                Title = "High memory usage observed",
                Summary = "Memory pressure was high during this session.",
                Detail = "The system memory was operating near its capacity, which increases the likelihood of stuttering.",
                Severity = DiagnosticSeverity.Warning,
                Category = DiagnosticCategory.Memory,
                Confidence = DiagnosticConfidence.High,
                Type = InsightType.Fact,
                Scope = InsightScope.Session,
                RelatedSessionId = session.SessionId,
                RelatedGameId = session.GameId,
                Evidence = $"Average RAM: {Math.Round(session.Report.AverageRamUsagePercent)}%, Peak RAM: {Math.Round(session.Report.PeakRamUsagePercent)}%",
                Recommendation = "Close unnecessary applications before starting the game."
            });
        }

        // 2. Policy Effectiveness (Fact)
        if (session.CurrentState == GameSessionState.Completed)
        {
            insights.Add(new GamingInsight
            {
                Id = $"Session_PolicySuccess_{session.SessionId}",
                Title = "Gaming profile applied successfully",
                Summary = "GamerHub successfully managed system state for this session.",
                Detail = "Requested performance modes were applied and the original system state was correctly restored upon exit.",
                Severity = DiagnosticSeverity.Info,
                Category = DiagnosticCategory.System,
                Confidence = DiagnosticConfidence.High,
                Type = InsightType.Fact,
                Scope = InsightScope.Session,
                RelatedSessionId = session.SessionId,
                RelatedGameId = session.GameId,
                Evidence = $"Optimization: {session.OptimizationSuccessful}, Restoration: {session.RestorationSuccessful}, Applied changes: {session.AppliedChanges.Count}",
                Recommendation = "No action required."
            });
        }
        else if (session.CurrentState == GameSessionState.CompletedWithWarnings)
        {
            insights.Add(new GamingInsight
            {
                Id = $"Session_PolicyWarn_{session.SessionId}",
                Title = "Gaming profile applied with warnings",
                Summary = "Some requested policies could not be applied or restored.",
                Detail = "One or more policies (like GPU Preference or Power Plan) failed during the session lifecycle.",
                Severity = DiagnosticSeverity.Notice,
                Category = DiagnosticCategory.System,
                Confidence = DiagnosticConfidence.High,
                Type = InsightType.Fact,
                Scope = InsightScope.Session,
                RelatedSessionId = session.SessionId,
                RelatedGameId = session.GameId,
                Evidence = $"Failed to apply/restore all policies. Changes logged: {session.AppliedChanges.Count}",
                Recommendation = "Check application permissions and review profile policies."
            });
        }

        return insights;
    }
}
