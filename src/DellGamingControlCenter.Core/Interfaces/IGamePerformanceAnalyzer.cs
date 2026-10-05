namespace DellGamingControlCenter.Core.Interfaces;

using System.Collections.Generic;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Models;

public interface IGamePerformanceAnalyzer
{
    /// <summary>Generates a comprehensive session report.</summary>
    Task<SessionReport> AnalyzeSessionAsync(GameSession session, IEnumerable<DiagnosticSnapshot> snapshots, IEnumerable<FpsSample> fpsSamples);
    
    /// <summary>Compares the current session with a baseline or previous session.</summary>
    Task<string> CompareSessionsAsync(GameSession currentSession, GameSession previousSession);
}
