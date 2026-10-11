namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Models;
using System.Threading.Tasks;

public interface IAuthorizationService
{
    ControlResult Authorize(ControlCapability capability, ControlContext context);
}

public interface IControlAuditLogger
{
    void LogControlEvent(ControlCapability capability, ControlResult result, string details = "");
}

public interface IChangePerformanceModeUseCase
{
    Task<ControlResult> ExecuteAsync(DellGamingControlCenter.Core.Enums.PerformanceMode mode);
    Task<ControlResult> RestorePreviousModeAsync();
    DellGamingControlCenter.Core.Enums.PerformanceMode GetCurrentMode();
}
