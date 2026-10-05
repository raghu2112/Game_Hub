using Xunit;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Services;
using DellGamingControlCenter.Core.Enums;
using System.Linq;

namespace DellGamingControlCenter.Tests
{
    public class ConfigurationTests
    {
        [Fact]
        public void ProfileCRUD_WorksCorrectly()
        {
            var logger = new LoggingService();
            var config = new ConfigurationService(logger);
            
            var newProfile = new GameProfile
            {
                GameId = "test-game-1",
                GameName = "Need for Speed",
                ExecutablePath = @"D:\Games\NFS.exe",
                PreferredGpu = GpuPreference.HighPerformance
            };
            
            // Create
            config.SaveGameProfile(newProfile);
            
            var profiles = config.GetGameProfiles().ToList();
            Assert.Contains(profiles, p => p.GameId == "test-game-1");
            
            // Update
            newProfile.FpsTarget = 60;
            config.SaveGameProfile(newProfile);
            
            var updated = config.GetGameProfiles().First(p => p.GameId == "test-game-1");
            Assert.Equal(60, updated.FpsTarget);
            Assert.Equal(GpuPreference.HighPerformance, updated.PreferredGpu);
        }
        
        [Fact]
        public void ActivityLogging_StoresAndRetrieves()
        {
            var logger = new LoggingService();
            logger.ClearLog();
            
            logger.Log("Test log entry 1", LogSeverity.Info, "Test");
            logger.Log("Test log entry 2", LogSeverity.Warning, "Test");
            
            var logs = logger.GetRecentActivity().ToList();
            Assert.True(logs.Count >= 2);
            Assert.Contains(logs, l => l.Message == "Test log entry 1");
        }
    }
}
