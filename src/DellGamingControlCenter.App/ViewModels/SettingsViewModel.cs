namespace DellGamingControlCenter.App.ViewModels;

public class SettingsViewModel : BaseViewModel
{
    private bool _startWithWindows;
    private bool _startMinimized;
    private bool _systemTray = true;
    
    private bool _autoOptimization = true;
    private bool _autoPerformanceMode = true;
    private bool _autoBackgroundManagement = true;
    private bool _autoRestore = true;

    private bool _requireOptimizationConfirmation = true;
    private bool _requireClosingConfirmation = true;

    private string _samplingInterval = "1 sec";
    private string _logLevel = "Info";

    public bool StartWithWindows { get => _startWithWindows; set => SetProperty(ref _startWithWindows, value); }
    public bool StartMinimized { get => _startMinimized; set => SetProperty(ref _startMinimized, value); }
    public bool SystemTray { get => _systemTray; set => SetProperty(ref _systemTray, value); }

    public bool AutoOptimization { get => _autoOptimization; set => SetProperty(ref _autoOptimization, value); }
    public bool AutoPerformanceMode { get => _autoPerformanceMode; set => SetProperty(ref _autoPerformanceMode, value); }
    public bool AutoBackgroundManagement { get => _autoBackgroundManagement; set => SetProperty(ref _autoBackgroundManagement, value); }
    public bool AutoRestore { get => _autoRestore; set => SetProperty(ref _autoRestore, value); }

    public bool RequireOptimizationConfirmation { get => _requireOptimizationConfirmation; set => SetProperty(ref _requireOptimizationConfirmation, value); }
    public bool RequireClosingConfirmation { get => _requireClosingConfirmation; set => SetProperty(ref _requireClosingConfirmation, value); }

    public string[] SamplingIntervals { get; } = new[] { "500 ms", "1 sec", "2 sec", "5 sec" };
    public string SamplingInterval { get => _samplingInterval; set => SetProperty(ref _samplingInterval, value); }

    public string[] LogLevels { get; } = new[] { "Debug", "Info", "Warning", "Error" };
    public string LogLevel { get => _logLevel; set => SetProperty(ref _logLevel, value); }
}
