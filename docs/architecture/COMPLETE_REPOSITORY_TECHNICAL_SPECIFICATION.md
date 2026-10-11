# GamerHub Complete Technical Specification & Forensic Audit

## 1. Executive Summary
**GamerHub** (internally `DellGamingControlCenter`) is a WPF-based (.NET 8.0) desktop application designed to monitor hardware telemetry, optimize system performance, and orchestrate gaming sessions. Although conceptually branded around Dell/Alienware hardware optimization, the underlying implementation is largely generic Windows-based, relying on WMI (Windows Management Instrumentation), Registry manipulation, and `System.Diagnostics` to execute optimizations.

**Current State**: The repository is in an advanced prototype or MVP state. The UI is fully built out and connected to functional backend services. However, several "advanced" hardware control features (like Fan Control, Overclocking, Undervolting, and direct Thermal management) are mocked, explicitly logged as "Unsupported on this hardware," or intentionally stubbed out due to hardware limitations (e.g., targeted for Dell Inspiron 3576 / Core i3-7020U / Radeon 520, which has locked multipliers and no thermal sensors exposed for the GPU).

---

## 2. Evidence Grading Key
Throughout this document, assertions are graded based on forensic evidence found in the repository:
*   🟢 **Source-Confirmed**: Verified by reading the implementation code.
*   🟡 **Inferred / Implied**: Deduced from variable names, UI bindings, or partial implementations.
*   🔴 **Contradicted**: Where documented intent or UI text differs from actual implementation.
*   ⚪ **Not Applicable**: Outside the scope of the current codebase.

---

## 3. Architecture Overview 
🟢 **Source-Confirmed**

The application strictly follows a multi-tier architecture, separated into three primary .NET 8.0 projects:

1.  **`DellGamingControlCenter.Core`**: (Class Library) Contains zero business logic. Holds all data contracts, Enums (e.g., `PerformanceMode`, `GpuPreference`), POCO Models (`GameSession`, `HardwareSnapshot`), and Interfaces (`IGameSessionManager`, `IDiagnosticEngine`).
2.  **`DellGamingControlCenter.Services`**: (Class Library) The implementation layer. Contains all hardware interaction, telemetry polling, background task management, registry editing, and diagnostic rules. No UI dependencies.
3.  **`DellGamingControlCenter.App`**: (WPF Application) The presentation layer. Follows the MVVM (Model-View-ViewModel) pattern. Contains `Views` (XAML), `ViewModels`, and the DI (Dependency Injection) container setup in `App.xaml.cs`.

### 3.1 Subsystem Communication
Communication between the App and Services layers is strictly mediated via Interfaces injected through `Microsoft.Extensions.DependencyInjection`.
Global, long-lived services (like `IMetricsSampler` and `IGameSessionManager`) expose .NET `event`s (e.g., `SnapshotCaptured`, `SessionChanged`). `ViewModels` subscribe to these events and marshal updates to the UI thread using a custom `UiThread.Run()` helper (or `Dispatcher.InvokeAsync`) to prevent thread-blocking deadlocks.

---

## 4. Feature Implementation Audit

### 4.1 Telemetry & Hardware Detection
🟢 **Source-Confirmed**
Hardware telemetry is aggressively gathered, primarily using WMI.
*   **CPU**: `HardwareDetector.cs` queries `Win32_Processor`. Live metrics query `PerformanceCounter("Processor", "% Processor Time", "_Total")`. Base clock is hardcoded as a fallback (2304 MHz for i3-7020U).
*   **GPU**: Uses `Win32_VideoController`. Detects both Integrated (Intel) and Discrete (AMD Radeon 520). 🔴 **Contradicted**: GPU Utilization telemetry exists in the model but is often mocked or returned as `null`/`-1` because the Radeon 520 doesn't expose easy thermal/utilization metrics through standard WMI.
*   **RAM**: Uses `Win32_OperatingSystem` (`TotalVisibleMemorySize`, `FreePhysicalMemory`) for live usage, and `Win32_PhysicalMemory` for hardware details.
*   **Storage**: Uses `Win32_DiskDrive` and `Win32_LogicalDisk`. Correctly differentiates between SSD and HDD (based on model string matching).

### 4.2 Game Session Orchestration (`GameSessionManager.cs`)
🟢 **Source-Confirmed**
This is the heart of the application. The `GameSessionManager` manages the lifecycle of a game.
1.  **StartSessionAsync**: 
    *   Validates game executable.
    *   Fires `GameSessionState.Optimizing`.
    *   Applies Policies (GPU Preference, Power Plan, Process Priority).
    *   Launches the process via `Process.Start()`.
2.  **Monitoring**: 
    *   It uses a "Watcher Task" (`Task.Run` loop) that monitors the launched process ID.
    *   It maintains a `ConcurrentDictionary<string, Task>` of active watchers to prevent GC collection of the `Process` object.
3.  **StopSessionAsync / Restoration**:
    *   Upon process exit, it transitions to `GameSessionState.Restoring`.
    *   It reverses the policies applied at startup (e.g., puts the power plan back to Balanced).
    *   Fires a `SessionChanged` event so the UI knows gaming has concluded.

