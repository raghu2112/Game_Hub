using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

namespace DellGamingControlCenter.App.ViewModels;

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

public class HardwareCapabilityItem
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public bool IsSupported { get; set; }
}

public class PerformanceViewModel : BaseViewModel
{
    private readonly IChangePerformanceModeUseCase _performanceModeUseCase;
    private readonly IGameSessionManager _sessionManager;
    private readonly IDiagnosticEngine _diagnosticEngine;
    private readonly IGpuPreferenceController _gpuController;
    private readonly IDellCapabilityDetector _capabilityDetector;
    private readonly IConfigurationService _configService;
    private readonly IPowerPlanController _powerPlanController;

    private DispatcherTimer _timer;

    // Current State
    private string _currentPowerModeString = "Unknown";
    public string CurrentPowerModeString { get => _currentPowerModeString; set => SetProperty(ref _currentPowerModeString, value); }

    private string _currentGpuPreferenceString = "Unknown";
    public string CurrentGpuPreferenceString { get => _currentGpuPreferenceString; set => SetProperty(ref _currentGpuPreferenceString, value); }

    // Active Game
    private string _activeGameName = "None";
    public string ActiveGameName { get => _activeGameName; set => SetProperty(ref _activeGameName, value); }

    private string _activeProfileName = "None";
    public string ActiveProfileName { get => _activeProfileName; set => SetProperty(ref _activeProfileName, value); }

    private string _activePowerPolicy = "N/A";
    public string ActivePowerPolicy { get => _activePowerPolicy; set => SetProperty(ref _activePowerPolicy, value); }

    private string _activeGpuPolicy = "N/A";
    public string ActiveGpuPolicy { get => _activeGpuPolicy; set => SetProperty(ref _activeGpuPolicy, value); }

    private string _activePriorityPolicy = "N/A";
    public string ActivePriorityPolicy { get => _activePriorityPolicy; set => SetProperty(ref _activePriorityPolicy, value); }

    private string _activePolicyStatus = "Inactive";
    public string ActivePolicyStatus { get => _activePolicyStatus; set => SetProperty(ref _activePolicyStatus, value); }

    // Insights
    public ObservableCollection<GamingInsight> PerformanceInsights { get; } = new();

    // Status / Feedback
    private string _operationStatus = "";
    public string OperationStatus { get => _operationStatus; set => SetProperty(ref _operationStatus, value); }

    public ObservableCollection<PerformanceModeItem> Modes { get; } = new();
    public ObservableCollection<HardwareCapabilityItem> Capabilities { get; } = new();

    public ICommand ActivateModeCommand { get; }

