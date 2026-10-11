using Xunit;
using Moq;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Services;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System;
using System.Linq;

namespace DellGamingControlCenter.Tests
{
    public class GameSessionIntegrationTests
    {
        [Fact]
        public async Task FullSessionLifecycle_WorksSuccessfully()
        {
            var logger = new Moq.Mock<ILoggingService>().Object;
            string pingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe");
            
            var profile = new GameProfile
            {
                GameId = "test-ping",
                GameName = "Test Ping",
                ExecutablePath = pingPath,
                LaunchArguments = "127.0.0.1 -n 3 -w 1000", // bounded lifetime: worst case ~4s (3 timeouts x 1s), best case ~2s
                PerformanceMode = PerformanceMode.Gaming
            };

            var configMock = new Moq.Mock<IConfigurationService>();
            configMock.Setup(c => c.GetConfig()).Returns(new AppConfiguration { GameProfiles = new List<GameProfile> { profile }, SessionHistory = new List<GameSession>() });
            var config = configMock.Object;
            var launcher = new Moq.Mock<IGameLauncher>();
            var monitor = new Moq.Mock<ISessionMonitoringService>();
            var bgManager = new Moq.Mock<IBackgroundProcessManager>();
            var policyEvaluatorMock = new Moq.Mock<IPolicyEvaluator>();
            policyEvaluatorMock.Setup(p => p.ApplyPreLaunchPoliciesAsync(Moq.It.IsAny<GameProfile>(), Moq.It.IsAny<GameSession>())).ReturnsAsync(new ProfileApplicationResult { Success = true });
            policyEvaluatorMock.Setup(p => p.ApplyPostLaunchPoliciesAsync(Moq.It.IsAny<GameProfile>(), Moq.It.IsAny<GameSession>())).ReturnsAsync(new ProfileApplicationResult { Success = true });
            policyEvaluatorMock.Setup(p => p.RestoreProfileAsync(Moq.It.IsAny<GameProfile>(), Moq.It.IsAny<GameSession>())).ReturnsAsync(true);
            var policyEvaluator = policyEvaluatorMock.Object;
            var cpuTelemetryMock = new Moq.Mock<ICpuTelemetryService>();
            cpuTelemetryMock.Setup(c => c.GetMetrics()).Returns(new CpuMetrics { UsagePercent = 10 });
            var cpuTelemetry = cpuTelemetryMock.Object;
            
            var memoryTelemetryMock = new Moq.Mock<IMemoryTelemetryService>();
            memoryTelemetryMock.Setup(m => m.GetMetrics()).Returns(new RamMetrics { UsedBytes = 1024, AvailableBytes = 1024 });
            var memoryTelemetry = memoryTelemetryMock.Object;
            
            var performanceAnalyzer = new Moq.Mock<IGamePerformanceAnalyzer>().Object;
            
            int processId = 0;
            launcher.Setup(l => l.LaunchGameAsync(Moq.It.IsAny<GameProfile>(), Moq.It.IsAny<CancellationToken>()))
                    .ReturnsAsync((GameProfile p, CancellationToken c) => 
                    {
                        var proc = Process.Start(new ProcessStartInfo { FileName = p.ExecutablePath, Arguments = p.LaunchArguments, WindowStyle = ProcessWindowStyle.Hidden });
                        if (proc != null) processId = proc.Id;
                        System.Threading.Thread.Sleep(500); // Give Windows time to register the process
                        return new GameSession { SessionId = Guid.NewGuid().ToString(), GameId = p.GameId, GameName = p.GameName, ExecutablePath = p.ExecutablePath, CurrentState = GameSessionState.Running };
                    });

            launcher.Setup(l => l.FindGameProcessAsync(Moq.It.IsAny<GameSession>(), Moq.It.IsAny<GameProfile>(), Moq.It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => processId);
            
            var orchestrator = new GameSessionManager(logger, launcher.Object, monitor.Object, bgManager.Object, config, policyEvaluator, cpuTelemetry, memoryTelemetry, performanceAnalyzer);
            
            var session = await orchestrator.StartSessionAsync(profile);
            
            // Wait adaptively for the background watcher to handle exit (bounded so a
            // regression still fails the test instead of hanging the run)
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline &&
                   session.CurrentState != GameSessionState.Completed &&
                   session.CurrentState != GameSessionState.CompletedWithWarnings &&
                   session.CurrentState != GameSessionState.Failed)
            {
                await Task.Delay(500);
            }
            
            // The background watcher in orchestrator should have handled exit
            Assert.True(session.CurrentState == GameSessionState.Completed || session.CurrentState == GameSessionState.CompletedWithWarnings,
                $"Session ended in unexpected state {session.CurrentState}; EndTime={session.EndTime}; RestorationSuccessful={session.RestorationSuccessful}; ProcessId={session.ProcessId}; " +
                $"Timeline: [{string.Join(" | ", session.TimelineEvents.Select(e => e.Description))}]");
            Assert.NotNull(session.EndTime);
            Assert.True(session.RestorationSuccessful);
        }
    }
}
