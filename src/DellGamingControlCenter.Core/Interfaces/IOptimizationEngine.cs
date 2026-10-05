namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Models;
using System.Threading.Tasks;

/// <summary>
/// Handles the optimization pipeline: Plan -> Backup -> Apply -> Verify -> Rollback.
/// </summary>
public interface IOptimizationEngine
{
    /// <summary>Generates a machine-readable optimization plan for a given game profile.</summary>
    OptimizationPlan GeneratePlan(GameProfile profile);

    /// <summary>Executes the optimization plan safely.</summary>
    Task<bool> ApplyPlanAsync(OptimizationPlan plan);

    /// <summary>Restores the system to a previous state using the rollback data in the plan.</summary>
    Task<bool> RollbackPlanAsync(OptimizationPlan plan);
}
