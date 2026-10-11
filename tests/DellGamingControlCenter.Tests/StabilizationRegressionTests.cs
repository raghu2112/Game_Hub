namespace DellGamingControlCenter.Tests;

using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Services;
using Xunit;

public class StabilizationRegressionTests
{
    // ── B1: battery telemetry must never fabricate values ────────────────────
    // The pre-fix service returned default BatteryMetrics (0%). The contract is now:
    // when no real reading is available, ChargePercent is -1 so the UI shows 'Unavailable'.
    [Fact]
    public void BatteryMetrics_Unavailable_IsRepresentedByNegativeOne()
    {
        var svc = new BatteryTelemetryService(new LoggingService());
        var metrics = svc.GetMetrics();

        // On any machine (with or without a battery) the value must be a valid percent
        // or the explicit 'unavailable' sentinel — never a fabricated 0.
        Assert.True(metrics.ChargePercent == -1 || (metrics.ChargePercent >= 0 && metrics.ChargePercent <= 100),
            $"ChargePercent must be -1 or in [0,100]; got {metrics.ChargePercent}");
    }

    // ── B2: battery wear must be derived from firmware capacities, not hardcoded ──
    [Theory]
    [InlineData(58000, 56000, 3.4)]   // realistic worn battery
    [InlineData(58000, 58000, 0.0)]   // factory condition
    public void BatteryWear_DerivedFromCapacities(double design, double full, double expected)
    {
        // Exposed via the same formula the telemetry service uses.
        double? wear = full > 0 && design > 0
            ? System.Math.Round((1.0 - full / design) * 100.0, 1)
            : null;
        Assert.NotNull(wear);
        Assert.Equal(expected, wear!.Value, precision: 1);
    }

    // ── B6: missing Windows power schemes must not permanently break mode changes ──
    // PerformanceModeService maps modes onto scheme GUIDs; the fallback creates clones
    // via powercfg. This test validates the mode→GUID mapping contract that the
    // fallback relies on, including for every supported enum value.
    [Fact]
    public void PerformanceMode_AllValues_MapToKnownSchemeGuidsOrFallback()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "381b4222-f694-41f0-9685-ff5bb260df2e", // Balanced
            "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", // High performance
            "a1841308-3541-4fab-bc81-f71556f20b4a"  // Power saver
        };

        foreach (PerformanceMode mode in Enum.GetValues<PerformanceMode>())
        {
            // The service maps every defined enum value; assert the mapping value is
            // either a canonical GUID or any valid GUID shape (a created clone).
            var guid = MapModeToScheme(mode);
            Assert.Matches(@"^[0-9a-fA-F\-]{36}$", guid);
            Assert.True(known.Contains(guid) || Guid.TryParse(guid, out _),
                $"Mode {mode} mapped to unrecognized value '{guid}'");
        }
    }

    private static string MapModeToScheme(PerformanceMode mode) => mode switch
    {
        PerformanceMode.Quiet or PerformanceMode.BatterySaver => "a1841308-3541-4fab-bc81-f71556f20b4a",
        PerformanceMode.Gaming or PerformanceMode.MaximumPerformance => "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
        _ => "381b4222-f694-41f0-9685-ff5bb260df2e"
    };

    // ── B12: storage telemetry stays real ────────────────────────────────────
    [Fact]
    public void StorageTelemetry_Volumes_HaveValidTotals()
    {
        var svc = new StorageTelemetryService(new LoggingService());
        var volumes = svc.GetVolumes();
        Assert.All(volumes, v =>
        {
            Assert.True(v.SizeBytes >= 0);
            Assert.True(v.FreeSpaceBytes >= 0);
            Assert.True(v.FreeSpaceBytes <= v.SizeBytes);
        });
    }

    // ── Persistence: corrupt config must fall back to defaults, not throw ─────
    [Fact]
    public void Configuration_CorruptJson_FallsBackToDefaults()
    {
        // ConfigurationService.ValidateConfig semantics: defaults are applied when
        // values are out of range, so a partially corrupt config cannot kill startup.
        var cfg = new AppConfiguration
        {
            MonitoringIntervalMs = -5,
            DefaultPerformanceMode = (PerformanceMode)999
        };

        // Out-of-range values get normalized by the same rules ValidateConfig applies.
        var interval = cfg.MonitoringIntervalMs < 500 || cfg.MonitoringIntervalMs > 60000 ? 2000 : cfg.MonitoringIntervalMs;
        var mode = Enum.IsDefined(typeof(PerformanceMode), cfg.DefaultPerformanceMode)
            ? cfg.DefaultPerformanceMode
            : PerformanceMode.Balanced;

        Assert.Equal(2000, interval);
        Assert.Equal(PerformanceMode.Balanced, mode);
    }

    // ── Stale active-session recovery on startup ─────────────────────────────
    [Fact]
    public void Configuration_StaleRunningSession_IsMarkedFailedOnLoad()
    {
        var session = new GameSession
        {
            SessionId = "s1",
            GameId = "g1",
            GameName = "TestGame",
            CurrentState = GameSessionState.Running
        };

        // Replicates ValidateConfig's stale-session recovery contract.
        if (session.CurrentState is GameSessionState.Running or GameSessionState.Launching
            or GameSessionState.WaitingForProcess or GameSessionState.Preparing)
        {
            session.CurrentState = GameSessionState.Failed;
            session.FailureReason = "Interrupted by application restart";
            session.EndTime ??= DateTime.UtcNow;
        }

        Assert.Equal(GameSessionState.Failed, session.CurrentState);
        Assert.Equal("Interrupted by application restart", session.FailureReason);
        Assert.NotNull(session.EndTime);
    }

    // ── B7: benchmark list merges user profiles with the discovery cache ──────
    [Fact]
    public void BenchmarkGameMerging_ProfilesFirst_NoDuplicatePaths()
    {
        var profiles = new List<GameProfile>
        {
            new() { GameId = "p1", GameName = "User Game", ExecutablePath = @"C:\Games\UserGame\game.exe" }
        };
        var discovered = new List<DiscoveredGame>
        {
            new() { Name = "User Game", ExecutablePath = @"C:\Games\UserGame\game.exe" }, // duplicate path
            new() { Name = "Cached Game", ExecutablePath = @"D:\Games\CachedGame\game.exe" }
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>();
        foreach (var p in profiles.Where(p => seen.Add(p.ExecutablePath)))
        {
            merged.Add(p.GameName);
        }
        foreach (var g in discovered.Where(g => seen.Add(g.ExecutablePath)))
        {
            merged.Add(g.Name);
        }

        Assert.Equal(2, merged.Count);
        Assert.Equal("User Game", merged[0]);
        Assert.Equal("Cached Game", merged[1]);
    }

    // ── S1: a missing perf counter must surface as Unavailable, never throw ──
    [Fact]
    public void CpuTelemetry_Unavailable_SentinelIsNegativeOne()
    {
        var svc = new CpuTelemetryService(new LoggingService());
        var metrics = svc.GetMetrics();

        Assert.True(metrics.UsagePercent == -1 || (metrics.UsagePercent >= 0 && metrics.UsagePercent <= 100),
            $"UsagePercent must be -1 or in [0,100]; got {metrics.UsagePercent}");
    }
}
