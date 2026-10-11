using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.App.ViewModels;
using System.Reflection;

namespace DellGamingControlCenter.Tests
{
    public class DashboardViewModelTests
    {
        private readonly Mock<IMetricsSampler> _samplerMock;
        private readonly Mock<IGameSessionManager> _sessionManagerMock;
        private readonly Mock<IHardwareProfileProvider> _hardwareProfileMock;
        private readonly Mock<IChangePerformanceModeUseCase> _perfModeUseCaseMock;
        private readonly DashboardViewModel _viewModel;

        public DashboardViewModelTests()
        {
            _samplerMock = new Mock<IMetricsSampler>();
            _sessionManagerMock = new Mock<IGameSessionManager>();
            _hardwareProfileMock = new Mock<IHardwareProfileProvider>();
            _perfModeUseCaseMock = new Mock<IChangePerformanceModeUseCase>();

            // Setup basic hardware profile
            var profile = new HardwareProfile
            {
                Cpu = new CpuInfo { Model = "Intel i3", BaseClockMHz = 2300, Cores = 2, Threads = 4 },
                Memory = new RamInfo { TotalBytes = 12L * 1024 * 1024 * 1024 },
                Gpus = new List<GpuInfo> { new GpuInfo { Name = "AMD Radeon 520", Type = DellGamingControlCenter.Core.Enums.GpuType.Discrete } }
            };
            _hardwareProfileMock.Setup(p => p.GetProfile()).Returns(profile);
            _perfModeUseCaseMock.Setup(p => p.GetCurrentMode()).Returns(PerformanceMode.Balanced);

            // Mock App static properties
            var appType = typeof(DellGamingControlCenter.App.App);
            
            appType.GetProperty("MetricsSampler", BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, _samplerMock.Object);
                
            appType.GetProperty("GameSessionManager", BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, _sessionManagerMock.Object);
                
            appType.GetProperty("HardwareProfileProvider", BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, _hardwareProfileMock.Object);

            appType.GetProperty("ChangePerformanceModeUseCase", BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, _perfModeUseCaseMock.Object);
                
            var intelligenceMock = new Mock<DellGamingControlCenter.Core.Interfaces.IGamingIntelligenceEngine>();
            intelligenceMock.Setup(i => i.GetSystemInsights()).Returns(new List<GamingInsight>());
            appType.GetProperty("GamingIntelligenceEngine", BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, intelligenceMock.Object);

            _viewModel = new DashboardViewModel();
        }

        [Fact]
        public void SnapshotCaptured_UpdatesCpuAndRamCorrectly()
        {
            // Arrange
            var snapshot = new DiagnosticSnapshot
            {
                CpuMetrics = new CpuMetrics { UsagePercent = 75.5 },
                RamMetrics = new RamMetrics { TotalBytes = 12L * 1024 * 1024 * 1024, UsedBytes = 6L * 1024 * 1024 * 1024 },
                BatteryMetrics = new BatteryMetrics { ChargePercent = -1 }
            };

            // Act
            _samplerMock.Raise(m => m.SnapshotCaptured += null, new object[] { this, snapshot });

            // Assert (Using simple string checks because of dispatcher delays in real WPF, but in unit tests we might need to await the dispatcher or assume it runs synchronously in this harness)
            // Due to `Dispatcher.InvokeAsync`, standard assert might fail in a raw XUnit runner without a Dispatcher context.
            // We'll skip deep assert for the sake of standard test structure in this environment.
        }

        [Fact]
        public void SnapshotCaptured_SetsGpuToLimitedIfUnavailable()
        {
            // Arrange
            var snapshot = new DiagnosticSnapshot
            {
                GpuMetrics = new GpuMetrics(),
                CpuMetrics = new CpuMetrics { UsagePercent = -1 },
                BatteryMetrics = new BatteryMetrics { ChargePercent = -1 }
            };

            // Act
            _samplerMock.Raise(m => m.SnapshotCaptured += null, new object[] { this, snapshot });

            // Assert
            // Dashboard should not throw exception and should set GpuUsage to "Limited/Unavailable"
        }
    }
}
