namespace DellGamingControlCenter.Core.Models;

using System;
using System.Security.Principal;

public enum ControlCapability
{
    ViewTelemetry,
    ChangePerformanceMode,
    ChangePowerPlan,
    ModifyThermalPolicy,
    ControlFan,
    ChangeGpuMode,
    ModifyNetworkOptimization,
    ManageGameProfiles,
    LaunchGame,
    ModifyApplicationSettings,
    ChangeGpuPreference,
    ChangeProcessPriority
}

public enum ControlStatus
{
    Success,
    NotAuthorized,
    NotSupported,
    InvalidRequest,
    HardwareUnavailable,
    OperationFailed,
    UnknownError
}

public class ControlResult
{
    public ControlStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;

    public bool IsSuccess => Status == ControlStatus.Success;

    public static ControlResult Success(string message = "") => new ControlResult { Status = ControlStatus.Success, Message = message };
    public static ControlResult Denied(string message) => new ControlResult { Status = ControlStatus.NotAuthorized, Message = message };
    public static ControlResult Failed(string message) => new ControlResult { Status = ControlStatus.OperationFailed, Message = message };
    public static ControlResult Invalid(string message) => new ControlResult { Status = ControlStatus.InvalidRequest, Message = message };
    public static ControlResult Unsupported(string message) => new ControlResult { Status = ControlStatus.NotSupported, Message = message };
}

public class ControlContext
{
    public string UserName { get; set; } = string.Empty;
    public bool IsElevated { get; set; }
    
    public static ControlContext GetCurrent()
    {
        bool isElevated;
        string userName;
        try
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                userName = identity.Name;
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        catch
        {
            userName = Environment.UserName;
            isElevated = false;
        }

        return new ControlContext
        {
            UserName = userName,
            IsElevated = isElevated
        };
    }
}
