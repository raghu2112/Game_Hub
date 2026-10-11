namespace DellGamingControlCenter.Core.Interfaces;

using System.Threading.Tasks;

public interface IWindowsPowerPlanService : IPowerPlanController
{
    // GetActivePowerSchemeGuid() and SetPowerSchemeAsync(string) are inherited
    // from IPowerPlanController — re-declaring them caused CS0108 x2.
}
