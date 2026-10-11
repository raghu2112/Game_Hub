namespace DellGamingControlCenter.Services;

using System.Threading.Tasks;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

public class ChangePerformanceModeUseCase : IChangePerformanceModeUseCase
{
    private readonly IPerformanceModeService _performanceService;
    private readonly IAuthorizationService _authorizationService;

    public ChangePerformanceModeUseCase(
        IPerformanceModeService performanceService,
        IAuthorizationService authorizationService)
    {
        _performanceService = performanceService;
        _authorizationService = authorizationService;
    }

    public async Task<ControlResult> ExecuteAsync(PerformanceMode mode)
    {
        // 1. Gather Context
        var context = ControlContext.GetCurrent();
        
        // 2. Authorize
        var authResult = _authorizationService.Authorize(ControlCapability.ChangePerformanceMode, context);
        if (!authResult.IsSuccess)
        {
            return authResult;
        }
        
        // 3. Validation
        if (!System.Enum.IsDefined(typeof(PerformanceMode), mode))
        {
            return ControlResult.Invalid($"Performance mode {mode} is not recognized.");
        }

        // 4. Execute Service
        bool success = await _performanceService.ApplyModeAsync(mode);
        if (success)
        {
            return ControlResult.Success($"Performance mode changed to {mode}.");
        }
        else
        {
            return ControlResult.Failed($"Failed to change performance mode to {mode}. Check OS support or hardware capabilities.");
        }
    }

    public async Task<ControlResult> RestorePreviousModeAsync()
    {
        var context = ControlContext.GetCurrent();
        var authResult = _authorizationService.Authorize(ControlCapability.ChangePerformanceMode, context);
        if (!authResult.IsSuccess)
        {
            return authResult;
        }

        bool success = await _performanceService.RestorePreviousModeAsync();
        if (success)
        {
            return ControlResult.Success("Restored previous performance mode.");
        }
        return ControlResult.Failed("Failed to restore previous performance mode.");
    }

    public PerformanceMode GetCurrentMode()
    {
        // Read-only operation, no authorization required
        return _performanceService.GetCurrentMode();
    }
}
