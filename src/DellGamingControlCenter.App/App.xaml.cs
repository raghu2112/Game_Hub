using System.Windows;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Services;

namespace DellGamingControlCenter.App
{
    public partial class App : Application
    {
        /// <summary>Set when OnExit begins so late UI marshal work (message boxes, refreshes) can stand down.</summary>
        public static bool IsShuttingDown { get; private set; }

        // Single-instance guard: two concurrent instances clobber each other's writes to the
        // shared %LocalAppData% files (activity_log.json, config.json) — last writer wins and
        // the other's entries vanish. Observed live during QA when a forgotten instance from a
        // previous run silently corrupted the audit run's log.
        private System.Threading.Mutex? _singleInstanceMutex;

        public static ILoggingService Logger { get; private set; } = null!;
        public static IConfigurationService ConfigService { get; private set; } = null!;
        public static IDellCapabilityDetector DellDetector { get; private set; } = null!;
        public static IWindowsCapabilityDetector WindowsDetector { get; private set; } = null!;
        
        // HAL
        public static ICpuTelemetryService CpuTelemetry { get; private set; } = null!;
        public static IMemoryTelemetryService MemoryTelemetry { get; private set; } = null!;
        public static IGpuTelemetryService GpuTelemetry { get; private set; } = null!;
        public static IStorageTelemetryService StorageTelemetry { get; private set; } = null!;
        public static IBatteryTelemetryService BatteryTelemetry { get; private set; } = null!;
        public static IThermalTelemetryService ThermalTelemetry { get; private set; } = null!;
        public static IHardwareTelemetryProvider HardwareTelemetryProvider { get; private set; } = null!;
        public static IProcessController ProcessController { get; private set; } = null!;
        public static IGpuPreferenceController GpuPreferenceController { get; private set; } = null!;
        public static IHardwareProfileProvider HardwareProfileProvider { get; private set; } = null!;
        
        public static IGameDiscoveryService GameDiscovery { get; private set; } = null!;

        public static IPerformanceModeService PerformanceModeService { get; private set; } = null!;
        public static IBackgroundProcessManager BackgroundProcessManager { get; private set; } = null!;
        public static IMemoryOptimizationService MemoryOptimizationService { get; private set; } = null!;
        public static ISessionMonitoringService SessionMonitoringService { get; private set; } = null!;
        public static IGameLauncher GameLauncher { get; private set; } = null!;
        public static IGameSessionManager GameSessionManager { get; private set; } = null!;

        public static IWindowsPowerPlanService WindowsPowerPlanService { get; private set; } = null!;
        public static IControlAuditLogger ControlAuditLogger { get; private set; } = null!;
        public static IAuthorizationService AuthorizationService { get; private set; } = null!;
        public static IChangePerformanceModeUseCase ChangePerformanceModeUseCase { get; private set; } = null!;

        // Phase 4 Services
        // Removed BottleneckAnalyzer
        public static IRecommendationEngine RecommendationEngine { get; private set; } = null!;
        public static IDiagnosticEngine DiagnosticEngine { get; private set; } = null!;
        public static IMetricsSampler MetricsSampler { get; private set; } = null!;
        public static ISystemHealthAnalyzer SystemHealthAnalyzer { get; private set; } = null!;
        public static ISmartOptimizer SmartOptimizer { get; private set; } = null!;
        public static IBenchmarkService BenchmarkService { get; private set; } = null!;
        public static IGameTuningService GameTuningService { get; private set; } = null!;
        public static IGamePerformanceAnalyzer GamePerformanceAnalyzer { get; private set; } = null!;
        public static IFpsProvider FpsProvider { get; private set; } = null!;
        public static IGamingIntelligenceEngine GamingIntelligenceEngine { get; private set; } = null!;

        // ViewModels
        public static ViewModels.DashboardViewModel DashboardViewModel { get; private set; } = null!;

        // Live graph histories: 60 s rolling window fed ONLY by the authoritative
        // MetricsSampler pipeline (2 s cadence). null = unavailable this tick (gap, never 0%).
        public static Core.Models.TelemetryHistory CpuHistory { get; } = new(30);
        public static Core.Models.TelemetryHistory RamHistory { get; } = new(30);
        public static Core.Models.TelemetryHistory GpuHistory { get; } = new(30);
        public static ViewModels.GameCenterViewModel GameCenterViewModel { get; private set; } = null!;
        public static ViewModels.PerformanceViewModel PerformanceViewModel { get; private set; } = null!;
        public static ViewModels.DiagnosticsViewModel DiagnosticsViewModel { get; private set; } = null!;
        public static ViewModels.SessionsViewModel SessionsViewModel { get; private set; } = null!;
        public static ViewModels.HardwareViewModel HardwareViewModel { get; private set; } = null!;
        public static ViewModels.SettingsViewModel SettingsViewModel { get; private set; } = null!;
        public static ViewModels.BenchmarkViewModel BenchmarkViewModel { get; private set; } = null!;
        public static ViewModels.MainViewModel MainViewModel { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new System.Threading.Mutex(true, @"Local\GamerHub.SingleInstance", out var firstInstance);
            if (!firstInstance)
            {
                // A GamerHub instance is already running: bring its window to the front
                // instead of silently spawning a competing one.
                ActivateExistingInstance();
                Shutdown();
                return;
            }
            base.OnStartup(e);
        }

