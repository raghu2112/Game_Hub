namespace DellGamingControlCenter.Services;

using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;
using System.Linq;

public class OptimizationEngine : IOptimizationEngine
{
    private readonly ILoggingService _logger;

    public OptimizationEngine(ILoggingService logger)
    {
        _logger = logger;
    }

    public OptimizationPlan GeneratePlan(GameProfile profile)
    {
        var plan = new OptimizationPlan
        {
            GameId = profile.GameId,
            GameName = profile.GameName
        };

        // 1. GPU Preference
        if (profile.PreferredGpu != GpuPreference.Auto)
        {
            plan.Actions.Add(new OptimizationAction
            {
                Type = "GpuPreference",
                Target = profile.ExecutablePath,
                Value = profile.PreferredGpu == GpuPreference.HighPerformance ? "HighPerformance" : "PowerSaving",
                Description = $"Set GPU preference for {profile.GameName} to {profile.PreferredGpu}",
                EstimatedBenefit = "Ensures the game runs on the discrete GPU for maximum performance.",
                RiskLevel = "Low"
            });
        }

        // 2. Process Priority
        if (profile.ProcessPriority != ProcessPriorityClass.Normal)
        {
            plan.Actions.Add(new OptimizationAction
            {
                Type = "ProcessPriority",
                Target = profile.ExecutablePath,
                Value = profile.ProcessPriority.ToString(),
                Description = $"Set process priority to {profile.ProcessPriority} upon launch.",
                EstimatedBenefit = "Gives the game scheduling preference over background tasks.",
                RiskLevel = profile.ProcessPriority == ProcessPriorityClass.AboveNormal ? "Low" : "Medium"
            });
        }

        // 3. Game Mode
        if (profile.EnableGameMode)
        {
            plan.Actions.Add(new OptimizationAction
            {
                Type = "Registry",
                Target = @"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled",
                Value = "1",
                Description = "Enable Windows Game Mode.",
                EstimatedBenefit = "Prioritizes CPU and GPU resources for games.",
                RiskLevel = "Low"
            });
        }

        _logger.Log($"Generated optimization plan for {profile.GameName} with {plan.Actions.Count} actions.", LogSeverity.Info, "Optimization");
        return plan;
    }

    public async Task<bool> ApplyPlanAsync(OptimizationPlan plan)
    {
        _logger.Log($"Applying optimization plan {plan.PlanId} for {plan.GameName}...", LogSeverity.Info, "Optimization");
        bool allSuccess = true;

        foreach (var action in plan.Actions)
        {
            action.Status = OptimizationStatus.Pending;
            try
            {
                if (action.Type == "GpuPreference")
                {
                    // Backup existing
                    action.RevertValue = GetGpuPreference(action.Target);
                    // Apply
                    SetGpuPreference(action.Target, action.Value);
                    // Verify
                    var verified = GetGpuPreference(action.Target);
                    if (verified == action.Value)
                    {
                        action.Status = OptimizationStatus.Verified;
                        _logger.Log($"Verified GPU preference for {plan.GameName}.", LogSeverity.Success, "Optimization");
                    }
                    else
                    {
                        action.Status = OptimizationStatus.Failed;
                        allSuccess = false;
                        _logger.Log($"Failed to verify GPU preference for {plan.GameName}. Expected: {action.Value}, Got: {verified}", LogSeverity.Error, "Optimization");
                    }
                }
                else if (action.Type == "Registry")
                {
                    // Read current for backup
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar", true);
                    if (key != null)
                    {
                        action.RevertValue = key.GetValue("AutoGameModeEnabled")?.ToString() ?? "Unknown";
                        key.SetValue("AutoGameModeEnabled", int.Parse(action.Value), RegistryValueKind.DWord);
                        
                        var verified = key.GetValue("AutoGameModeEnabled")?.ToString();
                        if (verified == action.Value)
                        {
                            action.Status = OptimizationStatus.Verified;
                            _logger.Log($"Verified Registry change for {action.Target}.", LogSeverity.Success, "Optimization");
                        }
                        else
                        {
                            action.Status = OptimizationStatus.Failed;
                            allSuccess = false;
                        }
                    }
                    else
                    {
                        action.Status = OptimizationStatus.Failed;
                        allSuccess = false;
                    }
                }
                else if (action.Type == "ProcessPriority")
                {
                    // For Process Priority, we can't apply it until the process starts. 
                    // So we just mark it as "Applied" for the game launcher to pick up later.
                    action.RevertValue = "Normal";
                    action.Status = OptimizationStatus.Applied;
                }
                else
                {
                    action.Status = OptimizationStatus.Failed;
                    allSuccess = false;
                }
            }
            catch (Exception ex)
            {
                action.Status = OptimizationStatus.Failed;
                allSuccess = false;
                _logger.Log($"Error applying action {action.Type} on {action.Target}: {ex.Message}", LogSeverity.Error, "Optimization");
            }
            
            // Fail fast: rollback if any action fails
            if (!allSuccess)
            {
                _logger.Log($"Optimization failed at action {action.Type}. Initiating rollback.", LogSeverity.Error, "Optimization");
                await RollbackPlanAsync(plan);
                return false;
            }
        }

        _logger.Log($"Optimization plan {plan.PlanId} successfully applied.", LogSeverity.Success, "Optimization");
        return true;
    }

    public async Task<bool> RollbackPlanAsync(OptimizationPlan plan)
    {
        _logger.Log($"Rolling back optimization plan {plan.PlanId}...", LogSeverity.Warning, "Optimization");
        bool allSuccess = true;

        // Rollback in reverse order
        foreach (var action in plan.Actions.AsEnumerable().Reverse())
        {
            if (action.Status != OptimizationStatus.Verified && action.Status != OptimizationStatus.Applied)
            {
                continue; // Only rollback things that were successfully changed
            }

            try
            {
                if (action.Type == "GpuPreference")
                {
                    if (string.IsNullOrEmpty(action.RevertValue) || action.RevertValue == "Unknown")
                    {
                        RemoveGpuPreference(action.Target);
                    }
                    else
                    {
                        SetGpuPreference(action.Target, action.RevertValue);
                    }
                    action.Status = OptimizationStatus.RolledBack;
                }
                else if (action.Type == "Registry")
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar", true);
                    if (key != null && action.RevertValue != "Unknown")
                    {
                        key.SetValue("AutoGameModeEnabled", int.Parse(action.RevertValue!), RegistryValueKind.DWord);
                    }
                    action.Status = OptimizationStatus.RolledBack;
                }
            }
            catch (Exception ex)
            {
                allSuccess = false;
                _logger.Log($"Error rolling back action {action.Type} on {action.Target}: {ex.Message}", LogSeverity.Error, "Optimization");
            }
        }
        
        return allSuccess;
    }

    private string GetGpuPreference(string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
            if (key != null)
            {
                var val = key.GetValue(executablePath)?.ToString();
                if (!string.IsNullOrEmpty(val))
                {
                    if (val.Contains("GpuPreference=2")) return "HighPerformance";
                    if (val.Contains("GpuPreference=1")) return "PowerSaving";
                }
            }
        }
        catch { }
        return "Unknown";
    }

    private void SetGpuPreference(string executablePath, string preference)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
        string val = preference == "HighPerformance" ? "GpuPreference=2;" : "GpuPreference=1;";
        key.SetValue(executablePath, val, RegistryValueKind.String);
    }
    
    private void RemoveGpuPreference(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", true);
        key?.DeleteValue(executablePath, false);
    }
}
