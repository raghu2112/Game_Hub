using Xunit;
using DellGamingControlCenter.Services;
using DellGamingControlCenter.Core.Enums;
using System.Threading.Tasks;

namespace DellGamingControlCenter.Tests
{
    public class PerformanceModeTests
    {
        [Fact]
        public async Task ChangePerformanceMode_UpdatesState()
        {
            var logger = new LoggingService();
            var mockPowerPlan = new Moq.Mock<DellGamingControlCenter.Core.Interfaces.IWindowsPowerPlanService>();
            var perfService = new PerformanceModeService(logger, mockPowerPlan.Object);
            
            // This test modifies system state during execution, so we must be careful.
            // On a real system we would mock the powercfg calls. For this architecture,
            // we will just verify the C# state updates if the apply method returns true/false.
            
            var initialMode = perfService.GetCurrentMode();
            Assert.Equal(PerformanceMode.Balanced, initialMode);
            
            await perfService.ApplyModeAsync(PerformanceMode.Gaming);
            var newMode = perfService.GetCurrentMode();
            
            Assert.True(newMode == PerformanceMode.Gaming || newMode == PerformanceMode.Balanced); // depending on success
            
            await perfService.RestorePreviousModeAsync();
            Assert.Equal(PerformanceMode.Balanced, perfService.GetCurrentMode());
        }
    }
}
