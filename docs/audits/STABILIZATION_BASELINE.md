# GamerHub Stabilization Baseline (Phase 0)

Date: 2026-10-07. All facts below were verified by running commands on this machine or by direct code inspection. No claim is taken from prior phase reports without re-verification.

## Toolchain
- System `dotnet`: runtimes only, **no SDK**.
- Repo-local SDK: `.dotnet/sdk/8.0.425` → all commands use `./.dotnet/dotnet.exe`.
- OS: Windows 11 x64 (win32 host), bash shell for commands.

## Build result (verified)
- `./.dotnet/dotnet.exe restore DellGamingControlCenter.sln` → exit 0, all 4 projects restored.
- `./.dotnet/dotnet.exe build DellGamingControlCenter.sln -c Release` → **Build succeeded**, exit 0, **2 warnings**:
  - CS0108 x2 in `Core/Interfaces/IWindowsPowerPlanService.cs` (hides `IPowerPlanController` members; interface-inheritance duplication, benign but flagged).

## Test result (verified)
- `./.dotnet/dotnet.exe test ... -c Release` → **46/46 passed** (22.7 s). The prior agent's "46/46" claim is confirmed on this machine. Includes a real 8 s session lifecycle integration test.

## Publish / packaging state (verified by inspection)
- `publish/` (repo root) contains **8 loose native DLLs** (`D3DCompiler_47_cor3.dll`, `wpfgfx_cor3.dll`, `vcruntime140_cor3.dll`, `PresentationNative_cor3.dll`, `PenImc_cor3.dll`, etc.) + PDBs next to `DellGamingControlCenter.exe` → **NOT a single-file EXE** despite `PublishSingleFile` being set in the csproj. Root cause: `bin/Release/.../win-x64/` (non-publish) output was apparently zipped previously — that folder holds **261 files / 172 MB** (framework-dependent Debug-style layout, not single-file).
- `build/release/` contains only `GamerHub-Setup-v1.0.0.exe` (Inno output) + docs. No portable single EXE exists anywhere.
- `installer/GamerHub.iss` packs `build/publish/*` (the 8-DLL layout) → installer ships a non-single-file app. Elevation: admin (OK for installer). App itself runs unelevated (authorization model confirms).
- `DellGamingControlCenter-v1.0.0-win-x64.zip` exists at root (per .gitignore: zips ignored) — consistent with "ZIP of many files" distribution the user rejects.

## Startup chain (read in full)
`App.Application_Startup` constructs ~30 services manually (static service-locator pattern, no DI container) → starts `MetricsSampler` (2 s interval) → constructs 9 ViewModels → shows `MainWindow`.

## Discovered defects (from code inspection; each to be fixed + regression-tested)

### Startup / crash risks
- **S1 (HIGH):** `CpuTelemetryService` constructor creates a `PerformanceCounter` unguarded. On machines with corrupted/disabled perf counters this throws → `Application_Startup` dies → **app never starts, no window**. Also `PerformanceCounter` creation is slow (~hundreds of ms) on the UI thread.
- **S2 (HIGH):** No `DispatcherUnhandledException`, `AppDomain.UnhandledException`, or `TaskScheduler.UnobservedTaskException` handlers. Any unhandled slip = silent process death or WER dialog with zero diagnostic trail.
- **S3 (MED):** `MainWindow` tray icon uses `SystemIcons.Application` instead of the app's own GamerHub icon.
- **S4 (MED):** `App.OnExit` blocks the UI thread with `StopSessionAsync(...).GetAwaiter().GetResult()`. The awaited chain (`WaitForExitAsync` continuation) captures the UI `SynchronizationContext` → **classic deadlock on exit** whenever a game session is active and the user quits (window X or tray Exit). Session restore may never run → policies left applied.

