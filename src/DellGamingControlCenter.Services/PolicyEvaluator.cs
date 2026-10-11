namespace DellGamingControlCenter.Services;

using System;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;
using System.Linq;

public class PolicyEvaluator : IPolicyEvaluator
{
    private readonly ILoggingService _logger;
    private readonly IChangeGpuPreferenceUseCase _gpuUseCase;
    private readonly IChangePerformanceModeUseCase _perfUseCase;
    private readonly IChangeProcessPriorityUseCase _priorityUseCase;
    private readonly IControlAuditLogger _auditLogger;
    private readonly IGpuPreferenceController _gpuController; // To check if supported

    public PolicyEvaluator(
        ILoggingService logger,
        IChangeGpuPreferenceUseCase gpuUseCase,
        IChangePerformanceModeUseCase perfUseCase,
        IChangeProcessPriorityUseCase priorityUseCase,
        IControlAuditLogger auditLogger,
        IGpuPreferenceController gpuController)
    {
        _logger = logger;
        _gpuUseCase = gpuUseCase;
        _perfUseCase = perfUseCase;
        _priorityUseCase = priorityUseCase;
        _auditLogger = auditLogger;
        _gpuController = gpuController;
    }

    public async Task<ProfileApplicationResult> ApplyPreLaunchPoliciesAsync(GameProfile profile, GameSession session)
    {
        var result = new ProfileApplicationResult { ProfileId = profile.GameId };
        _logger.Log($"Evaluating pre-launch policies for {profile.GameName}...", LogSeverity.Info, "PolicyEvaluator");

        // 1. GPU Preference
        if (profile.PreferredGpu != GpuPreference.Auto)
        {
            if (_gpuController.Status == CapabilityStatus.Supported)
            {
                var currentGpu = _gpuUseCase.GetCurrentPreference(session.ExecutablePath);
                session.OriginalSystemState.PreviousGpuPreference = currentGpu.ToString();

                if (currentGpu != profile.PreferredGpu)
                {
                    var res = await _gpuUseCase.ExecuteAsync(session.ExecutablePath, profile.PreferredGpu);
                    if (res.IsSuccess)
                    {
                        result.AppliedPolicies.Add(new PolicyResult { PolicyName = "GPU Preference", Status = "Applied", Reason = $"Set to {profile.PreferredGpu}" });
                        _auditLogger.LogControlEvent(ControlCapability.ChangeGpuPreference, res, $"Profile: {profile.GameName}, GPU set to {profile.PreferredGpu}");
                    }
                    else
                    {
                        result.FailedPolicies.Add(new PolicyResult { PolicyName = "GPU Preference", Status = "Failed", Reason = res.Message });
                        result.Success = false;
                    }
                }
                else
                {
                    result.SkippedPolicies.Add(new PolicyResult { PolicyName = "GPU Preference", Status = "Skipped", Reason = "Already at requested preference." });
                }
            }
            else
            {
                result.SkippedPolicies.Add(new PolicyResult { PolicyName = "GPU Preference", Status = "Skipped", Reason = "Unsupported on this device." });
            }
        }

        // 2. Power Policy
        var currentMode = _perfUseCase.GetCurrentMode();
        session.OriginalSystemState.PreviousPowerSchemeName = currentMode.ToString();
        
        if (profile.PerformanceMode != currentMode)
        {
            var res = await _perfUseCase.ExecuteAsync(profile.PerformanceMode);
            if (res.IsSuccess)
            {
                result.AppliedPolicies.Add(new PolicyResult { PolicyName = "Performance Mode", Status = "Applied", Reason = $"Set to {profile.PerformanceMode}" });
                _auditLogger.LogControlEvent(ControlCapability.ChangePerformanceMode, res, $"Profile: {profile.GameName}, Mode set to {profile.PerformanceMode}");
            }
            else
            {
                result.FailedPolicies.Add(new PolicyResult { PolicyName = "Performance Mode", Status = "Failed", Reason = res.Message });
                result.Success = false;
            }
        }
        else
        {
            result.SkippedPolicies.Add(new PolicyResult { PolicyName = "Performance Mode", Status = "Skipped", Reason = "Already at requested mode." });
        }

        return result;
    }

    public async Task<ProfileApplicationResult> ApplyPostLaunchPoliciesAsync(GameProfile profile, GameSession session)
    {
        var result = new ProfileApplicationResult { ProfileId = profile.GameId };
        _logger.Log($"Evaluating post-launch policies for {profile.GameName}...", LogSeverity.Info, "PolicyEvaluator");

        // 1. Process Priority
        if (profile.ProcessPriority != ProcessPriorityClass.Normal)
        {
            var currentPriority = _priorityUseCase.GetCurrentPriority(session.ProcessId);
            session.OriginalSystemState.PreviousProcessPriorities[session.ProcessId] = currentPriority;

            if (currentPriority != profile.ProcessPriority)
            {
                var res = await _priorityUseCase.ExecuteAsync(session.ProcessId, session.GameName, profile.ProcessPriority);
                if (res.IsSuccess)
                {
                    result.AppliedPolicies.Add(new PolicyResult { PolicyName = "Process Priority", Status = "Applied", Reason = $"Set to {profile.ProcessPriority}" });
                    _auditLogger.LogControlEvent(ControlCapability.ChangeProcessPriority, res, $"Profile: {profile.GameName}, Priority set to {profile.ProcessPriority}");
                }
                else
                {
                    result.FailedPolicies.Add(new PolicyResult { PolicyName = "Process Priority", Status = "Failed", Reason = res.Message });
                }
            }
            else
            {
                result.SkippedPolicies.Add(new PolicyResult { PolicyName = "Process Priority", Status = "Skipped", Reason = "Already at requested priority." });
            }
        }

        return result;
    }

    public async Task<bool> RestoreProfileAsync(GameProfile profile, GameSession session)
    {
        _logger.Log($"Restoring policies for {session.GameName}...", LogSeverity.Info, "PolicyEvaluator");
        bool allSuccess = true;

        // Restore GPU Preference
        if (!string.IsNullOrEmpty(session.OriginalSystemState.PreviousGpuPreference))
        {
            if (Enum.TryParse<GpuPreference>(session.OriginalSystemState.PreviousGpuPreference, out var previousGpu))
            {
                var currentGpu = _gpuUseCase.GetCurrentPreference(session.ExecutablePath);
                if (currentGpu != previousGpu) // Only restore if we need to
                {
                    var res = await _gpuUseCase.RestorePreviousPreferenceAsync(session.ExecutablePath, previousGpu);
                    allSuccess &= res.IsSuccess;
                }
            }
        }

        // Restore Power Plan
        if (!string.IsNullOrEmpty(session.OriginalSystemState.PreviousPowerSchemeName))
        {
            if (Enum.TryParse<PerformanceMode>(session.OriginalSystemState.PreviousPowerSchemeName, out var previousMode))
            {
                var currentMode = _perfUseCase.GetCurrentMode();
                if (currentMode != previousMode)
                {
                    var res = await _perfUseCase.RestorePreviousModeAsync();
                    allSuccess &= res.IsSuccess;
                }
            }
        }

        // Process Priority generally dies with the process, no need to manually restore if the process is dead.
        // If it's alive (e.g. tracking detached process), it could be restored here, but typically games exit.
        
        return allSuccess;
    }
}
