namespace DellGamingControlCenter.Services.Hardware;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class CompositeHardwareTelemetryProvider : IHardwareTelemetryProvider
{
    private readonly ILoggingService _logger;
    private readonly IHardwareTelemetryProvider _hwInfoProvider;
    private readonly IHardwareTelemetryProvider _lhmProvider;
    private readonly IHardwareTelemetryProvider _windowsProvider;
    private bool _isDisposed;

    public string ProviderName => "Composite Hardware Telemetry Pipeline";
    public bool IsAvailable => true;

    public IHardwareTelemetryProvider HWiNFOProvider => _hwInfoProvider;
    public IHardwareTelemetryProvider LhmProvider => _lhmProvider;
    public IHardwareTelemetryProvider WindowsProvider => _windowsProvider;

    public CompositeHardwareTelemetryProvider(ILoggingService logger)
        : this(logger, new HWiNFOSharedMemoryProvider(logger), new LibreHardwareMonitorTelemetryProvider(logger), new WindowsStandardTelemetryProvider(logger))
    {
    }

    public CompositeHardwareTelemetryProvider(
        ILoggingService logger,
        IHardwareTelemetryProvider hwInfoProvider,
        IHardwareTelemetryProvider lhmProvider,
        IHardwareTelemetryProvider windowsProvider)
    {
        _logger = logger;
        _hwInfoProvider = hwInfoProvider;
        _lhmProvider = lhmProvider;
        _windowsProvider = windowsProvider;
    }

    public async Task InitializeAsync()
    {
        _logger.Log("Initializing Composite Hardware Telemetry Providers...", LogSeverity.Info, "Hardware");
        await Task.WhenAll(
            _hwInfoProvider.InitializeAsync(),
            _lhmProvider.InitializeAsync(),
            _windowsProvider.InitializeAsync()
        );
        _logger.Log($"Hardware Providers Initialized: HWiNFO={_hwInfoProvider.IsAvailable}, LHM={_lhmProvider.IsAvailable}, Windows={_windowsProvider.IsAvailable}", LogSeverity.Info, "Hardware");
    }

    public HardwareTelemetrySnapshot CaptureSnapshot()
    {
        var now = DateTime.UtcNow;

        HardwareTelemetrySnapshot? hwinfoSnap = null;
        try
        {
            var snap = _hwInfoProvider.CaptureSnapshot();
            if (snap != null && (snap.DiscoveredSensors.Count > 0 || _hwInfoProvider.IsAvailable))
            {
                hwinfoSnap = snap;
            }
        }
        catch { }

        HardwareTelemetrySnapshot? lhmSnap = null;
        if (_lhmProvider.IsAvailable)
        {
            try { lhmSnap = _lhmProvider.CaptureSnapshot(); } catch { }
        }

        HardwareTelemetrySnapshot winSnap = _windowsProvider.CaptureSnapshot();

        // Build deduplicated sensor inventory:
        // Priority order for values: HWiNFO > LibreHardwareMonitor > Windows
        var allSensors = new List<HardwareSensorReading>();
        if (hwinfoSnap != null && hwinfoSnap.DiscoveredSensors.Count > 0)
        {
            allSensors.AddRange(hwinfoSnap.DiscoveredSensors);
        }
        if (lhmSnap != null && lhmSnap.DiscoveredSensors.Count > 0)
        {
            allSensors.AddRange(lhmSnap.DiscoveredSensors);
        }
        allSensors.AddRange(winSnap.DiscoveredSensors);

        // Deduplicate sensors by Component + SensorType + Normalized Name
        var deduplicatedInventory = DeduplicateSensors(allSensors);

        // Determine authoritative CPU Metrics
        var cpu = ResolveCpuMetrics(hwinfoSnap, lhmSnap, winSnap, now);

        // Determine authoritative RAM Metrics
        var ram = ResolveRamMetrics(hwinfoSnap, lhmSnap, winSnap, now);

        // Determine authoritative GPU Metrics
        var (discreteGpu, integratedGpu) = ResolveGpuMetrics(hwinfoSnap, lhmSnap, winSnap, now);

        // Determine authoritative Thermal Metrics
        var thermal = ResolveThermalMetrics(hwinfoSnap, lhmSnap, winSnap, cpu, discreteGpu, now);

        string activeSource = (hwinfoSnap != null && hwinfoSnap.DiscoveredSensors.Count > 0)
            ? "HWiNFO64 + LibreHardwareMonitor"
            : (_lhmProvider.IsAvailable ? "LibreHardwareMonitor + Windows Native" : "Windows Native");

        return new HardwareTelemetrySnapshot
        {
            Timestamp = now,
            ActiveProvider = activeSource,
            DiscoveredSensors = deduplicatedInventory,
            CpuMetrics = cpu,
            RamMetrics = ram,
            DiscreteGpuMetrics = discreteGpu,
            IntegratedGpuMetrics = integratedGpu,
            ThermalMetrics = thermal
        };
    }

