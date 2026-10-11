namespace DellGamingControlCenter.App.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;

public class DashboardViewModel : BaseViewModel
{
    private string _cpuUsage = "Unavailable";
    private string _cpuDetails = "Intel Core i3-7020U | 2.30 GHz | 2C / 4T";
    
    private string _gpuUsage = "Unavailable";
    private string _gpuDetails = "AMD Radeon 520";
    
    private string _ramUsage = "Unavailable";
    private string _ramDetails = "12 GB DDR4";
    
    private string _storageUsage = "Unavailable";
    
    private string _temperature = "Unsupported";
    private string _batteryStatus = "Limited";
    
    private string _gamingStatus = "No active gaming session";
    private string _performanceMode = "Default";
    
    private int _gamingHealthScore = 100;
    private string _recommendationText = "System is running optimally.";

    // Numeric percents for the clay gauges: -1 = genuinely unavailable (gauge renders "N/A", never a fabricated arc).
    private double _cpuPercent = -1;
    private double _gpuPercent = -1;
    private double _ramPercent = -1;
    public double CpuPercent { get => _cpuPercent; set => SetProperty(ref _cpuPercent, value); }
    public double GpuPercent { get => _gpuPercent; set => SetProperty(ref _gpuPercent, value); }
    public double RamPercent { get => _ramPercent; set => SetProperty(ref _ramPercent, value); }

    // Properties for UI binding
    public string CpuUsage { get => _cpuUsage; set => SetProperty(ref _cpuUsage, value); }
    public string CpuDetails { get => _cpuDetails; set => SetProperty(ref _cpuDetails, value); }
    
    public string GpuUsage { get => _gpuUsage; set => SetProperty(ref _gpuUsage, value); }
    public string GpuDetails { get => _gpuDetails; set => SetProperty(ref _gpuDetails, value); }
    
    public string RamUsage { get => _ramUsage; set => SetProperty(ref _ramUsage, value); }
    public string RamDetails { get => _ramDetails; set => SetProperty(ref _ramDetails, value); }
    
    public string StorageUsage { get => _storageUsage; set => SetProperty(ref _storageUsage, value); }
    
