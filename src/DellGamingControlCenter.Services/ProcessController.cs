namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

public class ProcessController : IProcessController
{
    private readonly ILoggingService _logger;

    public ProcessController(ILoggingService logger)
    {
        _logger = logger;
    }

    public List<ProcessInfo> GetProcesses()
    {
        var list = new List<ProcessInfo>();
        try
        {
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    list.Add(new ProcessInfo
                    {
                        ProcessId = p.Id,
                        Name = p.ProcessName,
                        WorkingSetBytes = p.WorkingSet64,
                        Priority = MapPriority(p.PriorityClass)
                    });
                }
                catch
                {
                    // Ignore access denied for some processes
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Failed to get processes: {ex.Message}", LogSeverity.Error, "Hardware");
        }
        return list;
    }

    public bool SetPriority(int processId, Core.Enums.ProcessPriorityClass priority)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            process.PriorityClass = priority switch
            {
                Core.Enums.ProcessPriorityClass.Normal => System.Diagnostics.ProcessPriorityClass.Normal,
                Core.Enums.ProcessPriorityClass.BelowNormal => System.Diagnostics.ProcessPriorityClass.BelowNormal,
                Core.Enums.ProcessPriorityClass.AboveNormal => System.Diagnostics.ProcessPriorityClass.AboveNormal,
                _ => System.Diagnostics.ProcessPriorityClass.Normal
            };
            _logger.Log($"Set priority for PID {processId} to {priority}", LogSeverity.Info, "Hardware");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Log($"Failed to set priority for PID {processId}: {ex.Message}", LogSeverity.Warning, "Hardware");
            return false;
        }
    }

    public Core.Enums.ProcessPriorityClass GetPriority(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return MapPriority(process.PriorityClass);
        }
        catch
        {
            return Core.Enums.ProcessPriorityClass.Normal;
        }
    }

    private Core.Enums.ProcessPriorityClass MapPriority(System.Diagnostics.ProcessPriorityClass priority)
    {
        return priority switch
        {
            System.Diagnostics.ProcessPriorityClass.BelowNormal => Core.Enums.ProcessPriorityClass.BelowNormal,
            System.Diagnostics.ProcessPriorityClass.AboveNormal => Core.Enums.ProcessPriorityClass.AboveNormal,
            _ => Core.Enums.ProcessPriorityClass.Normal
        };
    }
}
