namespace DellGamingControlCenter.App.ViewModels;

using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Interfaces;

public class SmartOptimizerViewModel : BaseViewModel
{
    private string _gameName = "";
    private bool _isScanning;
    private bool _hasPlan;
    private bool _isApplying;
    private string _statusText = "";

    public string GameName { get => _gameName; set => SetProperty(ref _gameName, value); }
    public bool IsScanning { get => _isScanning; set => SetProperty(ref _isScanning, value); }
    public bool HasPlan { get => _hasPlan; set => SetProperty(ref _hasPlan, value); }
    public bool IsApplying { get => _isApplying; set => SetProperty(ref _isApplying, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public ObservableCollection<OptimizationAction> PlanActions { get; } = new();

    public ICommand ScanCommand { get; }
    public ICommand ApplyCommand { get; }

    public SmartOptimizerViewModel()
    {
        ScanCommand = new AsyncRelayCommand(async _ => await ScanSystemAsync(), _ => !IsScanning && !IsApplying);
        ApplyCommand = new AsyncRelayCommand(async _ => await ApplyOptimizationAsync(), _ => HasPlan && !IsApplying);
    }

    public void Initialize(string gameName)
    {
        GameName = gameName;
        StatusText = "Ready to scan.";
        PlanActions.Clear();
        HasPlan = false;
    }

    private async Task ScanSystemAsync()
    {
        IsScanning = true;
        StatusText = "Collecting system metrics...";
        await Task.Delay(500);
        StatusText = "Analyzing memory...";
        await Task.Delay(500);
        StatusText = "Building recommendations...";
        await Task.Delay(500);
        var profile = new GameProfile { GameName = GameName, GameId = GameName };
        var plan = App.OptimizationEngine.GeneratePlan(profile);
        
        PlanActions.Clear();
        foreach(var action in plan.Actions)
        {
            PlanActions.Add(action);
        }
        
        HasPlan = plan.Actions.Count > 0;
        IsScanning = false;
        StatusText = HasPlan ? "Scan complete. Review the optimization plan below." : "No optimizations required.";
    }

    private async Task ApplyOptimizationAsync()
    {
        IsApplying = true;
        StatusText = "Applying optimizations...";
        
        var profile = new GameProfile { GameName = GameName, GameId = GameName };
        var plan = App.OptimizationEngine.GeneratePlan(profile);
        bool success = await App.OptimizationEngine.ApplyPlanAsync(plan);
        
        StatusText = success ? "Optimization complete." : "Optimization applied with some failures.";
        IsApplying = false;
        HasPlan = false;
    }
}