        private static void ActivateExistingInstance()
        {
            try
            {
                var current = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                foreach (var proc in System.Diagnostics.Process.GetProcessesByName(current))
                {
                    if (proc.Id == Environment.ProcessId || proc.MainWindowHandle == IntPtr.Zero) continue;
                    ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(proc.MainWindowHandle);
                    break;
                }
            }
            catch
            {
                // Best-effort activation; the existing instance stays where it is otherwise.
            }
        }

        private const int SW_RESTORE = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Allow software rendering so off-screen and headless captures (PrintWindow) can render WPF visual tree
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            // Phase 2: Global exception handlers - never die silently, always leave a diagnostic trail.
            DispatcherUnhandledException += (s, args) =>
            {
                // Walk to the root cause: XamlParseException wraps the real missing-resource
                // error several layers deep, and the outer message alone is useless for diagnosis.
                var root = args.Exception;
                var depth = 0;
                while (root.InnerException != null && depth++ < 10)
                {
                    root = root.InnerException;
                }
                Logger?.Log($"UI thread exception: {args.Exception.GetType().Name}: {args.Exception.Message}\nROOT CAUSE: {root.GetType().Name}: {root.Message}\n{args.Exception.StackTrace}", Core.Enums.LogSeverity.Error, "System");
                MessageBox.Show($"An unexpected error occurred and was logged.\n\n{root.Message}", "GamerHub", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true; // Keep the UI alive; log the full trace to activity_log.json
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                Logger?.Log($"FATAL non-UI exception (terminating={args.IsTerminating}): {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}", Core.Enums.LogSeverity.Error, "System");
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Logger?.Log($"Unobserved task exception: {args.Exception.GetBaseException().Message}", Core.Enums.LogSeverity.Warning, "System");
                args.SetObserved();
            };

            // Initialize Core Services
            Logger = new LoggingService();
            ConfigService = new ConfigurationService(Logger);
            DellDetector = new DellCapabilityDetector(Logger);
            WindowsDetector = new WindowsCapabilityDetector(Logger);

            // Initialize Hardware Telemetry Pipeline (Composite: HWiNFO64 -> LibreHardwareMonitor -> Windows Native)
            HardwareTelemetryProvider = new DellGamingControlCenter.Services.Hardware.CompositeHardwareTelemetryProvider(Logger);
            Task.Run(async () =>
            {
                try { await HardwareTelemetryProvider.InitializeAsync(); }
                catch (Exception ex) { Logger.Log($"Hardware provider init error: {ex.Message}", Core.Enums.LogSeverity.Warning, "Hardware"); }
            });

            // Initialize HAL (S1: PerformanceCounter creation is guarded inside the service)
            CpuTelemetry = new CpuTelemetryService(Logger, HardwareTelemetryProvider);
            MemoryTelemetry = new MemoryTelemetryService(Logger);
            GpuTelemetry = new GpuTelemetryService(Logger, HardwareTelemetryProvider);
            StorageTelemetry = new StorageTelemetryService(Logger);
            BatteryTelemetry = new BatteryTelemetryService(Logger);
            ThermalTelemetry = new ThermalTelemetryService(HardwareTelemetryProvider);
            ProcessController = new ProcessController(Logger);
            GpuPreferenceController = new GpuPreferenceController(Logger);
            HardwareProfileProvider = new HardwareProfileProvider(CpuTelemetry, MemoryTelemetry, GpuTelemetry, StorageTelemetry, BatteryTelemetry, DellDetector, HardwareTelemetryProvider);

            GameDiscovery = new GameDiscoveryService(Logger);
            
            // Security Foundation (Phase 2)
            WindowsPowerPlanService = new WindowsPowerPlanService(Logger);
            ControlAuditLogger = new ControlAuditLogger(Logger);
            AuthorizationService = new AuthorizationService(Logger, ControlAuditLogger);
            
            // Phase 4 Services (init before Phase 3 so they can be injected)
            var rules = new List<IDiagnosticRule>
            {
                new HighMemoryUsageRule(),
                new HighCpuUsageRule(),
                new GameOnHddRule(),
                new IntegratedGpuPreferenceRule(),
                new PowerPlanMismatchRule(),
                new BackgroundResourceRule()
            };
            DiagnosticEngine = new DiagnosticEngine(Logger, HardwareProfileProvider, CpuTelemetry, MemoryTelemetry, GpuTelemetry, BatteryTelemetry, ProcessController, WindowsPowerPlanService, GpuPreferenceController, rules, ThermalTelemetry, HardwareTelemetryProvider);
            MetricsSampler = new MetricsSampler(Logger, DiagnosticEngine);
            RecommendationEngine = new RecommendationEngine();
            SystemHealthAnalyzer = new SystemHealthAnalyzer();
            SmartOptimizer = new SmartOptimizer(DiagnosticEngine, RecommendationEngine, MetricsSampler);
            BenchmarkService = new BenchmarkService();
            GameTuningService = new GameTuningService(BenchmarkService, ConfigService);
            FpsProvider = new UnavailableFpsProvider();
            GamePerformanceAnalyzer = new GamePerformanceAnalyzer(Logger, SystemHealthAnalyzer, FpsProvider);
            GamingIntelligenceEngine = new GamingIntelligenceEngine(ConfigService, MetricsSampler);

            // Phase 3 Services (moved below Phase 4 so GamePerformanceAnalyzer is available)
            PerformanceModeService = new PerformanceModeService(Logger, WindowsPowerPlanService);
            ChangePerformanceModeUseCase = new ChangePerformanceModeUseCase(PerformanceModeService, AuthorizationService);
            var changeGpuUseCase = new ChangeGpuPreferenceUseCase(GpuPreferenceController, AuthorizationService);
            var changeProcessPriorityUseCase = new ChangeProcessPriorityUseCase(ProcessController, AuthorizationService);
            var policyEvaluator = new PolicyEvaluator(Logger, changeGpuUseCase, ChangePerformanceModeUseCase, changeProcessPriorityUseCase, ControlAuditLogger, GpuPreferenceController);

            BackgroundProcessManager = new BackgroundProcessManager(Logger, ConfigService);
            MemoryOptimizationService = new MemoryOptimizationService(Logger);
            SessionMonitoringService = new SessionMonitoringService(Logger, ConfigService, MetricsSampler);
            GameLauncher = new GameLauncher(Logger, policyEvaluator);
            GameSessionManager = new GameSessionManager(Logger, GameLauncher, SessionMonitoringService, BackgroundProcessManager, ConfigService, policyEvaluator, CpuTelemetry, MemoryTelemetry, GamePerformanceAnalyzer);

            Logger.Log("Application starting up...", Core.Enums.LogSeverity.Info, "System");

            // Start telemetry sampling loop (2s interval to be gentle on the 2C/4T i3-7020U)
            MetricsSampler.Start(TimeSpan.FromSeconds(2));

            // Feed the bounded live-graph histories from the authoritative pipeline.
            MetricsSampler.SnapshotCaptured += (_, snap) =>
            {
                CpuHistory.Push(snap.CpuMetrics.UsagePercent >= 0 ? snap.CpuMetrics.UsagePercent : null);
                RamHistory.Push(snap.RamMetrics.TotalBytes > 0
                    ? (double?)(snap.RamMetrics.UsedBytes * 100.0 / snap.RamMetrics.TotalBytes)
                    : null);
                GpuHistory.Push(snap.GpuMetrics.UsagePercent); // null stays null — no fake values
            };

            // Initialize ViewModels
            DashboardViewModel = new ViewModels.DashboardViewModel();
            GameCenterViewModel = new ViewModels.GameCenterViewModel();
            PerformanceViewModel = new ViewModels.PerformanceViewModel(
                ChangePerformanceModeUseCase,
                GameSessionManager,
                DiagnosticEngine,
                GpuPreferenceController,
                DellDetector,
                ConfigService,
                WindowsPowerPlanService
            );
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
                BenchmarkViewModel,
                GameSessionManager
            );

