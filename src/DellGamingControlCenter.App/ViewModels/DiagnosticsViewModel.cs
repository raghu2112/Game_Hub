namespace DellGamingControlCenter.App.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;
using System.Linq;
using System.Collections.Generic;

public class DiagnosticsViewModel : BaseViewModel
{
    private int _gamingHealthScore;
    private string _primaryBottleneckName = "None";
    private string _primaryBottleneckConfidence = "N/A";
    private string _primaryBottleneckExplanation = "";
    
    private string _secondaryBottleneckName = "None";
    private string _secondaryBottleneckConfidence = "N/A";

    private string _cpuUsage = "N/A";
    private string _gpuUsage = "N/A";
    private string _ramUsage = "N/A";
    private string _storageUsage = "N/A";
    private string _temperature = "N/A";

    private bool _isAnalyzing;

    public int GamingHealthScore { get => _gamingHealthScore; set => SetProperty(ref _gamingHealthScore, value); }
    public string PrimaryBottleneckName { get => _primaryBottleneckName; set => SetProperty(ref _primaryBottleneckName, value); }
    public string PrimaryBottleneckConfidence { get => _primaryBottleneckConfidence; set => SetProperty(ref _primaryBottleneckConfidence, value); }
    public string PrimaryBottleneckExplanation { get => _primaryBottleneckExplanation; set => SetProperty(ref _primaryBottleneckExplanation, value); }
    public string SecondaryBottleneckName { get => _secondaryBottleneckName; set => SetProperty(ref _secondaryBottleneckName, value); }
    public string SecondaryBottleneckConfidence { get => _secondaryBottleneckConfidence; set => SetProperty(ref _secondaryBottleneckConfidence, value); }
    
    public string CpuUsage { get => _cpuUsage; set => SetProperty(ref _cpuUsage, value); }
    public string GpuUsage { get => _gpuUsage; set => SetProperty(ref _gpuUsage, value); }
    public string RamUsage { get => _ramUsage; set => SetProperty(ref _ramUsage, value); }
    public string StorageUsage { get => _storageUsage; set => SetProperty(ref _storageUsage, value); }
    public string Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }

    public bool IsAnalyzing { get => _isAnalyzing; set => SetProperty(ref _isAnalyzing, value); }

    public ObservableCollection<OptimizationRecommendation> Recommendations { get; } = new();

    public ICommand RunDiagnosticCommand { get; }

    public DiagnosticsViewModel()
    {
        RunDiagnosticCommand = new AsyncRelayCommand(async _ => await RunDiagnosticAsync(), _ => !IsAnalyzing);
    }

    private async Task RunDiagnosticAsync()
    {
        IsAnalyzing = true;
        try
        {
            var history = App.MetricsSampler.GetHistory().ToList();
            if (!history.Any())
            {
                var current = await App.DiagnosticEngine.CaptureSnapshotAsync();
                history.Add(current);
            }
            
            var bottlenecks = await App.DiagnosticEngine.RunDiagnosticAnalysisAsync(history);
            var healthScore = App.SystemHealthAnalyzer.CalculateHealthScore(history);
            var analysis = new { Snapshots = history, HealthScore = healthScore, Bottlenecks = bottlenecks };
            
            // Populate Metrics
            var latest = analysis.Snapshots.LastOrDefault();
            if (latest != null)
            {
                CpuUsage = latest.CpuAvailability == MetricAvailability.Measured ? $"{Math.Round(latest.CpuUtilization)}%" : "N/A";
                GpuUsage = latest.GpuAvailability == MetricAvailability.Measured ? $"{Math.Round(latest.GpuUtilization)}%" : "N/A";
                RamUsage = latest.RamAvailability == MetricAvailability.Measured ? $"{Math.Round(latest.UsedRamBytes / (1024.0 * 1024 * 1024), 1)} GB" : "N/A";
                StorageUsage = latest.StorageAvailability == MetricAvailability.Measured ? $"{Math.Round(latest.DiskActiveTimePercent)}%" : "N/A";
                Temperature = latest.ThermalAvailability == MetricAvailability.Measured ? $"{Math.Round(latest.CpuTemperature)}°C" : "N/A";
            }

            // Health
            GamingHealthScore = analysis.HealthScore.OverallScore;

            // Bottlenecks
            PrimaryBottleneckName = "None";
            PrimaryBottleneckConfidence = "N/A";
            PrimaryBottleneckExplanation = "";
            SecondaryBottleneckName = "None";
            SecondaryBottleneckConfidence = "N/A";

            if (analysis.Bottlenecks.Count > 0)
            {
                var primary = analysis.Bottlenecks[0];
                PrimaryBottleneckName = primary.Type.ToString();
                PrimaryBottleneckConfidence = primary.Confidence.ToString();
                PrimaryBottleneckExplanation = primary.Explanation;

                Recommendations.Clear();
                foreach(var rec in primary.Recommendations)
                {
                    Recommendations.Add(rec);
                }

                if (analysis.Bottlenecks.Count > 1)
                {
                    var secondary = analysis.Bottlenecks[1];
                    SecondaryBottleneckName = secondary.Type.ToString();
                    SecondaryBottleneckConfidence = secondary.Confidence.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger.Log($"Diagnostic failed: {ex.Message}", LogSeverity.Error, "UI");
        }
        finally
        {
            IsAnalyzing = false;
        }
    }
}
