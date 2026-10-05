namespace DellGamingControlCenter.App.ViewModels;

using DellGamingControlCenter.Core.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public class OverlayViewModel : INotifyPropertyChanged
{
    private string _gameName = "Game";
    private string _fpsText = "N/A";
    private string _cpuText = "0%";
    private string _gpuText = "0%";
    private string _ramText = "0 GB";
    private string _tempText = "N/A";

    public string GameName { get => _gameName; set => SetProperty(ref _gameName, value); }
    public string FpsText { get => _fpsText; set => SetProperty(ref _fpsText, value); }
    public string CpuText { get => _cpuText; set => SetProperty(ref _cpuText, value); }
    public string GpuText { get => _gpuText; set => SetProperty(ref _gpuText, value); }
    public string RamText { get => _ramText; set => SetProperty(ref _ramText, value); }
    public string TempText { get => _tempText; set => SetProperty(ref _tempText, value); }

    public void UpdateFromSnapshot(GameSession session, DiagnosticSnapshot snapshot, FpsSample? fps)
    {
        GameName = session.GameName;
        CpuText = $"{snapshot.CpuUtilization:F0}%";
        GpuText = $"{snapshot.GpuUtilization:F0}%";
        RamText = $"{(snapshot.UsedRamBytes / 1024.0 / 1024.0 / 1024.0):F1} GB";
        
        if (snapshot.ThermalAvailability == Core.Enums.MetricAvailability.Measured)
            TempText = $"{snapshot.CpuTemperature:F0}°C";
            
        if (fps != null)
            FpsText = $"{fps.Fps:F0}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value)) return;
        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
