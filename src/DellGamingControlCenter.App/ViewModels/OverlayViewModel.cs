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

    /// <summary>Rendered when a reading is genuinely unavailable. Matches the sentinel
    /// convention used everywhere else in the app (-1 CPU usage, null GPU, 0 total RAM).</summary>
    public const string UnavailableText = "N/A";

    public void UpdateFromSnapshot(GameSession session, DiagnosticSnapshot snapshot, FpsSample? fps)
    {
        GameName = session.GameName;

        // CPU: the sampler reports -1 when no counter reading exists. Formatting that as a
        // percentage would print "-1%", i.e. an invented value.
        CpuText = snapshot.CpuMetrics.UsagePercent >= 0
            ? $"{snapshot.CpuMetrics.UsagePercent:F0}%"
            : UnavailableText;

        // GPU: null means the machine exposes no usable GPU counter (common on this class of
        // laptop). Surface the real number when we have one instead of hardcoding N/A.
        GpuText = snapshot.GpuMetrics.UsagePercent is double gpuUsage
            ? $"{gpuUsage:F0}%"
            : UnavailableText;

        // RAM: UsedBytes is meaningless without a total; without the guard an unknown system
        // reads back as a confident "0.0 GB".
        RamText = snapshot.RamMetrics.TotalBytes > 0
            ? $"{(snapshot.RamMetrics.UsedBytes / 1024.0 / 1024.0 / 1024.0):F1} GB"
            : UnavailableText;

        // Temperature: prefer the CPU sensor, fall back to the GPU sensor.
        var temp = snapshot.CpuMetrics.TemperatureCelsius ?? snapshot.GpuMetrics.TemperatureCelsius;
        TempText = temp is double t ? $"{t:F0}°C" : UnavailableText;

        // FPS: overwrite on every snapshot so a stale value is never left on screen after
        // the provider stops reporting.
        FpsText = fps is { } sample ? $"{sample.Fps:F0}" : UnavailableText;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value)) return;
        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
