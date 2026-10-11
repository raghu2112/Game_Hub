namespace DellGamingControlCenter.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;

public class DiagnosticEngine : IDiagnosticEngine
{
    private readonly ILoggingService _logger;
    private readonly IHardwareProfileProvider _hwProvider;
    private readonly ICpuTelemetryService _cpu;
    private readonly IMemoryTelemetryService _mem;
    private readonly IGpuTelemetryService _gpu;
    private readonly IBatteryTelemetryService _battery;
    private readonly IProcessController _process;
    private readonly IPowerPlanController _powerPlan;
    private readonly IGpuPreferenceController _gpuPreference;
    private readonly IEnumerable<IDiagnosticRule> _rules;
    private readonly IThermalTelemetryService? _thermal;
    private readonly IHardwareTelemetryProvider? _hardwareProvider;

    public DiagnosticEngine(
        ILoggingService logger,
        IHardwareProfileProvider hwProvider,
        ICpuTelemetryService cpu,
        IMemoryTelemetryService mem,
        IGpuTelemetryService gpu,
        IBatteryTelemetryService battery,
        IProcessController process,
        IPowerPlanController powerPlan,
        IGpuPreferenceController gpuPreference,
        IEnumerable<IDiagnosticRule> rules,
        IThermalTelemetryService? thermal = null,
        IHardwareTelemetryProvider? hardwareProvider = null)
    {
        _logger = logger;
        _hwProvider = hwProvider;
        _cpu = cpu;
        _mem = mem;
        _gpu = gpu;
        _battery = battery;
        _process = process;
        _powerPlan = powerPlan;
        _gpuPreference = gpuPreference;
        _rules = rules;
        _thermal = thermal;
        _hardwareProvider = hardwareProvider;
    }

    public Task<DiagnosticSnapshot> CaptureSnapshotAsync(GameProfile? activeGame = null, string? gameExecutablePath = null)
    {
        return Task.Run(() =>
        {
            var snapshot = new DiagnosticSnapshot
            {
                Timestamp = DateTime.UtcNow,
                ActiveGame = activeGame,
                GameExecutablePath = gameExecutablePath
            };

            try { snapshot.Hardware = _hwProvider.GetProfile(); } catch (Exception ex) { _logger.Log($"Hardware metrics failed: {ex.Message}", Core.Enums.LogSeverity.Warning, "Diagnostics"); }
            try { snapshot.CpuMetrics = _cpu.GetMetrics(); } catch (Exception ex) { _logger.Log($"CPU metrics failed: {ex.Message}", Core.Enums.LogSeverity.Warning, "Diagnostics"); }
            try { snapshot.RamMetrics = _mem.GetMetrics(); } catch (Exception ex) { _logger.Log($"RAM metrics failed: {ex.Message}", Core.Enums.LogSeverity.Warning, "Diagnostics"); }
            try { snapshot.GpuMetrics = _gpu.GetMetrics(); } catch (Exception ex) { _logger.Log($"GPU metrics failed: {ex.Message}", Core.Enums.LogSeverity.Warning, "Diagnostics"); }
            try { snapshot.BatteryMetrics = _battery.GetMetrics(); } catch (Exception ex) { _logger.Log($"Battery metrics failed: {ex.Message}", Core.Enums.LogSeverity.Warning, "Diagnostics"); }
            try { snapshot.ThermalMetrics = _thermal?.GetMetrics() ?? new ThermalMetrics(); } catch (Exception ex) { _logger.Log($"Thermal metrics failed: {ex.Message}", Core.Enums.LogSeverity.Warning, "Diagnostics"); }
            try 
            { 
                if (_hardwareProvider != null)
                {
                    var hwSnap = _hardwareProvider.CaptureSnapshot();
                    snapshot.SensorInventory = hwSnap.DiscoveredSensors;
                }
            } 
            catch (Exception ex) { _logger.Log($"Sensor inventory failed: {ex.Message}", Core.Enums.LogSeverity.Warning, "Diagnostics"); }
            try { snapshot.Processes = _process.GetProcesses(); } catch (Exception ex) { _logger.Log($"Process enumeration failed: {ex.Message}", Core.Enums.LogSeverity.Warning, "Diagnostics"); }
            try { snapshot.ActivePowerSchemeGuid = _powerPlan.GetActivePowerSchemeGuid(); } catch (Exception ex) { _logger.Log($"Power plan retrieval failed: {ex.Message}", Core.Enums.LogSeverity.Warning, "Diagnostics"); }
            try 
            { 
                snapshot.ActiveGpuPreference = string.IsNullOrEmpty(gameExecutablePath) 
                    ? Core.Enums.GpuPreference.Auto 
                    : _gpuPreference.GetPreference(gameExecutablePath);
            } catch (Exception ex) { _logger.Log($"GPU preference retrieval failed: {ex.Message}", Core.Enums.LogSeverity.Warning, "Diagnostics"); }

            return snapshot;
        });
    }

    public Task<DiagnosticReport> RunDiagnosticAnalysisAsync(IEnumerable<DiagnosticSnapshot> history)
    {
        return Task.Run(() =>
        {
            var report = new DiagnosticReport();
            
            if (history == null || !history.Any())
            {
                report.StatusMessage = "No telemetry history available.";
                report.IsPartial = true;
                return report;
            }

            try
            {
                foreach (var rule in _rules)
                {
                    var findings = rule.Evaluate(history);
                    report.Findings.AddRange(findings);
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"Diagnostic evaluation failed: {ex.Message}", Core.Enums.LogSeverity.Error, "Diagnostics");
                report.IsPartial = true;
                report.StatusMessage = "Partial analysis due to evaluation errors.";
            }

            return report;
        });
    }
}
