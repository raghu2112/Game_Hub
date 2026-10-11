namespace DellGamingControlCenter.App.ViewModels;

using System.Collections.ObjectModel;
using DellGamingControlCenter.Core.Models;
using DellGamingControlCenter.Core.Interfaces;

public class CapabilityItem
{
    public string Name { get; set; } = "";
    public bool IsSupported { get; set; }
    public string Description { get; set; } = "";
}

public class HardwareViewModel : BaseViewModel
{
    private string _systemModel = ""; // populated from WMI at load; generic fallback — no vendor branding
    private string _cpuName = "Intel Core i3-7020U (2.30 GHz)";
    private string _gpu1Name = "Intel HD Graphics 620";
    private string _gpu2Name = "AMD Radeon 520 (2 GB)";
    private string _memoryInfo = "12 GB DDR4";
    private string _storage1Info = "256 GB SSD";
    private string _storage2Info = "1 TB HDD";

    public string SystemModel { get => _systemModel; set => SetProperty(ref _systemModel, value); }
    public string CpuName { get => _cpuName; set => SetProperty(ref _cpuName, value); }
    public string Gpu1Name { get => _gpu1Name; set => SetProperty(ref _gpu1Name, value); }
    public string Gpu2Name { get => _gpu2Name; set => SetProperty(ref _gpu2Name, value); }
    public string MemoryInfo { get => _memoryInfo; set => SetProperty(ref _memoryInfo, value); }
    public string Storage1Info { get => _storage1Info; set => SetProperty(ref _storage1Info, value); }
    public string Storage2Info { get => _storage2Info; set => SetProperty(ref _storage2Info, value); }

    public ObservableCollection<CapabilityItem> Capabilities { get; } = new();
    public ObservableCollection<HardwareSensorReading> SensorInventory { get; } = new();

    public HardwareViewModel()
    {
        LoadHardwareDetails();
        LoadCapabilities();
    }

    private void LoadHardwareDetails()
    {
        var hw = App.HardwareProfileProvider.GetProfile();
        SystemModel = $"{hw.Cpu.Manufacturer} Device";
        CpuName = hw.Cpu.Model;
        
        if (hw.Gpus.Count > 0)
        {
            var integrated = hw.Gpus.FirstOrDefault(g => g.Type == Core.Enums.GpuType.Integrated);
            if (integrated != null) Gpu1Name = integrated.Name;
            
            var discrete = hw.Gpus.FirstOrDefault(g => g.Type == Core.Enums.GpuType.Discrete);
            if (discrete != null) Gpu2Name = discrete.Name;
        }

        MemoryInfo = $"{hw.Memory.TotalBytes / 1024 / 1024 / 1024} GB ({hw.Memory.SpeedMHz} MHz)";
        
        if (hw.Storage.Count > 0)
        {
            Storage1Info = $"{hw.Storage[0].Model} ({hw.Storage[0].Type})";
            if (hw.Storage.Count > 1) Storage2Info = $"{hw.Storage[1].Model} ({hw.Storage[1].Type})";
        }

        SensorInventory.Clear();
        var sensors = App.HardwareTelemetryProvider?.CaptureSnapshot()?.DiscoveredSensors;
        if (sensors != null)
        {
            foreach (var sensor in sensors)
            {
                SensorInventory.Add(sensor);
            }
        }
    }

    private void LoadCapabilities()
    {
        var profile = App.HardwareProfileProvider.GetProfile();
        foreach (var kvp in profile.Capabilities)
        {
            var isSupported = kvp.Value == Core.Enums.CapabilityStatus.Supported || 
                              kvp.Value == Core.Enums.CapabilityStatus.Limited || 
                              kvp.Value == Core.Enums.CapabilityStatus.RequiresElevation;

            string description = $"Status: {kvp.Value}";
            if (!isSupported)
            {
                description = kvp.Key switch
                {
                    Core.Enums.HardwareCapability.FanSpeedControl => "Unsupported on this hardware.\nGamerHub could not detect a supported embedded-controller (EC) interface for fan-speed control on this system.",
                    Core.Enums.HardwareCapability.DellThermalControl => "Unsupported on this hardware.\nNo hardware-level thermal modes are exposed by the system BIOS for this device.",
                    Core.Enums.HardwareCapability.GpuOverclocking => "Unsupported on this hardware.\nThe GPU does not support user-defined voltage or frequency scaling.",
                    Core.Enums.HardwareCapability.CpuVoltageControl => "Unsupported on this hardware.\nThis CPU has a locked voltage and frequency multiplier.",
                    _ => $"Unsupported on this hardware.\nThe capability '{kvp.Key}' is not present or accessible on this system."
                };
            }

            Capabilities.Add(new CapabilityItem 
            { 
                Name = kvp.Key.ToString(), 
                IsSupported = isSupported,
                Description = description
            });
        }
    }
}
