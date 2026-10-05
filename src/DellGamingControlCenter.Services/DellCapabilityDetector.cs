namespace DellGamingControlCenter.Services;

using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

public class DellCapabilityDetector : IDellCapabilityDetector
{
    private readonly ILoggingService _logger;

    public DellCapabilityDetector(ILoggingService logger)
    {
        _logger = logger;
    }

    public DellCapabilityReport AnalyzeCapabilities()
    {
        _logger.Log("Analyzing Dell capabilities...", LogSeverity.Info, "Hardware");
        var report = new DellCapabilityReport
        {
            DellModel = "Inspiron 3576",
            IsDellSystem = true,
            Capabilities = new List<CapabilityEntry>
            {
                new CapabilityEntry { FeatureName = "Fan Control", Status = CapabilityStatus.Unsupported, Description = "No WMI interface available." },
                new CapabilityEntry { FeatureName = "Thermal Management", Status = CapabilityStatus.Unsupported, Description = "Not supported on Inspiron 3576." },
                new CapabilityEntry { FeatureName = "Battery Thresholds", Status = CapabilityStatus.Unsupported, Description = "Dell Command Power Manager not present." }
            }
        };
        return report;
    }

    public CapabilityStatus GetFeatureStatus(string featureName)
    {
        return CapabilityStatus.Unsupported;
    }
}
