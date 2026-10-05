namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

/// <summary>
/// Service responsible for determining Dell-specific capabilities (WMI, ACPI, Thermal).
/// </summary>
public interface IDellCapabilityDetector
{
    /// <summary>
    /// Analyzes the system for Dell-specific APIs and returns a capability report.
    /// This dictates which UI controls are visible or enabled.
    /// </summary>
    DellCapabilityReport AnalyzeCapabilities();

    /// <summary>Checks if a specific Dell feature is supported.</summary>
    CapabilityStatus GetFeatureStatus(string featureName);
}
