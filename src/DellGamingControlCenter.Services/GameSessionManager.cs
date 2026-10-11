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
    private readonly ISessionMonitoringService _monitor;
    private readonly IBackgroundProcessManager _bgManager;
    private readonly IConfigurationService _config;
    private readonly IPolicyEvaluator _policyEvaluator;

    private readonly ConcurrentDictionary<string, GameSession> _activeSessions = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessionTokens = new();

    // Roots the fire-and-forget exit watcher while it is suspended. Without an external
    // reference, a suspended `await` inside a discarded task is only kept alive through a
    // circular reference (task → continuation → state machine → awaiter → task) — a GC
    // island. When collected, the Process object dies with it and its Exited registration
    // is finalized away: the session stays Running forever and policies are never restored.
    // Observed intermittently under memory pressure; timing-dependent, hence rare.
    private readonly ConcurrentDictionary<string, Task> _watcherTasks = new();

    public event EventHandler<GameSessionEventArgs>? SessionChanged;

    public GameSession? GetActiveSession()
    {
        return _activeSessions.Values.FirstOrDefault(s => s.CurrentState == GameSessionState.Running || s.CurrentState == GameSessionState.Launching || s.CurrentState == GameSessionState.WaitingForProcess);
    }

    private readonly ICpuTelemetryService _cpuTelemetry;
    private readonly IMemoryTelemetryService _memoryTelemetry;
    private readonly IGamePerformanceAnalyzer _performanceAnalyzer;

    public GameSessionManager(
        ILoggingService logger,
        IGameLauncher launcher,
        ISessionMonitoringService monitor,
        IBackgroundProcessManager bgManager,
        IConfigurationService config,
        IPolicyEvaluator policyEvaluator,
        ICpuTelemetryService cpuTelemetry,
        IMemoryTelemetryService memoryTelemetry,
        IGamePerformanceAnalyzer performanceAnalyzer)
    {
        _logger = logger;
        _launcher = launcher;
        _monitor = monitor;
        _bgManager = bgManager;
        _config = config;
        _policyEvaluator = policyEvaluator;
        _cpuTelemetry = cpuTelemetry;
        _memoryTelemetry = memoryTelemetry;
        _performanceAnalyzer = performanceAnalyzer;
    }

    private readonly SemaphoreSlim _launchLock = new SemaphoreSlim(1, 1);

    public async Task<GameSession> StartSessionAsync(GameProfile profile)
    {
        await _launchLock.WaitAsync();
        try
        {
            if (GetActiveSession() != null)
            {
                _logger.Log($"Rejected session for {profile.GameName}: Another session is already active.", LogSeverity.Warning, "SessionManager");
                return new GameSession { CurrentState = GameSessionState.Failed, FailureReason = "Another session is already active." };
            }

            _logger.Log($"Orchestrating game session for {profile.GameName}...", LogSeverity.Info, "SessionManager");


        var cts = new CancellationTokenSource();
        var session = await _launcher.LaunchGameAsync(profile, cts.Token);
        
        _activeSessions[session.SessionId] = session;
        _sessionTokens[session.SessionId] = cts;

        if (session.CurrentState == GameSessionState.Failed || session.CurrentState == GameSessionState.Cancelled)
        {
            await RestoreFailedSessionAsync(session);
            SessionChanged?.Invoke(this, new GameSessionEventArgs(session));
            return session;
        }

        // Capture Baseline Snapshot
        var cpuMetrics = _cpuTelemetry.GetMetrics() ?? new CpuMetrics();
        var ramMetrics = _memoryTelemetry.GetMetrics() ?? new RamMetrics();
        
        session.Baseline = new BaselineSnapshot
        {
            CpuUsage = cpuMetrics.UsagePercent,
            RamUsageBytes = ramMetrics.UsedBytes,
            AvailableRamBytes = ramMetrics.AvailableBytes,
            PowerMode = "Unknown", // Will be tracked internally by policy evaluator
            Timestamp = DateTime.UtcNow
        };
        session.TimelineEvents.Add(new PerformanceEvent { Type = PerformanceEventType.GameStarted, Description = $"Started {session.GameName}" });

        // Wait for process
        var pid = await _launcher.FindGameProcessAsync(session, profile, cts.Token);
        if (pid.HasValue)
        {
            session.ProcessId = pid.Value;
            session.CurrentState = GameSessionState.Running;
            
            // Apply Post-Launch Policy (e.g. Process Priority)
            var policyResult = await _policyEvaluator.ApplyPostLaunchPoliciesAsync(profile, session);
            
            foreach (var policy in policyResult.AppliedPolicies)
            {
                session.AppliedChanges.Add(new OptimizationAction { Description = policy.PolicyName, Value = policy.Status });
            }

            if (!policyResult.Success)
            {
                _logger.Log($"Some post-launch policies failed to apply for {profile.GameName}.", LogSeverity.Warning, "SessionManager");
            }
            session.TimelineEvents.Add(new PerformanceEvent { Type = PerformanceEventType.PowerModeChanged, Description = $"Profile policies applied." });

            // Start Monitoring
            _ = _monitor.StartMonitoringAsync(session, cts.Token);

            // Start background watcher for exit (rooted in _watcherTasks so GC cannot collect it)
            _watcherTasks[session.SessionId] = WatchProcessAsync(session);
        }
        else
        {
            session.CurrentState = GameSessionState.Failed;
            session.FailureReason = "Process exited immediately or could not be detected.";
            _logger.Log($"Process not detected. Session {session.SessionId} failed and will be restored.", LogSeverity.Error, "SessionManager");
            await RestoreFailedSessionAsync(session);
        }

        SessionChanged?.Invoke(this, new GameSessionEventArgs(session));
        return session;
        }
        finally
        {
            _launchLock.Release();
        }
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
            
            try
            {
                process.EnableRaisingEvents = true;
                var tcs = new TaskCompletionSource();
                process.Exited += (s, e) => tcs.TrySetResult();

                if (process.HasExited)
                {
                    tcs.TrySetResult();
                }

                // Safety net: a 1 s HasExited poll alongside the event. The event is
                // primary (verified: it wins on this machine); the poll is cheap,
                // timer-rooted (immune to GC island issues), and bounds the worst case
                // to one extra second should the Exited callback ever be skipped.
                var poll = Task.Run(async () =>
                {
                    while (!process.HasExited)
                    {
                        await Task.Delay(1000);
                        process.Refresh();
                    }
                });

                await Task.WhenAny(tcs.Task, poll);
            }
            catch (Exception ex) // usually Win32Exception or InvalidOperationException
            {
                _logger.Log($"Could not attach events to process {session.ProcessId}. Falling back to polling. ({ex.Message})", LogSeverity.Warning, "SessionManager");
                // Fallback polling loop
                while (!process.HasExited)
                {
                    await Task.Delay(2000);
                    process.Refresh();
                }
            }
            
            await HandleProcessExitAsync(session.SessionId);
        }
        catch
        {
            // Process already exited, not found, or total access denied
            await HandleProcessExitAsync(session.SessionId);
        }
        finally
        {
            _watcherTasks.TryRemove(session.SessionId, out _);
        }
    }

    private async Task RestoreFailedSessionAsync(GameSession session)
    {
        _logger.Log($"Restoring policies for failed session {session.SessionId}...", LogSeverity.Info, "SessionManager");

        var gameProfile = _config.GetConfig().GameProfiles.FirstOrDefault(p => p.GameId == session.GameId);
        if (gameProfile != null)
        {
            await _policyEvaluator.RestoreProfileAsync(gameProfile, session);
        }

        _activeSessions.TryRemove(session.SessionId, out _);
        _sessionTokens.TryRemove(session.SessionId, out _);
    }

    private readonly object _restoreLock = new object();

    private async Task RestoreSessionAsync(GameSession session)
    {
        lock (_restoreLock)
        {
            if (session.CurrentState == GameSessionState.Restoring || 
                session.CurrentState == GameSessionState.Completed || 
                session.CurrentState == GameSessionState.CompletedWithWarnings)
            {
                return;
            }
            session.CurrentState = GameSessionState.Restoring;
        }

        _logger.Log($"Restoring system state for session {session.SessionId}...", LogSeverity.Info, "SessionManager");

        await _monitor.StopMonitoringAsync(session);

        bool allRestored = true;

        // Restore applied policies via Evaluator
        var gameProfile = _config.GetConfig().GameProfiles.FirstOrDefault(p => p.GameId == session.GameId);
        if (gameProfile != null)
        {
            var restoreSuccess = await _policyEvaluator.RestoreProfileAsync(gameProfile, session);
            allRestored &= restoreSuccess;
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
        
        // Phase 8: Bounded history to 100
        while (config.SessionHistory.Count > 100)
        {
            config.SessionHistory.RemoveAt(0); // Remove oldest
        }
        
        _config.SaveConfig(config);

        _activeSessions.TryRemove(session.SessionId, out _);
        _sessionTokens.TryRemove(session.SessionId, out _);
        
        SessionChanged?.Invoke(this, new GameSessionEventArgs(session));
    }
}
