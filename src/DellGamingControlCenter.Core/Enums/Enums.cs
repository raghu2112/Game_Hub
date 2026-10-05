namespace DellGamingControlCenter.Core.Enums;

/// <summary>
/// Indicates whether a hardware capability is available on this system.
/// Every hardware feature query returns one of these states rather than crashing.
/// </summary>
public enum CapabilityStatus
{
    /// <summary>Feature is fully supported and functional.</summary>
    Supported,

    /// <summary>Feature exists but with limitations (e.g. read-only, partial data).</summary>
    Limited,

    /// <summary>Feature is not supported by this hardware/firmware.</summary>
    Unsupported,

    /// <summary>Feature requires administrator elevation to access.</summary>
    RequiresElevation,

    /// <summary>Feature status could not be determined (sensor missing, WMI error, etc.).</summary>
    Unavailable,

    /// <summary>An error occurred while detecting this capability.</summary>
    Error
}

/// <summary>Performance mode that defines a set of Windows-level optimizations.</summary>
public enum PerformanceMode
{
    Quiet,
    BatterySaver,
    Balanced,
    Gaming,
    MaximumPerformance
}

/// <summary>Type of storage device.</summary>
public enum StorageType
{
    Unknown,
    HDD,
    SSD,
    NVMe
}

/// <summary>GPU type classification.</summary>
public enum GpuType
{
    Unknown,
    Integrated,
    Discrete
}

/// <summary>Thermal severity level for temperature readings.</summary>
public enum ThermalState
{
    Normal,
    Warm,
    Hot,
    Critical,
    Unknown
}

/// <summary>Game compatibility rating based on hardware analysis.</summary>
public enum CompatibilityRating
{
    /// <summary>🟢 Runs comfortably on this hardware.</summary>
    RunsComfortably,

    /// <summary>🟡 Runs with optimization applied.</summary>
    RunsWithOptimization,

    /// <summary>🟠 Requires heavy optimization, marginal experience.</summary>
    HeavyOptimizationRequired,

    /// <summary>🔴 Not recommended for this hardware.</summary>
    NotRecommended,

    /// <summary>Not enough data to determine.</summary>
    Unknown
}

/// <summary>Severity level for activity log entries.</summary>
public enum LogSeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>Category for background processes.</summary>
public enum ProcessCategory
{
    CriticalSystem,
    WindowsSystem,
    Security,
    Driver,
    UserApplication,
    KnownSafeToPause,
    Unknown
}

/// <summary>Status of a game session lifecycle.</summary>
public enum GameSessionState
{
    Created,
    Idle,
    Preparing,
    Optimizing,
    Launching,
    WaitingForProcess,
    Running,
    Paused,
    Stopping,
    Restoring,
    Completed,
    CompletedWithWarnings,
    Failed,
    Cancelled
}

/// <summary>Memory pressure level classification.</summary>
public enum MemoryPressureLevel
{
    Normal,
    Elevated,
    High,
    Critical
}

/// <summary>Rule type for background processes.</summary>
public enum BackgroundProcessRuleType
{
    NeverTouch,
    Ask,
    CloseBeforeGame
}

// ─── Phase 4: Diagnostics ──────────────────────────────────────────────

public enum MetricAvailability
{
    Measured,
    Detected,
    Inferred,
    Recommended,
    Estimated,
    Unavailable
}

public enum BottleneckType
{
    None,
    CPU,
    GPU,
    Memory,
    Storage,
    Thermal,
    BackgroundProcess,
    Power,
    Unknown
}

public enum Severity
{
    Low,
    Moderate,
    High,
    Critical
}

public enum ConfidenceLevel
{
    Low,
    Medium,
    High
}

public enum RiskLevel
{
    None,
    Low,
    Medium,
    High
}

public enum ImpactLevel
{
    Unknown,
    Low,
    Medium,
    High
}

/// <summary>Status of a game profile.</summary>
public enum ProfileState
{
    NotConfigured,
    Configured,
    Optimized,
    Running,
    Error
}

/// <summary>GPU preference for a game.</summary>
public enum GpuPreference
{
    Auto,
    Integrated,
    HighPerformance
}

/// <summary>Safe process priorities.</summary>
public enum ProcessPriorityClass
{
    Normal,
    BelowNormal,
    AboveNormal
}

/// <summary>Status of an optimization action.</summary>
public enum OptimizationStatus
{
    Pending,
    Applied,
    Verified,
    Failed,
    RolledBack,
    Unknown
}
