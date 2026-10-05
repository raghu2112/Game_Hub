namespace DellGamingControlCenter.Core.Interfaces;

using System.Collections.Generic;
using DellGamingControlCenter.Core.Models;

public interface IBottleneckAnalyzer
{
    /// <summary>Analyzes rolling window of snapshots to detect sustained bottlenecks.</summary>
    List<BottleneckResult> Analyze(IEnumerable<DiagnosticSnapshot> history);
}
