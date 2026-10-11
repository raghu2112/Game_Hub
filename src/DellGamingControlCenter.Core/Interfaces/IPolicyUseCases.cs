namespace DellGamingControlCenter.Core.Interfaces;

using System.Threading.Tasks;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;
using System.Collections.Generic;

public interface IChangeGpuPreferenceUseCase
{
    Task<ControlResult> ExecuteAsync(string executablePath, GpuPreference preference);
    Task<ControlResult> RestorePreviousPreferenceAsync(string executablePath, GpuPreference previousPreference);
    GpuPreference GetCurrentPreference(string executablePath);
}

public interface IChangeProcessPriorityUseCase
{
    Task<ControlResult> ExecuteAsync(int processId, string processName, ProcessPriorityClass priority);
    Task<ControlResult> RestorePreviousPriorityAsync(int processId, string processName, ProcessPriorityClass previousPriority);
    ProcessPriorityClass GetCurrentPriority(int processId);
}

public interface IPolicyEvaluator
{
    Task<ProfileApplicationResult> ApplyPreLaunchPoliciesAsync(GameProfile profile, GameSession session);
    Task<ProfileApplicationResult> ApplyPostLaunchPoliciesAsync(GameProfile profile, GameSession session);
    Task<bool> RestoreProfileAsync(GameProfile profile, GameSession session);
}
