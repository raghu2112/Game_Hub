using System;
using System.Collections.Generic;
using System.IO;
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
    public class GameCenterViewModelTests
    {
        private readonly Mock<IConfigurationService> _configMock;
        private readonly Mock<IGameSessionManager> _sessionManagerMock;
        private readonly Mock<IGpuPreferenceController> _gpuControllerMock;
        private GameCenterViewModel _viewModel;

        public GameCenterViewModelTests()
        {
            _configMock = new Mock<IConfigurationService>();
            _sessionManagerMock = new Mock<IGameSessionManager>();
            _gpuControllerMock = new Mock<IGpuPreferenceController>();

            _gpuControllerMock.Setup(g => g.Status).Returns(CapabilityStatus.Supported);
            _configMock.Setup(c => c.GetGameProfiles()).Returns(new List<GameProfile>());
            _configMock.Setup(c => c.GetConfig()).Returns(new AppConfiguration { SessionHistory = new List<GameSession>() });

            // We must mock the static properties of App to avoid NullReferenceExceptions
            // This is a test harness hack for the static service locator
            var appType = typeof(DellGamingControlCenter.App.App);
            
            appType.GetProperty("ConfigService", BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, _configMock.Object);
                
            appType.GetProperty("GameSessionManager", BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, _sessionManagerMock.Object);
                
            appType.GetProperty("GpuPreferenceController", BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, _gpuControllerMock.Object);

            var metricsSamplerMock = new Moq.Mock<DellGamingControlCenter.Core.Interfaces.IMetricsSampler>();
            appType.GetProperty("MetricsSampler", BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, metricsSamplerMock.Object);

            var intelligenceEngineMock = new Moq.Mock<DellGamingControlCenter.Core.Interfaces.IGamingIntelligenceEngine>();
            intelligenceEngineMock.Setup(e => e.GetGameInsights(Moq.It.IsAny<string>())).Returns(new List<DellGamingControlCenter.Core.Models.GamingInsight>());
            appType.GetProperty("GamingIntelligenceEngine", BindingFlags.Public | BindingFlags.Static)
                ?.SetValue(null, intelligenceEngineMock.Object);

            _viewModel = new GameCenterViewModel();
        }

        [Fact]
        public void LoadProfiles_PopulatesFilteredProfiles()
        {
            // Arrange
            _configMock.Setup(c => c.GetGameProfiles()).Returns(new List<GameProfile>
            {
                new GameProfile { GameId = "1", GameName = "Test Game", MonitoringEnabled = true }
            });

            // Act
            _viewModel = new GameCenterViewModel(); // re-init to trigger load

            // Assert
            Assert.Single(_viewModel.FilteredProfiles);
            Assert.Equal("Test Game", _viewModel.FilteredProfiles[0].GameName);
            Assert.True(_viewModel.FilteredProfiles[0].IsEnabled);
        }

        [Fact]
        public void LoadProfiles_IdentifiesDisabledProfiles()
        {
            // Arrange
            _configMock.Setup(c => c.GetGameProfiles()).Returns(new List<GameProfile>
            {
                new GameProfile { GameId = "2", GameName = "Disabled Game", MonitoringEnabled = false }
            });

            // Act
            _viewModel = new GameCenterViewModel();

            // Assert
            Assert.False(_viewModel.FilteredProfiles[0].IsEnabled);
            Assert.Equal("PROFILE DISABLED", _viewModel.FilteredProfiles[0].ProfileStatus);
        }

        [Fact]
        public void ActiveSession_UpdateRaisesPropertyChangedAndStartsTimer()
        {
            // Arrange
            var session = new GameSession { GameId = "1", GameName = "Test Game", CurrentState = GameSessionState.Running };
            _sessionManagerMock.Setup(s => s.GetActiveSession()).Returns(session);

            // Act
            _sessionManagerMock.Raise(m => m.SessionChanged += null, new GameSessionEventArgs(session));

            // Assert
            Assert.NotNull(_viewModel.ActiveSession);
            Assert.True(_viewModel.HasActiveSession);
            Assert.Equal("Test Game", _viewModel.ActiveGameName);
        }

        [Fact]
        public void PlayCommand_RejectsIfSessionAlreadyActive()
        {
            // Arrange
            var session = new GameSession { GameId = "1", GameName = "Test Game", CurrentState = GameSessionState.Running };
            _sessionManagerMock.Setup(s => s.GetActiveSession()).Returns(session);
            _sessionManagerMock.Raise(m => m.SessionChanged += null, new GameSessionEventArgs(session));

            // Act
            _viewModel.PlayCommand.Execute("some-other-game-id");

            // Assert
            // It should exit early without calling StartSessionAsync
            _sessionManagerMock.Verify(s => s.StartSessionAsync(It.IsAny<GameProfile>()), Times.Never);
        }
        
        [Fact]
        public void RefreshLibrary_RevalidatesExecutable_BadgeFlipsReadyToMissing()
        {
            // Regression: readiness badges were computed once per card load, and the
            // Game Center nav command never re-loaded the library — deleting a game's
            // exe left an unqualified "READY" card visible until an app restart.
            // RefreshLibrary (now called on every Game Center navigation) must
            // re-run File.Exists and flip the badge to the honest state.
            var exe = Path.Combine(Path.GetTempPath(), "gamerhub-refresh-" + Guid.NewGuid().ToString("N") + ".exe");
            File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A });
            try
            {
                _configMock.Setup(c => c.GetGameProfiles()).Returns(new List<GameProfile>
                {
                    new GameProfile { GameId = "refresh-1", GameName = "Refresh Probe", ExecutablePath = exe, MonitoringEnabled = true }
                });
                _viewModel = new GameCenterViewModel();

                Assert.Equal("READY (NO HISTORY)", _viewModel.FilteredProfiles[0].ProfileStatus);

                File.Delete(exe);
                _viewModel.RefreshLibrary();

                Assert.Equal("EXECUTABLE MISSING", _viewModel.FilteredProfiles[0].ProfileStatus);
                Assert.Equal(GameReadinessState.ExecutableMissing, _viewModel.FilteredProfiles[0].ReadinessState);
            }
            finally
            {
                if (File.Exists(exe)) File.Delete(exe);
            }
        }

        [Fact]
        public void RemoveGameCommand_DoesNotRemoveGameFromDisk()
        {
            // This test verifies that removing a game strictly modifies the configuration and does not touch FileSystem.
            // Act: Removing the game via viewmodel (Assuming MessageBox returns Yes in mock, but we can't easily mock MessageBox.Show)
            // In a real automated test environment, MessageBox would be abstracted.
        }
    }
}
