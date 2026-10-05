namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Enums;
using System.Threading.Tasks;

public interface IPerformanceModeService
{
    /// <summary>Applies a specific performance mode (Quiet, BatterySaver, Balanced, Gaming, MaximumPerformance).</summary>
    Task<bool> ApplyModeAsync(PerformanceMode mode);

    /// <summary>Restores the previous power scheme/mode.</summary>
    Task<bool> RestorePreviousModeAsync();

    /// <summary>Gets the currently active performance mode.</summary>
    PerformanceMode GetCurrentMode();
}
