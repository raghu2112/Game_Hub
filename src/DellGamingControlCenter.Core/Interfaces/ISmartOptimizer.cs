namespace DellGamingControlCenter.Core.Interfaces;

using System.Threading.Tasks;
using DellGamingControlCenter.Core.Models;

public interface ISmartOptimizer
{
    /// <summary>Runs full diagnostic pipeline and produces an optimization plan to apply.</summary>
    Task<OptimizationPlan> GenerateSmartPlanAsync(GameProfile profile);
}
