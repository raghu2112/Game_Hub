namespace DellGamingControlCenter.Core.Interfaces;

using System.Collections.Generic;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Models;

public interface IDiagnosticEngine
{
    Task<DiagnosticSnapshot> CaptureSnapshotAsync(GameProfile? activeGame = null, string? gameExecutablePath = null);
    Task<DiagnosticReport> RunDiagnosticAnalysisAsync(IEnumerable<DiagnosticSnapshot> history);
}

public interface IDiagnosticRule
{
    IEnumerable<DiagnosticFinding> Evaluate(IEnumerable<DiagnosticSnapshot> history);
}
