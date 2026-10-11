namespace DellGamingControlCenter.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Threading;
using DellGamingControlCenter.App.ViewModels;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using Moq;
using Xunit;

/// <summary>
/// Performance Center UI verification against SIMULATED services.
///
/// Scope statement (important): every service below is a Moq fake. Nothing here proves
/// that Windows accepted a power-plan change. OS-level behaviour is verified separately
/// by <see cref="PerformanceModeMappingTests"/> (scripted IWindowsPowerPlanService fake)
/// and by the pre-existing live evidence in HARDWARE_TUNING_VERIFICATION.md. The active
/// Windows power scheme is never read or written by this class.
///
/// Why reflection is used on the ViewModel's private handler: AsyncRelayCommand.Execute is
/// `async void`, so invoking the command cannot be awaited deterministically. Tests call the
/// production handler the command delegates to and await the real returned Task, which keeps
/// every assertion synchronous-safe while still exercising the real routing logic.
/// </summary>
public class PerformanceModeUiTests : IDisposable
{
    private const BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly Mock<IChangePerformanceModeUseCase> _useCase = new();
    private readonly Mock<IGameSessionManager> _sessions = new();
    private readonly Mock<IDiagnosticEngine> _diagnostics = new();
    private readonly Mock<IGpuPreferenceController> _gpu = new();
    private readonly Mock<IDellCapabilityDetector> _capabilities = new();
    private readonly Mock<IConfigurationService> _config = new();
    private readonly Mock<IPowerPlanController> _powerPlan = new();
    private readonly PerformanceViewModel _vm;

    /// <summary>What the (fake) backend currently reports as the active mode. Mutable so a
    /// test can model the backend actually changing state before it answers success.</summary>
    private PerformanceMode _backendMode = PerformanceMode.Balanced;

    public PerformanceModeUiTests()
    {
        SetAppStatic("Logger", new FakeLoggingService());
        SetAppStatic("GamingIntelligenceEngine", IntelligenceReturning(new List<GamingInsight>()));

        _useCase.Setup(u => u.GetCurrentMode()).Returns(() => _backendMode);
        _useCase.Setup(u => u.ExecuteAsync(It.IsAny<PerformanceMode>()))
                .ReturnsAsync(ControlResult.Success());
        _sessions.Setup(s => s.GetActiveSession()).Returns((GameSession?)null);
        _config.Setup(c => c.GetConfig()).Returns(new AppConfiguration());

        _vm = CreateViewModel();
    }

    private PerformanceViewModel CreateViewModel() => new(
        _useCase.Object,
        _sessions.Object,
        _diagnostics.Object,
        _gpu.Object,
        _capabilities.Object,
        _config.Object,
        _powerPlan.Object);