    private static CpuMetrics ResolveCpuMetrics(
        HardwareTelemetrySnapshot? hwinfo,
        HardwareTelemetrySnapshot? lhm,
        HardwareTelemetrySnapshot win,
        DateTime now)
    {
        // CPU Total usage
        double usage = -1;
        if (hwinfo?.CpuMetrics.UsagePercent >= 0) usage = hwinfo.CpuMetrics.UsagePercent;
        else if (lhm?.CpuMetrics.UsagePercent >= 0) usage = lhm.CpuMetrics.UsagePercent;
        else if (win.CpuMetrics.UsagePercent >= 0) usage = win.CpuMetrics.UsagePercent;

        // Per-core loads
        double[] perCore = [];
        if (hwinfo?.CpuMetrics.PerCoreUsage.Length > 0) perCore = hwinfo.CpuMetrics.PerCoreUsage;
        else if (lhm?.CpuMetrics.PerCoreUsage.Length > 0) perCore = lhm.CpuMetrics.PerCoreUsage;
        else if (win.CpuMetrics.PerCoreUsage.Length > 0) perCore = win.CpuMetrics.PerCoreUsage;

        // Current Clock
        double clock = 0;
        var clockState = SensorReadingState.Unavailable;
        string? clockReason = "Live CPU clock requires administrator elevation or HWiNFO64 shared memory (nominal base is 2300 MHz)";
        if ((hwinfo?.CpuMetrics.ClockState == SensorReadingState.Available || hwinfo?.CpuMetrics.ClockState == SensorReadingState.Stale) && hwinfo.CpuMetrics.CurrentClockMHz > 0)
        {
            clock = hwinfo.CpuMetrics.CurrentClockMHz;
            clockState = hwinfo.CpuMetrics.ClockState;
            clockReason = hwinfo.CpuMetrics.ClockDiagnosticReason;
        }
        else if (lhm?.CpuMetrics.ClockState == SensorReadingState.Available && lhm.CpuMetrics.CurrentClockMHz > 0)
        {
            clock = lhm.CpuMetrics.CurrentClockMHz;
            clockState = SensorReadingState.Available;
            clockReason = null;
        }

        double[] perCoreClocks = [];
        if (hwinfo?.CpuMetrics.PerCoreClocksMHz.Length > 0) perCoreClocks = hwinfo.CpuMetrics.PerCoreClocksMHz;
        else if (lhm?.CpuMetrics.PerCoreClocksMHz.Length > 0) perCoreClocks = lhm.CpuMetrics.PerCoreClocksMHz;

        // Temperatures
        double? temp = null;
        var tempState = SensorReadingState.Unavailable;
        string? diagReason = null;
        double[] perCoreTemps = [];

        if (hwinfo?.CpuMetrics.TemperatureCelsius.HasValue == true)
        {
            temp = hwinfo.CpuMetrics.TemperatureCelsius.Value;
            tempState = hwinfo.CpuMetrics.TemperatureState;
            diagReason = hwinfo.CpuMetrics.TemperatureDiagnosticReason;
            perCoreTemps = hwinfo.CpuMetrics.PerCoreTemperaturesCelsius;
        }
        else if (lhm?.CpuMetrics.TemperatureCelsius.HasValue == true)
        {
            temp = lhm.CpuMetrics.TemperatureCelsius.Value;
            tempState = lhm.CpuMetrics.TemperatureState;
            diagReason = lhm.CpuMetrics.TemperatureDiagnosticReason;
            perCoreTemps = lhm.CpuMetrics.PerCoreTemperaturesCelsius;
        }
        else
        {
            temp = null;
            tempState = SensorReadingState.Unavailable;
            diagReason = "Direct CPU core temperature requires administrator elevation (WinRing0) or running HWiNFO64 with shared memory.";
        }

        // Power & Voltage
        double? power = hwinfo?.CpuMetrics.PackagePowerWatts ?? lhm?.CpuMetrics.PackagePowerWatts;
        var powerState = hwinfo?.CpuMetrics.PowerState ?? ((power.HasValue && power.Value > 0) ? SensorReadingState.Available : SensorReadingState.Unavailable);
        string? powerReason = hwinfo?.CpuMetrics.PowerDiagnosticReason ?? (powerState == SensorReadingState.Available ? null : "CPU package energy MSR (0x611) requires administrator elevation or HWiNFO64");

        double? voltage = hwinfo?.CpuMetrics.CoreVoltageVolts ?? lhm?.CpuMetrics.CoreVoltageVolts;
        var voltageState = hwinfo?.CpuMetrics.VoltageState ?? ((voltage.HasValue && voltage.Value > 0) ? SensorReadingState.Available : SensorReadingState.Unavailable);
        string? voltageReason = hwinfo?.CpuMetrics.VoltageDiagnosticReason ?? (voltageState == SensorReadingState.Available ? null : "CPU core voltage VID registers require administrator elevation or HWiNFO64");

        bool? throttling = hwinfo?.CpuMetrics.IsThermalThrottling ?? lhm?.CpuMetrics.IsThermalThrottling;

        return new CpuMetrics
        {
            UsagePercent = usage,
            PerCoreUsage = perCore,
            CurrentClockMHz = clock,
            PerCoreClocksMHz = perCoreClocks,
            TemperatureCelsius = temp,
            PerCoreTemperaturesCelsius = perCoreTemps,
            PackagePowerWatts = power,
            CoreVoltageVolts = voltage,
            IsThermalThrottling = throttling,
            TemperatureState = tempState,
            TemperatureDiagnosticReason = diagReason,
            ClockState = clockState,
            ClockDiagnosticReason = clockReason,
            PowerState = powerState,
            PowerDiagnosticReason = powerReason,
            VoltageState = voltageState,
            VoltageDiagnosticReason = voltageReason,
            Timestamp = now
        };
    }

