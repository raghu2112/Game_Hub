namespace DellGamingControlCenter.Services;

using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;

public class PerformanceModeService : IPerformanceModeService
{
    private readonly ILoggingService _logger;
    private string? _previousSchemeGuid;
    private PerformanceMode _currentMode = PerformanceMode.Balanced;

    // Standard Windows Power Scheme GUIDs
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";

    public PerformanceModeService(ILoggingService logger)
    {
        _logger = logger;
    }

    public PerformanceMode GetCurrentMode() => _currentMode;

    public async Task<bool> ApplyModeAsync(PerformanceMode mode)
    {
        _logger.Log($"Applying performance mode: {mode}", LogSeverity.Info, "Performance");
        
        string activeGuid = GetActivePowerSchemeGuid();
        if (_previousSchemeGuid == null)
        {
            _previousSchemeGuid = activeGuid;
        }

        string targetGuid = mode switch
        {
            PerformanceMode.Quiet => PowerSaverGuid,
            PerformanceMode.BatterySaver => PowerSaverGuid,
            PerformanceMode.Balanced => BalancedGuid,
            PerformanceMode.Gaming => HighPerformanceGuid,
            PerformanceMode.MaximumPerformance => HighPerformanceGuid,
            _ => BalancedGuid
        };

        if (activeGuid == targetGuid)
        {
            _currentMode = mode;
            return true;
        }

        bool success = await SetPowerSchemeAsync(targetGuid);
        if (success)
        {
            _currentMode = mode;
            _logger.Log($"Performance mode applied: {mode}", LogSeverity.Success, "Performance");
            return true;
        }
        else
        {
            _logger.Log($"Failed to apply performance mode {mode}. Restoring previous.", LogSeverity.Warning, "Performance");
            await RestorePreviousModeAsync();
            return false;
        }
    }

    public async Task<bool> RestorePreviousModeAsync()
    {
        if (string.IsNullOrEmpty(_previousSchemeGuid))
            return true;

        _logger.Log($"Restoring previous power scheme ({_previousSchemeGuid})...", LogSeverity.Info, "Performance");
        
        bool success = await SetPowerSchemeAsync(_previousSchemeGuid);
        if (success)
        {
            _previousSchemeGuid = null;
            _currentMode = PerformanceMode.Balanced; // Reset to default state representation
            _logger.Log("Previous power scheme restored.", LogSeverity.Success, "Performance");
            return true;
        }
        
        _logger.Log("Failed to restore previous power scheme.", LogSeverity.Error, "Performance");
        return false;
    }

    private string GetActivePowerSchemeGuid()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/getactivescheme",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Example output: Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)
            var match = Regex.Match(output, @"([0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12})");
            if (match.Success)
            {
                return match.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Error reading active power scheme: {ex.Message}", LogSeverity.Error, "Performance");
        }
        return BalancedGuid; // Fallback
    }

    private async Task<bool> SetPowerSchemeAsync(string guid)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = $"/setactive {guid}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            await process.WaitForExitAsync();
            
            // Verify
            string active = GetActivePowerSchemeGuid();
            return active.Equals(guid, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.Log($"Error setting power scheme {guid}: {ex.Message}", LogSeverity.Error, "Performance");
            return false;
        }
    }
}
