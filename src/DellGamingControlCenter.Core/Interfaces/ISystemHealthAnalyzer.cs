namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Models;
using System.Collections.Generic;

public interface ISystemHealthAnalyzer
{
    /// <summary>Calculates a Gaming Health Score from current snapshots.</summary>
    GamingHealthScore CalculateHealthScore(IEnumerable<DiagnosticSnapshot> recentSnapshots);
}