    private static RamMetrics ResolveRamMetrics(
        HardwareTelemetrySnapshot? hwinfo,
        HardwareTelemetrySnapshot? lhm,
        HardwareTelemetrySnapshot win,
        DateTime now)
    {
        if (win.RamMetrics.TotalBytes > 0)
        {
            return win.RamMetrics;
        }
        if (lhm?.RamMetrics.TotalBytes > 0)
        {
            return lhm.RamMetrics;
        }
        if (hwinfo?.RamMetrics.TotalBytes > 0)
        {
            return hwinfo.RamMetrics;
        }
        return new RamMetrics { Timestamp = now };
    }

    private static (GpuMetrics Discrete, GpuMetrics Integrated) ResolveGpuMetrics(
        HardwareTelemetrySnapshot? hwinfo,
        HardwareTelemetrySnapshot? lhm,
        HardwareTelemetrySnapshot win,
        DateTime now)
    {
        // Discrete GPU (AMD Radeon 520)
        double? amdUsage = hwinfo?.DiscreteGpuMetrics.UsagePercent 
            ?? lhm?.DiscreteGpuMetrics.UsagePercent 
            ?? win.DiscreteGpuMetrics.UsagePercent;

        double? amdTemp = hwinfo?.DiscreteGpuMetrics.TemperatureCelsius 
            ?? lhm?.DiscreteGpuMetrics.TemperatureCelsius;

        double? amdClock = hwinfo?.DiscreteGpuMetrics.CoreClockMHz 
            ?? lhm?.DiscreteGpuMetrics.CoreClockMHz;

        double? amdMemClock = hwinfo?.DiscreteGpuMetrics.MemoryClockMHz 
            ?? lhm?.DiscreteGpuMetrics.MemoryClockMHz;

        int? amdFan = hwinfo?.DiscreteGpuMetrics.FanRpm 
            ?? lhm?.DiscreteGpuMetrics.FanRpm;

        long? amdVramUsed = lhm?.DiscreteGpuMetrics.VramUsedBytes;
        long? amdVramAvail = lhm?.DiscreteGpuMetrics.VramAvailableBytes;
        long amdVramTotal = lhm?.DiscreteGpuMetrics.VramTotalBytes ?? (2048L * 1024 * 1024);
        long? amdSharedUsed = lhm?.DiscreteGpuMetrics.SharedMemoryUsedBytes;
        long? amdSharedAvail = lhm?.DiscreteGpuMetrics.SharedMemoryAvailableBytes;
        long? amdSharedTotal = lhm?.DiscreteGpuMetrics.SharedMemoryTotalBytes;
        var memState = lhm?.DiscreteGpuMetrics.MemoryState ?? SensorReadingState.Unavailable;
        string? memReason = lhm?.DiscreteGpuMetrics.MemoryDiagnosticReason;

        var discrete = new GpuMetrics
        {
            UsagePercent = amdUsage,
            TemperatureCelsius = amdTemp,
            CoreClockMHz = amdClock,
            MemoryClockMHz = amdMemClock,
            FanRpm = amdFan,
            VramUsedBytes = amdVramUsed,
            VramAvailableBytes = amdVramAvail,
            VramTotalBytes = amdVramTotal,
            SharedMemoryUsedBytes = amdSharedUsed,
            SharedMemoryAvailableBytes = amdSharedAvail,
            SharedMemoryTotalBytes = amdSharedTotal,
            TemperatureState = hwinfo?.DiscreteGpuMetrics.TemperatureState ?? (amdTemp.HasValue ? SensorReadingState.Available : SensorReadingState.Unavailable),
            TemperatureDiagnosticReason = hwinfo?.DiscreteGpuMetrics.TemperatureDiagnosticReason ?? (amdTemp.HasValue ? null : "AMD Radeon 520 in PCIe D3cold power-saving sleep"),
            MemoryState = memState,
            MemoryDiagnosticReason = memReason,
            Timestamp = now
        };

        // Integrated GPU (Intel HD Graphics 620)
        double? intelUsage = lhm?.IntegratedGpuMetrics.UsagePercent ?? win.DiscreteGpuMetrics.UsagePercent;
        var integrated = new GpuMetrics
        {
            UsagePercent = intelUsage,
            VramUsedBytes = lhm?.IntegratedGpuMetrics.VramUsedBytes,
            VramTotalBytes = lhm?.IntegratedGpuMetrics.VramTotalBytes,
            PowerWatts = lhm?.IntegratedGpuMetrics.PowerWatts,
            TemperatureState = SensorReadingState.Unavailable,
            TemperatureDiagnosticReason = "Shares CPU package thermal envelope",
            Timestamp = now
        };

        return (discrete, integrated);
    }

