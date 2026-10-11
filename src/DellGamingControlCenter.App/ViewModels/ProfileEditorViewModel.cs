namespace DellGamingControlCenter.App.ViewModels;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

public class ProfileEditorViewModel : BaseViewModel
{
    private GameProfile _profile;
    private readonly Action _onSave;
    private readonly Action _onCancel;

    public string GameName => _profile.GameName;
    public string ExecutablePath => _profile.ExecutablePath;

    public IEnumerable<GpuPreference> GpuPreferences => Enum.GetValues<GpuPreference>();
    public IEnumerable<PerformanceMode> PerformanceModes => Enum.GetValues<PerformanceMode>();
    public IEnumerable<ProcessPriorityClass> ProcessPriorities => Enum.GetValues<ProcessPriorityClass>();

    public GpuPreference SelectedGpuPreference
    {
        get => _profile.PreferredGpu;
        set { _profile.PreferredGpu = value; OnPropertyChanged(); }
    }

    public PerformanceMode SelectedPerformanceMode
    {
        get => _profile.PerformanceMode;
        set { _profile.PerformanceMode = value; OnPropertyChanged(); }
    }

    public ProcessPriorityClass SelectedProcessPriority
    {
        get => _profile.ProcessPriority;
        set { _profile.ProcessPriority = value; OnPropertyChanged(); }
    }

    public string LaunchArguments
    {
        get => _profile.LaunchArguments ?? "";
        set { _profile.LaunchArguments = value; OnPropertyChanged(); }
    }

    public bool IsGpuPreferenceSupported => App.GpuPreferenceController.Status == CapabilityStatus.Supported;
    public string UnsupportedGpuVisibility => IsGpuPreferenceSupported ? "Collapsed" : "Visible";

    // The requirements specify indicating unsupported capabilities:
    // "For unsupported capabilities: Fan Control - Unavailable on this device"
    public string FanControlStatus => "Unavailable on this device";
    public string UndervoltStatus => "Unavailable on this device";
    public string ThermalControlStatus => "Unavailable on this device";

    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }
    private string _errorMessage = "";

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public ProfileEditorViewModel(GameProfile profile, Action onSave, Action onCancel)
    {
        _profile = profile;
        _onSave = onSave;
        _onCancel = onCancel;

        SaveCommand = new RelayCommand(_ => {
            ErrorMessage = "";
            
            if (string.IsNullOrWhiteSpace(_profile.GameName))
            {
                ErrorMessage = "Game name cannot be empty.";
                return;
            }
            if (string.IsNullOrWhiteSpace(_profile.ExecutablePath))
            {
                ErrorMessage = "Executable path cannot be empty.";
                return;
            }
            
            if (!IsGpuPreferenceSupported && _profile.PreferredGpu != GpuPreference.Auto)
            {
                _profile.PreferredGpu = GpuPreference.Auto;
                OnPropertyChanged(nameof(SelectedGpuPreference));
            }

            App.ConfigService.SaveGameProfile(_profile);
            _onSave();
        });
        CancelCommand = new RelayCommand(_ => _onCancel());
    }
}