            // Launch Main Window
            var mainWindow = new MainWindow
            {
                DataContext = MainViewModel
            };
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
            }
            catch { }

            if (Logger != null)
                Logger.Log("Application shutting down. Executing cleanup...", Core.Enums.LogSeverity.Info, "System");

            // Attempt to synchronously end any active session so policies are restored.
            // S4: fire the async chain without capturing the UI SynchronizationContext, otherwise
            // awaited continuations that were posted to the dispatcher deadlock the exit path.
            IsShuttingDown = true;
            if (GameSessionManager != null)
            {
                var active = GameSessionManager.GetActiveSession();
                if (active != null)
                {
                    Logger?.Log($"Force restoring policies for active session {active.GameName} before exit.", Core.Enums.LogSeverity.Warning, "System");

                    // Stop the sampler FIRST: its SnapshotCaptured handlers marshal onto this
                    // (currently blocked) UI thread and raced the teardown, surfacing a
                    // thread-affinity exception during restore.
                    try { MetricsSampler?.Stop(); } catch { }

                    try
                    {
                        // Run the whole async chain on the thread pool so continuations never
                        // marshal back to this (blocked) UI thread.
                        Task.Run(() => GameSessionManager.StopSessionAsync(active.SessionId))
                            .GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        var root = ex is AggregateException ag ? ag.GetBaseException() : ex;
                        Logger?.Log($"Session restore during exit failed: {root.GetType().Name}: {root.Message}", Core.Enums.LogSeverity.Error, "System");
                    }
                }
            }

            if (ConfigService != null)
            {
                ConfigService.SaveConfig(ConfigService.GetConfig());
            }
            
            if (MetricsSampler != null)
                MetricsSampler.Stop();

            try { (HardwareTelemetryProvider as IDisposable)?.Dispose(); } catch { }

            base.OnExit(e);
        }
    }
}
