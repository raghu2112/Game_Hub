namespace DellGamingControlCenter.Core.Models;

using System;
using System.Collections.Generic;

public enum DiagnosticSeverity
{
    Info,
    Notice,
    Warning,
    Critical
}

public enum DiagnosticCategory
{
    CPU,
    Memory,
    GPU,
    Storage,
    Battery,
    Process,
    Power,
    System,
    Gaming
}

public enum DiagnosticConfidence
{
    Low,
    Medium,
    High
}

public class DiagnosticFinding
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DiagnosticCategory Category { get; init; }
    public DiagnosticSeverity Severity { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string ObservedValue { get; init; } = "";
    public string ReferenceRange { get; init; } = "";
    public DiagnosticConfidence Confidence { get; init; } = DiagnosticConfidence.High;
    public string Evidence { get; init; } = "";
    public string RecommendedAction { get; init; } = "";
}

public class DiagnosticSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    // Hardware Specs
    public HardwareProfile Hardware { get; set; } = new();
    
    // Live Telemetry
    public CpuMetrics CpuMetrics { get; set; } = new();
    public RamMetrics RamMetrics { get; set; } = new();
    public GpuMetrics GpuMetrics { get; set; } = new();
    public BatteryMetrics BatteryMetrics { get; set; } = new();
    public ThermalMetrics ThermalMetrics { get; set; } = new();
    public List<HardwareSensorReading> SensorInventory { get; set; } = new();
    
    public List<ProcessInfo> Processes { get; set; } = new();
    public string ActivePowerSchemeGuid { get; set; } = "";
    
    // Game Context
    public GameProfile? ActiveGame { get; set; }
    public string? GameExecutablePath { get; set; }
    public Enums.GpuPreference ActiveGpuPreference { get; set; } = Enums.GpuPreference.Auto;
}

public class DiagnosticReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<DiagnosticFinding> Findings { get; set; } = new();
    public bool IsPartial { get; set; } = false;
    public string StatusMessage { get; set; } = "Complete";
}