    public string Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }
    public string BatteryStatus { get => _batteryStatus; set => SetProperty(ref _batteryStatus, value); }
    
    public string GamingStatus { get => _gamingStatus; set => SetProperty(ref _gamingStatus, value); }
    public string PerformanceMode { get => _performanceMode; set => SetProperty(ref _performanceMode, value); }
    
    public int GamingHealthScore { get => _gamingHealthScore; set => SetProperty(ref _gamingHealthScore, value); }
    public string RecommendationText { get => _recommendationText; set => SetProperty(ref _recommendationText, value); }

    public ObservableCollection<string> RecentGames { get; } = new();
    public ObservableCollection<GamingInsight> GamingInsights { get; } = new();

    public ICommand ChangeModeCommand { get; }
    public ICommand ViewDiagnosticsCommand { get; }
    public ICommand ViewRecommendationCommand { get; }

    public DashboardViewModel()
    {
        ChangeModeCommand = new RelayCommand(_ => App.MainViewModel.NavigatePerformanceCommand.Execute(null));
        ViewDiagnosticsCommand = new RelayCommand(_ => App.MainViewModel.NavigateDiagnosticsCommand.Execute(null));
        ViewRecommendationCommand = new RelayCommand(_ => App.MainViewModel.NavigateGameCenterCommand.Execute(null));

        // Subscriptions
        App.MetricsSampler.SnapshotCaptured += OnSnapshotCaptured;
        App.GameSessionManager.SessionChanged += OnSessionChanged;
        
        RefreshStaticData();
    }

    private void RefreshStaticData()
    {
        // Populate initial static details if possible
        var profiles = App.HardwareProfileProvider.GetProfile();
        if (profiles.Cpu != null)
        {
            CpuDetails = $"{profiles.Cpu.Model} | {profiles.Cpu.BaseClockMHz / 1000.0:F2} GHz | {profiles.Cpu.Cores}C / {profiles.Cpu.Threads}T";
        }
        
        if (profiles.Memory != null)
        {
            RamDetails = $"{Math.Round(profiles.Memory.TotalBytes / (1024.0 * 1024 * 1024), 1)} GB";
        }

        var dgpu = profiles.Gpus.FirstOrDefault(g => g.Type == Core.Enums.GpuType.Discrete);
        if (dgpu != null)
        {
            GpuDetails = dgpu.Name;
        }

        PerformanceMode = App.ChangePerformanceModeUseCase.GetCurrentMode().ToString();

        RefreshInsights();
    }

    private void OnSnapshotCaptured(object? sender, DiagnosticSnapshot snapshot)
    {
        RunOnUIThread(() => 
        {
            // CPU
            if (snapshot.CpuMetrics.UsagePercent >= 0)
            {
                CpuUsage = $"{Math.Round(snapshot.CpuMetrics.UsagePercent)}%";
                CpuPercent = snapshot.CpuMetrics.UsagePercent;
            }
            else
            {
                CpuUsage = "Unavailable";
                CpuPercent = -1;
            }

            // RAM
            if (snapshot.RamMetrics.TotalBytes > 0)
            {
                double usedGb = snapshot.RamMetrics.UsedBytes / (1024.0 * 1024 * 1024);
                double totalGb = snapshot.RamMetrics.TotalBytes / (1024.0 * 1024 * 1024);
                double percent = (snapshot.RamMetrics.UsedBytes / (double)snapshot.RamMetrics.TotalBytes) * 100;
                RamUsage = $"{Math.Round(percent)}% ({Math.Round(usedGb, 1)} GB used)";
                RamPercent = percent;
            }
            else
            {
                RamPercent = -1;
            }
            
            // GPU
            if (snapshot.GpuMetrics.UsagePercent.HasValue)
            {
                GpuUsage = $"{Math.Round(snapshot.GpuMetrics.UsagePercent.Value)}%";
                GpuPercent = snapshot.GpuMetrics.UsagePercent.Value;
            }
            else
            {
                GpuPercent = -1;
            }

            // Storage (B12: real free-space from the existing storage telemetry service)
            try
            {
                // IL3000-safe: AppContext.BaseDirectory works under single-file publish.
                var exeRoot = Path.GetPathRoot(Environment.ProcessPath) ?? Path.GetPathRoot(AppContext.BaseDirectory) ?? "C:\\";
                var volume = App.StorageTelemetry.GetVolumes()
                    .FirstOrDefault(v => v.DriveLetter?.TrimEnd('\\').Equals(exeRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) == true)
                    ?? App.StorageTelemetry.GetVolumes().FirstOrDefault();
                if (volume != null && volume.SizeBytes > 0)
                {
                    double freeGb = volume.FreeSpaceBytes / (1024.0 * 1024 * 1024);
                    double totalGb = volume.SizeBytes / (1024.0 * 1024 * 1024);
                    StorageUsage = $"{Math.Round(totalGb - freeGb)} / {Math.Round(totalGb)} GB used";
                }
                else
                {
                    StorageUsage = "Unavailable";
                }
            }
            catch
            {
                StorageUsage = "Unavailable";
            }

            // Battery (B1: -1 means genuinely unavailable, never render it as a value)
            if (snapshot.BatteryMetrics.ChargePercent >= 0)
            {
                BatteryStatus = $"{Math.Round((double)snapshot.BatteryMetrics.ChargePercent)}% ({(snapshot.BatteryMetrics.IsCharging ? "Charging" : (snapshot.BatteryMetrics.IsAcConnected ? "AC Connected" : "On Battery"))})";
            }
            else
            {
                BatteryStatus = "Unavailable";
            }

            // Diagnostics / Recommendations
            RecommendationText = "System is running optimally.";
            GamingHealthScore = 100;
            
            // Authoritative thermal reading with honest states
            if (snapshot.ThermalMetrics?.CpuTemperature.HasValue == true)
            {
                Temperature = $"{Math.Round(snapshot.ThermalMetrics.CpuTemperature.Value)}°C";
            }
            else if (snapshot.CpuMetrics?.TemperatureCelsius.HasValue == true)
            {
                Temperature = $"{Math.Round(snapshot.CpuMetrics.TemperatureCelsius.Value)}°C";
            }
            else if (snapshot.ThermalMetrics?.AmbientTemperature.HasValue == true)
            {
                Temperature = $"{Math.Round(snapshot.ThermalMetrics.AmbientTemperature.Value)}°C (ACPI)";
            }
            else if (snapshot.ThermalMetrics?.CpuTemperatureState == Core.Enums.SensorReadingState.Unavailable ||
                     snapshot.CpuMetrics?.TemperatureState == Core.Enums.SensorReadingState.Unavailable)
            {
                Temperature = "Unavailable";
            }
            else
            {
                Temperature = "Unsupported";
            }
        });
    }

    private void RunOnUIThread(Action action)
    {
        if (Application.Current?.Dispatcher != null)
        {
            if (Application.Current.Dispatcher.Thread == System.Threading.Thread.CurrentThread)
            {
                action();
            }
            else
            {
                try 
                { 
                    // Prevent deadlocks in test environments where dispatcher loop isn't running
                    var operation = Application.Current.Dispatcher.InvokeAsync(action);
                    operation.Wait(TimeSpan.FromMilliseconds(500));
                    if (operation.Status != System.Windows.Threading.DispatcherOperationStatus.Completed) action();
                } 
                catch { action(); }
            }
        }
        else
        {
            action();
        }
    }

    private void OnSessionChanged(object? sender, GameSessionEventArgs e)
    {
        RunOnUIThread(() => 
        {
            var session = App.GameSessionManager.GetActiveSession();
            if (session != null)
            {
                GamingStatus = $"Playing: {session.GameName}";
                PerformanceMode = App.ChangePerformanceModeUseCase.GetCurrentMode().ToString(); // Might have changed via policy
            }
            else
            {
                GamingStatus = "No active gaming session";
                PerformanceMode = App.ChangePerformanceModeUseCase.GetCurrentMode().ToString();
                
                // Refresh insights when session completes
                RefreshInsights();
            }
        });
    }

    private void RefreshInsights()
    {
        RunOnUIThread(() =>
        {
            var insights = App.GamingIntelligenceEngine.GetSystemInsights()
                .OrderByDescending(i => i.Severity)
                .Take(3)
                .ToList();

            // In-place update to avoid UI layout thrashing
            for (int i = 0; i < insights.Count; i++)
            {
                if (i < GamingInsights.Count)
                {
                    if (GamingInsights[i].Id != insights[i].Id || GamingInsights[i].Severity != insights[i].Severity)
                    {
                        GamingInsights[i] = insights[i];
                    }
                }
                else
                {
                    GamingInsights.Add(insights[i]);
                }
            }

            while (GamingInsights.Count > insights.Count)
            {
                GamingInsights.RemoveAt(GamingInsights.Count - 1);
            }
        });
    }
}
