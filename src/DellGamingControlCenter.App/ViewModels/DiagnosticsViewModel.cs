namespace DellGamingControlCenter.App.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

public class DiagnosticsViewModel : BaseViewModel
{
    private string _cpuUsage = "N/A";
    private string _gpuUsage = "N/A";
    private string _ramUsage = "N/A";
    private string _storageUsage = "N/A";
    private string _temperature = "N/A";
    private string _reportStatus = "Ready";

    private bool _isAnalyzing;

    // Bottleneck summary bound by DiagnosticsView.xaml.
    // These were bound in the view but never declared, so the cards rendered their
    // placeholder text with an empty name (" LIMITATION"). They are now driven by
    // the same authoritative findings list the rest of the page shows, and the view
    // collapses the cards while both names are empty.
    private string _primaryBottleneckName = string.Empty;
    private string _primaryBottleneckConfidence = string.Empty;
    private string _primaryBottleneckExplanation = string.Empty;
    private string _secondaryBottleneckName = string.Empty;
    private string _secondaryBottleneckConfidence = string.Empty;

    private int _gamingHealthScore = 0;

    public string CpuUsage { get => _cpuUsage; set => SetProperty(ref _cpuUsage, value); }
    public string GpuUsage { get => _gpuUsage; set => SetProperty(ref _gpuUsage, value); }
    public string RamUsage { get => _ramUsage; set => SetProperty(ref _ramUsage, value); }
    public string StorageUsage { get => _storageUsage; set => SetProperty(ref _storageUsage, value); }
    public string Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }

    public string ReportStatus { get => _reportStatus; set => SetProperty(ref _reportStatus, value); }

    /// <summary>Title of the most severe finding; empty when the analysis found nothing.</summary>
    public string PrimaryBottleneckName { get => _primaryBottleneckName; set => SetProperty(ref _primaryBottleneckName, value); }

    /// <summary>Engine confidence behind <see cref="PrimaryBottleneckName"/>.</summary>
    public string PrimaryBottleneckConfidence { get => _primaryBottleneckConfidence; set => SetProperty(ref _primaryBottleneckConfidence, value); }

    /// <summary>What the primary finding actually observed.</summary>
    public string PrimaryBottleneckExplanation { get => _primaryBottleneckExplanation; set => SetProperty(ref _primaryBottleneckExplanation, value); }

    /// <summary>Title of the next finding; empty when there is only one (or none).</summary>
    public string SecondaryBottleneckName { get => _secondaryBottleneckName; set => SetProperty(ref _secondaryBottleneckName, value); }

    /// <summary>Engine confidence behind <see cref="SecondaryBottleneckName"/>.</summary>
    public string SecondaryBottleneckConfidence { get => _secondaryBottleneckConfidence; set => SetProperty(ref _secondaryBottleneckConfidence, value); }

    public bool IsAnalyzing { get => _isAnalyzing; set => SetProperty(ref _isAnalyzing, value); }

    public int GamingHealthScore { get => _gamingHealthScore; set => SetProperty(ref _gamingHealthScore, value); }

    public ObservableCollection<DiagnosticFinding> Findings { get; } = new();
    public ObservableCollection<OptimizationRecommendation> Recommendations { get; } = new();

    public ICommand RunDiagnosticCommand { get; }

    public DiagnosticsViewModel()
    {
        RunDiagnosticCommand = new AsyncRelayCommand(async _ => await RunDiagnosticAsync(), _ => !IsAnalyzing);
    }

    /// <summary>
    /// Called when the Diagnostics page becomes visible (navigation or VIEW DETAILS).
    /// Refreshes metrics from the shared sampler history and runs the authoritative
    /// DiagnosticEngine analysis — the page must never sit on stale "N/A" data.
    /// </summary>
    public Task OnNavigatedToAsync()
    {
        return RunDiagnosticAsync();
    }

    private async Task RunDiagnosticAsync()
    {
        IsAnalyzing = true;
        ReportStatus = "Analyzing...";
        try
        {
            var history = App.MetricsSampler.GetHistory().OrderBy(s => s.Timestamp).ToList();
            if (!history.Any())
            {
                var current = await App.DiagnosticEngine.CaptureSnapshotAsync();
                history.Add(current);
            }

            var report = await App.DiagnosticEngine.RunDiagnosticAnalysisAsync(history);

            // Populate metrics from the latest snapshot (precise, honest statuses).
            var latest = history.LastOrDefault();
            if (latest != null)
            {
                var cpu = latest.CpuMetrics.UsagePercent;
                CpuUsage = cpu >= 0 ? $"{Math.Round(cpu)}%" : "Unavailable";

                var gpu = latest.GpuMetrics.UsagePercent;
                GpuUsage = gpu.HasValue && gpu.Value >= 0 ? $"Utilization: {Math.Round(gpu.Value)}%" : "Utilization unavailable";

                RamUsage = $"{Math.Round(latest.RamMetrics.UsedBytes / (1024.0 * 1024 * 1024), 1)} GB";
                StorageUsage = "Volume snapshot pending";

                if (latest.ThermalMetrics?.CpuTemperature.HasValue == true)
                {
                    Temperature = $"{Math.Round(latest.ThermalMetrics.CpuTemperature.Value)}°C";
                }
                else if (latest.CpuMetrics?.TemperatureCelsius.HasValue == true)
                {
                    Temperature = $"{Math.Round(latest.CpuMetrics.TemperatureCelsius.Value)}°C";
                }
                else if (latest.ThermalMetrics?.AmbientTemperature.HasValue == true)
                {
                    Temperature = $"{Math.Round(latest.ThermalMetrics.AmbientTemperature.Value)}°C (ACPI)";
                }
                else if (latest.ThermalMetrics?.CpuTemperatureState == Core.Enums.SensorReadingState.Unavailable ||
                         latest.CpuMetrics?.TemperatureState == Core.Enums.SensorReadingState.Unavailable)
                {
                    Temperature = "Unavailable";
                }
                else
                {
                    Temperature = "Unsupported";
                }
            }

            var findings = report.Findings.OrderByDescending(f => f.Severity).ToList();
            ReportStatus = findings.Count == 0
                ? "No issues detected."
                : $"{findings.Count} finding(s) on the latest analysis.";

            // WHY card + secondary card (see the field comment). `findings` is already
            // sorted by severity, so index 0/1 are the primary and secondary limiting factors.
            var primaryFinding = findings.Count > 0 ? findings[0] : null;
            var secondaryFinding = findings.Count > 1 ? findings[1] : null;
            PrimaryBottleneckName = primaryFinding?.Title ?? string.Empty;
            PrimaryBottleneckConfidence = primaryFinding?.Confidence.ToString() ?? string.Empty;
            PrimaryBottleneckExplanation = primaryFinding?.Description ?? string.Empty;
            SecondaryBottleneckName = secondaryFinding?.Title ?? string.Empty;
            SecondaryBottleneckConfidence = secondaryFinding?.Confidence.ToString() ?? string.Empty;

            InPlaceReplace(Findings, findings);

            // Derive recommendations from the authoritative findings via the shared engine.
            var recs = App.RecommendationEngine.GenerateRecommendations(findings, new GameProfile());
            InPlaceReplace(Recommendations, recs.Take(6).ToList());

            // Health score mirrors the same authoritative severity set as the Dashboard.
            int score = 100;
            foreach (var f in findings)
            {
                score -= f.Severity switch
                {
                    DiagnosticSeverity.Critical => 40,
                    DiagnosticSeverity.Warning => 20,
                    DiagnosticSeverity.Notice => 5,
                    _ => 0
                };
            }
            GamingHealthScore = Math.Max(0, score);
        }
        catch (Exception ex)
        {
            App.Logger.Log($"Diagnostic failed: {ex}", LogSeverity.Error, "UI");
            ReportStatus = "Error during analysis.";
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private static void InPlaceReplace<T>(ObservableCollection<T> collection, IReadOnlyList<T> items)
    {
        int i = 0;
        for (; i < items.Count; i++)
        {
            if (i < collection.Count) collection[i] = items[i];
            else collection.Add(items[i]);
        }
        while (collection.Count > items.Count)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }
}