    public PerformanceViewModel(
        IChangePerformanceModeUseCase performanceModeUseCase,
        IGameSessionManager sessionManager,
        IDiagnosticEngine diagnosticEngine,
        IGpuPreferenceController gpuController,
        IDellCapabilityDetector capabilityDetector,
        IConfigurationService configService,
        IPowerPlanController powerPlanController)
    {
        _performanceModeUseCase = performanceModeUseCase;
        _sessionManager = sessionManager;
        _diagnosticEngine = diagnosticEngine;
        _gpuController = gpuController;
        _capabilityDetector = capabilityDetector;
        _configService = configService;
        _powerPlanController = powerPlanController;

        Modes.Add(new PerformanceModeItem { Mode = PerformanceMode.Quiet, Title = "QUIET", Description = "Reduces noise by limiting fan speeds and performance.", PowerImpact = "Low", PerformanceIntent = "Minimal", Warning = "May cause stuttering in games." });
        Modes.Add(new PerformanceModeItem { Mode = PerformanceMode.BatterySaver, Title = "BATTERY SAVER", Description = "Maximizes battery life at the expense of performance.", PowerImpact = "Very Low", PerformanceIntent = "Minimal" });
        Modes.Add(new PerformanceModeItem { Mode = PerformanceMode.Balanced, Title = "BALANCED", Description = "Default Windows power behavior.", PowerImpact = "Medium", PerformanceIntent = "Standard" });
        Modes.Add(new PerformanceModeItem { Mode = PerformanceMode.Gaming, Title = "GAMING", Description = "Prioritizes Windows performance configuration and game mode.", PowerImpact = "High", PerformanceIntent = "Maximum", Warning = "May increase power consumption and heat." });
        Modes.Add(new PerformanceModeItem { Mode = PerformanceMode.MaximumPerformance, Title = "MAXIMUM PERFORMANCE", Description = "Disables all power saving features.", PowerImpact = "Very High", PerformanceIntent = "Maximum", Warning = "Not recommended for battery use." });

        ActivateModeCommand = new AsyncRelayCommand(ExecuteActivateModeAsync);

        _sessionManager.SessionChanged += (s, e) =>
        {
            // B5: also refresh the current power mode — after a session ends the policy
            // evaluator restores the previous scheme, so the active-mode highlight must
            // be re-read, not just the active-game card. B10: fire-and-forget dispatch,
            // never block a background event with a timed dispatcher wait.
            UiThread.Run(() =>
            {
                LoadCurrentMode();
                LoadActiveGameState();
            });
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (s, e) => LoadInsights();
        _timer.Start();

        LoadAllData();
    }

    private void LoadAllData()
    {
        LoadCurrentMode();
        LoadCapabilities();
        LoadActiveGameState();
        LoadInsights();
    }

    private void LoadCurrentMode()
    {
        var current = _performanceModeUseCase.GetCurrentMode();
        CurrentPowerModeString = current.ToString();

        foreach(var mode in Modes)
        {
            mode.IsActive = (mode.Mode == current);
        }

        // We only show GPU Preference if the GPU controller indicates AMD GPU is present
        // When no dGPU switch API is present we default to the discrete GPU text if supported.
        CurrentGpuPreferenceString = "Windows Default"; 
    }

    private void LoadCapabilities()
    {
        Capabilities.Clear();
        Capabilities.Add(new HardwareCapabilityItem { Name = "Power Management", Status = "Supported", IsSupported = true });
        Capabilities.Add(new HardwareCapabilityItem { Name = "GPU Preference", Status = "Supported", IsSupported = true });
        Capabilities.Add(new HardwareCapabilityItem { Name = "CPU Frequency", Status = "Unsupported", IsSupported = false });
        Capabilities.Add(new HardwareCapabilityItem { Name = "GPU Temperature", Status = "Unsupported", IsSupported = false });
        Capabilities.Add(new HardwareCapabilityItem { Name = "Fan Control", Status = "Unsupported", IsSupported = false });
        Capabilities.Add(new HardwareCapabilityItem { Name = "Thermal Control", Status = "Unsupported", IsSupported = false });
        Capabilities.Add(new HardwareCapabilityItem { Name = "Battery Threshold", Status = "Unsupported", IsSupported = false });
    }

    private void LoadActiveGameState()
    {
        var session = _sessionManager.GetActiveSession();
        if (session != null)
        {
            ActiveGameName = session.GameName;
            var profile = _configService.GetConfig().GameProfiles.FirstOrDefault(p => p.GameId == session.GameId);
            
            if (profile != null)
            {
                ActiveProfileName = profile.GameName;
                ActivePowerPolicy = profile.PerformanceMode.ToString();
                ActiveGpuPolicy = profile.PreferredGpu.ToString();
                ActivePriorityPolicy = profile.ProcessPriority.ToString();
                ActivePolicyStatus = session.CurrentState == GameSessionState.Running ? "Applied" : session.CurrentState.ToString();
            }
            else
            {
                ActiveProfileName = "Default";
                ActivePowerPolicy = "N/A";
                ActiveGpuPolicy = "N/A";
                ActivePriorityPolicy = "N/A";
                ActivePolicyStatus = "Inactive";
            }
        }
        else
        {
            ActiveGameName = "None";
            ActiveProfileName = "None";
            ActivePowerPolicy = "N/A";
            ActiveGpuPolicy = "N/A";
            ActivePriorityPolicy = "N/A";
            ActivePolicyStatus = "Inactive";
        }
    }

    private void LoadInsights()
    {
        UiThread.Run(() =>
        {
            var insights = App.GamingIntelligenceEngine.GetSystemInsights()
                .OrderByDescending(i => i.Severity)
                .ToList();

            // In-place update to avoid UI layout thrashing
            for (int i = 0; i < insights.Count; i++)
            {
                if (i < PerformanceInsights.Count)
                {
                    if (PerformanceInsights[i].Id != insights[i].Id || PerformanceInsights[i].Severity != insights[i].Severity)
                    {
                        PerformanceInsights[i] = insights[i];
                    }
                }
                else
                {
                    PerformanceInsights.Add(insights[i]);
                }
            }

            while (PerformanceInsights.Count > insights.Count)
            {
                PerformanceInsights.RemoveAt(PerformanceInsights.Count - 1);
            }
        });
    }

    private async Task ExecuteActivateModeAsync(object? parameter)
    {
        if (parameter is PerformanceMode mode)
        {
            OperationStatus = $"Applying {mode}...";
            var result = await _performanceModeUseCase.ExecuteAsync(mode);
            if (result.IsSuccess)
            {
                OperationStatus = "Applied";
                LoadCurrentMode();
            }
            else
            {
                OperationStatus = $"Failed: {result.Message}";
            }
        }
    }
}
