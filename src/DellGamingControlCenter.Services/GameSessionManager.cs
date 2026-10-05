namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class GameSessionManager : IGameSessionManager
{
    private readonly ILoggingService _logger;
    private readonly IGameLauncher _launcher;
    private readonly IPerformanceModeService _perfService;
    private readonly ISessionMonitoringService _monitor;
    private readonly IBackgroundProcessManager _bgManager;
    private readonly IConfigurationService _config;
    private readonly IOptimizationEngine _optimizationEngine;

    private readonly ConcurrentDictionary<string, GameSession> _activeSessions = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessionTokens = new();

    private readonly IHardwareDetector _hardwareDetector;
    private readonly IGamePerformanceAnalyzer _performanceAnalyzer;

    public GameSessionManager(
        ILoggingService logger,
        IGameLauncher launcher,
        IPerformanceModeService perfService,
        ISessionMonitoringService monitor,
        IBackgroundProcessManager bgManager,
        IConfigurationService config,
        IOptimizationEngine optimizationEngine,
        IHardwareDetector hardwareDetector,
        IGamePerformanceAnalyzer performanceAnalyzer)
    {
        _logger = logger;
        _launcher = launcher;
        _perfService = perfService;
        _monitor = monitor;
        _bgManager = bgManager;
        _config = config;
        _optimizationEngine = optimizationEngine;
        _hardwareDetector = hardwareDetector;
        _performanceAnalyzer = performanceAnalyzer;
    }

    public async Task<GameSession> StartSessionAsync(GameProfile profile)
    {
        _logger.Log($"Orchestrating game session for {profile.GameName}...", LogSeverity.Info, "SessionManager");

        var cts = new CancellationTokenSource();
        var session = await _launcher.LaunchGameAsync(profile, cts.Token);
        
        _activeSessions[session.SessionId] = session;
        _sessionTokens[session.SessionId] = cts;

        if (session.CurrentState == GameSessionState.Failed || session.CurrentState == GameSessionState.Cancelled)
        {
            return session;
        }

        // Capture Baseline Snapshot
        var hw = _hardwareDetector.GetSnapshot();
        var cpuMetrics = _hardwareDetector.GetCpuMetrics();
        var ramMetrics = _hardwareDetector.GetRamMetrics();
        
        session.Baseline = new BaselineSnapshot
        {
            CpuUsage = cpuMetrics.UsagePercent,
            RamUsageBytes = ramMetrics.UsedBytes,
            AvailableRamBytes = ramMetrics.AvailableBytes,
            PowerMode = _perfService.GetCurrentMode().ToString(),
            Timestamp = DateTime.UtcNow
        };
        session.TimelineEvents.Add(new PerformanceEvent { Type = PerformanceEventType.GameStarted, Description = $"Started {session.GameName}" });

        // Apply Performance Mode if requested
        if (profile.PerformanceMode != PerformanceMode.Balanced)
        {
            session.OriginalSystemState.PreviousPowerSchemeName = _perfService.GetCurrentMode().ToString();
            await _perfService.ApplyModeAsync(profile.PerformanceMode);
            session.TimelineEvents.Add(new PerformanceEvent { Type = PerformanceEventType.PowerModeChanged, Description = $"Power Mode changed to {profile.PerformanceMode}" });
        }

        // Wait for process
        var pid = await _launcher.FindGameProcessAsync(session, profile, cts.Token);
        if (pid.HasValue)
        {
            session.ProcessId = pid.Value;
            session.CurrentState = GameSessionState.Running;
            
            // Apply Runtime Priority
            await _launcher.ApplyRuntimeOptimizationsAsync(session, pid.Value);

            // Start Monitoring
            _ = _monitor.StartMonitoringAsync(session, cts.Token);

            // Start background watcher for exit
            _ = WatchProcessAsync(session);
        }
        else
        {
            session.CurrentState = GameSessionState.CompletedWithWarnings;
            _logger.Log($"Process not detected. Session {session.SessionId} will not be monitored.", LogSeverity.Warning, "SessionManager");
            await RestoreSessionAsync(session);
        }

        return session;
    }

    public async Task StopSessionAsync(string sessionId)
    {
        if (_sessionTokens.TryGetValue(sessionId, out var cts))
        {
            _logger.Log($"Manual stop requested for session {sessionId}.", LogSeverity.Info, "SessionManager");
            cts.Cancel();
            if (_activeSessions.TryGetValue(sessionId, out var session))
            {
                await RestoreSessionAsync(session);
            }
        }
    }

    public async Task HandleProcessExitAsync(string sessionId)
    {
        if (_activeSessions.TryGetValue(sessionId, out var session))
        {
            _logger.Log($"Process exited naturally for session {sessionId}.", LogSeverity.Info, "SessionManager");
            if (session.CurrentState == GameSessionState.Running)
            {
                session.CurrentState = GameSessionState.Stopping;
            }
            await RestoreSessionAsync(session);
        }
    }

    private async Task WatchProcessAsync(GameSession session)
    {
        try
        {
            var process = Process.GetProcessById(session.ProcessId);
            process.EnableRaisingEvents = true;
            
            var tcs = new TaskCompletionSource();
            process.Exited += (s, e) => tcs.TrySetResult();

            if (process.HasExited)
            {
                tcs.TrySetResult();
            }

            await tcs.Task;
            await HandleProcessExitAsync(session.SessionId);
        }
        catch
        {
            // Process might have already exited fast or access denied
            await HandleProcessExitAsync(session.SessionId);
        }
    }

    private async Task RestoreSessionAsync(GameSession session)
    {
        if (session.CurrentState == GameSessionState.Restoring || session.CurrentState == GameSessionState.Completed)
            return;

        session.CurrentState = GameSessionState.Restoring;
        _logger.Log($"Restoring system state for session {session.SessionId}...", LogSeverity.Info, "SessionManager");

        await _monitor.StopMonitoringAsync(session);

        bool allRestored = true;

        // Restore Power Mode
        if (session.OriginalSystemState.PreviousPowerSchemeName != null)
        {
            allRestored &= await _perfService.RestorePreviousModeAsync();
        }

        // Restore Process Priorities (if the process is still somehow alive but we're restoring, we'd revert it)
        // Usually it's dead by now, so no action required for dead processes.

        // Rollback Optimization Engine Changes (Registry, Game Mode, GPU Pref)
        if (session.AppliedChanges.Count > 0)
        {
            var rollbackPlan = new OptimizationPlan
            {
                PlanId = session.OptimizationSessionId,
                GameId = session.GameId,
                GameName = session.GameName,
                Actions = session.AppliedChanges
            };
            allRestored &= await _optimizationEngine.RollbackPlanAsync(rollbackPlan);
            session.RestoredChanges = session.AppliedChanges;
        }

        session.RestorationSuccessful = allRestored;
        session.CurrentState = allRestored ? GameSessionState.Completed : GameSessionState.CompletedWithWarnings;
        session.EndTime = DateTime.UtcNow;
        session.TimelineEvents.Add(new PerformanceEvent { Type = PerformanceEventType.GameExited, Description = $"Exited {session.GameName}" });

        // Generate Post-Session Report
        session.Report = await _performanceAnalyzer.AnalyzeSessionAsync(session, new List<DiagnosticSnapshot>(), new List<FpsSample>());

        _logger.Log($"Session {session.SessionId} ended. State: {session.CurrentState}", LogSeverity.Success, "SessionManager");

        // Save to history
        var config = _config.GetConfig();
        config.SessionHistory.Add(session);
        _config.SaveConfig(config);

        _activeSessions.TryRemove(session.SessionId, out _);
        _sessionTokens.TryRemove(session.SessionId, out _);
    }
}
