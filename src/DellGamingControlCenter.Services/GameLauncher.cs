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
    private readonly IPolicyEvaluator _policyEvaluator;

    public GameLauncher(ILoggingService logger, IPolicyEvaluator policyEvaluator)
    {
        _logger = logger;
        _policyEvaluator = policyEvaluator;
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

        // Apply pre-launch policies (GPU Preference, Power Mode)
        session.CurrentState = GameSessionState.Optimizing;
        var preLaunchResult = await _policyEvaluator.ApplyPreLaunchPoliciesAsync(profile, session);
        session.OptimizationSuccessful = preLaunchResult.Success;

        foreach (var policy in preLaunchResult.AppliedPolicies)
        {
            session.AppliedChanges.Add(new OptimizationAction { Description = policy.PolicyName, Value = policy.Status });
        }

        if (!preLaunchResult.Success)
        {
            _logger.Log("Some pre-launch policies failed. Proceeding with launch anyway as per safety rules.", LogSeverity.Warning, "Launcher");
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
                UseShellExecute = false
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

}
