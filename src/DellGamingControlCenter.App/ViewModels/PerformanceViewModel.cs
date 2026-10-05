namespace DellGamingControlCenter.App.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using DellGamingControlCenter.Core.Enums;

public class PerformanceModeItem : BaseViewModel
{
    public PerformanceMode Mode { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string PowerImpact { get; set; } = "";
    public string PerformanceIntent { get; set; } = "";
    public string Warning { get; set; } = "";
    
    private bool _isActive;
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
}

public class PerformanceViewModel : BaseViewModel
{
    private PerformanceModeItem? _currentMode;
    public PerformanceModeItem? CurrentMode { get => _currentMode; set => SetProperty(ref _currentMode, value); }
    
    public ObservableCollection<PerformanceModeItem> Modes { get; } = new();

    public ICommand ActivateModeCommand { get; }

    public PerformanceViewModel()
    {
        Modes.Add(new PerformanceModeItem { Mode = PerformanceMode.Quiet, Title = "QUIET", Description = "Reduces noise by limiting fan speeds and performance.", PowerImpact = "Low", PerformanceIntent = "Minimal", Warning = "May cause stuttering in games." });
        Modes.Add(new PerformanceModeItem { Mode = PerformanceMode.BatterySaver, Title = "BATTERY SAVER", Description = "Maximizes battery life at the expense of performance.", PowerImpact = "Very Low", PerformanceIntent = "Minimal" });
        Modes.Add(new PerformanceModeItem { Mode = PerformanceMode.Balanced, Title = "BALANCED", Description = "Default Windows power behavior.", PowerImpact = "Medium", PerformanceIntent = "Standard" });
        Modes.Add(new PerformanceModeItem { Mode = PerformanceMode.Gaming, Title = "GAMING", Description = "Prioritizes Windows performance configuration and game mode.", PowerImpact = "High", PerformanceIntent = "Maximum", Warning = "May increase power consumption and heat." });
        Modes.Add(new PerformanceModeItem { Mode = PerformanceMode.MaximumPerformance, Title = "MAXIMUM PERFORMANCE", Description = "Disables all power saving features.", PowerImpact = "Very High", PerformanceIntent = "Maximum", Warning = "Not recommended for battery use." });

        ActivateModeCommand = new AsyncRelayCommand(ExecuteActivateModeAsync);

        // Load current mode
        LoadCurrentMode();
    }

    private void LoadCurrentMode()
    {
        var current = App.PerformanceModeService.GetCurrentMode();
        foreach(var mode in Modes)
        {
            mode.IsActive = (mode.Mode == current);
            if (mode.IsActive) CurrentMode = mode;
        }
    }

    private async Task ExecuteActivateModeAsync(object? parameter)
    {
        if (parameter is PerformanceMode mode)
        {
            await App.PerformanceModeService.ApplyModeAsync(mode);
            LoadCurrentMode();
        }
    }
}
