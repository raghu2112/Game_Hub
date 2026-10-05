using Xunit;
using DellGamingControlCenter.Services;
using System.Threading.Tasks;

namespace DellGamingControlCenter.Tests
{
    public class MemoryOptimizationTests
    {
        [Fact]
        public async Task MemoryAnalysis_ReturnsValidData()
        {
            var logger = new LoggingService();
            var memService = new MemoryOptimizationService(logger);
            
            var result = await memService.AnalyzeMemoryAsync();
            
            Assert.True(result.TotalRamBytes > 0);
            Assert.True(result.UsedRamBytes > 0);
            Assert.NotNull(result.TopProcessesBytes);
            // Should be up to 5 processes
            Assert.True(result.TopProcessesBytes.Count <= 5);
        }
    }
}