    private static ThermalMetrics ResolveThermalMetrics(
        HardwareTelemetrySnapshot? hwinfo,
        HardwareTelemetrySnapshot? lhm,
        HardwareTelemetrySnapshot win,
        CpuMetrics cpu,
        GpuMetrics discreteGpu,
        DateTime now)
    {
        double? cpuTemp = cpu.TemperatureCelsius;
        double? gpuTemp = discreteGpu.TemperatureCelsius;
        double? ambient = win.ThermalMetrics.AmbientTemperature;
        int? fanRpm = hwinfo?.ThermalMetrics.FanRpm ?? lhm?.ThermalMetrics.FanRpm;
        bool? isThrottling = cpu.IsThermalThrottling;

        var cpuTempState = cpu.TemperatureState;
        var gpuTempState = discreteGpu.TemperatureState;
        var fanState = (hwinfo?.ThermalMetrics.FanRpm.HasValue == true) ? hwinfo.ThermalMetrics.FanRpmState :
            (fanRpm.HasValue ? SensorReadingState.Available :
            (hwinfo != null && hwinfo.DiscoveredSensors.Count > 0 ? SensorReadingState.Unsupported : SensorReadingState.Unavailable));

        ThermalState cpuThermalState = ThermalState.Unknown;
        if (cpuTemp.HasValue)
        {
            cpuThermalState = cpuTemp.Value switch
            {
                < 65 => ThermalState.Normal,
                < 80 => ThermalState.Warm,
                < 92 => ThermalState.Hot,
                _ => ThermalState.Critical
            };
        }

        ThermalState gpuThermalState = ThermalState.Unknown;
        if (gpuTemp.HasValue)
        {
            gpuThermalState = gpuTemp.Value switch
            {
                < 65 => ThermalState.Normal,
                < 78 => ThermalState.Warm,
                < 88 => ThermalState.Hot,
                _ => ThermalState.Critical
            };
        }

        return new ThermalMetrics
        {
            CpuTemperature = cpuTemp,
            GpuTemperature = gpuTemp,
            AmbientTemperature = ambient,
            FanRpm = fanRpm,
            CpuThermalState = cpuThermalState,
            GpuThermalState = gpuThermalState,
            IsCpuThrottling = isThrottling,
            CpuTemperatureState = cpuTempState,
            GpuTemperatureState = gpuTempState,
            FanRpmState = fanState,
            DiagnosticReason = cpu.TemperatureDiagnosticReason,
            Timestamp = now
        };
    }

