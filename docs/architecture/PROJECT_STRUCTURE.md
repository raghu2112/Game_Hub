# GamerHub Project Structure Guide

This guide details the organization of the GamerHub repository, explaining the responsibilities of each directory, architectural conventions, and instructions for extending the codebase.

---

## 1. Directory Structure

```text
Game Hub/
├── DellGamingControlCenter.sln      # Primary Visual Studio solution
├── README.md                        # Project landing page & quick start
├── .gitignore                       # Git ignore configuration
├── docs/                            # Project documentation
│   ├── architecture/                # System architecture & structural guides
│   ├── development/                 # Build & run instructions
│   ├── hardware/                    # Telemetry capabilities & hardware contracts
│   ├── testing/                     # Test suite documentation & QA guides
│   ├── releases/                    # Release engineering & publish procedures
│   └── audits/                      # Codebase audit & stabilization reports
├── src/                             # Application source code
│   ├── DellGamingControlCenter.App/ # WPF presentation & composition root
│   ├── DellGamingControlCenter.Core/# Domain contracts, models, enums
│   └── DellGamingControlCenter.Services/ # HAL, services, telemetry providers
├── tests/                           # Verification projects
│   └── DellGamingControlCenter.Tests/# 182 xUnit unit & integration tests
├── scripts/                         # Operational & installation scripts
├── installer/                       # Inno Setup packaging script (GamerHub.iss)
├── qa/                              # Automated UI testing & visual capture harnesses
├── assets/                          # Icons, artwork, and branding assets
├── build/                           # Build deliverables (build/release/)
└── dist/                            # Packaged releases (installers, portable zips)
```

---

## 2. Project Responsibilities

### `DellGamingControlCenter.Core`
- **Location:** `src/DellGamingControlCenter.Core/`
- **Target:** `net8.0-windows`
- **Dependencies:** None.
- **Key Folders:**
  - `Interfaces/`: Public contracts (`IHardwareTelemetryProvider.cs`, `IGameSessionManager.cs`, `IPerformanceModeService.cs`, etc.).
  - `Models/`: Data transfer objects, entity definitions, and immutable records (`AppModels.cs`, `HardwareModels.cs`, `DiagnosticModels.cs`, `Phase6Models.cs`, `Phase7Models.cs`, `SecurityModels.cs`).
  - `Enums/`: Shared domain enumerations (`Enums.cs`).

### `DellGamingControlCenter.Services`
- **Location:** `src/DellGamingControlCenter.Services/`
- **Target:** `net8.0-windows`
- **Dependencies:** `DellGamingControlCenter.Core`, `System.Management`, `Microsoft.Win32.Registry`, `System.Diagnostics.PerformanceCounter`, `LibreHardwareMonitorLib`.
- **Key Folders & Files:**
  - `Hardware/`: Telemetry providers (`CompositeHardwareTelemetryProvider.cs`, `HWiNFOSharedMemoryProvider.cs`, `LibreHardwareMonitorTelemetryProvider.cs`, `WindowsStandardTelemetryProvider.cs`).
  - Root: Service implementations (`MetricsSampler.cs`, `DiagnosticEngine.cs`, `GameSessionManager.cs`, `WindowsPowerPlanService.cs`, `AuthorizationService.cs`, `ConfigurationService.cs`, `GameDiscoveryService.cs`, `BenchmarkService.cs`).

### `DellGamingControlCenter.App`
- **Location:** `src/DellGamingControlCenter.App/`
- **Target:** `net8.0-windows` (`WinExe`, WPF, Windows Forms for notification icons)
- **Dependencies:** `DellGamingControlCenter.Core`, `DellGamingControlCenter.Services`.
- **Key Folders & Files:**
  - `App.xaml` / `App.xaml.cs`: Composition root and application entry point.
  - `MainWindow.xaml` / `.cs`: Primary shell window hosting the navigation sidebar and content frame.
  - `Views/`: Navigation user controls (`DashboardView.xaml`, `GameCenterView.xaml`, `PerformanceView.xaml`, `DiagnosticsView.xaml`, `SessionsView.xaml`, `HardwareView.xaml`, `SettingsView.xaml`, `BenchmarkView.xaml`) and modal dialogs (`GameEditorWindow.xaml`, `ProfileEditorWindow.xaml`, `SessionReportWindow.xaml`).
  - `ViewModels/`: MVVM ViewModels corresponding to views and dialogs.
  - `Themes/`: Application styling (`DarkGamingTheme.xaml` defining brushes, button templates, and card styles).
  - `Controls/`: Reusable custom UI components (`CircularGauge.cs`, `MeterBar.cs`, `UsageGraph.cs`, `Hardware3DCoreView.xaml`, `MeshBuilder3D.cs`).
  - `Converters/`: WPF value converters (`CountToVisibilityConverter.cs`, `StringToVisibilityConverter.cs`, `InverseBooleanConverter.cs`).

### `DellGamingControlCenter.Tests`
- **Location:** `tests/DellGamingControlCenter.Tests/`
- **Target:** `net8.0-windows`
- **Dependencies:** Core, Services, App, `xunit`, `Moq`, `Microsoft.NET.Test.Sdk`.
- **Contents:** 182 comprehensive unit, protocol, UI surface, and security tests.

---

## 3. Extension & Contribution Conventions

### Where to Add New Code

| Component Type | Destination Folder | Naming & Design Pattern |
| :--- | :--- | :--- |
| **New Domain Model** | `src/DellGamingControlCenter.Core/Models/` | Suffix with entity name (e.g., `CustomProfile.cs`). Keep clean of framework references. |
| **New Service Interface** | `src/DellGamingControlCenter.Core/Interfaces/` | Prefix with `I` (e.g., `ICustomService.cs`). |
| **New Service Implementation** | `src/DellGamingControlCenter.Services/` | Implement interface from Core. Register as singleton in `App.xaml.cs`. |
| **New Telemetry Provider** | `src/DellGamingControlCenter.Services/Hardware/` | Implement `IHardwareTelemetryProvider`. Wire into `CompositeHardwareTelemetryProvider`. |
| **New View (Page)** | `src/DellGamingControlCenter.App/Views/` | Use UserControl ending in `View.xaml`. Declare `x:Class` matching namespace. |
| **New ViewModel** | `src/DellGamingControlCenter.App/ViewModels/` | Inherit from `BaseViewModel`. Suffix with `ViewModel.cs`. |
| **New Unit Test** | `tests/DellGamingControlCenter.Tests/` | Suffix with `Tests.cs`. Mark test methods with `[Fact]` or `[Theory]`. |

### Naming Conventions
- **Namespaces:** Match the folder structure under `DellGamingControlCenter.*`.
- **WPF Resources:** Use descriptive PascalCase resource keys with suffix matching the resource type (e.g., `HeaderTextStyle`, `AccentSignalCyan`, `CardBackgroundBrush`).
- **Telemetry Rules:** When introducing new telemetry points, always report `double.NaN` when unsupported and mark `IsSupported = false`. Never mock or infer unverified hardware readings.
