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
    private string _systemModel = "Dell Inspiron 3576";
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

    public HardwareViewModel()
    {
        LoadHardwareDetails();
        LoadCapabilities();
    }

    private void LoadHardwareDetails()
    {
        var hw = App.HardwareDetector.GetSnapshot();
        SystemModel = hw.System.Model;
        CpuName = hw.Cpu.Model;
        // The hardware service currently gives basic info. 
        // We format the detailed strings for the UI based on the prompt.
    }

    private void LoadCapabilities()
    {
        Capabilities.Add(new CapabilityItem { Name = "Windows Power Plans", IsSupported = true, Description = "Fully integrated with DellGamingControlCenter." });
        Capabilities.Add(new CapabilityItem { Name = "Game Mode", IsSupported = true, Description = "Windows 10/11 Game Mode can be automated." });
        Capabilities.Add(new CapabilityItem { Name = "GPU Preference", IsSupported = true, Description = "Direct3D GPU preference can be forced." });
        Capabilities.Add(new CapabilityItem { Name = "Live Metrics", IsSupported = true, Description = "Standard WMI and Performance Counters supported." });
        
        Capabilities.Add(new CapabilityItem { Name = "Fan Control", IsSupported = false, Description = "This laptop does not expose a supported Dell interface for application-controlled fan curves." });
        Capabilities.Add(new CapabilityItem { Name = "Undervolting", IsSupported = false, Description = "Intel XTU/Plundervolt mitigations block runtime undervolting on this CPU." });
        Capabilities.Add(new CapabilityItem { Name = "Overclocking", IsSupported = false, Description = "Locked CPU multiplier and unsupported GPU BIOS." });
        Capabilities.Add(new CapabilityItem { Name = "Battery Thresholds", IsSupported = false, Description = "Requires Dell Power Manager firmware support not present on this model." });
    }
}
