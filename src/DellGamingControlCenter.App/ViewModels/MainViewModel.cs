namespace DellGamingControlCenter.App.ViewModels;

using System.Windows.Input;
using System.Reflection;

public class MainViewModel : BaseViewModel
{
    private BaseViewModel _currentViewModel;
    private string _version;

    public string Version
    {
        get => _version;
        set => SetProperty(ref _version, value);
    }

    public BaseViewModel CurrentViewModel
    {
        get => _currentViewModel;
        set => SetProperty(ref _currentViewModel, value);
    }

    public ICommand NavigateDashboardCommand { get; }
    public ICommand NavigateGameCenterCommand { get; }
    public ICommand NavigatePerformanceCommand { get; }
    public ICommand NavigateDiagnosticsCommand { get; }
    public ICommand NavigateSessionsCommand { get; }
    public ICommand NavigateHardwareCommand { get; }
    public ICommand NavigateSettingsCommand { get; }
    public ICommand NavigateBenchmarksCommand { get; }

    // Child ViewModels
    public DashboardViewModel DashboardViewModel { get; }
    public GameCenterViewModel GameCenterViewModel { get; }
    public PerformanceViewModel PerformanceViewModel { get; }
    public DiagnosticsViewModel DiagnosticsViewModel { get; }
    public SessionsViewModel SessionsViewModel { get; }
    public HardwareViewModel HardwareViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public BenchmarkViewModel BenchmarkViewModel { get; }

    public MainViewModel(
        DashboardViewModel dashboardViewModel,
        GameCenterViewModel gameCenterViewModel,
        PerformanceViewModel performanceViewModel,
        DiagnosticsViewModel diagnosticsViewModel,
        SessionsViewModel sessionsViewModel,
        HardwareViewModel hardwareViewModel,
        SettingsViewModel settingsViewModel,
        BenchmarkViewModel benchmarkViewModel)
    {
        DashboardViewModel = dashboardViewModel;
        GameCenterViewModel = gameCenterViewModel;
        PerformanceViewModel = performanceViewModel;
        DiagnosticsViewModel = diagnosticsViewModel;
        SessionsViewModel = sessionsViewModel;
        HardwareViewModel = hardwareViewModel;
        SettingsViewModel = settingsViewModel;
        BenchmarkViewModel = benchmarkViewModel;

        _currentViewModel = DashboardViewModel;

        var versionObj = Assembly.GetExecutingAssembly().GetName().Version;
        _version = versionObj != null ? $"v{versionObj.Major}.{versionObj.Minor}.{versionObj.Build}" : "v1.0.0";

        NavigateDashboardCommand = new RelayCommand(_ => CurrentViewModel = DashboardViewModel);
        NavigateGameCenterCommand = new RelayCommand(_ => CurrentViewModel = GameCenterViewModel);
        NavigatePerformanceCommand = new RelayCommand(_ => CurrentViewModel = PerformanceViewModel);
        NavigateDiagnosticsCommand = new RelayCommand(_ => CurrentViewModel = DiagnosticsViewModel);
        NavigateSessionsCommand = new RelayCommand(_ => CurrentViewModel = SessionsViewModel);
        NavigateHardwareCommand = new RelayCommand(_ => CurrentViewModel = HardwareViewModel);
        NavigateSettingsCommand = new RelayCommand(_ => CurrentViewModel = SettingsViewModel);
        NavigateBenchmarksCommand = new RelayCommand(_ => CurrentViewModel = BenchmarkViewModel);
    }
}
