using Xunit;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Services;
using DellGamingControlCenter.Core.Enums;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System;

namespace DellGamingControlCenter.Tests
{
    public class GameSessionIntegrationTests
    {
        [Fact]
        public async Task FullSessionLifecycle_WorksSuccessfully()
        {
            var logger = new LoggingService();
            var config = new ConfigurationService(logger);
            var optEngine = new OptimizationEngine(logger);
            var launcher = new GameLauncher(logger, optEngine);
            var perfService = new PerformanceModeService(logger);
            var bgManager = new BackgroundProcessManager(logger, config);
            var monitor = new SessionMonitoringService(logger, config);
            var healthAnalyzer = new SystemHealthAnalyzer();
            var fpsProvider = new UnavailableFpsProvider();
            
            var hardwareDetector = new HardwareDetector(logger);
            var performanceAnalyzer = new GamePerformanceAnalyzer(logger, healthAnalyzer, fpsProvider);
            var orchestrator = new GameSessionManager(logger, launcher, perfService, monitor, bgManager, config, optEngine, hardwareDetector, performanceAnalyzer);
            
            // We use ping.exe as a harmless test "game" to simulate a process
            string pingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe");
            
            var profile = new GameProfile
            {
                GameId = "test-ping",
                GameName = "Test Ping",
                ExecutablePath = pingPath,
                LaunchArguments = "127.0.0.1 -n 6", // runs for ~5 seconds then dies
                PerformanceMode = PerformanceMode.Gaming
            };
            
            var session = await orchestrator.StartSessionAsync(profile);
            
            // Wait for the ping process to exit and orchestrator to clean up
            await Task.Delay(8000);
            
            // The background watcher in orchestrator should have handled exit
            Assert.True(session.CurrentState == GameSessionState.Completed || session.CurrentState == GameSessionState.CompletedWithWarnings);
            Assert.NotNull(session.EndTime);
            Assert.True(session.RestorationSuccessful);
        }
    }
}
