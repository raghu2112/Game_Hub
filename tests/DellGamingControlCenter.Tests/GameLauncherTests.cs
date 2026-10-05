using Xunit;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Services;
using DellGamingControlCenter.Core.Enums;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace DellGamingControlCenter.Tests
{
    public class GameLauncherTests
    {
        [Fact]
        public async Task LaunchGame_MissingExecutable_FailsSafely()
        {
            var logger = new LoggingService();
            var engine = new OptimizationEngine(logger);
            var launcher = new GameLauncher(logger, engine);
            
            var profile = new GameProfile
            {
                GameId = "test",
                GameName = "Missing Game",
                ExecutablePath = @"C:\FakePath\doesnotexist.exe"
            };
            
            var session = await launcher.LaunchGameAsync(profile, CancellationToken.None);
            
            Assert.Equal(GameSessionState.Failed, session.CurrentState);
            Assert.Equal("Executable not found.", session.FailureReason);
        }
        
        [Fact]
        public async Task FindProcess_TimesOut_ReturnsNull()
        {
            var logger = new LoggingService();
            var engine = new OptimizationEngine(logger);
            var launcher = new GameLauncher(logger, engine);
            
            var session = new GameSession
            {
                GameName = "NonExistentProcessTest",
                ExecutablePath = @"C:\Games\shouldneverrun.exe"
            };
            
            // Fast cancellation to simulate timeout quickly
            using var cts = new CancellationTokenSource(100);
            
            var profile = new GameProfile();
            var pid = await launcher.FindGameProcessAsync(session, profile, cts.Token);
            Assert.Null(pid);
        }
    }
}
