namespace DellGamingControlCenter.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class GameLauncher : IGameLauncher
{
    private readonly ILoggingService _logger;
    private readonly IOptimizationEngine _optimizationEngine;

    public GameLauncher(ILoggingService logger, IOptimizationEngine optimizationEngine)
    {
        _logger = logger;
        _optimizationEngine = optimizationEngine;
    }

    public async Task<GameSession> LaunchGameAsync(GameProfile profile, CancellationToken cancellationToken)
    {
        var session = new GameSession
        {
            GameId = profile.GameId,
            GameName = profile.GameName,
            ExecutablePath = profile.ExecutablePath,
            CurrentState = GameSessionState.Preparing
        };
        
        _logger.Log($"Preparing to launch {session.GameName}...", LogSeverity.Info, "Launcher");

        if (!File.Exists(profile.ExecutablePath))
        {
            session.CurrentState = GameSessionState.Failed;
            session.FailureReason = "Executable not found.";
            _logger.Log($"Failed to launch {session.GameName}: Executable not found.", LogSeverity.Error, "Launcher");
            return session;
        }

        // Generate Plan
        var plan = _optimizationEngine.GeneratePlan(profile);
        session.OptimizationSessionId = plan.PlanId;

        // Apply pre-launch optimizations
        session.CurrentState = GameSessionState.Optimizing;
        bool optSuccess = await _optimizationEngine.ApplyPlanAsync(plan);
        session.OptimizationSuccessful = optSuccess;
        
        // Populate applied changes for rollback tracing
        session.AppliedChanges = plan.Actions;

        if (!optSuccess)
        {
            _logger.Log("Some optimizations failed. Proceeding with launch anyway as per safety rules.", LogSeverity.Warning, "Launcher");
            // The optimization engine handles rollback for failed strict items, but we can still launch.
        }

        if (cancellationToken.IsCancellationRequested)
        {
            session.CurrentState = GameSessionState.Cancelled;
            return session;
        }

        session.CurrentState = GameSessionState.Launching;
        
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = profile.ExecutablePath,
                WorkingDirectory = profile.WorkingDirectory ?? Path.GetDirectoryName(profile.ExecutablePath),
                UseShellExecute = true
            };
            
            if (!string.IsNullOrEmpty(profile.LaunchArguments))
            {
                startInfo.Arguments = profile.LaunchArguments;
            }

            Process.Start(startInfo);
            _logger.Log($"Launched {session.GameName}.", LogSeverity.Success, "Launcher");
            session.CurrentState = GameSessionState.WaitingForProcess;
        }
        catch (Exception ex)
        {
            session.CurrentState = GameSessionState.Failed;
            session.FailureReason = $"Launch exception: {ex.Message}";
            _logger.Log(session.FailureReason, LogSeverity.Error, "Launcher");
        }

        return session;
    }

    public async Task<int?> FindGameProcessAsync(GameSession session, GameProfile profile, CancellationToken cancellationToken)
    {
        _logger.Log($"Polling for process: {session.GameName}", LogSeverity.Info, "Launcher");
        
        var namesToCheck = new List<string> { Path.GetFileNameWithoutExtension(session.ExecutablePath) };
        if (profile.ProcessNames != null && profile.ProcessNames.Any())
        {
            namesToCheck.AddRange(profile.ProcessNames);
        }
        if (!string.IsNullOrEmpty(profile.PrimaryExecutable))
        {
            namesToCheck.Add(Path.GetFileNameWithoutExtension(profile.PrimaryExecutable));
        }
        
        namesToCheck = namesToCheck.Distinct().ToList();

        // 30 second default timeout
        int maxAttempts = 60; // 60 * 500ms = 30s
        for (int i = 0; i < maxAttempts; i++)
        {
            if (cancellationToken.IsCancellationRequested) return null;

            foreach (var name in namesToCheck)
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length > 0)
                {
                    int pid = processes[0].Id;
                    _logger.Log($"Detected running process {name} (PID: {pid})", LogSeverity.Success, "Launcher");
                    return pid;
                }
            }

            try
            {
                await Task.Delay(500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        _logger.Log($"Timeout waiting for process {session.GameName} to start.", LogSeverity.Warning, "Launcher");
        return null;
    }

    public async Task<bool> ApplyRuntimeOptimizationsAsync(GameSession session, int processId)
    {
        return await Task.Run(() =>
        {
            try
            {
                var process = Process.GetProcessById(processId);
                
                // Find priority action
                var priorityAction = session.AppliedChanges.Find(a => a.Type == "ProcessPriority");
                if (priorityAction != null && priorityAction.Status == OptimizationStatus.Applied)
                {
                    if (Enum.TryParse<Core.Enums.ProcessPriorityClass>(priorityAction.Value, out var targetPriority))
                    {
                        // Backup original
                        session.OriginalSystemState.PreviousProcessPriorities[processId] = (Core.Enums.ProcessPriorityClass)(int)process.PriorityClass;
                        priorityAction.RevertValue = process.PriorityClass.ToString();
                        
                        _logger.Log($"Applying runtime priority {targetPriority} to PID {processId}", LogSeverity.Info, "Launcher");
                        process.PriorityClass = (System.Diagnostics.ProcessPriorityClass)(int)targetPriority;
                        
                        // Verify
                        if (process.PriorityClass == (System.Diagnostics.ProcessPriorityClass)(int)targetPriority)
                        {
                            priorityAction.Status = OptimizationStatus.Verified;
                        }
                        else
                        {
                            priorityAction.Status = OptimizationStatus.Failed;
                            _logger.Log("Priority application failed verification.", LogSeverity.Warning, "Launcher");
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.Log($"Failed to apply runtime optimization to PID {processId}: {ex.Message} (Requires elevation?)", LogSeverity.Warning, "Launcher");
                return false;
            }
        });
    }
}
