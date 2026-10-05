namespace DellGamingControlCenter.Core.Interfaces;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Models;

public interface IDiagnosticEngine
{
    /// <summary>Captures a single immediate snapshot of system metrics.</summary>
    Task<DiagnosticSnapshot> CaptureSnapshotAsync();
    
    /// <summary>Analyzes a sliding window of snapshots to generate a "Why Is My Game Slow?" report.</summary>
    Task<List<BottleneckResult>> RunDiagnosticAnalysisAsync(IEnumerable<DiagnosticSnapshot> history);
}
