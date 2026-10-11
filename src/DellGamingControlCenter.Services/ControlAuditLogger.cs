namespace DellGamingControlCenter.Services;

using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using System;

public class ControlAuditLogger : IControlAuditLogger
{
    private readonly ILoggingService _logger;

    public ControlAuditLogger(ILoggingService logger)
    {
        _logger = logger;
    }

    public void LogControlEvent(ControlCapability capability, ControlResult result, string details = "")
    {
        var severity = result.IsSuccess ? Core.Enums.LogSeverity.Success : Core.Enums.LogSeverity.Warning;
        string message = $"[AUDIT] Capability: {capability} | Result: {result.Status} | Details: {details} | Message: {result.Message}";
        
        _logger.Log(message, severity, "SecurityAudit");
    }
}
