namespace DellGamingControlCenter.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Services;

public class DiagnosticRulesTests
{
    private DiagnosticSnapshot CreateSnapshot(double cpu, double mem, string? exe = null, GpuPreference gpuPref = GpuPreference.Auto, string powerPlan = "")
    {
        return new DiagnosticSnapshot
        {
            Timestamp = DateTime.UtcNow,
            CpuMetrics = new CpuMetrics { UsagePercent = cpu },
            RamMetrics = new RamMetrics { UsagePercent = mem },
            GameExecutablePath = exe,
            ActiveGpuPreference = gpuPref,
            ActivePowerSchemeGuid = powerPlan,
            Hardware = new HardwareProfile
            {
                Storage = new List<StorageDrive>
                {
                    new StorageDrive { Type = DellGamingControlCenter.Core.Enums.StorageType.SSD }
                }
            }
        };
    }

    [Fact]
    public void HighCpuRule_NoTrigger_OnSingleSample()
    {
        var rule = new HighCpuUsageRule();
        var history = new List<DiagnosticSnapshot>
        {
            CreateSnapshot(95, 50) // Only one snapshot
        };

        var findings = rule.Evaluate(history).ToList();
        Assert.Empty(findings);
    }

    [Fact]
    public void HighCpuRule_Triggers_OnSustainedHighUsage()
    {
        var rule = new HighCpuUsageRule();
        var history = new List<DiagnosticSnapshot>
        {
            CreateSnapshot(95, 50),
            CreateSnapshot(96, 50),
            CreateSnapshot(92, 50)
        };

        var findings = rule.Evaluate(history).ToList();
        Assert.Single(findings);
        Assert.Equal(DiagnosticSeverity.Warning, findings[0].Severity);
        Assert.Equal(DiagnosticCategory.CPU, findings[0].Category);
    }

    [Fact]
    public void MemoryRule_Triggers_Critical()
    {
        var rule = new HighMemoryUsageRule();
        var history = new List<DiagnosticSnapshot>
        {
            CreateSnapshot(10, 95),
            CreateSnapshot(10, 96),
            CreateSnapshot(10, 94)
        };

        var findings = rule.Evaluate(history).ToList();
        Assert.Single(findings);
        Assert.Equal(DiagnosticSeverity.Critical, findings[0].Severity);
    }
    
    [Fact]
    public void MemoryRule_Triggers_Warning()
    {
        var rule = new HighMemoryUsageRule();
        var history = new List<DiagnosticSnapshot>
        {
            CreateSnapshot(10, 86),
            CreateSnapshot(10, 87),
            CreateSnapshot(10, 88)
        };

        var findings = rule.Evaluate(history).ToList();
        Assert.Single(findings);
        Assert.Equal(DiagnosticSeverity.Warning, findings[0].Severity);
    }

    [Fact]
    public void GameOnHddRule_Triggers_WhenOnHdd()
    {
        var rule = new GameOnHddRule();
        var snap = CreateSnapshot(10, 10, @"D:\Games\Game.exe");
        snap.Hardware.Storage.Add(new StorageDrive { Type = DellGamingControlCenter.Core.Enums.StorageType.HDD });
        var history = new List<DiagnosticSnapshot>
        {
            snap
        };

        var findings = rule.Evaluate(history).ToList();
        Assert.Single(findings);
        Assert.Equal(DiagnosticCategory.Storage, findings[0].Category);
    }
    
    [Fact]
    public void GameOnHddRule_NoTrigger_WhenOnSsd()
    {
        var rule = new GameOnHddRule();
        var history = new List<DiagnosticSnapshot>
        {
            CreateSnapshot(10, 10, @"C:\Games\Game.exe")
        };

        var findings = rule.Evaluate(history).ToList();
        Assert.Empty(findings);
    }

    [Fact]
    public void IntegratedGpuPreferenceRule_Triggers()
    {
        var rule = new IntegratedGpuPreferenceRule();
        var history = new List<DiagnosticSnapshot>
        {
            CreateSnapshot(10, 10, @"C:\Game.exe", GpuPreference.Integrated)
        };

        var findings = rule.Evaluate(history).ToList();
        Assert.Single(findings);
        Assert.Equal(DiagnosticCategory.GPU, findings[0].Category);
    }

    [Fact]
    public void PowerPlanMismatchRule_Triggers_PowerSaver()
    {
        var rule = new PowerPlanMismatchRule();
        var history = new List<DiagnosticSnapshot>
        {
            CreateSnapshot(10, 10, @"C:\Game.exe", GpuPreference.Auto, "a1841308-3541-4fab-bc81-f71556f20b4a")
        };

        var findings = rule.Evaluate(history).ToList();
        Assert.Single(findings);
        Assert.Equal(DiagnosticCategory.Power, findings[0].Category);
    }
}
