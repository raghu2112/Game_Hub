namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class BackgroundProcessManager : IBackgroundProcessManager
{
    private readonly ILoggingService _logger;
    private readonly IConfigurationService _configService;

    // Explicit safe-to-close list derived from real-world telemetry
    private readonly HashSet<string> _knownSafe = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "discord", "spotify", "skype", "slack", "teams"
    };

    private readonly HashSet<string> _criticalSystem = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "csrss", "wininit", "smss", "services", "lsass", "explorer",
        "taskmgr", "dwm", "spoolsv", "System", "Idle"
    };

    public BackgroundProcessManager(ILoggingService logger, IConfigurationService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    public Task<List<string>> GetClosableProcessesAsync()
    {
        return Task.Run(() =>
        {
            var closable = new List<string>();
            var rules = _configService.GetConfig().BackgroundRules;
            var processes = Process.GetProcesses();

            foreach (var p in processes)
            {
                try
                {
                    var name = p.ProcessName;
                    
                    if (_criticalSystem.Contains(name))
                        continue;

                    var rule = rules.FirstOrDefault(r => string.Equals(r.ProcessName, name, StringComparison.OrdinalIgnoreCase));
                    if (rule != null)
                    {
                        if (rule.RuleType == BackgroundProcessRuleType.CloseBeforeGame || rule.RuleType == BackgroundProcessRuleType.Ask)
                        {
                            if (!closable.Contains(name))
                                closable.Add(name);
                        }
                    }
                    else if (_knownSafe.Contains(name))
                    {
                        if (!closable.Contains(name))
                            closable.Add(name);
                    }
                }
                catch
                {
                    // Ignore access denied for elevated processes
                }
            }

            return closable;
        });
    }

    public async Task<bool> RequestCloseAsync(IEnumerable<string> processNames)
    {
        bool allSuccess = true;
        await Task.Run(() =>
        {
            foreach (var name in processNames)
            {
                if (_criticalSystem.Contains(name))
                {
                    _logger.Log($"Refused to close critical process: {name}", LogSeverity.Warning, "BackgroundManager");
                    allSuccess = false;
                    continue;
                }

                var processes = Process.GetProcessesByName(name);
                foreach (var p in processes)
                {
                    try
                    {
                        _logger.Log($"Requesting graceful close for {p.ProcessName} (PID: {p.Id})", LogSeverity.Info, "BackgroundManager");
                        bool closed = p.CloseMainWindow();
                        if (!closed)
                        {
                            // Could not gracefully close. We won't force kill unless explicitly allowed.
                            _logger.Log($"Could not gracefully close {p.ProcessName}. Skipping force kill.", LogSeverity.Warning, "BackgroundManager");
                            allSuccess = false;
                        }
                        else
                        {
                            p.WaitForExit(3000);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"Error closing {p.ProcessName}: {ex.Message}", LogSeverity.Error, "BackgroundManager");
                        allSuccess = false;
                    }
                }
            }
        });
        return allSuccess;
    }
}
