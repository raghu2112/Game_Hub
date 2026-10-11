namespace DellGamingControlCenter.Core.Models;

using DellGamingControlCenter.Core.Enums;

// ─── Dell Capability Report ─────────────────────────────────────────────

public sealed class CapabilityEntry
{
    public required string FeatureName { get; init; }
    public CapabilityStatus Status { get; init; }
    public string Description { get; init; } = "";
    public string Method { get; init; } = "";
}

public sealed class DellCapabilityReport
{
    public string DellModel { get; init; } = "Unknown";
    public bool IsDellSystem { get; init; }
    public List<CapabilityEntry> Capabilities { get; init; } = [];
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

public sealed class ProcessInfo
{
    public int ProcessId { get; init; }
    public string Name { get; init; } = "";
    public long WorkingSetBytes { get; init; }
    public ProcessPriorityClass Priority { get; init; }
}

public sealed class HardwareProfile
{
    public CpuInfo Cpu { get; init; } = new();
    public List<GpuInfo> Gpus { get; init; } = [];
    public RamInfo Memory { get; init; } = new();
    public List<StorageDrive> Storage { get; init; } = [];
    public BatteryInfo? Battery { get; init; }
    public Dictionary<HardwareCapability, CapabilityStatus> Capabilities { get; init; } = new();
}

// ─── Windows Capability Report ──────────────────────────────────────────

public sealed class WindowsCapabilityReport
{
    public List<CapabilityEntry> Capabilities { get; init; } = [];
    public string ActivePowerPlan { get; init; } = "Unknown";
    public List<string> AvailablePowerPlans { get; init; } = [];
    public bool GameModeEnabled { get; init; }
    public bool GameDvrEnabled { get; init; }
    public bool HagsAvailable { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

// ─── Game Models ────────────────────────────────────────────────────────

public sealed class DiscoveredGame
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; init; } = "Unknown";
    public string ExecutablePath { get; init; } = "";
    public string InstallDirectory { get; init; } = "";
    public string? LauncherName { get; init; }
    public long ExecutableSizeBytes { get; init; }
    public StorageType InstalledOnDriveType { get; init; } = StorageType.Unknown;
    public string DriveLetter { get; init; } = "";
    public CompatibilityRating EstimatedCompatibility { get; init; } = CompatibilityRating.Unknown;
    public ProfileState ProfileState { get; init; } = ProfileState.NotConfigured;
    public DateTime DiscoveredAt { get; init; } = DateTime.UtcNow;
}

public sealed class GameProfile
{
    public string GameId { get; set; } = "";
    public string GameName { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
    public string InstallDirectory { get; init; } = "";
    public string? LauncherPath { get; init; }
    
    // GPU
    public GpuPreference PreferredGpu { get; set; } = GpuPreference.Auto;
    
    // Performance
    public PerformanceMode PerformanceMode { get; set; } = PerformanceMode.Balanced;
    public ProcessPriorityClass ProcessPriority { get; set; } = ProcessPriorityClass.Normal;
    public string? CpuAffinityHex { get; set; } // Advanced / Experimental
    public int? FpsTarget { get; set; }
    
    // Optimization
    public bool OptimizeMemoryOnLaunch { get; set; }
    public bool EnableGameMode { get; set; } = true;
    public bool OptimizeVisualEffects { get; set; }
    public List<string> ProcessesToPause { get; set; } = [];
    
    // Display recommendations
    public string? RecommendedResolution { get; set; }
    public string? WindowMode { get; set; }
    public string? VSyncRecommendation { get; set; }
    public string? GraphicsPresetRecommendation { get; set; }
    
    // Launch
    public string? LaunchArguments { get; set; }
    public string? WorkingDirectory { get; set; }
    
    // Process Detection (Phase 6)
    public List<string> ProcessNames { get; set; } = [];
    public string? PrimaryExecutable { get; set; }
    public string? LauncherExecutable { get; set; }
    
    // Monitoring (Phase 6 additions)
    public bool MonitorCpu { get; set; } = true;
    public bool MonitorGpu { get; set; } = true;
    public bool MonitorRam { get; set; } = true;
    public bool MonitorTemperature { get; set; } = true;
    public bool MonitoringEnabled { get; set; } = true;
    public bool OverlayEnabled { get; set; } = false;
    public bool BackgroundManagementEnabled { get; set; } = true;
    public bool AutoRestore { get; set; } = true;
    public int DiagnosticSamplingInterval { get; set; } = 2000;

    // Metadata
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLaunched { get; set; }
    public DateTime? LastOptimized { get; set; }
}

// ─── Optimization Pipeline ──────────────────────────────────────────────

public sealed class OptimizationAction
{
    public string ActionId { get; init; } = Guid.NewGuid().ToString();
    public string Description { get; init; } = "";
    public string Type { get; init; } = ""; // E.g., "GpuPreference", "ProcessPriority", "Registry"
    public string Target { get; init; } = ""; // What is being modified
    public string Value { get; init; } = ""; // New value
    public OptimizationStatus Status { get; set; } = OptimizationStatus.Pending;
    public string? RevertValue { get; set; } // Previous value for rollback
    public string EstimatedBenefit { get; init; } = "";
    public string RiskLevel { get; init; } = "Low";
}

public sealed class OptimizationPlan
{
    public string PlanId { get; init; } = Guid.NewGuid().ToString();
    public string GameId { get; init; } = "";
    public string GameName { get; init; } = "";
    public List<OptimizationAction> Actions { get; init; } = [];
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

public sealed class PolicyResult
{
    public string PolicyName { get; init; } = "";
    public string Status { get; init; } = ""; // Applied, Skipped, Failed
    public string Reason { get; init; } = "";
}

public sealed class ProfileApplicationResult
{
    public bool Success { get; set; } = true;
    public string ProfileId { get; set; } = "";
    public List<PolicyResult> AppliedPolicies { get; init; } = [];
    public List<PolicyResult> SkippedPolicies { get; init; } = [];
    public List<PolicyResult> FailedPolicies { get; init; } = [];
}

public sealed class SessionSystemState
{
    public string? PreviousPowerSchemeGuid { get; set; }
    public string? PreviousPowerSchemeName { get; set; }
    public bool? PreviousGameModeEnabled { get; set; }
    public string? PreviousGpuPreference { get; set; }
    public Dictionary<int, ProcessPriorityClass> PreviousProcessPriorities { get; set; } = new();
    public List<string> ProcessesClosed { get; set; } = new();
}

public sealed class GameSession
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString();
    public string GameId { get; init; } = "";
    public string GameName { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
    public string OptimizationSessionId { get; set; } = "";
    public DateTime StartTime { get; init; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    
    public GameSessionState CurrentState { get; set; } = GameSessionState.Created;
    
    // Original State
    public SessionSystemState OriginalSystemState { get; set; } = new();
    
    // Results
    public bool OptimizationSuccessful { get; set; }
    public bool RestorationSuccessful { get; set; }
    public string? FailureReason { get; set; }
    
    // Performance Metrics (Peak/Averages)
    public double PeakCpuUsage { get; set; }
    public double PeakGpuUsage { get; set; }
    public double PeakRamUsageBytes { get; set; }
    public double? PeakTemperatureCelsius { get; set; }
    
    public int ProcessId { get; set; }
    
    // Track what we applied and restored
    public List<OptimizationAction> AppliedChanges { get; set; } = [];
    public List<OptimizationAction> RestoredChanges { get; set; } = [];
    
    // Phase 6 Additions
    public BaselineSnapshot? Baseline { get; set; }
    public List<PerformanceEvent> TimelineEvents { get; set; } = [];
    public SessionReport? Report { get; set; }

    // Phase 8 Analytics
    public double AverageCpuUsage { get; set; }
    public double AverageRamUsageBytes { get; set; }
    public double AverageGpuUsage { get; set; }
    
    [System.Text.Json.Serialization.JsonIgnore]
    public int CpuSampleCount { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public double TotalCpuUsage { get; set; }
    
    [System.Text.Json.Serialization.JsonIgnore]
    public int RamSampleCount { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public double TotalRamUsageBytes { get; set; }
    
    public List<DiagnosticFinding> SessionFindings { get; set; } = new();
    public string GameStorageLocation { get; set; } = "";
}

// ─── Phase 3: Background & Memory ──────────────────────────────────────

public sealed class MemoryAnalysisResult
{
    public long TotalRamBytes { get; init; }
    public long AvailableRamBytes { get; init; }
    public long UsedRamBytes { get; init; }
    public long CommitTotalBytes { get; init; }
    public long CommitLimitBytes { get; init; }
    public MemoryPressureLevel PressureLevel { get; init; } = MemoryPressureLevel.Normal;
    
    // Top 5 processes
    public Dictionary<string, long> TopProcessesBytes { get; init; } = new();
}

public sealed class BackgroundProcessRule
{
    public string ProcessName { get; init; } = "";
    public BackgroundProcessRuleType RuleType { get; set; } = BackgroundProcessRuleType.NeverTouch;
}

public sealed class AppConfiguration
{
    public int MonitoringIntervalMs { get; set; } = 2000;
    public bool AdvancedModeEnabled { get; set; }
    public PerformanceMode DefaultPerformanceMode { get; set; } = PerformanceMode.Balanced;
    
    // Automation Settings
    public bool AutomaticOptimization { get; set; } = false;
    public bool AutomaticPerformanceMode { get; set; } = false;
    public bool AutomaticBackgroundManagement { get; set; } = false;
    public bool AutomaticRestore { get; set; } = true;
    public bool RequireOptimizationConfirmation { get; set; } = true;
    public bool RequireBackgroundProcessConfirmation { get; set; } = true;

    public ThermalThresholds ThermalThresholds { get; set; } = new();
    public List<GameProfile> GameProfiles { get; set; } = [];
    public List<BackgroundProcessRule> BackgroundRules { get; set; } = [];
    public List<GameSession> SessionHistory { get; set; } = [];
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}

public sealed class ThermalThresholds
{
    public double WarmCelsius { get; set; } = 65.0;
    public double HotCelsius { get; set; } = 80.0;
    public double CriticalCelsius { get; set; } = 95.0;
}

// ─── Configuration Backup ───────────────────────────────────────────────

public sealed class ConfigBackup
{
    public string? PreviousPowerPlanGuid { get; set; }
    public Dictionary<string, string> PreviousGpuPreferences { get; set; } = new();
    public Dictionary<string, string> PreviousRegistryValues { get; set; } = new();
    public Dictionary<string, int> PreviousProcessPriorities { get; set; } = new();
    public DateTime BackedUpAt { get; set; } = DateTime.UtcNow;
}

// ─── Activity Log ───────────────────────────────────────────────────────

public sealed class ActivityLogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogSeverity Severity { get; init; } = LogSeverity.Info;
    public string Category { get; init; } = "General";
    public string Message { get; init; } = "";
    public string? Detail { get; init; }
}
