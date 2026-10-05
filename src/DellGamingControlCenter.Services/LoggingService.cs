namespace DellGamingControlCenter.Services;

using System.Text.Json;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

public class LoggingService : ILoggingService
{
    private readonly List<ActivityLogEntry> _log = new();
    private readonly string _logFilePath;

    public LoggingService()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DellGamingControlCenter");
        Directory.CreateDirectory(appData);
        _logFilePath = Path.Combine(appData, "activity_log.json");
        LoadLog();
    }

    public void Log(string message, LogSeverity severity = LogSeverity.Info, string category = "General", string? detail = null)
    {
        var entry = new ActivityLogEntry
        {
            Timestamp = DateTime.Now,
            Message = message,
            Severity = severity,
            Category = category,
            Detail = detail
        };

        lock (_log)
        {
            _log.Add(entry);
            if (_log.Count > 1000)
            {
                _log.RemoveAt(0); // Keep last 1000 entries
            }
        }

        SaveLogAsync();
    }

    public IEnumerable<ActivityLogEntry> GetRecentActivity(int maxEntries = 100)
    {
        lock (_log)
        {
            return _log.OrderByDescending(x => x.Timestamp).Take(maxEntries).ToList();
        }
    }

    public void ClearLog()
    {
        lock (_log)
        {
            _log.Clear();
        }
        SaveLogAsync();
    }

    private void LoadLog()
    {
        try
        {
            if (File.Exists(_logFilePath))
            {
                var json = File.ReadAllText(_logFilePath);
                var loaded = JsonSerializer.Deserialize<List<ActivityLogEntry>>(json);
                if (loaded != null)
                {
                    _log.AddRange(loaded);
                }
            }
        }
        catch
        {
            // Ignore corrupted log file
        }
    }

    private async void SaveLogAsync()
    {
        try
        {
            List<ActivityLogEntry> copy;
            lock (_log)
            {
                copy = _log.ToList();
            }
            var json = JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_logFilePath, json);
        }
        catch
        {
            // Ignore save failures
        }
    }
}