### 4.3 Hardware Abstraction / Optimizations
The application attempts to optimize the system using native Windows mechanisms rather than proprietary driver calls.
*   **GPU Preference (`GpuPreferenceController.cs`)**: 🟢 **Source-Confirmed**. Edits the Windows Registry at `HKCU\SOFTWARE\Microsoft\DirectX\UserGpuPreferences`. Sets the `GpuPreference` string to `1` (Power Saving / Integrated) or `2` (High Performance / Discrete). Refuses to write keys if the `.exe` doesn't exist to prevent registry bloat.
*   **Power Plans (`WindowsPowerPlanService.cs` & `PerformanceModeService.cs`)**: 🟢 **Source-Confirmed**. Uses the `powercfg.exe` command-line utility via `Process.Start` to change active power schemes (e.g., `powercfg /setactive <GUID>`). It attempts to detect missing schemes and duplicates them using `powercfg /duplicatescheme`.
*   **Process Priority (`ProcessController.cs`)**: 🟢 **Source-Confirmed**. Uses standard `System.Diagnostics.Process` to find running instances of a game and elevate their priority (e.g., `ProcessPriorityClass.High`).
*   **Memory Optimization**: 🔴 **Contradicted**. The UI suggests "Standby List Cleanup", but `MemoryOptimizationService.TryCleanupStandbyListAsync()` returns `false` and explicitly logs that undocumented APIs/elevation are required and thus refuses to execute it.

### 4.4 Diagnostic Engine & Rules (`DiagnosticEngine.cs`, `DiagnosticRules.cs`)
🟢 **Source-Confirmed**
The system continuously buffers `DiagnosticSnapshot` objects (captured every 2 seconds by `MetricsSampler`).
The `DiagnosticEngine` runs a collection of `IDiagnosticRule` implementations against the snapshot history:
*   `HighMemoryUsageRule`: Flags if RAM > 85% or 92%.
*   `HighCpuUsageRule`: Flags if CPU > 90% sustained.
*   `GameOnHddRule`: Detects if the game executable is located on a drive identified as an HDD, warning about slow load times.
*   `IntegratedGpuPreferenceRule`: Warns if the game profile is explicitly set to use the Integrated GPU instead of High Performance.
*   `PowerPlanMismatchRule`: Warns if a game is active but Windows is in Power Saver mode.

### 4.5 UI and MVVM (`DellGamingControlCenter.App`)
🟢 **Source-Confirmed**
*   **Initialization**: `App.xaml.cs` builds a standard `Microsoft.Extensions.DependencyInjection` `ServiceCollection`. It registers all services as singletons. It starts background services (like `MetricsSampler`) upon startup.
*   **Navigation**: Controlled by `MainViewModel`. Changes `CurrentViewModel` and updates boolean flags (`IsDashboardActive`, `IsGameCenterActive`) to drive UI highlight states.
*   **Pages**:
    *   `DashboardViewModel`: Displays live telemetry, current active game, and top-level health score.
    *   `GameCenterViewModel`: Manages the library of games (`DiscoveredGames`). Can launch games.
    *   `PerformanceViewModel`: Allows manual switching of `PerformanceMode`. Displays hardware capability matrix.
    *   `DiagnosticsViewModel`: Displays the results of the `DiagnosticEngine`.
    *   `SessionsViewModel`: Displays a history of past gaming sessions (loaded from a local JSON config).
    *   `OverlayViewModel`: Data context for an in-game overlay (though injection mechanisms into fullscreen games are not implemented; it appears to be a transparent WPF window).
*   **Thread Safety**: A `UiThread.Run(Action)` helper is used extensively in ViewModels to safely marshal WMI events and background task completions back to the WPF Dispatcher thread.

### 4.6 Data Persistence
🟢 **Source-Confirmed**
Configuration and session history are stored locally in the user's `AppData\Local\DellGamingControlCenter` directory as JSON files.
*   `config.json`: Managed by `ConfigurationService`. Stores `GameProfile`s, `AppConfiguration` (settings), and `SessionHistory`.
*   `game_cache.json`: Managed by `GameDiscoveryService`. Caches the locations of found game executables.
*   `activity_log.json`: Managed by `LoggingService`. Keeps the last 1000 application events.
*   `benchmarks/`: Managed by `BenchmarkService`. Stores historical benchmark JSON files per game.

---

## 5. Security & Elevation Context
🟢 **Source-Confirmed**
The application uses an `IAuthorizationService` (`AuthorizationService.cs`) and `ControlAuditLogger.cs`.
Currently, the `AuthorizationService` simply checks if the application is running as an Administrator (by checking `WindowsPrincipal` and `WindowsBuiltInRole.Administrator`).
If the app is not elevated, policies like `ChangeGpuPreference` and `ChangeProcessPriority` will fail gracefully, return a `ControlResult.Failed`, and log an audit event, rather than crashing the application.

---

## 6. Constraints & Missing Implementations
*   **Hardware Control Mocking**: As verified in `HardwareProfileProvider.cs` and `DellCapabilityDetector.cs`, fan control, thermal reading/control, and CPU/GPU overclocking are explicitly marked as `CapabilityStatus.Unsupported`. The UI displays them, but notes they are unsupported on the current hardware.
*   **FPS Monitoring**: `UnavailableFpsProvider.cs` always returns `IsAvailable() = false`. There is no actual DirectX/Vulkan hook implemented to count frames.
*   **Game Discovery**: `GameDiscoveryService.cs` searches hardcoded paths (`D:\`, `C:\Program Files (x86)\Steam\...`) and looks for hardcoded executables (e.g., `gta-sa.exe`, `RDR.exe`, `eurotrucks2.exe`). It is not a generic game scanner (like reading uninstall registry keys or parsing Steam VDF files).

---

## 7. Conclusion
GamerHub is a robust, well-structured WPF MVVM application acting as a Windows system orchestrator. It successfully implements software-level gaming optimizations (Power Plans, Registry GPU Preferences, Process Priority) and robust telemetry polling (WMI). It explicitly avoids dangerous or undocumented API calls, gracefully degrading features that are unsupported by the host hardware (specifically targeted at low-end/locked laptops like the i3-7020U).
