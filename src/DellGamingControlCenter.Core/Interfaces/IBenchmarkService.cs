namespace DellGamingControlCenter.Core.Interfaces;

using System.Threading.Tasks;
using System.Collections.Generic;
using DellGamingControlCenter.Core.Models;

public interface IBenchmarkService
{
    Task<BenchmarkRun> StartBenchmarkAsync(GameProfile profile, BenchmarkType type, bool isBaseline);
    Task<BenchmarkRun> StopBenchmarkAsync(BenchmarkRun currentRun, SessionReport report);
    Task<List<BenchmarkRun>> GetHistoryAsync(string gameId);
    Task<BenchmarkComparison> CompareBenchmarksAsync(BenchmarkRun baseline, BenchmarkRun optimized);
    Task ExportToCsvAsync(BenchmarkRun run, string path);
    Task ExportToJsonAsync(BenchmarkRun run, string path);
    Task CleanupOldBenchmarksAsync(string gameId);
}
