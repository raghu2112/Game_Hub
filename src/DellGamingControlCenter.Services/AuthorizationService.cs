namespace DellGamingControlCenter.Services;

using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class AuthorizationService : IAuthorizationService
{
    private readonly ILoggingService _logger;
    private readonly IControlAuditLogger _auditLogger;

    public AuthorizationService(ILoggingService logger, IControlAuditLogger auditLogger)
    {
        _logger = logger;
        _auditLogger = auditLogger;
    }

    public ControlResult Authorize(ControlCapability capability, ControlContext context)
    {
        // For Phase 2, we determine basic policies based on capability and local Windows identity context.
        
        // Capabilities that require elevation
        bool requiresElevation = capability switch
        {
            // Note: ChangePerformanceMode and ChangePowerPlan DO NOT require elevation on Windows
            ControlCapability.ModifyThermalPolicy => true,
            ControlCapability.ControlFan => true,
            ControlCapability.ChangeGpuMode => true,
            ControlCapability.ModifyNetworkOptimization => true,
            _ => false
        };

        if (requiresElevation && !context.IsElevated)
        {
            var deniedResult = ControlResult.Denied($"Capability {capability} requires Administrator privileges.");
            _auditLogger.LogControlEvent(capability, deniedResult, $"User: {context.UserName}, Elevated: {context.IsElevated}");
            return deniedResult;
        }

        var successResult = ControlResult.Success();
        _auditLogger.LogControlEvent(capability, successResult, $"User: {context.UserName}, Elevated: {context.IsElevated}");
        return successResult;
    }
}
