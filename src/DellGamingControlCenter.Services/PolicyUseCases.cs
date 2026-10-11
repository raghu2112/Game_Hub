namespace DellGamingControlCenter.Services;

using System.Threading.Tasks;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

public class ChangeGpuPreferenceUseCase : IChangeGpuPreferenceUseCase
{
    private readonly IGpuPreferenceController _gpuPreferenceController;
    private readonly IAuthorizationService _authService;

    public ChangeGpuPreferenceUseCase(IGpuPreferenceController gpuPreferenceController, IAuthorizationService authService)
    {
        _gpuPreferenceController = gpuPreferenceController;
        _authService = authService;
    }

    public Task<ControlResult> ExecuteAsync(string executablePath, GpuPreference preference)
    {
        var authResult = _authService.Authorize(ControlCapability.ChangeGpuPreference, ControlContext.GetCurrent());
        if (!authResult.IsSuccess) return Task.FromResult(authResult);

        if (_gpuPreferenceController.Status != CapabilityStatus.Supported)
            return Task.FromResult(ControlResult.Unsupported("GPU Preference control is not supported."));

        if (string.IsNullOrWhiteSpace(executablePath))
            return Task.FromResult(ControlResult.Invalid("Executable path is missing."));

        // R7-A: surface "target does not exist" as its own honest outcome so the UI
        // never implies a preference was applied to an executable Windows cannot find.
        if (!System.IO.File.Exists(executablePath))
            return Task.FromResult(ControlResult.Invalid($"Executable not found at '{executablePath}'. GPU preference was not set."));

        var success = _gpuPreferenceController.SetPreference(executablePath, preference);
        
        return Task.FromResult(success ? ControlResult.Success() : ControlResult.Failed("Failed to set GPU preference."));
    }

    public Task<ControlResult> RestorePreviousPreferenceAsync(string executablePath, GpuPreference previousPreference)
    {
        var authResult = _authService.Authorize(ControlCapability.ChangeGpuPreference, ControlContext.GetCurrent());
        if (!authResult.IsSuccess) return Task.FromResult(authResult);

        if (string.IsNullOrWhiteSpace(executablePath))
            return Task.FromResult(ControlResult.Invalid("Executable path is missing."));

        bool success;
        if (previousPreference == GpuPreference.Auto)
        {
            success = _gpuPreferenceController.RemovePreference(executablePath);
        }
        else
        {
            success = _gpuPreferenceController.SetPreference(executablePath, previousPreference);
        }

        return Task.FromResult(success ? ControlResult.Success() : ControlResult.Failed("Failed to restore GPU preference."));
    }

    public GpuPreference GetCurrentPreference(string executablePath)
    {
        return _gpuPreferenceController.GetPreference(executablePath);
    }
}

public class ChangeProcessPriorityUseCase : IChangeProcessPriorityUseCase
{
    private readonly IProcessController _processController;
    private readonly IAuthorizationService _authService;

    public ChangeProcessPriorityUseCase(IProcessController processController, IAuthorizationService authService)
    {
        _processController = processController;
        _authService = authService;
    }

    public Task<ControlResult> ExecuteAsync(int processId, string processName, ProcessPriorityClass priority)
    {
        var authResult = _authService.Authorize(ControlCapability.ChangeProcessPriority, ControlContext.GetCurrent());
        if (!authResult.IsSuccess) return Task.FromResult(authResult);

        var success = _processController.SetPriority(processId, priority);
        return Task.FromResult(success ? ControlResult.Success() : ControlResult.Failed($"Failed to set priority for {processName} (PID {processId})."));
    }

    public Task<ControlResult> RestorePreviousPriorityAsync(int processId, string processName, ProcessPriorityClass previousPriority)
    {
        var authResult = _authService.Authorize(ControlCapability.ChangeProcessPriority, ControlContext.GetCurrent());
        if (!authResult.IsSuccess) return Task.FromResult(authResult);

        var success = _processController.SetPriority(processId, previousPriority);
        return Task.FromResult(success ? ControlResult.Success() : ControlResult.Failed($"Failed to restore priority for {processName}."));
    }

    public ProcessPriorityClass GetCurrentPriority(int processId)
    {
        return _processController.GetPriority(processId);
    }
}
