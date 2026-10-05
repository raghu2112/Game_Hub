using System.Windows;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Services;

namespace DellGamingControlCenter.App
{
    public partial class App : Application
    {
        public static ILoggingService Logger { get; private set; } = null!;
        public static IConfigurationService ConfigService { get; private set; } = null!;
        public static IHardwareDetector HardwareDetector { get; private set; } = null!;
        public static IDellCapabilityDetector DellDetector { get; private set; } = null!;
        public static IWindowsCapabilityDetector WindowsDetector { get; private set; } = null!;
        public static IGameDiscoveryService GameDiscovery { get; private set; } = null!;
        public static IOptimizationEngine OptimizationEngine { get; private set; } = null!;

        public static IPerformanceModeService PerformanceModeService { get; private set; } = null!;
        public static IBackgroundProcessManager BackgroundProcessManager { get; private set; } = null!;
        public static IMemoryOptimizationService MemoryOptimizationService { get; private set; } = null!;
        public static ISessionMonitoringService SessionMonitoringService { get; private set; } = null!;
        public static IGameLauncher GameLauncher { get; private set; } = null!;
        public static IGameSessionManager GameSessionManager { get; private set; } = null!;

        // Phase 4 Services
        public static IBottleneckAnalyzer BottleneckAnalyzer { get; private set; } = null!;
        public static IRecommendationEngine RecommendationEngine { get; private set; } = null!;
        public static IDiagnosticEngine DiagnosticEngine { get; private set; } = null!;
        public static IMetricsSampler MetricsSampler { get; private set; } = null!;
        public static ISystemHealthAnalyzer SystemHealthAnalyzer { get; private set; } = null!;
        public static ISmartOptimizer SmartOptimizer { get; private set; } = null!;
        public static IBenchmarkService BenchmarkService { get; private set; } = null!;
        public static IGameTuningService GameTuningService { get; private set; } = null!;
        public static IGamePerformanceAnalyzer GamePerformanceAnalyzer { get; private set; } = null!;
        public static IFpsProvider FpsProvider { get; private set; } = null!;

        // ViewModels
        public static ViewModels.DashboardViewModel DashboardViewModel { get; private set; } = null!;
        public static ViewModels.GameCenterViewModel GameCenterViewModel { get; private set; } = null!;
        public static ViewModels.PerformanceViewModel PerformanceViewModel { get; private set; } = null!;
        public static ViewModels.DiagnosticsViewModel DiagnosticsViewModel { get; private set; } = null!;
        public static ViewModels.SessionsViewModel SessionsViewModel { get; private set; } = null!;
        public static ViewModels.HardwareViewModel HardwareViewModel { get; private set; } = null!;
        public static ViewModels.SettingsViewModel SettingsViewModel { get; private set; } = null!;
        public static ViewModels.BenchmarkViewModel BenchmarkViewModel { get; private set; } = null!;
        public static ViewModels.MainViewModel MainViewModel { get; private set; } = null!;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Initialize Core Services
            Logger = new LoggingService();
            ConfigService = new ConfigurationService(Logger);
            HardwareDetector = new HardwareDetector(Logger);
            DellDetector = new DellCapabilityDetector(Logger);
            WindowsDetector = new WindowsCapabilityDetector(Logger);
            GameDiscovery = new GameDiscoveryService(Logger);
            OptimizationEngine = new OptimizationEngine(Logger);
            
            // Phase 4 Services (init before Phase 3 so they can be injected)
            BottleneckAnalyzer = new BottleneckAnalyzer(Logger);
            DiagnosticEngine = new DiagnosticEngine(Logger, BottleneckAnalyzer);
            MetricsSampler = new MetricsSampler(Logger, DiagnosticEngine);
            RecommendationEngine = new RecommendationEngine();
            SystemHealthAnalyzer = new SystemHealthAnalyzer();
            SmartOptimizer = new SmartOptimizer(DiagnosticEngine, RecommendationEngine, MetricsSampler, OptimizationEngine);
            BenchmarkService = new BenchmarkService();
            GameTuningService = new GameTuningService(BenchmarkService, ConfigService);
            FpsProvider = new UnavailableFpsProvider();
            GamePerformanceAnalyzer = new GamePerformanceAnalyzer(Logger, SystemHealthAnalyzer, FpsProvider);

            // Phase 3 Services (moved below Phase 4 so GamePerformanceAnalyzer is available)
            PerformanceModeService = new PerformanceModeService(Logger);
            BackgroundProcessManager = new BackgroundProcessManager(Logger, ConfigService);
            MemoryOptimizationService = new MemoryOptimizationService(Logger);
            SessionMonitoringService = new SessionMonitoringService(Logger, ConfigService);
            GameLauncher = new GameLauncher(Logger, OptimizationEngine);
            GameSessionManager = new GameSessionManager(Logger, GameLauncher, PerformanceModeService, SessionMonitoringService, BackgroundProcessManager, ConfigService, OptimizationEngine, HardwareDetector, GamePerformanceAnalyzer);

            Logger.Log("Application starting up...", Core.Enums.LogSeverity.Info, "System");

            // Initialize ViewModels
            DashboardViewModel = new ViewModels.DashboardViewModel();
            GameCenterViewModel = new ViewModels.GameCenterViewModel();
            PerformanceViewModel = new ViewModels.PerformanceViewModel();
            DiagnosticsViewModel = new ViewModels.DiagnosticsViewModel();
            SessionsViewModel = new ViewModels.SessionsViewModel();
            HardwareViewModel = new ViewModels.HardwareViewModel();
            SettingsViewModel = new ViewModels.SettingsViewModel();
            BenchmarkViewModel = new ViewModels.BenchmarkViewModel(BenchmarkService, GameTuningService, GameDiscovery, GameSessionManager);

            MainViewModel = new ViewModels.MainViewModel(
                DashboardViewModel,
                GameCenterViewModel,
                PerformanceViewModel,
                DiagnosticsViewModel,
                SessionsViewModel,
                HardwareViewModel,
                SettingsViewModel,
                BenchmarkViewModel
            );

            // Launch Main Window
            var mainWindow = new MainWindow
            {
                DataContext = MainViewModel
            };
            mainWindow.Show();
        }
    }
}
