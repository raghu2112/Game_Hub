namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

/// <summary>
/// Service for logging all application and optimization activity.
/// </summary>
public interface ILoggingService
{
    /// <summary>Logs an event to the activity log.</summary>
    void Log(string message, LogSeverity severity = LogSeverity.Info, string category = "General", string? detail = null);

    /// <summary>Retrieves the recent activity log.</summary>
    IEnumerable<ActivityLogEntry> GetRecentActivity(int maxEntries = 100);

    /// <summary>Clears the activity log.</summary>
    void ClearLog();
}
