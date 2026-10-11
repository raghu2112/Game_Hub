using System.Threading.Tasks;
using Xunit;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Services;

namespace DellGamingControlCenter.Tests
{
    public class SecurityPolicyTests
    {
        private class FakeLoggingService : ILoggingService
        {
            public void Log(string message, LogSeverity severity = LogSeverity.Info, string category = "General", string? detail = null) { }
            public System.Collections.Generic.IEnumerable<ActivityLogEntry> GetRecentActivity(int maxEntries = 100) => new System.Collections.Generic.List<ActivityLogEntry>();
            public void ClearLog() { }
        }

        private class FakeControlAuditLogger : IControlAuditLogger
        {
            public void LogControlEvent(ControlCapability capability, ControlResult result, string details = "") { }
        }

        private class FakePerformanceModeService : IPerformanceModeService
        {
            public bool ShouldSucceed { get; set; } = true;
            public PerformanceMode GetCurrentMode() => PerformanceMode.Balanced;
            public Task<bool> ApplyModeAsync(PerformanceMode mode) => Task.FromResult(ShouldSucceed);
            public Task<bool> RestorePreviousModeAsync() => Task.FromResult(true);
        }
        
        private class FakeAuthorizationService : IAuthorizationService
        {
            public bool IsElevated { get; set; } = false;

            public ControlResult Authorize(ControlCapability capability, ControlContext context)
            {
                if (capability == ControlCapability.ChangePerformanceMode && !IsElevated)
                {
                    return ControlResult.Denied("Requires Administrator privileges.");
                }
                return ControlResult.Success();
            }
        }

        [Fact]
        public void AuthorizationService_AllowedOperation_WhenElevated()
        {
            var authService = new AuthorizationService(new FakeLoggingService(), new FakeControlAuditLogger());
            var context = new ControlContext { UserName = "Test", IsElevated = true };
            
            var result = authService.Authorize(ControlCapability.ModifyThermalPolicy, context);
            
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void AuthorizationService_DeniedOperation_WhenNotElevated()
        {
            var authService = new AuthorizationService(new FakeLoggingService(), new FakeControlAuditLogger());
            var context = new ControlContext { UserName = "Test", IsElevated = false };
            
            var result = authService.Authorize(ControlCapability.ModifyThermalPolicy, context);
            
            Assert.False(result.IsSuccess);
            Assert.Equal(ControlStatus.NotAuthorized, result.Status);
        }

        [Fact]
        public async Task UseCase_AllowedOperation_ReturnsSuccess()
        {
            var authService = new FakeAuthorizationService { IsElevated = true };
            var perfService = new FakePerformanceModeService { ShouldSucceed = true };
            var useCase = new ChangePerformanceModeUseCase(perfService, authService);

            var result = await useCase.ExecuteAsync(PerformanceMode.Gaming);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task UseCase_DeniedOperation_ReturnsNotAuthorized()
        {
            var authService = new FakeAuthorizationService { IsElevated = false };
            var perfService = new FakePerformanceModeService { ShouldSucceed = true };
            var useCase = new ChangePerformanceModeUseCase(perfService, authService);

            var result = await useCase.ExecuteAsync(PerformanceMode.Gaming);

            Assert.False(result.IsSuccess);
            Assert.Equal(ControlStatus.NotAuthorized, result.Status);
        }

        [Fact]
        public async Task UseCase_HardwareFailure_ReturnsOperationFailed()
        {
            var authService = new FakeAuthorizationService { IsElevated = true };
            var perfService = new FakePerformanceModeService { ShouldSucceed = false };
            var useCase = new ChangePerformanceModeUseCase(perfService, authService);

            var result = await useCase.ExecuteAsync(PerformanceMode.Gaming);

            Assert.False(result.IsSuccess);
            Assert.Equal(ControlStatus.OperationFailed, result.Status);
        }

        [Fact]
        public async Task UseCase_InvalidRequest_ReturnsInvalidRequest()
        {
            var authService = new FakeAuthorizationService { IsElevated = true };
            var perfService = new FakePerformanceModeService { ShouldSucceed = true };
            var useCase = new ChangePerformanceModeUseCase(perfService, authService);

            // Casting an invalid int to enum
            var result = await useCase.ExecuteAsync((PerformanceMode)999);

            Assert.False(result.IsSuccess);
            Assert.Equal(ControlStatus.InvalidRequest, result.Status);
        }
    }
}