### Broken / fake / dead UI (button audit)
- **B1 (HIGH):** `BatteryTelemetryService.GetMetrics()` returns a default `BatteryMetrics` ("Dummy for now") with `ChargePercent=0`; Dashboard treats `>=0` as valid → displays **"0% (On Battery)" as fabricated data**.
- **B2 (HIGH):** `BatteryTelemetryService.GetBatteryInfo()` hardcodes `WearPercent = 3.4` ("based on initial diagnostic") — fabricated value shown to users.
- **B3 (HIGH):** `SessionsViewModel` loads history only in its constructor → **Sessions page never refreshes** after a session ends (stale page until app restart).
- **B4 (HIGH):** `SessionsViewModel.ViewReportCommand` is a **placeholder** — shows a MessageBox "Opening report...". The real session report data (`GameSession.Report`, timeline, findings) exists but is never shown.
- **B5 (HIGH):** `PerformanceViewModel` refreshes active-game state on `SessionChanged` but **never re-reads the current power mode** → after a session ends and policies are restored, the mode buttons show stale active state.
- **B6 (MED):** `PerformanceModeService` only knows 3 hard-coded scheme GUIDs. On Windows 11 the "High performance" / "Power saver" schemes often **do not exist** → `powercfg /setactive` fails → GAMING mode button reports failure (or silently fails to change anything). No `duplicatescheme` fallback.
- **B7 (MED):** `BenchmarkViewModel` lists games **only** from the discovery service cache (hard-coded search paths: `D:\`, Steam paths). Games the user adds in Game Center **never appear** in Benchmarks → benchmark buttons look dead when discovery cache is empty; no explanation shown.
- **B8 (MED):** `SettingsViewModel` is **pure decoration** — 11 properties, zero persistence, zero behavior. Sampling interval / start-with-windows / confirmation toggles do nothing and reset on restart.
- **B9 (MED):** `GameCenterViewModel.ActiveCpuUsage`/`ActiveRamUsage` call `MetricsSampler.GetHistory()` (copies the entire 300-snapshot buffer) **twice per getter**, re-evaluated per 2 s tick — needless allocations on a 2C/4T CPU.
- **B10 (LOW):** `MainViewModel` SessionChanged handler: `Dispatcher.InvokeAsync(...).Wait(500)` then fallback executes the action a second time if the dispatcher is slow — double-execution pattern; should be a fire-and-forget `BeginInvoke`.
- **B11 (LOW):** `AsyncRelayCommand` swallows all exceptions to Debug output only → commands fail **silently** with no log trail.
- **B12 (LOW):** Dashboard `StorageUsage` hardcodes "Unavailable" while working storage telemetry (`StorageTelemetryService.GetVolumes()`) exists and is unused.
- **B13 (INFO):** Dead/orphaned views: `OverlayWindow`, `SmartOptimizerWindow`, `GameDetailsView`, `SmartOptimizerViewModel` are never instantiated anywhere (compiled but unreachable). Left in place (harmless), noted for the record.
- **B14 (LOW):** `MemoryTelemetryService.GetRamInfo()` returns `TotalBytes=0` if the WMI query fails → Hardware page would show "0 GB". No fallback.

### Architecture observations (preserved, not bugs)
- Control path is correctly layered: VM → UseCase → AuthorizationService → controller (GPU pref = HKCU registry, power plan = `powercfg`, priority = `Process.PriorityClass`). Read-only telemetry is separate from control. Audit logging exists.
- Exactly ONE telemetry pipeline (`MetricsSampler` → `SnapshotCaptured`) feeds Dashboard, Game Center, Sessions monitor, intelligence engine. No per-page polling loops found (PerformanceViewModel has a 5 s insight timer reading the shared engine — acceptable).
- GPU telemetry honestly returns null usage (shown "Unavailable"); temperature is labeled "Unsupported" everywhere. Good.
- Persistence: `%LocalAppData%\DellGamingControlCenter`, atomic temp-file move, corrupt-config fallback to defaults, stale-session recovery, bounded history (100). Solid.
- Policy restore is idempotent (only writes when current ≠ target). Launch serialized by semaphore. Failed launch restores policies. Good.

## Release risks
- R1: No true single-file EXE exists (see Publish state). Must produce `build/release/` single EXE and verify standalone.
- R2: Installer currently packages the 8-DLL layout; must be re-pointed at the single-file publish output.
- R3: Old zips at repo root contain stale builds; final release must be validated from fresh publish output only.