    public void Dispose()
    {
        // The VM starts a DispatcherTimer in its constructor; stop it so a test run does not
        // leave a live timer on a pooled thread.
        if (typeof(PerformanceViewModel).GetField("_timer", NonPublicInstance)?.GetValue(_vm) is DispatcherTimer timer)
        {
            timer.Stop();
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static void SetAppStatic(string property, object? value)
        => typeof(DellGamingControlCenter.App.App)
            .GetProperty(property, BindingFlags.Public | BindingFlags.Static)
            ?.SetValue(null, value);

    private static IGamingIntelligenceEngine IntelligenceReturning(List<GamingInsight> insights)
    {
        var mock = new Mock<IGamingIntelligenceEngine>();
        mock.Setup(i => i.GetSystemInsights()).Returns(insights);
        return mock.Object;
    }

    private static Task InvokeActivateAsync(PerformanceViewModel vm, object? parameter)
    {
        var handler = typeof(PerformanceViewModel)
            .GetMethod("ExecuteActivateModeAsync", NonPublicInstance);
        Assert.NotNull(handler);
        return (Task)handler!.Invoke(vm, new[] { parameter })!;
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, int ms = 5000)
    {
        var done = await Task.WhenAny(task, Task.Delay(ms));
        Assert.True(ReferenceEquals(done, task), $"operation timed out after {ms} ms");
        return await task;
    }

    private static async Task WithTimeout(Task task, int ms = 5000)
    {
        var done = await Task.WhenAny(task, Task.Delay(ms));
        Assert.True(ReferenceEquals(done, task), $"operation timed out after {ms} ms");
        await task;
    }

    // ── 1. Each visible mode maps to the correct existing enum ────────────

    [Fact]
    public void Modes_PresentExactlyTheFiveRealEnumValues_InDisplayOrder()
    {
        Assert.Equal(5, _vm.Modes.Count);

        Assert.Equal(new[]
        {
            (PerformanceMode.Quiet, "QUIET"),
            (PerformanceMode.BatterySaver, "BATTERY SAVER"),
            (PerformanceMode.Balanced, "BALANCED"),
            (PerformanceMode.Gaming, "GAMING"),
            (PerformanceMode.MaximumPerformance, "MAXIMUM PERFORMANCE")
        }, _vm.Modes.Select(m => (m.Mode, m.Title)));

        // Every card must carry a real enum value — never a UI-only placeholder.
        Assert.All(_vm.Modes, m => Assert.True(Enum.IsDefined(typeof(PerformanceMode), m.Mode)));
    }

    [Fact]
    public void ActivateModeCommand_IsBound_AndAcceptsARealModeParameter()
    {
        Assert.NotNull(_vm.ActivateModeCommand);
        Assert.IsType<AsyncRelayCommand>(_vm.ActivateModeCommand);
        Assert.True(_vm.ActivateModeCommand.CanExecute(PerformanceMode.Gaming));
    }

    // ── 2. Displayed active mode comes from the backend, not the request ──

    [Fact]
    public void ActiveMode_ReflectsBackendCurrentMode_NotTheRequestedOne()
    {
        _backendMode = PerformanceMode.Gaming;
        var vm = CreateViewModel();

        Assert.Equal("Gaming", vm.CurrentPowerModeString);
        Assert.Single(vm.Modes.Where(m => m.IsActive));
        Assert.Equal(PerformanceMode.Gaming, vm.Modes.Single(m => m.IsActive).Mode);
    }

    // ── 3. Loading state: no false success before the backend confirms ────

    [Fact]
    public async Task ActivateMode_InFlight_ShowsApplying_AndDoesNotClaimSuccessOrMoveHighlight()
    {
        var gate = new TaskCompletionSource<ControlResult>();
        _useCase.Setup(u => u.ExecuteAsync(It.IsAny<PerformanceMode>())).Returns(gate.Task);

        var running = InvokeActivateAsync(_vm, PerformanceMode.Gaming);

        // Backend has NOT answered yet: the UI must say it is applying, must not say
        // Applied, and must not have moved the highlight to the requested mode.
        Assert.Equal("Applying Gaming...", _vm.OperationStatus);
        Assert.DoesNotContain("Applied", _vm.OperationStatus);
        Assert.Equal(PerformanceMode.Balanced, _vm.Modes.Single(m => m.IsActive).Mode);

        // The backend now really is on Gaming and reports success.
        _backendMode = PerformanceMode.Gaming;
        gate.SetResult(ControlResult.Success());
        await WithTimeout(running);

        Assert.Equal("Applied", _vm.OperationStatus);
        Assert.Equal(PerformanceMode.Gaming, _vm.Modes.Single(m => m.IsActive).Mode);
    }

    // ── 4. Success path re-reads state after confirmation ─────────────────

    [Fact]
    public async Task ActivateMode_Success_ReportsApplied_AndReReadsActiveMode()
    {
        _useCase.Setup(u => u.ExecuteAsync(PerformanceMode.Gaming)).ReturnsAsync(ControlResult.Success());

        // Only queried again once the backend has confirmed the change.
        _backendMode = PerformanceMode.Gaming;

        await WithTimeout(InvokeActivateAsync(_vm, PerformanceMode.Gaming));

        _useCase.Verify(u => u.ExecuteAsync(PerformanceMode.Gaming), Times.Once);
        Assert.Equal("Applied", _vm.OperationStatus);
        Assert.Equal(PerformanceMode.Gaming, _vm.Modes.Single(m => m.IsActive).Mode);
    }

    // ── 5. Failure path must not imply a system change ────────────────────

    [Theory]
    [InlineData(ControlStatus.OperationFailed, "power plan refused")]
    [InlineData(ControlStatus.NotAuthorized, "policy denied this action")]
    [InlineData(ControlStatus.NotSupported, "no control offered")]
    public async Task ActivateMode_Failure_ReportsMessage_AndKeepsPreviousHighlight(
        ControlStatus status, string message)
    {
        _useCase.Setup(u => u.ExecuteAsync(It.IsAny<PerformanceMode>()))
                .ReturnsAsync(new ControlResult { Status = status, Message = message });

        await WithTimeout(InvokeActivateAsync(_vm, PerformanceMode.MaximumPerformance));

        Assert.StartsWith("Failed:", _vm.OperationStatus);
        Assert.Contains(message, _vm.OperationStatus);
        Assert.DoesNotContain("Applied", _vm.OperationStatus);

        // The highlight must stay on the backend-confirmed mode, never on the failed request.
        Assert.Equal(PerformanceMode.Balanced, _vm.Modes.Single(m => m.IsActive).Mode);
        Assert.Equal("Balanced", _vm.CurrentPowerModeString);
    }

    [Fact]
    public async Task ActivateMode_NonModeParameter_IsIgnored_WithoutFalseSuccess()
    {
        await WithTimeout(InvokeActivateAsync(_vm, "not-a-mode"));

        _useCase.Verify(u => u.ExecuteAsync(It.IsAny<PerformanceMode>()), Times.Never);
        Assert.Equal(string.Empty, _vm.OperationStatus);
    }

    // ── 6. Unsupported hardware stays explicitly unsupported ──────────────

    [Fact]
    public void Capabilities_KeepUnsupportedFeaturesExplicitlyUnsupported()
    {
        var byName = _vm.Capabilities.ToDictionary(c => c.Name);

        Assert.True(byName["Power Management"].IsSupported);
        Assert.True(byName["GPU Preference"].IsSupported);

        foreach (var name in new[]
                 {
                     "CPU Frequency", "GPU Temperature", "Fan Control",
                     "Thermal Control", "Battery Threshold"
                 })
        {
            Assert.False(byName[name].IsSupported, $"{name} must stay Unsupported");
            Assert.Equal("Unsupported", byName[name].Status);
        }
    }

    // ── 7. No active session => honest defaults, never invented state ─────

    [Fact]
    public void NoActiveSession_ReportsNoneAndNaPolicy_NeverFabricated()
    {
        Assert.Equal("None", _vm.ActiveGameName);
        Assert.Equal("None", _vm.ActiveProfileName);
        Assert.Equal("N/A", _vm.ActivePowerPolicy);
        Assert.Equal("N/A", _vm.ActiveGpuPolicy);
        Assert.Equal("N/A", _vm.ActivePriorityPolicy);
        Assert.Equal("Inactive", _vm.ActivePolicyStatus);
    }

    private sealed class FakeLoggingService : ILoggingService
    {
        public void Log(string message, LogSeverity severity = LogSeverity.Info, string category = "General", string? detail = null) { }
        public IEnumerable<ActivityLogEntry> GetRecentActivity(int maxEntries = 100) => new List<ActivityLogEntry>();
        public void ClearLog() { }
    }
}
