namespace DellGamingControlCenter.Services;

using System;
using Microsoft.Win32;
using DellGamingControlCenter.Core.Interfaces;
using DellGamingControlCenter.Core.Enums;

public class GpuPreferenceController : IGpuPreferenceController
{
    private readonly ILoggingService _logger;
    private const string RegistryPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public GpuPreferenceController(ILoggingService logger)
    {
        _logger = logger;
    }

    public CapabilityStatus Status => CapabilityStatus.Supported;

    public GpuPreference GetPreference(string executablePath)
    {
        string normalizedPath = NormalizePath(executablePath);
        if (string.IsNullOrEmpty(normalizedPath)) return GpuPreference.Auto;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            if (key != null)
            {
                var value = key.GetValue(normalizedPath) as string;
                if (value != null)
                {
                    if (value.Contains("GpuPreference=1")) return GpuPreference.Integrated;
                    if (value.Contains("GpuPreference=2")) return GpuPreference.HighPerformance;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"Failed to get GPU preference: {ex.Message}", LogSeverity.Warning, "Hardware");
        }
        return GpuPreference.Auto;
    }

    public bool SetPreference(string executablePath, GpuPreference preference)
    {
        string normalizedPath = NormalizePath(executablePath);
        if (string.IsNullOrEmpty(normalizedPath)) return false;

        // R7-A: a per-application graphics preference is keyed by a real executable
        // path that Windows matches at process start. Writing a value for a path that
        // does not exist (typo, uninstalled game, wrong drive) reports success while
        // changing nothing Windows will ever honor — the exact "accepted but not
        // effective" failure Round 7 exists to catch. Refuse instead.
        if (!System.IO.File.Exists(normalizedPath))
        {
            _logger.Log($"GPU preference not set: executable not found at '{normalizedPath}'.", LogSeverity.Warning, "Hardware");
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            string value = preference switch
            {
                GpuPreference.Integrated => "GpuPreference=1;",
                GpuPreference.HighPerformance => "GpuPreference=2;",
                _ => "GpuPreference=0;"
            };
            key.SetValue(normalizedPath, value);
            _logger.Log($"Set GPU preference for {normalizedPath} to {preference}", LogSeverity.Info, "Hardware");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Log($"Failed to set GPU preference: {ex.Message}", LogSeverity.Error, "Hardware");
            return false;
        }
    }

    public bool RemovePreference(string executablePath)
    {
        string normalizedPath = NormalizePath(executablePath);
        if (string.IsNullOrEmpty(normalizedPath)) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key != null && key.GetValue(normalizedPath) != null)
            {
                key.DeleteValue(normalizedPath);
                _logger.Log($"Removed GPU preference for {normalizedPath}", LogSeverity.Info, "Hardware");
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.Log($"Failed to remove GPU preference: {ex.Message}", LogSeverity.Warning, "Hardware");
            return false;
        }
    }

    private string NormalizePath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return System.IO.Path.GetFullPath(path);
        }
        catch
        {
            return string.Empty;
        }
    }
}
