using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using DellGamingControlCenter.Core.Enums;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Services;

namespace DellGamingControlCenter.Tests
{
    public class DiagnosticTests
    {
        private IBottleneckAnalyzer GetAnalyzer()
        {
            var logger = new LoggingService();
            return new BottleneckAnalyzer(logger);
        }

        private List<DiagnosticSnapshot> GenerateSnapshots(int count, double cpu, double gpu, double ramPercent = 0.5, double temp = 60)
        {
            var snaps = new List<DiagnosticSnapshot>();
            for (int i = 0; i < count; i++)
            {
                snaps.Add(new DiagnosticSnapshot
                {
                    CpuMetrics = new CpuMetrics { UsagePercent = cpu, TemperatureCelsius = temp },
                    GpuMetrics = new GpuMetrics { UsagePercent = gpu, TemperatureCelsius = temp },
                    RamMetrics = new RamMetrics { TotalBytes = 8_000_000_000, UsedBytes = (long)(8_000_000_000 * ramPercent) }
                });
            }
            return snaps;
        }

        [Fact]
        public void EmptySession_ReturnsNoBottleneck()
        {
            var analyzer = GetAnalyzer();
            var results = analyzer.Analyze(new List<DiagnosticSnapshot>());
            Assert.Empty(results);
        }
        
        [Fact]
        public void SingleSpike_DoesNotTriggerSustainedBottleneck()
        {
            var analyzer = GetAnalyzer();
            var snaps = GenerateSnapshots(9, 30, 30); // 9 normal
            snaps.Add(new DiagnosticSnapshot { CpuMetrics = new CpuMetrics { UsagePercent = 100 }, GpuMetrics = new GpuMetrics { UsagePercent = 100 } }); // 1 spike
            
            var results = analyzer.Analyze(snaps);
            Assert.Empty(results.Where(r => r.Type == BottleneckType.CPU || r.Type == BottleneckType.GPU));
        }

        [Fact]
        public void SustainedGpu_TriggersBottleneck()
        {
            var analyzer = GetAnalyzer();
            // 10 seconds of 98% GPU, 50% CPU
            var snaps = GenerateSnapshots(10, 50, 98);
            
            var results = analyzer.Analyze(snaps);
            
            Assert.Contains(results, r => r.Type == BottleneckType.GPU);
            var gpuBtn = results.First(r => r.Type == BottleneckType.GPU);
            
            Assert.Equal(ConfidenceLevel.High, gpuBtn.Confidence);
            Assert.Equal(Severity.High, gpuBtn.Severity);
        }

        [Fact]
        public void SustainedCpu_TriggersBottleneck()
        {
            var analyzer = GetAnalyzer();
            // 10 seconds of 98% CPU, 50% GPU
            var snaps = GenerateSnapshots(10, 98, 50);
            
            var results = analyzer.Analyze(snaps);
            
            Assert.Contains(results, r => r.Type == BottleneckType.CPU);
            var cpuBtn = results.First(r => r.Type == BottleneckType.CPU);
            
            Assert.Equal(ConfidenceLevel.High, cpuBtn.Confidence);
            Assert.Equal(Severity.High, cpuBtn.Severity);
        }

        [Fact]
        public void MemoryPressure_TriggersCriticalBottleneck()
        {
            var analyzer = GetAnalyzer();
            // 10 seconds of 95% RAM
            var snaps = GenerateSnapshots(10, 50, 50, 0.95);
            
            var results = analyzer.Analyze(snaps);
            
            Assert.Contains(results, r => r.Type == BottleneckType.Memory);
            var ramBtn = results.First(r => r.Type == BottleneckType.Memory);
            
            Assert.Equal(Severity.Critical, ramBtn.Severity);
        }

        [Fact]
        public void ThermalWarning_TriggersBottleneck()
        {
            var analyzer = GetAnalyzer();
            // 10 seconds of 88C
            var snaps = GenerateSnapshots(10, 50, 50, 0.5, 88);
            
            var results = analyzer.Analyze(snaps);
            
            Assert.Contains(results, r => r.Type == BottleneckType.Thermal);
            var tempBtn = results.First(r => r.Type == BottleneckType.Thermal);
            
            Assert.Equal(Severity.High, tempBtn.Severity);
        }

        [Fact]
        public void MultipleBottlenecks_AreRankedBySeverityAndConfidence()
        {
            var analyzer = GetAnalyzer();
            // CPU 95%, GPU 98%, RAM 95%
            var snaps = GenerateSnapshots(10, 95, 98, 0.95);
            
            var results = analyzer.Analyze(snaps);
            
            Assert.True(results.Count >= 3);
            
            // RAM >92% is Critical severity, so it should be first
            Assert.Equal(BottleneckType.Memory, results[0].Type);
        }

        [Fact]
        public void RecommendationEngine_GeneratesCorrectRecommendations()
        {
            var recEngine = new RecommendationEngine();
            var profile = new GameProfile { ExecutablePath = "test.exe", PerformanceMode = PerformanceMode.Balanced, PreferredGpu = GpuPreference.Auto };
            
            var bottlenecks = new List<DiagnosticFinding>
            {
                new DiagnosticFinding { Category = DiagnosticCategory.GPU, Severity = DiagnosticSeverity.Critical, Title = "GPU Bottleneck" }
            };

            var recs = recEngine.GenerateRecommendations(bottlenecks, profile);
            
            // Should recommend Gaming mode, GPU preference, and GPU graphics reduction
            Assert.Contains(recs, r => r.Source == "Diagnostic Engine" && r.Title.Contains("GPU-heavy"));
            Assert.Contains(recs, r => r.Source == "Power Policy" && r.Title.Contains("Gaming"));
            Assert.Contains(recs, r => r.Source == "Hardware Policy" && r.Title.Contains("Radeon 520"));
        }

        [Fact]
        public void SystemHealthAnalyzer_CalculatesScoresAccurately()
        {
            var healthAnalyzer = new SystemHealthAnalyzer();
            var snaps = GenerateSnapshots(10, 90, 85, 0.95); // High CPU, GPU, RAM
            
            var score = healthAnalyzer.CalculateHealthScore(snaps);
            
            Assert.True(score.CpuScore < 50);
            Assert.True(score.GpuScore < 90); // 85 - 80 = 5 * 3 = 15 -> 85 score
            Assert.True(score.RamScore <= 50); 
            Assert.True(score.RamScore <= 50); 
            Assert.NotEmpty(score.Deductions);
        }
    }
}
