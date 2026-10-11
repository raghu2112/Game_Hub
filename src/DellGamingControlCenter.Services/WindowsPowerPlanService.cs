namespace DellGamingControlCenter.Services;

using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Enums;

public class WindowsPowerPlanService : IWindowsPowerPlanService, IPowerPlanController
{
    private readonly ILoggingService _logger;
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public WindowsPowerPlanService(ILoggingService logger)
    {
        _logger = logger;
    }

    [System.Runtime.InteropServices.DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public string GetActivePowerSchemeGuid()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr activePolicyGuidPtr) == 0)
            {
                var guid = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(activePolicyGuidPtr);
                LocalFree(activePolicyGuidPtr);
                return guid.ToString().ToLowerInvariant();
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Error reading active power scheme: {ex.Message}", LogSeverity.Error, "PowerPlan");
        }
        return BalancedGuid;
    }

    public async Task<bool> SetPowerSchemeAsync(string guid)
    {
        // Validation: Ensure GUID format is valid to prevent command injection
        if (!Regex.IsMatch(guid, @"^[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}$"))
        {
            _logger.Log($"Invalid power scheme GUID format: {guid}", LogSeverity.Error, "PowerPlan");
            return false;
        }

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
            
            string active = GetActivePowerSchemeGuid();
            return active.Equals(guid, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.Log($"Error setting power scheme {guid}: {ex.Message}", LogSeverity.Error, "PowerPlan");
            return false;
        }
    }
}