    private static List<HardwareSensorReading> DeduplicateSensors(List<HardwareSensorReading> all)
    {
        var deduplicated = new List<HardwareSensorReading>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Put Available readings first so they win deduplication over Unavailable
        var ordered = all.OrderByDescending(r => r.State == SensorReadingState.Available)
                         .ThenByDescending(r => r.SourceProvider.Contains("HWiNFO"))
                         .ThenByDescending(r => r.SourceProvider.Contains("Libre"));

        foreach (var r in ordered)
        {
            string key = $"{r.ComponentType}_{r.SensorType}_{NormalizeSensorName(r.SensorName)}";
            if (seenKeys.Add(key))
            {
                deduplicated.Add(r);
            }
        }

        return deduplicated.OrderBy(r => r.ComponentType)
                           .ThenBy(r => r.SensorType)
                           .ThenBy(r => r.SensorName)
                           .ToList();
    }

    private static string NormalizeSensorName(string name)
    {
        return name.Replace(" ", "")
                   .Replace("#", "")
                   .Replace("Total", "")
                   .Replace("Usage", "")
                   .Replace("Load", "")
                   .ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try { _hwInfoProvider.Dispose(); } catch { }
        try { _lhmProvider.Dispose(); } catch { }
        try { _windowsProvider.Dispose(); } catch { }
    }
}
