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
/// Service-level verification of the visible mode -> Windows power-scheme mapping, driven
/// through the REAL <see cref="PerformanceModeService"/> against a scripted
/// IWindowsPowerPlanService fake.
///
/// What this proves: the mode the user clicks maps to the scheme GUID the service actually
/// hands to Windows, and the mode label is derived from the scheme Windows reports back.
///
/// What this does NOT prove: that Windows accepted the change. The fake never shells out,
/// so no system state is read or modified here. The B6 `powercfg /duplicatescheme` fallback
/// inside PerformanceModeService deliberately shells out to a real process and is therefore
/// NOT exercised by this class (it would create a real power scheme); that path is covered by
/// the prior authorized live run recorded in HARDWARE_TUNING_VERIFICATION.md.
/// </summary>
public class PerformanceModeMappingTests
{
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";

    /// <summary>Scripted power-plan backend: activating a GUID makes it the active one.</summary>
    private sealed class ScriptedPowerPlan : IWindowsPowerPlanService
    {
        public string Active { get; set; } = BalancedGuid;
        public readonly List<string> Requests = new();
        public string GetActivePowerSchemeGuid() => Active;
        public Task<bool> SetPowerSchemeAsync(string guid)
        {
            Requests.Add(guid);
            Active = guid.ToLowerInvariant();
            return Task.FromResult(true);
        }
    }

    private sealed class FakeLoggingService : ILoggingService
    {
        public void Log(string message, LogSeverity severity = LogSeverity.Info, string category = "General", string? detail = null) { }
        public IEnumerable<ActivityLogEntry> GetRecentActivity(int maxEntries = 100) => new List<ActivityLogEntry>();
        public void ClearLog() { }
    }

    // Every mode card the Performance page actually renders, paired with the scheme the
    // service must hand to Windows for it, and an initial scheme that differs so the switch
    // is genuinely exercised. initialScheme deliberately never equals expectedGuid.
    public static IEnumerable<object[]> VisibleModeToScheme()
    {
        yield return new object[] { PerformanceMode.Quiet, PowerSaverGuid, BalancedGuid };
        yield return new object[] { PerformanceMode.BatterySaver, PowerSaverGuid, BalancedGuid };
        yield return new object[] { PerformanceMode.Balanced, BalancedGuid, PowerSaverGuid };
        yield return new object[] { PerformanceMode.Gaming, HighPerfGuid, BalancedGuid };
        yield return new object[] { PerformanceMode.MaximumPerformance, HighPerfGuid, PowerSaverGuid };
    }

    /// <summary>
    /// Windows exposes only three power schemes for five UI modes, so several cards share one
    /// GUID (Quiet/BatterySaver, Gaming/MaximumPerformance). The service therefore cannot name
    /// the alias that was requested — it must report a mode that is consistent with the scheme
    /// Windows actually runs. This is documented in HARDWARE_TUNING_VERIFICATION.md §5.1, not
    /// a defect; the assertion below locks the scheme, which is the part Windows honours.
    /// </summary>
    private static IEnumerable<PerformanceMode> ModesSharingScheme(string guid) =>
        guid.ToLowerInvariant() switch
        {
            var g when g == PowerSaverGuid => new[] { PerformanceMode.Quiet, PerformanceMode.BatterySaver },
            var g when g == HighPerfGuid => new[] { PerformanceMode.Gaming, PerformanceMode.MaximumPerformance },
            _ => new[] { PerformanceMode.Balanced }
        };

    [Theory]
    [MemberData(nameof(VisibleModeToScheme))]
    public async Task EachVisibleMode_RequestsTheCorrectSchemeGuid(
        PerformanceMode mode, string expectedGuid, string initialGuid)
    {
        var power = new ScriptedPowerPlan { Active = initialGuid };
        var svc = new PerformanceModeService(new FakeLoggingService(), power);

        Assert.True(await svc.ApplyModeAsync(mode));

        Assert.Equal(expectedGuid.ToLowerInvariant(), power.Active);
        Assert.Equal(new[] { expectedGuid.ToLowerInvariant() }, power.Requests);

        // The label must name a mode that runs this scheme — never a mode that does not.
        Assert.Contains(svc.GetCurrentMode(), ModesSharingScheme(expectedGuid));
    }

    [Theory]
    [InlineData(PerformanceMode.Quiet)]
    [InlineData(PerformanceMode.BatterySaver)]
    [InlineData(PerformanceMode.Balanced)]
    [InlineData(PerformanceMode.Gaming)]
    [InlineData(PerformanceMode.MaximumPerformance)]
    public async Task FirstApply_SnapshotsTheOriginalScheme_ForLaterRestore(PerformanceMode mode)
    {
        var power = new ScriptedPowerPlan { Active = PowerSaverGuid };
        var svc = new PerformanceModeService(new FakeLoggingService(), power);

        Assert.True(await svc.ApplyModeAsync(mode));
        Assert.True(await svc.RestorePreviousModeAsync());

        // Restore must return Windows to the pre-existing scheme, and the label must be
        // derived from what was actually restored.
        Assert.Equal(PowerSaverGuid.ToLowerInvariant(), power.Active);
        Assert.Equal(PerformanceMode.BatterySaver, svc.GetCurrentMode());
    }

    [Fact]
    public async Task ReApplyingTheAlreadyActiveScheme_IsANoOpOnTheBackend()
    {
        var power = new ScriptedPowerPlan { Active = HighPerfGuid };
        var svc = new PerformanceModeService(new FakeLoggingService(), power);

        Assert.True(await svc.ApplyModeAsync(PerformanceMode.Gaming));

        // Windows is already on the target scheme: the service must not issue a redundant
        // switch, and must still report the mode as active.
        Assert.Empty(power.Requests);
        Assert.Equal(PerformanceMode.Gaming, svc.GetCurrentMode());
    }

    [Fact]
    public void GetCurrentMode_DoesNotFabricate_WhenSchemePrefetchIsUnavailable()
    {
        // GetActivePowerSchemeGuid returning empty = "could not read the system". The service
        // must keep its code default rather than invent a mode Windows may not be running.
        var power = new ScriptedPowerPlan { Active = "" };
        var svc = new PerformanceModeService(new FakeLoggingService(), power);

        Assert.Equal(PerformanceMode.Balanced, svc.GetCurrentMode());
    }
}
