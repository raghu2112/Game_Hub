namespace DellGamingControlCenter.App.ViewModels;

using System;
using System.Linq;
using DellGamingControlCenter.Core.Enums;

public class SettingsViewModel : BaseViewModel
{
    private bool _loading;
    private bool _systemTray = true;

    // Persisted in config.json (real behavior — these map to AppConfiguration fields).
    private bool _autoOptimization;
    private bool _autoPerformanceMode;
    private bool _autoBackgroundManagement;
    private bool _autoRestore = true;
    private bool _requireOptimizationConfirmation = true;
    private bool _requireClosingConfirmation = true;
    private string _samplingInterval = "2 sec";

    public bool SystemTray { get => _systemTray; set => SetProperty(ref _systemTray, value); }

    public bool AutoOptimization { get => _autoOptimization; set { if (SetProperty(ref _autoOptimization, value)) Persist(); } }
    public bool AutoPerformanceMode { get => _autoPerformanceMode; set { if (SetProperty(ref _autoPerformanceMode, value)) Persist(); } }
    public bool AutoBackgroundManagement { get => _autoBackgroundManagement; set { if (SetProperty(ref _autoBackgroundManagement, value)) Persist(); } }
    public bool AutoRestore { get => _autoRestore; set { if (SetProperty(ref _autoRestore, value)) Persist(); } }

    public bool RequireOptimizationConfirmation { get => _requireOptimizationConfirmation; set { if (SetProperty(ref _requireOptimizationConfirmation, value)) Persist(); } }
    public bool RequireClosingConfirmation { get => _requireClosingConfirmation; set { if (SetProperty(ref _requireClosingConfirmation, value)) Persist(); } }

    public string[] SamplingIntervals { get; } = new[] { "1 sec", "2 sec", "5 sec" };
    public string SamplingInterval
    {
        get => _samplingInterval;
        set
        {
            if (SetProperty(ref _samplingInterval, value))
            {
                Persist();
            }
        }
    }

    // Load-only information (StartWithWindows needs a registry run key + uninstaller
    // coordination; LogLevel is fixed at Info by design) — shown but not fake-editable.
    public bool StartWithWindows { get; }
    public bool StartMinimized { get; }
    public string[] LogLevels { get; } = { "Info" };
    public string LogLevel { get; } = "Info";

    public SettingsViewModel()
    {
        // StartWithWindows reflects the real registry Run key state.
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            StartWithWindows = key?.GetValue("DellGamingControlCenter") != null;
        }
        catch
        {
            StartWithWindows = false;
        }
        StartMinimized = false;

        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        _loading = true;
        try
        {
            var cfg = App.ConfigService.GetConfig();
            _autoOptimization = cfg.AutomaticOptimization;
            _autoPerformanceMode = cfg.AutomaticPerformanceMode;
            _autoBackgroundManagement = cfg.AutomaticBackgroundManagement;
            _autoRestore = cfg.AutomaticRestore;
            _requireOptimizationConfirmation = cfg.RequireOptimizationConfirmation;
            _requireClosingConfirmation = cfg.RequireBackgroundProcessConfirmation;

            var ms = cfg.MonitoringIntervalMs;
            _samplingInterval = ms <= 1000 ? "1 sec" : ms <= 2000 ? "2 sec" : "5 sec";

            OnPropertyChanged(nameof(AutoOptimization));
            OnPropertyChanged(nameof(AutoPerformanceMode));
            OnPropertyChanged(nameof(AutoBackgroundManagement));
            OnPropertyChanged(nameof(AutoRestore));
            OnPropertyChanged(nameof(RequireOptimizationConfirmation));
            OnPropertyChanged(nameof(RequireClosingConfirmation));
            OnPropertyChanged(nameof(SamplingInterval));
        }
        finally
        {
            _loading = false;
        }
    }

    private void Persist()
    {
        if (_loading)
        {
            return;
        }

        var cfg = App.ConfigService.GetConfig();
        cfg.AutomaticOptimization = _autoOptimization;
        cfg.AutomaticPerformanceMode = _autoPerformanceMode;
        cfg.AutomaticBackgroundManagement = _autoBackgroundManagement;
        cfg.AutomaticRestore = _autoRestore;
        cfg.RequireOptimizationConfirmation = _requireOptimizationConfirmation;
        cfg.RequireBackgroundProcessConfirmation = _requireClosingConfirmation;
        cfg.MonitoringIntervalMs = _samplingInterval switch
        {
            "1 sec" => 1000,
            "5 sec" => 5000,
            _ => 2000
        };

        App.ConfigService.SaveConfig(cfg);

        // Apply the new sampling interval live so the change takes effect without a restart.
        if (App.MetricsSampler != null)
        {
            App.MetricsSampler.Stop();
            App.MetricsSampler.Start(TimeSpan.FromMilliseconds(cfg.MonitoringIntervalMs));
        }
    }
}
