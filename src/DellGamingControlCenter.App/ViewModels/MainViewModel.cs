namespace DellGamingControlCenter.App.ViewModels;

using System.Windows.Input;
using System.Reflection;

public class MainViewModel : BaseViewModel
{
    private BaseViewModel _currentViewModel;
    private string _version;
    private string _globalStatus = "System Ready";

    public string Version
    {
        get => _version;
        set => SetProperty(ref _version, value);
    }

    public string GlobalStatus
    {
        get => _globalStatus;
        set => SetProperty(ref _globalStatus, value);
    }

    public BaseViewModel CurrentViewModel
    {
        get => _currentViewModel;
        set
        {
            if (SetProperty(ref _currentViewModel, value))
            {
                RaiseNavState();
                OnPageEntered(value);
            }
        }
    }

    /// <summary>
    /// Page-entry hooks. Pages are long-lived singletons, so any "on page becomes
    /// visible" work must run here — otherwise it happens exactly once (at service
    /// construction) and the page goes stale for the rest of the process lifetime.
    /// </summary>
    private static void OnPageEntered(BaseViewModel viewModel)
    {
        switch (viewModel)
        {
            case DiagnosticsViewModel diagnostics:
                // Requirement: the diagnostic runs AUTOMATICALLY when the page becomes
                // visible (navigation or Dashboard VIEW DETAILS), not only on the manual
                // RUN DIAGNOSTIC button. RunDiagnosticAsync logs its own failures.
                _ = diagnostics.OnNavigatedToAsync();
                break;

            case BenchmarkViewModel benchmarks:
                // Profiles added in Game Center after startup must show up here without
                // an app restart (B7's merge previously ran only at construction).
                benchmarks.RefreshGames();
                break;
        }
    }

    // ── Top-nav / secondary-nav selection state (derived from CurrentViewModel) ──
    // Tag-bound to nav buttons; the theme's ClayNavButton lights up on Tag=True.
    public bool IsDashboardActive => ReferenceEquals(CurrentViewModel, DashboardViewModel);
    public bool IsGameCenterActive => ReferenceEquals(CurrentViewModel, GameCenterViewModel);
    public bool IsPerformanceActive => ReferenceEquals(CurrentViewModel, PerformanceViewModel);
    public bool IsDiagnosticsActive => ReferenceEquals(CurrentViewModel, DiagnosticsViewModel);
    public bool IsSessionsActive => ReferenceEquals(CurrentViewModel, SessionsViewModel);
    public bool IsHardwareActive => ReferenceEquals(CurrentViewModel, HardwareViewModel);
    public bool IsSettingsActive => ReferenceEquals(CurrentViewModel, SettingsViewModel);
    public bool IsBenchmarksActive => ReferenceEquals(CurrentViewModel, BenchmarkViewModel);

    // Section roll-ups for the contextual secondary strip
    public bool IsHomeSection => IsDashboardActive;
    public bool IsGamingSection => IsGameCenterActive || IsSessionsActive || IsBenchmarksActive;
    public bool IsPerformanceSection => IsPerformanceActive || IsDiagnosticsActive;
    public bool IsHardwareSection => IsHardwareActive;
    public bool IsSettingsSection => IsSettingsActive;

    private void RaiseNavState()
    {
        OnPropertyChanged(nameof(IsDashboardActive));
        OnPropertyChanged(nameof(IsGameCenterActive));
        OnPropertyChanged(nameof(IsPerformanceActive));
        OnPropertyChanged(nameof(IsDiagnosticsActive));
        OnPropertyChanged(nameof(IsSessionsActive));
        OnPropertyChanged(nameof(IsHardwareActive));
        OnPropertyChanged(nameof(IsSettingsActive));
        OnPropertyChanged(nameof(IsBenchmarksActive));
        OnPropertyChanged(nameof(IsHomeSection));
        OnPropertyChanged(nameof(IsGamingSection));
        OnPropertyChanged(nameof(IsPerformanceSection));
        OnPropertyChanged(nameof(IsHardwareSection));
        OnPropertyChanged(nameof(IsSettingsSection));
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
        BenchmarkViewModel benchmarkViewModel,
        Core.Interfaces.IGameSessionManager gameSessionManager)
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
        RaiseNavState(); // initial nav state (Dashboard active on launch)

        var versionObj = Assembly.GetExecutingAssembly().GetName().Version;
        _version = versionObj != null ? $"v{versionObj.Major}.{versionObj.Minor}.{versionObj.Build}" : "v1.0.0";

        NavigateDashboardCommand = new RelayCommand(_ => CurrentViewModel = DashboardViewModel);
        NavigateGameCenterCommand = new RelayCommand(_ =>
        {
            // Re-validate the library on every arrival — including re-clicking the
            // already-active tab: an executable may have been moved or deleted while
            // this page was open, and a stale "READY" badge would be dishonest.
            GameCenterViewModel.RefreshLibrary();
            CurrentViewModel = GameCenterViewModel;
        });
        NavigatePerformanceCommand = new RelayCommand(_ => CurrentViewModel = PerformanceViewModel);
        NavigateDiagnosticsCommand = new RelayCommand(_ => CurrentViewModel = DiagnosticsViewModel);
        NavigateSessionsCommand = new RelayCommand(_ => CurrentViewModel = SessionsViewModel);
        NavigateHardwareCommand = new RelayCommand(_ => CurrentViewModel = HardwareViewModel);
        NavigateSettingsCommand = new RelayCommand(_ => CurrentViewModel = SettingsViewModel);
        NavigateBenchmarksCommand = new RelayCommand(_ => CurrentViewModel = BenchmarkViewModel);

        // B10: the old handler blocked the background event thread with a timed dispatcher
        // wait and could then run the action a second time. Fire-and-forget instead.
        gameSessionManager.SessionChanged += (s, e) =>
        {
            UiThread.Run(() =>
            {
                var session = gameSessionManager.GetActiveSession();
                GlobalStatus = session != null ? "Game Active" : "System Ready";
            });
        };
    }
}
