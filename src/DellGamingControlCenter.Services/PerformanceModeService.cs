namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;

public class PerformanceModeService : IPerformanceModeService
{
    private readonly ILoggingService _logger;
    private readonly IWindowsPowerPlanService _powerPlanService;
    private string? _previousSchemeGuid;
    private PerformanceMode _currentMode = PerformanceMode.Balanced;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    // B6: schemes we created ourselves, remembered per mode so restore stays deterministic.
    private readonly Dictionary<PerformanceMode, string> _customSchemeByMode = new();
    private bool _modeSyncedFromSystem;

    // Standard Windows Power Scheme GUIDs
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";

    public PerformanceModeService(ILoggingService logger, IWindowsPowerPlanService powerPlanService)
    {
        _logger = logger;
        _powerPlanService = powerPlanService;
    }

    public PerformanceMode GetCurrentMode()
    {
        if (!_modeSyncedFromSystem)
        {
            // First call after launch: reflect the REAL active scheme, not the assumed
            // default. Otherwise the UI shows 'Balanced' after a restart even when the
            // previous run left High performance active.
            _modeSyncedFromSystem = true;
            var active = _powerPlanService.GetActivePowerSchemeGuid();
            if (string.IsNullOrEmpty(active))
            {
                // Scheme prefetch unavailable (or mocked): keep the code default.
                return _currentMode;
            }
            _currentMode = active switch
            {
                var g when g.Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) => PerformanceMode.Gaming,
                var g when g.Equals(PowerSaverGuid, StringComparison.OrdinalIgnoreCase) => PerformanceMode.BatterySaver,
                var g when g.Equals(BalancedGuid, StringComparison.OrdinalIgnoreCase) => PerformanceMode.Balanced,
                _ => ResolveCustomSchemeMode(active)
            };
        }
        return _currentMode;
    }

    private PerformanceMode ResolveCustomSchemeMode(string guid)
    {
        foreach (var kvp in _customSchemeByMode)
        {
            if (kvp.Value.Equals(guid, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Key;
            }
        }
        // Unknown/custom scheme (e.g. created by duplicatescheme in a previous run):
        // treat as Balanced-equivalent rather than pretending a mode is active.
        return PerformanceMode.Balanced;
    }

    public async Task<bool> ApplyModeAsync(PerformanceMode mode)
    {
        await _semaphore.WaitAsync();
        try
        {
            _logger.Log($"Applying performance mode: {mode}", LogSeverity.Info, "Performance");
            
            string activeGuid = _powerPlanService.GetActivePowerSchemeGuid();
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

            // Prefer a scheme we successfully created earlier for this mode (B6).
            if (_customSchemeByMode.TryGetValue(mode, out var customGuid))
            {
                targetGuid = customGuid;
            }

            if (activeGuid == targetGuid)
            {
                _currentMode = mode;
                return true;
            }

            bool success = await _powerPlanService.SetPowerSchemeAsync(targetGuid);
            if (!success)
            {
                // B6: Windows 11 laptops frequently lack the legacy 'High performance' and
                // 'Power saver' schemes. Create a duplicate from the active scheme once and retry.
                _logger.Log($"Power scheme {targetGuid} not present; attempting to create a duplicate scheme...", LogSeverity.Info, "Performance");
                string? createdGuid = TryCreateSchemeFrom(targetGuid, mode);
                if (createdGuid != null)
                {
                    success = await _powerPlanService.SetPowerSchemeAsync(createdGuid);
                }
            }

            if (success)
            {
                _currentMode = mode;
                _logger.Log($"Performance mode applied: {mode}", LogSeverity.Success, "Performance");
                return true;
            }
            else
            {
                _logger.Log($"Failed to apply performance mode {mode}. Restoring previous.", LogSeverity.Warning, "Performance");
                await RestorePreviousModeInternalAsync();
                return false;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> RestorePreviousModeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            return await RestorePreviousModeInternalAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }        // B6 helper: 'powercfg /duplicatescheme <sourceGuid> <newGuid>' clones the source
        // scheme under a fresh GUID so this laptop's missing default schemes still become
        // selectable. The GUID is remembered per mode so later selects/restores are stable.
        private string? TryCreateSchemeFrom(string fallbackTargetGuid, PerformanceMode mode)
        {
            var source = _powerPlanService.GetActivePowerSchemeGuid();
            var newGuid = Guid.NewGuid().ToString();
            try
            {
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powercfg",
                        Arguments = $"/duplicatescheme {source} {newGuid}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit(15000);
                if (process.ExitCode == 0)
                {
                    _logger.Log($"Created power scheme {newGuid} for mode {mode} (cloned from {source}).", LogSeverity.Success, "Performance");
                    _customSchemeByMode[mode] = newGuid;
                    return newGuid;
                }
                _logger.Log($"powercfg duplicatescheme failed with exit code {process.ExitCode}.", LogSeverity.Warning, "Performance");
            }
            catch (Exception ex)
            {
                _logger.Log($"Failed to duplicate power scheme: {ex.Message}", LogSeverity.Error, "Performance");
            }
            return null;
        }

        private async Task<bool> RestorePreviousModeInternalAsync()
    {
        if (string.IsNullOrEmpty(_previousSchemeGuid))
            return true;

        _logger.Log($"Restoring previous power scheme ({_previousSchemeGuid})...", LogSeverity.Info, "Performance");
        
        bool success = await _powerPlanService.SetPowerSchemeAsync(_previousSchemeGuid);
        if (success)
        {
            // R7-B: derive the mode label from the scheme Windows actually restored,
            // not from a hardcoded default. Power saver restores to BatterySaver,
            // not Balanced — the UI must not claim a mode Windows is not running.
            var restoredGuid = _powerPlanService.GetActivePowerSchemeGuid();
            _currentMode = restoredGuid switch
            {
                var g when g.Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) => PerformanceMode.Gaming,
                var g when g.Equals(PowerSaverGuid, StringComparison.OrdinalIgnoreCase) => PerformanceMode.BatterySaver,
                var g when g.Equals(BalancedGuid, StringComparison.OrdinalIgnoreCase) => PerformanceMode.Balanced,
                _ => ResolveCustomSchemeMode(restoredGuid)
            };
            _previousSchemeGuid = null;
            _logger.Log($"Previous power scheme restored (label: {_currentMode}).", LogSeverity.Success, "Performance");
            return true;
        }
        
        _logger.Log("Failed to restore previous power scheme.", LogSeverity.Error, "Performance");
        return false;
    }
}
