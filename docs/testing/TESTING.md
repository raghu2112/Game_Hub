# GamerHub Testing & Quality Assurance Guide

This document describes the test architecture, test execution procedures, test categories, and verification boundaries for GamerHub.

---

## 1. Test Suite Architecture

GamerHub's automated test suite is contained in `tests/DellGamingControlCenter.Tests/DellGamingControlCenter.Tests.csproj` and executed with **xUnit** and **Moq**.

- **Target Framework:** `net8.0-windows`
- **Total Automated Tests:** 182 passed tests
- **Execution Mode:** Single-threaded serial execution (`DisableTestParallelization = true` in `TestConfig.cs`) to ensure stability across shared memory mocks, Windows power policies, and WPF thread dispatchers.

---

## 2. Test Categories & Coverage Map

| Test Suite File | Test Scope | Focus Areas |
| :--- | :--- | :--- |
| [`HWiNFOSharedMemoryProtocolTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/HWiNFOSharedMemoryProtocolTests.cs) | Unit / Protocol | Memory structure packing, byte offsets, mutex timeouts, signature validation, stale sensor detection, and conversion math. |
| [`HardwareTelemetryProviderTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/HardwareTelemetryProviderTests.cs) | Unit / Integration | 3-tier fallback cascade (`CompositeHardwareTelemetryProvider`), fallback to Windows Native when HWiNFO is offline, and sensor merging. |
| [`HardwareControlTruthTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/HardwareControlTruthTests.cs) | Contract / Security | Validates that unsupported hardware controls (undervolt, fan curves) throw unsupported exceptions and never perform raw hardware writes. |
| [`HardwareAbstractionTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/HardwareAbstractionTests.cs) | Unit | HAL service behavior (`CpuTelemetryService`, `MemoryTelemetryService`, `GpuTelemetryService`). |
| [`SecurityPolicyTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/SecurityPolicyTests.cs) | Unit / Security | Validates `AuthorizationService`, `ControlAuditLogger`, and power policy boundaries. Ensures unelevated safety. |
| [`PerformanceModeTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/PerformanceModeTests.cs) | Unit | Power plan scheme GUID mappings and switching logic. |
| [`PerformanceModeMappingTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/PerformanceModeMappingTests.cs) | Integration | Windows OS power scheme GUID resolution and verification against `powercfg`. |
| [`GameSessionIntegrationTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/GameSessionIntegrationTests.cs) | Integration | Game launch, session monitoring, baseline capture, and pre-launch policy restoration upon process exit. |
| [`GameLauncherTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/GameLauncherTests.cs) | Unit | Executable path validation, process startup safety, and missing file error handling. |
| [`DiagnosticTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/DiagnosticTests.cs) | Unit | `DiagnosticEngine`, `BottleneckAnalyzer`, and `RecommendationEngine` rules. |
| [`DiagnosticRulesTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/DiagnosticRulesTests.cs) | Unit | Rule evaluation for high CPU, high RAM, disk contention, and power plan mismatch. |
| [`BenchmarkTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/BenchmarkTests.cs) | Unit | Benchmark run aggregation, baseline comparisons, and metric calculations. |
| [`ThemeResourceTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/ThemeResourceTests.cs) | UI Surface | Validates that all static and dynamic theme brushes, text styles, and card templates exist in `DarkGamingTheme.xaml`. |
| [`ClaymorphismContrastTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/ClaymorphismContrastTests.cs) | Accessibility | WCAG contrast ratio verification across foreground text and background card brushes. |
| [`UiBindingAndConverterTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/UiBindingAndConverterTests.cs) | UI Logic | Value converters (`CountToVisibilityConverter`, `StringToVisibilityConverter`, `InverseBooleanConverter`). |
| [`ViewModelTelemetryFlowTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/ViewModelTelemetryFlowTests.cs) | MVVM Flow | Ensures telemetry snapshots flow correctly into ViewModel observable properties without data fabrication. |
| [`WpfSurfaceRuntimeTests.cs`](file:///E:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/WpfSurfaceRuntimeTests.cs) | UI Surface | Validates XAML structure, modal dialog contracts, and desktop overlay contracts. |

---

## 3. How to Run Tests

### Command Line
```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

dotnet test tests/DellGamingControlCenter.Tests/DellGamingControlCenter.Tests.csproj -c Release
```

### Filtering Specific Tests
```powershell
# Run only HWiNFO protocol tests
dotnet test tests/DellGamingControlCenter.Tests/DellGamingControlCenter.Tests.csproj --filter "FullyQualifiedName~HWiNFO"

# Run only UI resource tests
dotnet test tests/DellGamingControlCenter.Tests/DellGamingControlCenter.Tests.csproj --filter "FullyQualifiedName~ThemeResourceTests"
```

---

## 4. Distinction Between Testing Tiers

1. **Unit Tests (182 Tests):**
   - Executed via `dotnet test`. Completely mock-isolated or in-memory. Fast execution (~16-20 seconds).
   - Validates business logic, protocol parsers, rule engines, and ViewModel binding flow.
2. **Automated UI/QA Harnesses (`qa/` scripts):**
   - Executed against the built executable (`build/release/DellGamingControlCenter.exe`).
   - Uses Windows UI Automation (UIA) to click buttons, verify navigation tabs, validate marker text, open modal dialogs, and capture screenshots.
3. **Live Runtime Smoke Tests:**
   - Launches the published single-file `.exe` in an interactive Windows session.
   - Verifies process spawning, window message pump responsiveness (`Responding: True`), absence of unhandled exceptions, and clean exit via `CloseMainWindow()`.
