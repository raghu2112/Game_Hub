using Xunit;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Services;
using DellGamingControlCenter.Core.Enums;
using System.Linq;

namespace DellGamingControlCenter.Tests
{
    public class OptimizationPipelineTests
    {
        [Fact]
        public void GeneratePlan_WithHighPerformanceGPU_CreatesGpuAction()
        {
            var logger = new LoggingService();
            var engine = new OptimizationEngine(logger);
            
            var profile = new GameProfile 
            {
                GameId = "test1",
                GameName = "GTA IV",
                ExecutablePath = @"C:\Games\GTAIV.exe",
                PreferredGpu = GpuPreference.HighPerformance,
                ProcessPriority = ProcessPriorityClass.AboveNormal,
                EnableGameMode = true
            };
            
            var plan = engine.GeneratePlan(profile);
            
            Assert.Equal(3, plan.Actions.Count);
            
            var gpuAction = plan.Actions.FirstOrDefault(a => a.Type == "GpuPreference");
            Assert.NotNull(gpuAction);
            Assert.Equal("HighPerformance", gpuAction.Value);
            
            var priorityAction = plan.Actions.FirstOrDefault(a => a.Type == "ProcessPriority");
            Assert.NotNull(priorityAction);
            Assert.Equal("AboveNormal", priorityAction.Value);
        }
        
        [Fact]
        public async void ApplyPlan_MockFail_TriggersRollback()
        {
            // Note: Since Registry/WMI touches the real system, a fully isolated unit test
            // would require interfaces for the Registry wrapper. 
            // This test just ensures the engine structure catches failures.
            var logger = new LoggingService();
            var engine = new OptimizationEngine(logger);
            
            var profile = new GameProfile 
            {
                GameId = "test2",
                GameName = "Fail Test",
                ExecutablePath = @"C:\Games\Fail.exe",
                PreferredGpu = GpuPreference.HighPerformance
            };
            
            var plan = engine.GeneratePlan(profile);
            // Intentionally corrupting the plan to force a failure state during apply
            var invalidAction = new OptimizationAction { Type = "InvalidActionForFailure" };
            plan.Actions.Clear();
            plan.Actions.Add(invalidAction);
            
            var result = await engine.ApplyPlanAsync(plan);
            
            Assert.False(result);
            Assert.Equal(OptimizationStatus.Failed, plan.Actions[0].Status);
        }
    }
}
