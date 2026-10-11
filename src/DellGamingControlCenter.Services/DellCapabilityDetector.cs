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

    private DellCapabilityReport? _cachedReport;

    public DellCapabilityReport AnalyzeCapabilities()
    {
        if (_cachedReport != null) return _cachedReport;

        _logger.Log("Analyzing hardware capabilities...", LogSeverity.Info, "Hardware");
        
        bool isDell = CheckIsDellSystem();
        
        var report = new DellCapabilityReport
        {
            DellModel = GetDellModel(),
            IsDellSystem = isDell,
            Capabilities = new List<CapabilityEntry>
            {
                new CapabilityEntry { FeatureName = "Fan Control", Status = CheckWmiClassExists(@"root\WMI", "DellSMBIOS") ? CapabilityStatus.RequiresElevation : CapabilityStatus.Unsupported, Description = "Dell SMBIOS WMI interface" },
                new CapabilityEntry { FeatureName = "Thermal Management", Status = CheckWmiClassExists(@"root\dcim\sysman", "DCIM_ThermalInformation") ? CapabilityStatus.RequiresElevation : CapabilityStatus.Unsupported, Description = "DCIM Thermal interface" },
                new CapabilityEntry { FeatureName = "Battery Thresholds", Status = CheckWmiClassExists(@"root\dcim\sysman", "DCIM_Battery") ? CapabilityStatus.RequiresElevation : CapabilityStatus.Unsupported, Description = "Dell Command Power Manager interface" }
            }
        };
        _cachedReport = report;
        return report;
    }

    public CapabilityStatus GetFeatureStatus(string featureName)
    {
        var report = AnalyzeCapabilities();
        var feature = report.Capabilities.FirstOrDefault(c => c.FeatureName.Equals(featureName, StringComparison.OrdinalIgnoreCase));
        return feature?.Status ?? CapabilityStatus.Unsupported;
    }

    private bool CheckIsDellSystem()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                var manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                if (manufacturer.Contains("Dell", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private string GetDellModel()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                return obj["Model"]?.ToString() ?? "Unknown";
            }
        }
        catch { }
        return "Unknown";
    }

    private bool CheckWmiClassExists(string namespaceName, string className)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(new System.Management.ManagementScope(namespaceName), new System.Management.WqlObjectQuery($"SELECT * FROM meta_class WHERE __class = '{className}'"));
            var count = searcher.Get().Count;
            return count > 0;
        }
        catch
        {
            // If the namespace doesn't exist or access is denied, we can't use it.
            return false;
        }
    }
}
