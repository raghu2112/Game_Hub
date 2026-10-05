namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Models;
using System.Threading.Tasks;

public interface IMemoryOptimizationService
{
    /// <summary>Analyzes current memory usage and pressure.</summary>
    Task<MemoryAnalysisResult> AnalyzeMemoryAsync();

    /// <summary>Attempts to free standby memory (advanced feature, requires elevation).</summary>
    Task<bool> TryCleanupStandbyListAsync();
}
