namespace DellGamingControlCenter.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Services;
using Xunit;

/// <summary>
/// Round 7 hardware-control truth regressions. Two confirmed defects found by the live
/// probe (tmp/r7/hwprobe) are guarded here:
///   R7-A: GpuPreferenceController.SetPreference wrote a HKCU DirectxUserGpuPreferences
///         value for a NONEXISTENT executable and reported success — a preference Windows
///         will never honor, i.e. "accepted but not effective".
///   R7-B: PerformanceModeService.RestorePreviousModeAsync hardcoded the mode label to
///         Balanced after a restore, so the UI claimed a mode Windows was not running
///         (e.g. Power saver restores to BatterySaver, not Balanced).
/// </summary>
public class HardwareControlTruthTests
{
    private class FakeLoggingService : ILoggingService
    {
        public void Log(string message, LogSeverity severity = LogSeverity.Info, string category = "General", string? detail = null) { }
        public IEnumerable<ActivityLogEntry> GetRecentActivity(int maxEntries = 100) => new List<ActivityLogEntry>();
        public void ClearLog() { }
    }

    private class FakeAuditLogger : IControlAuditLogger
    {
        public void LogControlEvent(ControlCapability capability, ControlResult result, string details = "") { }
    }

    // ── R7-A ────────────────────────────────────────────────────────────────

    [Fact]
    public void GpuPreference_SetOnMissingExecutable_ReturnsFalse_AndWritesNothing()
    {
        var logger = new FakeLoggingService();
        var controller = new GpuPreferenceController(logger);
        string missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "r7_definitely_missing_" + Guid.NewGuid().ToString("N"), "game.exe");

        bool set = controller.SetPreference(missing, GpuPreference.HighPerformance);

        Assert.False(set);
        // No value may exist for a path Windows cannot run.
        Assert.Equal(GpuPreference.Auto, controller.GetPreference(missing));
    }

    [Fact]
    public void GpuPreference_SetOnRealExecutable_RoundTripsAndRemoves()
    {
        var logger = new FakeLoggingService();
        var controller = new GpuPreferenceController(logger);
        string real = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
        Assert.True(System.IO.File.Exists(real));

        try
        {
            Assert.True(controller.SetPreference(real, GpuPreference.HighPerformance));
            Assert.Equal(GpuPreference.HighPerformance, controller.GetPreference(real));

            Assert.True(controller.SetPreference(real, GpuPreference.Integrated));
            Assert.Equal(GpuPreference.Integrated, controller.GetPreference(real));
        }
        finally
        {
            Assert.True(controller.RemovePreference(real));
        }

        Assert.Equal(GpuPreference.Auto, controller.GetPreference(real));
    }

    [Fact]
    public async Task GpuUseCase_MissingExecutable_ReportsInvalid_NotSilentSuccess()
    {
        var logger = new FakeLoggingService();
        var controller = new GpuPreferenceController(logger);
        var useCase = new ChangeGpuPreferenceUseCase(controller, new AuthorizationService(logger, new FakeAuditLogger()));
        string missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "r7_missing_y.exe");

        var result = await useCase.ExecuteAsync(missing, GpuPreference.HighPerformance);

        Assert.False(result.IsSuccess);
        Assert.Equal(GpuPreference.Auto, controller.GetPreference(missing));
    }

    // ── R7-B ────────────────────────────────────────────────────────────────
    // A scripted IWindowsPowerPlanService: switching to a GUID "activates" it and
    // reading returns whatever was last set — enough to prove the label logic.

    private class ScriptedPowerPlan : IWindowsPowerPlanService
    {
        public string Active { get; set; } = "381b4222-f694-41f0-9685-ff5bb260df2e"; // Balanced
        public bool FailNextSet { get; set; }
        public string GetActivePowerSchemeGuid() => Active;
        public Task<bool> SetPowerSchemeAsync(string guid)
        {
            if (FailNextSet) { FailNextSet = false; return Task.FromResult(false); }
            // Mimic Windows: activating a scheme makes it the active one.
            Active = guid.ToLowerInvariant();
            return Task.FromResult(true);
        }
    }

    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";
    private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    [Fact]
    public async Task RestorePreviousMode_LabelsFromRestoredScheme_NotBalancedDefault()
    {
        var power = new ScriptedPowerPlan { Active = PowerSaverGuid };
        var svc = new PerformanceModeService(new FakeLoggingService(), power);

        // First read syncs the real scheme → BatterySaver.
        Assert.Equal(PerformanceMode.BatterySaver, svc.GetCurrentMode());

        // Move to Gaming, then restore. The restored scheme is Power saver, so the label
        // must come back as BatterySaver — NOT the old hardcoded Balanced.
        Assert.True(await svc.ApplyModeAsync(PerformanceMode.Gaming));
        Assert.Equal(PerformanceMode.Gaming, svc.GetCurrentMode());

        Assert.True(await svc.RestorePreviousModeAsync());
        Assert.Equal(PowerSaverGuid, power.Active);            // Windows state
        Assert.Equal(PerformanceMode.BatterySaver, svc.GetCurrentMode()); // UI label agrees
    }

    [Fact]
    public async Task RestorePreviousMode_BalancedLabel_WhenBalancedRestored()
    {
        var power = new ScriptedPowerPlan { Active = BalancedGuid };
        var svc = new PerformanceModeService(new FakeLoggingService(), power);
        Assert.Equal(PerformanceMode.Balanced, svc.GetCurrentMode());

        Assert.True(await svc.ApplyModeAsync(PerformanceMode.Gaming));
        Assert.True(await svc.RestorePreviousModeAsync());

        Assert.Equal(PerformanceMode.Balanced, svc.GetCurrentMode());
    }

    [Fact]
    public async Task RestorePreviousMode_Failure_KeepsCurrentMode_AndReportsFalse()
    {
        var power = new ScriptedPowerPlan { Active = BalancedGuid };
        var svc = new PerformanceModeService(new FakeLoggingService(), power);
        Assert.True(await svc.ApplyModeAsync(PerformanceMode.Gaming));
        Assert.Equal(PerformanceMode.Gaming, svc.GetCurrentMode());

        // Force the restore's set to fail.
        power.FailNextSet = true;
        bool restored = await svc.RestorePreviousModeAsync();

        Assert.False(restored);
        // Mode label must not silently flip to something the system is not running.
        Assert.Equal(PerformanceMode.Gaming, svc.GetCurrentMode());
        Assert.Equal(HighPerfGuid.ToLowerInvariant(), power.Active);
    }

    [Fact]
    public async Task ApplyMode_ReflectsRealActiveScheme_NotOptimisticLabel()
    {
        var power = new ScriptedPowerPlan { Active = PowerSaverGuid };
        var svc = new PerformanceModeService(new FakeLoggingService(), power);

        Assert.True(await svc.ApplyModeAsync(PerformanceMode.Gaming));
        Assert.Equal(HighPerfGuid.ToLowerInvariant(), power.Active);
        Assert.Equal(PerformanceMode.Gaming, svc.GetCurrentMode());

        Assert.True(await svc.ApplyModeAsync(PerformanceMode.BatterySaver));
        Assert.Equal(PowerSaverGuid, power.Active);
        Assert.Equal(PerformanceMode.BatterySaver, svc.GetCurrentMode());
    }
}
