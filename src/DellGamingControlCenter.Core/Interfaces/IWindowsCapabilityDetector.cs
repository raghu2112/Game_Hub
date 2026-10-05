namespace DellGamingControlCenter.Core.Interfaces;

using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Enums;

/// <summary>
/// Service responsible for analyzing standard Windows capabilities and features (Game Mode, Power Plans, etc.).
/// </summary>
public interface IWindowsCapabilityDetector
{
    /// <summary>Analyzes Windows features and returns a capability report.</summary>
    WindowsCapabilityReport AnalyzeCapabilities();

    /// <summary>Checks if a specific Windows feature is supported.</summary>
    CapabilityStatus GetFeatureStatus(string featureName);
}
