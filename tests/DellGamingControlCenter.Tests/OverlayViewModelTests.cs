namespace DellGamingControlCenter.Tests;

using DellGamingControlCenter.App.ViewModels;
using DellGamingControlCenter.Core.Models;
using Xunit;

/// <summary>
/// Overlay honesty regressions.
///
/// The overlay window itself is a transparent, click-through topmost WPF desktop window —
/// not an injected DirectX/Vulkan surface — and is not reachable from navigation, so its
/// window behaviour is covered by <see cref="WpfSurfaceRuntimeTests"/>. What can be verified
/// deterministically is the data contract: the overlay must never invent a value for a
/// reading the machine did not provide, and must surface real readings when it has them.
/// </summary>
public class OverlayViewModelTests
{
    private static GameSession Session() => new()
    {
        SessionId = "s1",
        GameId = "g1",
        GameName = "Test Game"
    };

    [Fact]
    public void UnavailableTelemetry_RendersAsNa_NeverAsMinusOneOrZero()
    {
        var vm = new OverlayViewModel();

        // CPU -1 = no counter reading; GPU null = no usable counter; RAM total 0 = unknown.
        vm.UpdateFromSnapshot(Session(), new DiagnosticSnapshot
        {
            CpuMetrics = new CpuMetrics { UsagePercent = -1 },
            GpuMetrics = new GpuMetrics(),
            RamMetrics = new RamMetrics()
        }, fps: null);

        Assert.Equal("Test Game", vm.GameName);
        Assert.Equal(OverlayViewModel.UnavailableText, vm.CpuText);
        Assert.Equal(OverlayViewModel.UnavailableText, vm.GpuText);
        Assert.Equal(OverlayViewModel.UnavailableText, vm.RamText);
        Assert.Equal(OverlayViewModel.UnavailableText, vm.TempText);
        Assert.Equal(OverlayViewModel.UnavailableText, vm.FpsText);

        Assert.DoesNotContain("-", vm.CpuText);
        Assert.DoesNotContain("0%", vm.GpuText);
        Assert.DoesNotContain("0.0", vm.RamText);
    }

    [Fact]
    public void AvailableTelemetry_SurfacesTheRealReadings()
    {
        var vm = new OverlayViewModel();

        vm.UpdateFromSnapshot(Session(), new DiagnosticSnapshot
        {
            CpuMetrics = new CpuMetrics
            {
                UsagePercent = 42.4,
                TemperatureCelsius = 61.0
            },
            GpuMetrics = new GpuMetrics { UsagePercent = 87.0 },
            RamMetrics = new RamMetrics
            {
                TotalBytes = 16L * 1024 * 1024 * 1024,
                UsedBytes = 8L * 1024 * 1024 * 1024
            }
        }, fps: new FpsSample { Fps = 144 });

        Assert.Equal("42%", vm.CpuText);
        Assert.Equal("87%", vm.GpuText);
        Assert.Equal("8.0 GB", vm.RamText);
        Assert.Equal("61°C", vm.TempText);
        Assert.Equal("144", vm.FpsText);
    }

    [Fact]
    public void MissingFps_ClearsTheStaleValue_NeverLeavesTheLastKnownNumber()
    {
        var vm = new OverlayViewModel();
        var snapshot = new DiagnosticSnapshot
        {
            CpuMetrics = new CpuMetrics { UsagePercent = 30 },
            RamMetrics = new RamMetrics { TotalBytes = 1024, UsedBytes = 512 }
        };

        vm.UpdateFromSnapshot(Session(), snapshot, fps: new FpsSample { Fps = 200 });
        Assert.Equal("200", vm.FpsText);

        // The provider stops reporting: a stale FPS must not be left on screen.
        vm.UpdateFromSnapshot(Session(), snapshot, fps: null);
        Assert.Equal(OverlayViewModel.UnavailableText, vm.FpsText);
    }

    [Fact]
    public void Temperature_FallsBackToTheGpuSensor_WhenTheCpuHasNone()
    {
        var vm = new OverlayViewModel();

        vm.UpdateFromSnapshot(Session(), new DiagnosticSnapshot
        {
            CpuMetrics = new CpuMetrics { UsagePercent = 10 },
            GpuMetrics = new GpuMetrics { UsagePercent = 20, TemperatureCelsius = 74.6 }
        }, fps: null);

        Assert.Equal("75°C", vm.TempText);
    }
}
