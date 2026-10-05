namespace DellGamingControlCenter.App.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

public class DashboardViewModel : BaseViewModel
{
    private string _cpuUsage = "N/A";
    private string _gpuUsage = "N/A";
    private string _ramUsage = "N/A";
    private string _temperature = "N/A";
    private string _gamingStatus = "No game running";
    private string _performanceMode = "Gaming";
    private int _gamingHealthScore = 100;
    private string _recommendationText = "System is running optimally.";

    public string CpuUsage { get => _cpuUsage; set => SetProperty(ref _cpuUsage, value); }
    public string GpuUsage { get => _gpuUsage; set => SetProperty(ref _gpuUsage, value); }
    public string RamUsage { get => _ramUsage; set => SetProperty(ref _ramUsage, value); }
    public string Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }
    
    public string GamingStatus { get => _gamingStatus; set => SetProperty(ref _gamingStatus, value); }
    public string PerformanceMode { get => _performanceMode; set => SetProperty(ref _performanceMode, value); }
    public int GamingHealthScore { get => _gamingHealthScore; set => SetProperty(ref _gamingHealthScore, value); }
    public string RecommendationText { get => _recommendationText; set => SetProperty(ref _recommendationText, value); }

    public ObservableCollection<string> RecentGames { get; } = new();

    public ICommand ChangeModeCommand { get; }
    public ICommand ViewDiagnosticsCommand { get; }
    public ICommand ViewRecommendationCommand { get; }

    public DashboardViewModel()
    {
        ChangeModeCommand = new RelayCommand(_ => App.MainViewModel.NavigatePerformanceCommand.Execute(null));
        ViewDiagnosticsCommand = new RelayCommand(_ => App.MainViewModel.NavigateDiagnosticsCommand.Execute(null));
        ViewRecommendationCommand = new RelayCommand(_ => App.MainViewModel.NavigateGameCenterCommand.Execute(null));
    }

    public void UpdateDashboard(DiagnosticSnapshot snapshot, GamingHealthScore health, BottleneckResult primaryBottleneck)
    {
        CpuUsage = snapshot.CpuAvailability == MetricAvailability.Measured ? $"{Math.Round(snapshot.CpuUtilization)}%" : "N/A";
        GpuUsage = snapshot.GpuAvailability == MetricAvailability.Measured ? $"{Math.Round(snapshot.GpuUtilization)}%" : "N/A";
        
        if (snapshot.RamAvailability == MetricAvailability.Measured)
        {
            RamUsage = $"{Math.Round(snapshot.UsedRamBytes / (1024.0 * 1024 * 1024), 1)} / {Math.Round(snapshot.TotalRamBytes / (1024.0 * 1024 * 1024), 1)} GB";
        }
        else
        {
            RamUsage = "N/A";
        }

        Temperature = snapshot.ThermalAvailability == MetricAvailability.Measured ? $"{Math.Round(snapshot.CpuTemperature)}°C" : "N/A";
        
        if (health != null)
        {
            GamingHealthScore = health.OverallScore;
        }

        if (primaryBottleneck != null && primaryBottleneck.Type != BottleneckType.None)
        {
            RecommendationText = primaryBottleneck.Explanation;
        }
        else
        {
            RecommendationText = "System is running optimally.";
        }
    }
}
