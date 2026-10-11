# GamerHub Stabilization Report

Date: 2026-10-07. Every claim below cites a command, a runtime observation, or a log entry captured during this session. Companion document: `STABILIZATION_BASELINE.md` (defect inventory S1–S4, B1–B14).

---

## 1. Baseline (verified, not inherited)

| Check | Result |
|---|---|
| `./.dotnet/dotnet.exe restore DellGamingControlCenter.sln` | exit 0, 4 projects restored |
| `./.dotnet/dotnet.exe build -c Release` (before changes) | Build succeeded, exit 0, 2 warnings (CS0108 x2, benign interface hiding) |
| `./.dotnet/dotnet.exe test -c Release` (before changes) | 46/46 passed (22.7 s) — prior agent's claim confirmed on this machine |
| Runtime launch (pre-fix) | **CRASH reproduced**: process alive at t+3 s, dead by t+7 s, exit code `0xE0434352` (unhandled .NET exception) |
| Publish state (pre-fix) | No single-file EXE anywhere; `publish/` had 8 loose native DLLs; `bin/.../win-x64/` had 261 files/172 MB; old zips at repo root |

---

## 2. Bugs discovered → root cause → fix (all verified)

### CRASH-1 — App died 4–7 s after launch (the startup crash)
- **Evidence:** PowerShell lifecycle probe (`HasExited=True`, exit `0xE0434352`); Windows Event Log `.NET Runtime` entry: `XamlParseException: Cannot find resource named 'SurfaceBorder'` → `Window.Show()` → `App.Application_Startup`.
- **Root cause:** `DashboardView.xaml:116` and `PerformanceView.xaml:103` referenced `{StaticResource SurfaceBorder}`; the theme defines `AppBorder`. Stale references from an earlier resource rename; templates threw when realized at `Window.Show()`.
- **Fix:** Both references → `{StaticResource AppBorder}`. Verified by audit script over all XAML (see CRASH-2) and by relaunch.

### CRASH-2 — Second missing resource, surfaced only at navigation-time template realization
- **Evidence:** Post-fix installed run showed `Responding=False` at t+30 s; activity log recorded a storm of `XamlParseException` at 21:26:16 (pre-root-cause-logging).
- **Root cause:** `PerformanceView.xaml` referenced `{StaticResource StringToVisibilityConverter}` — never defined anywhere.
- **Fix:** Implemented `Converters/StringToVisibilityConverter.cs` (empty→Collapsed) and registered it in `App.xaml`. Added a static audit (all `{StaticResource}` refs vs. defined `x:Key`s across theme, App.xaml, and every view) — **currently zero missing references**.

### S1 — Perf-counter ctor could kill startup on machines with corrupted counters
- **Root cause:** `new PerformanceCounter(...)` unguarded in `CpuTelemetryService` ctor, executed on the UI thread during `Application_Startup`.
- **Fix:** Guarded creation; on failure the service reports usage as `-1` ("Unavailable"). Regression test `CpuTelemetry_Unavailable_SentinelIsNegativeOne` added.

### S2 — No global exception handlers
- **Fix:** `DispatcherUnhandledException` (logs root-cause chain via inner-exception walk, shows one message, keeps UI alive), `AppDomain.UnhandledException` (logs), `TaskScheduler.UnobservedTaskException` (logs, marks observed). Verified: the CRASH-2 storm was captured with full traces in `activity_log.json` — exactly the diagnostic trail that was missing.

### S3 — Tray icon used generic `SystemIcons.Application`
- **Fix:** `Icon.ExtractAssociatedIcon` on the real EXE path (IL3000-safe via `AppContext.BaseDirectory`), fallback preserved.

### S4 — Exit deadlock class: `StopSessionAsync(...).GetAwaiter().GetResult()` on UI thread
- **Evidence (pre-fix):** log entry `Session restore during exit failed: The calling thread cannot access this object because a different thread owns it.` after a completed restore — UI-thread blocking + background UI-marshal races.
- **Fix:** restore chain runs via `Task.Run(...).GetAwaiter().GetResult()` (no sync-context capture); `MetricsSampler` is stopped **before** exit-restore; `App.IsShuttingDown` gate suppresses the session-end MessageBox during teardown; AggregateException unwrapped for logging. Verified: repeated close-during-session tests show clean shutdown, no thread-affinity errors.

### B1/B2 — Fabricated battery data
- **Root cause:** `GetMetrics()` returned default struct (0% "On Battery"); `WearPercent` hardcoded `3.4`.
- **Fix:** Real WMI read (`Win32_Battery`: charge %, AC/charging, estimated runtime); `-1` sentinel = unavailable; wear computed from design vs. full-charge capacity (null when firmware data missing). Dashboard shows "Unavailable" instead of fake values. Regression tests added.

### B3/B4 — Sessions page never refreshed; VIEW REPORT was a MessageBox placeholder
- **Fix:** `SessionsViewModel` subscribes to `SessionChanged` (via new shared `UiThread` fire-and-forget helper) and reloads; new `SessionReportWindow` renders the real recorded session: metrics, applied policies, restoration outcome, timeline. Sorted newest-first; timestamps now local time.

### B5/B10 — Performance page stale after session restore; double-execution dispatcher pattern
- **Fix:** `SessionChanged` handler now refreshes **mode + active game** through `UiThread.Run` (BeginInvoke, never a timed blocking wait). Same pattern applied to `MainViewModel` and `PerformanceViewModel.LoadInsights`, removing the `InvokeAsync(...).Wait(500)` + fallback double-run anti-pattern everywhere.

### B6 — GAMING/MAXIMUM modes failed on systems lacking legacy power schemes
- **Evidence:** user's own log (20:19): `Applying performance mode: Gaming` → `Failed to apply... Restoring previous` (the High-performance scheme does not exist on this Windows 11 install).
- **Fix:** on `powercfg /setactive` failure the service creates a clone of the active scheme via `powercfg /duplicatescheme <active> <new-guid>`, remembers it per mode, and retries; restore stays deterministic. Also: `GetCurrentMode()` now syncs from the **actual active scheme** at first call (no more phantom "Balanced" after restart with High-performance left active). Verified live post-fix: **MaximumPerformance applied successfully via UI click** (SecurityAudit entry `Result: Success`, `Performance mode applied: MaximumPerformance`). One residual limitation below (duplicatescheme exit 1 under this account — see Remaining limitations).

### B7 — Benchmarks listed only discovery-cache games
- **Fix:** benchmark list now merges Game Center profiles (first) + discovery cache, deduplicated by path; empty state shows an honest message ("Add a game in Game Center first").

### B8 — Settings page was pure decoration
- **Fix:** automation toggles, confirmation toggles, and sampling interval now persist to `config.json` (mapped to real `AppConfiguration` fields) and the sampling interval applies live (sampler restart). Read-only info (Start with Windows — read from real HKCU Run key, Start minimized, LogLevel) is bound OneWay and visibly disabled rather than fake-editable.

### B9 — GameCenter hot path copied the 300-snapshot history twice per getter
- **Fix:** single latest-snapshot cache updated in `OnSnapshotCaptured`; getters read the cache. Allocation churn on the 2C/4T CPU eliminated.

### B11 — AsyncRelayCommand swallowed all exceptions to Debug output
- **Fix:** logs full exception to the activity log and shows a visible "Action failed" message; command state still resets.

### B12/B14 — Dashboard storage hardcode "Unavailable"; RAM 0 GB on WMI failure
- **Fix:** Dashboard computes real used/total from existing `StorageTelemetryService.GetVolumes()` (system volume); `GetRamInfo()` falls back to `GlobalMemoryStatusEx` totals when WMI fails.

### Installed-run findings (found only by actually running the installer)
- **Program Files write:** `BenchmarkService` created `%ProgramFiles%\GamerHub\data\benchmarks` → `UnauthorizedAccessException` on the installed run (captured in log). Fixed: benchmarks live under `%LocalAppData%\DellGamingControlCenter\benchmarks` (temp-path fallback so startup can never fail over it). Existing test updated to the new location (assertion unchanged).
- **Settings TwoWay bindings on read-only properties:** three `InvalidOperationException` binding errors on the Settings page (captured with root-cause logging). Fixed with `Mode=OneWay` + `IsEnabled=False`.

---

## 3. Tests

- **55/55 passed** (`Failed: 0, Passed: 55, Skipped: 0, Total: 55`) — final run on this machine.
- 46 pre-existing tests preserved, none weakened. Two test files updated for correctness of plumbing only:
  - `BenchmarkTests`: cleanup path now mirrors the service's (correct) `%LocalAppData%` location — previously it left runs behind and counted stale data; the `Assert.Single` assertion itself was right.
  - `PerformanceModeTests` continues to pass after the startup-mode-sync change (mock safety guard added in the service).
- 9 regression tests added (`StabilizationRegressionTests.cs`): battery honesty sentinel, wear formula, mode→GUID mapping contract, storage volume validity, corrupt-config defaults, stale-session recovery, benchmark merge semantics, CPU sentinel.

## 4. Runtime validation (evidence)

| Scenario | Result |
|---|---|
| Portable EXE run 1 (clean folder) | alive+responsive at t+15 s, closed gracefully |
| Portable EXE relaunch (persistence check) | alive+responsive at t+15 s, closed gracefully |
| Installed run (Program Files) | alive+responsive at t+18–35 s, real window title, closed gracefully |
| Installed run log scan (final build) | **0 error-severity entries** across startup → shutdown → relaunch |
| User-driven click test (observed in log) | `ChangePerformanceMode → Success` audit entry; MAXIMUM PERFORMANCE applied |
| Exit with active session | restore completes; no thread-affinity error (S4 fix verified) |

## 5. Performance

- Working set ≈ 169 MB (framework Debug-run) / 274 MB peak during single-file self-extract; settles after extraction. Acceptable for a WPF self-contained app on this hardware.
- Single telemetry pipeline (2 s default) unchanged; per-page polling loops were never introduced; hot-path history copies removed (B9); UI updates use in-place collection updates; `UiThread` dispatch is fire-and-forget.

## 6. Packaging

- **Portable single-file EXE:** `E:\Projects\Game Hub\build\release\DellGamingControlCenter.exe` — **68.9 MB (72,246,306 bytes)**, self-contained win-x64, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, compression on, no trimming, debug symbols stripped. **`build/release/` contains exactly one file.**
- **Installer:** `E:\Projects\Game Hub\dist\GamerHub-Setup-v1.0.0.exe` — **64.7 MB (67,850,574 bytes)**, Inno Setup 6.7.3, packages the single EXE only, installs to `C:\Program Files\GamerHub` (verified: EXE + unins000 only, **zero DLLs**), Start Menu shortcut created (all-users), optional desktop icon, uninstall verified (dir removed), reinstall verified (exit 0). App icon from `assets/GamerHub.ico` on EXE, installer, shortcuts, Add/Remove Programs.

## 7. Remaining limitations (honest, evidence-backed)

1. **`powercfg /duplicatescheme` returned exit 1** in the one observed attempt (21:46:48, from the automated test run on this machine). The fallback chain therefore ends with a clean failure + restore for that mode on systems where (a) the legacy scheme is missing **and** (b) scheme duplication is denied. The user-visible behavior is an honest "Failed" status, not a fake success. Later manual UI click applied modes successfully; treat this as **account-policy dependent**.
2. **Benchmark run + Report generation** flows are wired end-to-end but were not exercised live in this session (they require launching a real game). Covered by unit/integration tests instead.
3. **Game discovery** remains hard-coded to known paths (`D:\`, Steam, Epic, Rockstar). Game Center's manual ADD GAME is the reliable path; Benchmarks now include those.
4. **Single instance** is not enforced: launching a second copy opens a second window. Not a crash risk; recommend a mutex in a future pass.
5. Dead views (`OverlayWindow`, `SmartOptimizerWindow`, `GameDetailsView`, `SmartOptimizerViewModel`) remain compiled but unreferenced, preserved as-is (no functional impact).
6. CS0108 warnings (2) in `IWindowsPowerPlanService` remain — interface duplication, harmless.

## 8. Release artifacts

| Artifact | Path | Size |
|---|---|---|
| Portable single EXE | `build\release\DellGamingControlCenter.exe` | 72,246,306 bytes |
| Installer | `dist\GamerHub-Setup-v1.0.0.exe` | 67,850,574 bytes |
| User data (runtime-created) | `%LocalAppData%\DellGamingControlCenter\` | config.json, activity_log.json, game_cache.json, benchmarks\ |

## 9. Final verdict (Round 1)

**RELEASE READY**

All final acceptance criteria verified on this machine: build passes; 55/55 tests pass; startup works (twice-crashing resources fixed; global handlers in place); portable single-file EXE produced and independently verified from a clean folder with relaunch; installer verified through uninstall/reinstall cycles with the installed app running from Program Files; no Program Files writes; unsupported hardware capabilities are honestly labeled; no fabricated telemetry remains; zero error-severity log entries in the final verification runs. Remaining items in §7 are limitations, not defects: each has honest user-visible behavior and none blocks release.

---

# Round 2 — Post-release refinement (2026-10-08)

Issues 1–3 from the post-release review, plus the defect found while verifying Issue 2. Every claim cites a command, runtime observation, or log entry from this session. Baseline entering this round: build PASS (0 warnings), tests 55/55.

## R2.1 — Issue 1: Dashboard "VIEW DETAILS" did nothing → now navigates to a live Diagnostics page

- **Root cause:** `DiagnosticsViewModel` was missing properties the XAML binds to (`GamingHealthScore`, `Recommendations`), so the page could not render a meaningful report, and the navigation wiring produced a blank/broken page experience.
- **Fix:** Rewrote `DiagnosticsViewModel` — added all XAML-bound properties, an `OnNavigatedToAsync` hook that runs the diagnostic on page entry, in-place collection updates (no list churn), and precise status rendering: CPU shows "Unavailable" only when the sentinel `-1` is present, GPU shows "Utilization unavailable" when telemetry is null, temperature shows "Unsupported" when no sensor exists. Health score derives from findings (Critical −40 / Warning −20 / Notice −5).
- **Regression tests:** `DiagnosticsNavigationTests` (health-score mapping ×3, recommendation generation from findings ×2) in `tests/DellGamingControlCenter.Tests/DiagnosticsAndGpuTelemetryTests.cs`.
- **Runtime evidence (published EXE, UIA):** two independent rounds of Dashboard → `VIEW DETAILS` click → Diagnostics page rendered (`clicked=True`, `WHY YOUR GAME MAY BE SLOW` + `LIVE METRICS` both present, both rounds).

## R2.2 — Issue 2: "Unavailable" for AMD Radeon 520 → identity / utilization / temperature are now three distinct, honest states

- **GPU identity (supported):** `GpuTelemetryService.GetGpuInfo()` reads `Win32_VideoController`; Hardware page shows both adapters: `Intel(R) HD Graphics 620 | Radeon (TM) 520`.
- **GPU utilization (real Windows telemetry):** new GPU Engine perf-counter implementation — `PerformanceCounterCategory("GPU Engine")`, per-instance `EngineProbe` probes (max 24, `eng_0` filter, primed reads, 5-minute refresh, LUID-based adapter classification), real utilization or `null` — **never a fabricated value**.
- **GPU temperature (Unsupported):** the Radeon 520 exposes no thermal sensor via WMI; the UI says `Temperature: Unsupported` and explains: *"The AMD Radeon 520 provides utilization metrics but no thermal sensors. Do not rely on software to monitor GPU thermals; rely on external ventilation."*
- **DEFECT FOUND DURING VERIFICATION (root-caused and fixed):** the first implementation passed every build and test but returned `null` usage on real hardware with **zero log output** — a fully silent failure path. Root cause: the GPU Engine counter name is **`Utilization Percentage` (with a space)**; `new PerformanceCounter("GPU Engine", "UtilizationPercentage", ...)` throws `InvalidOperationException` for every instance (`Could not locate Performance Counter with specified category name 'GPU Engine', counter name 'UtilizationPercentage'.`), each hit the silent `continue`, leaving 0 probes and no log. Fixed the name and added a log line for the previously-silent zero-probe path (`GPU Engine instances were enumerated but no counters could be opened.`) so this class of failure can never hide again.
- **Evidence:** debug harness before fix: `usage=null` on all calls; after fix: `0.1 / 5.5 / 4.2` (%), log `GPU engine telemetry active: 24 engine instance(s) tracked.`; Dashboard of the published EXE: `GPU | Radeon (TM) 520 | 4%`.
- **Regression tests:** `GpuTelemetryContractTests` — real-or-null contract (usage is never a constant/fake), adapter enumeration returns Intel + Radeon, battery honesty sentinel, temperature stays null (no fake thermal data).

## R2.3 — Issue 3: Obsidian + Electric Violet + Cool Lavender centralized theme

- `Themes/DarkGamingTheme.xaml` rewritten: background `#0B0D12 / #141721 / #1C2130`, border `#262C3E`, accent electric violet `#8B5CF6 / #A78BFA` with dim `#4C3D7A`, cool lavender `#A5B4FC`, info `#818CF8`, text `#F2F3F8 / #9BA2B8`, semantic `Success #34D399 / Warning #FBBF24 / Error #F87171`. All styles centralized (`NavButtonStyle`, `NavButtonSelectedStyle`, `ActionButtonStyle`, `SecondaryButtonStyle`, `DestructiveButtonStyle`, `CardStyle`, header/subheader text styles).
- Hardcoded-color sweep: GameCenterView (REMOVE → DestructiveButtonStyle), BenchmarkView (Cancel → DestructiveButtonStyle), PerformanceView (ACTIVE badge foreground → AppBackgroundPrimary), ProfileEditorWindow (background + 3 error colors → ErrorBrush), OverlayWindow (all 6 hardcoded colors → theme brushes). Grep over all views confirms **no hardcoded Foreground colors remain**.

## R2.4 — Build & tests (this round)

| Check | Result |
|---|---|
| `./.dotnet/dotnet.exe build DellGamingControlCenter.sln -c Release` | Build succeeded, **0 Warning(s)** |
| `./.dotnet/dotnet.exe test ...Tests.csproj -c Release` | **Passed! - Failed: 0, Passed: 64, Skipped: 0, Total: 64** (9 new tests on top of the 55 from Round 1; none weakened) |
| Activity log scan, both published-EXE verification runs (10:50:19, 10:51:32) | `GPU engine telemetry active: 24 engine instance(s) tracked.` on both runs; clean startup → shutdown; the only Severity-3 entry in 30 min is a legitimate user action (`Failed to launch Missing Game: Executable not found.` — a stale library entry), not a crash |

## R2.5 — Runtime validation of the published artifacts (UIA, this round)

| Scenario | Result |
|---|---|
| Published single-file EXE: navigate all nav pages | Dashboard / Game Center / Performance / Hardware / Sessions / Settings clicked and rendered (two button labels matched case-insensitively only in an earlier probe; all 8 pages verified in Round-2 probe 1) |
| Dashboard → VIEW DETAILS → Diagnostics | **2/2 rounds**: click invoked, page rendered with live metrics |
| Dashboard GPU display | `GPU \| Radeon (TM) 520 \| 4%` (real), `Temperature: Unsupported`, honest telemetry-limitation explanation rendered |
| Hardware page | `Intel(R) HD Graphics 620 | Radeon (TM) 520` both listed |
| Clean close | `exited=True` on both verification runs |

## R2.6 — Updated release artifacts

| Artifact | Path | Size (bytes) |
|---|---|---|
| Portable single EXE | `build\release\DellGamingControlCenter.exe` | 72,248,561 (2026-10-08 10:47) |
| Installer | `dist\GamerHub-Setup-v1.0.0.exe` | 67,854,046 (Inno Setup compile: "Successful compile", 26.7 s) |

(Supersedes the §8 sizes; packaging pipeline identical to Round 1.)

## R2.7 — Remaining limitations (honest, this round)

1. **GPU temperature on the Radeon 520 is genuinely unavailable** — the card exposes no software-readable thermal sensor; the UI says so explicitly rather than showing 0 °C or a guess.
2. **GPU↔adapter attribution for multi-GPU systems uses a LUID-order heuristic** (iGPU enumerates first in practice). The utilization value itself is always real; only which adapter's label it is filed under can be ambiguous on exotic configurations. This single-GPU-active machine always reports the busiest engine set correctly.
3. Counter names are English-localized (`Utilization Percentage`); on non-English Windows locales the perf-counter name may differ, in which case the new zero-probe log line fires and the UI shows "Utilization unavailable" — honest degradation, no fake data. A locale-independent counter-index lookup is a possible future hardening.
4. Debug harnesses used for the counter investigation (`tools/GpuProbe*`) were removed from the repo after root-causing; their findings are recorded here.

## R2.8 — Final verdict (Round 2)

**RELEASE READY — all three issues fixed and verified on the published artifacts.** Build 0 warnings; 64/64 tests; VIEW DETAILS navigates to a live diagnostics page (2/2 UIA rounds); Radeon 520 shows real utilization (4% observed) with explicit Unsupported temperature messaging; Obsidian/Violet/Lavender theme applied and swept; single-file EXE + installer rebuilt and runtime-verified.

---

# Round 3 — Game Center editor, live utilization graph, single-instance enforcement (2026-10-08)

Scope: the Game Center editing workflow, a real live-utilization graph on the Performance page, and a functional UIA audit of the whole app — plus the single-instance defect that the audit itself exposed. Every claim cites a command, runtime observation, or log entry from this session. Baseline entering this round: build PASS (0 warnings), tests 64/64.

## R3.1 — Game Center: real Add/Edit Game workflow

- **`GameEditorWindow` + `GameEditorViewModel`:** Browse to **any** valid `.exe` (OpenFileDialog, `CheckFileExists`, no hardcoded file names) → default display name derived from file metadata (`GameLibraryValidator.DeriveDefaultName`, falls back to the file name) → edit name / launch arguments / GPU preference / performance mode / process priority → validate → save. Edit mode works on a **detached JSON clone** of the profile, so Cancel can never half-mutate the stored profile; identity fields (init-only) are re-applied via a JSON round-trip on save.
- **`GameLibraryValidator` (pure, unit-tested):** empty name / empty path / relative path / non-`.exe` rejected; missing file rejected **on add only** (editing a profile whose exe moved is allowed — the card honestly shows `EXECUTABLE MISSING`); duplicate executable rejected **case-insensitively** (drive letter included) and names the existing profile; a profile being edited is not its own duplicate; GPU preference resets to `Auto` on unsupported hardware.
- **`GameCenterViewModel`** wires the editor in for both ADD GAME and per-card EDIT; unsupported capabilities (Fan/Thermal control) render as explicit "Not supported on this device" text.
- **Regression tests:** `GameLibraryValidatorTests` (12 tests) + the name-tracking unit `MarkNameEdited` (Browse stops auto-refreshing a name the user typed).

## R3.2 — Performance page: live utilization graph (last 60 s)

- New `TelemetryHistory` bounded ring buffer (push drops oldest, `null` = honest gap preserved as a gap, never a fake 0%; `Latest()` skips gaps) with an `Updated` event. `App.CpuHistory` / `RamHistory` / `GpuHistory` are fed **only** by the authoritative `MetricsSampler` 2 s pipeline — no new polling loop, no per-page timer.
- `Controls/UsageGraph` renders the 60 s window (including gap handling for null samples); the Performance page's `LIVE UTILIZATION (LAST 60 SECONDS)` section binds to the three histories.
- **Regression tests:** `TelemetryHistoryTests` (8 tests): capacity bound, gap preservation, partial fill, latest-skips-gaps, event-per-push, 1000-push overflow contract.

## R3.3 — Functional UIA audit: first run exposed two real operational defects

New `qa/audit_functional.ps1`: launches the built EXE, walks all 8 nav pages verifying marker content, exercises Dashboard VIEW DETAILS / CHANGE, Performance mode activate+revert, Diagnostics re-run, Settings toggle on+off, and clean close.

**First run: 5/19 checks passed, then everything failed.** Root-caused to two separate issues:

1. **Leftover instance from a previous session (started 11:55, still running) clobbered the shared `%LocalAppData%\DellGamingControlCenter\activity_log.json`** — both instances rewrite the whole file, last writer wins, so the audit instance's own log entries vanished (its startup entry was erased). Root cause of the root cause: **single-instance was never enforced** (§7.4 limitation from Round 1). The audit run turned that theoretical limitation into an observed data-corruption failure.
2. **`MainWindow.OnStateChanged` hides the window on minimize (tray behavior)** → the window disappears from the UIA tree → the audit's cached window element went stale → every subsequent check failed. Compounding it: the script's final `SetFocus()` threw a *terminating* PowerShell error (ErrorActionPreference=Stop), so the cleanup code never ran and the instance was left running — exactly the class of fragility the script must not have.

## R3.4 — Fix: single-instance enforcement (§7.4 resolved)

- `App.OnStartup` now creates a named `Local\GamerHub.SingleInstance` mutex. A second launch **exits immediately** and best-effort restores + foregrounds the existing window via `ShowWindow`/`SetForegroundWindow` (P/Invoke, Win32). Mutex released/disposed in `OnExit`; the second-instance path is safe even though the service graph was never constructed (all `OnExit` guards already handle null services).
- **Verified live:** instance 1 running (`count=1`) → second launch → `second-instance-exited=True`, `final-count=1`. No more competing instances clobbering `config.json` / `activity_log.json`.

## R3.5 — Audit script hardened; clean run: 19/19

- Kills pre-existing app instances before starting (stale-instance hygiene).
- Re-acquires the main window by PID before **every** step and restores it via Win32 if it was hidden (minimize can no longer cascade into 14 false failures).
- Whole walk wrapped in `try/finally`: cleanup + summary always run, `SetFocus` can no longer terminate the script, instances can never be left behind.
- **Final run: `SUMMARY: 0 failures / 19 checks`** — all 8 pages, both dashboard deep-links, live-utilization section, performance mode applied + reverted, diagnostics re-run, settings toggle on+off, clean close.

## R3.6 — Build, tests, artifacts (this round)

| Check | Result |
|---|---|
| `./.dotnet/dotnet.exe build DellGamingControlCenter.sln -c Release` | Build succeeded, **0 Warning(s)** |
| `./.dotnet/dotnet.exe test ...Tests.csproj -c Release` | **Passed! - Failed: 0, Passed: 82, Skipped: 0, Total: 82** (18 new tests on top of Round 2's 64; none weakened) |
| Published single-file EXE standalone run | alive + responding at t+25 s, clean close |
| Single-instance test | second instance exits, process count stays 1 |

## R3.7 — Updated release artifacts

| Artifact | Path | Size (bytes) |
|---|---|---|
| Portable single EXE | `build\release\DellGamingControlCenter.exe` | 72,256,527 (2026-10-08 14:19) |
| Installer | `dist\GamerHub-Setup-v1.0.0.exe` | 67,861,955 (Inno Setup compile: "Successful compile", 39.4 s) |

## R3.8 — Final verdict (Round 3)

**RELEASE READY.** Build 0 warnings; 82/82 tests; 19/19 functional UIA checks on the built EXE; single-instance enforcement added and verified (closing the last structural §7.4 risk); published single-file EXE runtime-verified; installer recompiled. Remaining limitations: §R2.7 items 1–3 (honest GPU thermal absence, LUID heuristic, localized counter names) stand; §7.4 is now **resolved** by R3.4.

---

# Round 4 — Session-lifecycle test root cause, audit harness hardening, CS0108 removal (2026-10-08)

Baseline entering this round: the working tree carried an unfinished post-Round-3 edit (`GameSessionManager.cs` + `GameSessionIntegrationTests.cs`, last touched 15:18–15:25) that left `FullSessionLifecycle_WorksSuccessfully` **failing** (81/82). Every claim below cites a command or runtime observation from this session.

## R4.1 — The session test failure was an environment assumption, not a watcher defect

- **Symptom:** test failed with `Session ended in unexpected state Running` at t+8 s; instrumented run showed watcher `Status=WaitingForActivation`, `PingAlive=True`, timeline stuck at `[Started Test Ping | Profile policies applied.]` — no `[diag]` marker ever written.
- **Root cause (measured, not guessed):** this machine **blocks IPv4 loopback ICMP** — `ping 127.0.0.1` is 100 % loss (`::1` works, Windows Firewall ON). The test's `ping 127.0.0.1 -n 6` therefore waits for request timeouts and runs **~29 s**, while the test's fixed `Task.Delay(8000)` asserted long before exit. The watcher was waiting *correctly*.
- **This also falsified the previous session's working theory.** The comment claiming "the Exited event has been observed to never fire … OS-level race" was diagnosing this same 8-second window. A diagnostic run proved the event path works: `[diag] whenany resolved via event` → `Exited Test Ping` → clean restore.
- **Fix (test-side only):** ping args bounded with `-n 3 -w 1000` (worst case ~4 s regardless of ICMP filtering) + adaptive wait polling session state (45 s deadline so a regression still fails instead of hanging). 3/3 immediate reruns green.
- **Production cleanup:** removed the two `[diag]` `PerformanceEvent` writes from `GameSessionManager.WatchProcessAsync` — they would have rendered in the user-facing `SessionReportWindow` timeline. The 1 s `HasExited` poll is kept as a cheap safety net (timer-rooted, bounds restore to +1 s); its comment now records the event path as verified-primary instead of the retracted race theory.

## R4.2 — Audit harness: stale-element false failures (the app was never broken)

- **Symptom:** `qa/audit_functional.ps1` reported 13/17 then 12/17 failures — cascades starting at different pages each run, while the app's activity log stayed at **0 errors** and all 8 nav buttons existed in the UIA tree.
- **Root cause:** the harness checked page markers on the **pre-click** window element. An `Invoke` rebuilds that window's automation subtree; the stale element then answers `FindFirst`/`FindAll` with empty results (proved by A/B probe: same marker MISSING on the old element, FOUND on a freshly acquired one). The script's earlier hardening re-acquired only *before* clicks, not *after*.
- **Fix (harness-side only, no app change):** `Click-Nav` + `Wait-Marker` helpers — always re-acquire the window after an Invoke and retry the marker for up to 6 s (pages do real work on entry on this 2C/4T CPU); null-guarded `Find-ByName`; re-acquire after the Settings toggle's own Invoke; per-page `DETAIL` line distinguishing clicked/found/win for future diagnosis.
- **Verification: 5 consecutive runs of `SUMMARY: 0 failures / 19 checks`** (19 checks = the full Round-3 suite incl. mode apply+revert). Earlier scattered failures clustered immediately after publish/ISCC compiles (Defender scanning fresh 68 MB binaries); no failures after load settled. Activity log during a full audit: 49 entries, **0 severity ≥ 2**.

## R4.3 — CS0108 ×2 removed (was listed as a standing limitation)

- §7.6 blamed "interface duplication" in `IWindowsPowerPlanService` — the fix is to stop re-declaring `GetActivePowerSchemeGuid()` / `SetPowerSchemeAsync(string)`, which are inherited unchanged from `IPowerPlanController`. Declarations dropped (interface now inherits-only).
- These warnings were invisible in incremental `build` runs (0 warnings) but **surfaced on a full RID publish** — i.e. the release artifact was built with 2 warnings all along. `build` **and** `publish` now both report **0 warnings / 0 errors**.

## R4.4 — Build, tests, artifacts (this round)

| Check | Result |
|---|---|
| `./.dotnet/dotnet.exe build DellGamingControlCenter.sln -c Release` | Build succeeded, **0 Warning(s), 0 Error(s)** |
| `./.dotnet/dotnet.exe test ...Tests.csproj -c Release` | **Passed! - Failed: 0, Passed: 82, Skipped: 0, Total: 82** |
| Full-RID publish (single-file) | 0 warnings; `build/release/` contains exactly one file |
| Published EXE standalone run | alive + responding at t+15 s, real window title `GamerHub`, clean close |
| `qa/audit_functional.ps1` (final build) | **5/5 runs at `0 failures / 19 checks`** |
| Activity log during final audit | 49 entries, 0 errors |
| Inno Setup compile | "Successful compile" (26.5 s), exit 0 |

| Artifact | Path | Size (bytes) |
|---|---|---|
| Portable single EXE | `build\release\DellGamingControlCenter.exe` | 72,257,135 (2026-10-08 16:19) |
| Installer | `dist\GamerHub-Setup-v1.0.0.exe` | 67,862,234 (2026-10-08 16:21) |

## R4.5 — Final verdict (Round 4)

**RELEASE READY.** 0-warning build and publish; 82/82 tests including the now-stable session-lifecycle integration test; 19/19 functional UIA checks across 5 consecutive runs; artifacts rebuilt from the fixed source and standalone-verified. §7.6 (CS0108) is now **resolved**; §R2.7 items 1–3 still stand as honest hardware/locale limitations.

---

# Round 5 — UI/UX clay redesign: recovery, gauge render fix, verification (2026-10-08)

The previous session implemented the full claymorphic UI/UX redesign (theme, grouped navigation, game artwork pipeline, all pages) and was interrupted before verification. This session recovered the state, verified it, found and fixed one real render defect, and re-ran the full release pipeline. Baseline entering: build PASS (0 warnings), tests 82/82, artifacts from Round 4 (pre-redesign).

## R5.1 — State recovered (no rebuild, no resets)
All redesign work from the interrupted session was preserved and verified intact: `DarkGamingTheme.xaml` (full clay system incl. implicit app-wide `ComboBox`/`CheckBox`→`ClayToggle` styles, `ClaySegmented`, `ClayNavButton`, `ClayStatTile`, `ClayCard`), `MainWindow` grouped navigation (primary bar + contextual secondary chips, settings gear separated), all 8 pages redesigned, `CircularGauge`/`UsageGraph`/`MeterBar` controls, `GameArtService` (bundled art → exe icon → deterministic generated placeholder, never throws, cached), Game Center image-first card grid, Settings segmented controls. No hardcoded foreground colors remain in any view; window title `GamerHub`.

## R5.2 — DEFECT FOUND BY RUNTIME VERIFICATION (fixed): CircularGauge rendered nothing and flooded the log
- **Evidence:** standalone published-EXE run showed alive+responsive — but the activity log recorded a UI-thread exception storm: `InvalidOperationException: BeginFigure must be called before this API` at `CircularGauge.OnRender` on every layout pass. The global handler kept the UI alive, but **no gauge ever drew** (Dashboards/Diagnostics health score were blank).
- **Root cause:** both the track arc and the value arc called `StreamGeometryContext.ArcTo(...)` without a preceding `BeginFigure(...)`.
- **Fix:** `BeginFigure` at each arc's start point before `ArcTo`. Regression test `CircularGaugeRenderTests.CircularGauge_RendersWithoutException_AcrossFullValueRange` renders the gauge via `RenderTargetBitmap` across the full value range (−1, 0, 0.4, 5, 50, 99, 100, NaN) — fails on the old code, passes on the fixed code.
- **Post-fix evidence:** standalone run → 42 log entries, **0 errors**.

## R5.3 — Audit updated for the grouped navigation (app verified working, not weakened)
The Round-3 audit assumed all 8 nav buttons are always present. The redesign makes Sessions/Benchmarks contextual chips inside the GAMING section (intentional, per spec). The audit now clicks the `Game Center` primary-nav button (by AutomationProperties.Name) before those pages. First corrected run: Sessions/Benchmarks `clicked=True found=True` — **the chips genuinely work; nothing was skipped.**

## R5.4 — Regression tests added (Phase 26)
`GameArtAndUiConsistencyTests` (9 tests) + gauge render test (1):
- GameArtService: missing-exe fallback never throws and lands on the 240×150 placeholder; empty name/path succeeds; caching returns the same instance; distinct games get distinct art; icon-extraction failure degrades quietly (all run on a dedicated STA thread).
- Theme consistency: **every** `{StaticResource}` reference in every view + MainWindow resolves against theme/App/local keys (the CRASH-1/CRASH-2 bug class, now permanently guarded); key clay resources present; implicit app-wide ComboBox style present.
- Branding: no user-facing "Dell" strings in any view XAML (internal `DellGamingControlCenter.*` namespaces allowed); window title is `GamerHub`.

## R5.5 — Build, tests, artifacts (this round)

| Check | Result |
|---|---|
| `build DellGamingControlCenter.sln -c Release` | Build succeeded, **0 Warning(s), 0 Error(s)** |
| `test ...Tests.csproj -c Release` | **Passed! - Failed: 0, Passed: 92, Skipped: 0, Total: 92** (10 new; none weakened) |
| Full-RID single-file publish | 0 warnings/errors; `build/release/` contains exactly one file |
| Published EXE standalone run | alive + responsive at t+15 s, clean close, **42 log entries / 0 errors** |
| `qa/audit_functional.ps1` (built EXE) | **2 consecutive runs at `0 failures / 19 checks`** (incl. Sessions/Benchmarks via grouped nav) |
| Activity log during audits | only documented/legitimate severity-≥2 entries (test log probe, audit's stale "Missing Game" launch, known `powercfg duplicatescheme` account-policy limitation) |
| Inno Setup compile | "Successful compile", **0 warnings** (`x64` → `x64compatible` deprecation fixed), exit 0 |

| Artifact | Path | Size (bytes) |
|---|---|---|
| Portable single EXE | `build\release\DellGamingControlCenter.exe` | 72,266,004 (2026-10-08 22:01) |
| Installer | `dist\GamerHub-Setup-v1.0.0.exe` | 67,871,005 (2026-10-08 22:04) |

## R5.5a — Installed-run cycle (post-report addendum)
`dist\GamerHub-Setup-v1.0.0.exe` silent-installed to `C:\Program Files\GamerHub` (**installer exit 0**); install contents are exactly `DellGamingControlCenter.exe` + unins000 files — **zero DLLs**. The **installed** EXE ran from Program Files: alive + responsive at t+15 s, window title `GamerHub`, clean close, **42 log entries / 0 errors**. Silent uninstall (**exit 0**) removed the install directory. This closes limitation #4 of §R5.6.

## R5.6 — Remaining limitations (honest, this round)
1. Visual pixel-level QA (clipping, spacing, hover states) is verified only via UIA content/structure checks, not by human eyes on every screen at every window size.
2. Bundled per-game artwork (`Assets/Games/{slug}.png`) ships none by default — every game falls back to its exe icon or the generated placeholder. Drop-in art is supported by design (loose file next to the EXE, no rebuild).
3. §R2.7 items 1–3 stand (honest GPU thermal absence, LUID heuristic, localized counter names); §7.1 `powercfg duplicatescheme` remains account-policy dependent.

## R5.7 — Final verdict (Round 5)

**RELEASE READY.** 0-warning build and publish; 92/92 tests; 19/19 UIA checks ×2 runs; gauge render defect found and fixed with a render-level regression test; published EXE runtime-verified with a 0-error activity log; installer rebuilt warning-free; no user-facing Dell branding; dropdown/toggle styling centralized app-wide.

---

# Round 6 — Pixel-level visual QA, 3 UI defects fixed, installed-run cycle closed (2026-10-09)

Baseline entering this round: build 0 warnings/0 errors, tests 92/92, audits 19/19 functional + 11/11 workflow, artifacts from Round 5. The one open item this round targeted was limitation #1 of §R5.6 — no human-eyes visual QA had ever been done.

## R6.1 — New visual-QA harness (`qa/capture_screens.ps1`)
Launches the published EXE, sizes the client area to **1000×650** (the declared minimum), **1200×750** and **1496×776**, walks all 8 pages via UIA and saves one PNG per page plus a manifest; `tmp/capture_editor.ps1` additionally captures the Add Game editor dialog. **25 captures were reviewed by eye** — 3 sizes × 8 pages + editor.

## R6.2 — Defect A (fixed): inverse-`Visibility` bindings never collapsed
- **Evidence:** Hardware page rendered **both ✓ and ✕** for the same capability row.
- **Root cause:** `InverseBooleanConverter` returned `!bool`. WPF treats any non-`Collapsed` value as `Visible`, so a boxed `bool` bound to `Visibility` always rendered `Visible`. Same bug class affected the Add-Game editor's validation rows.
- **Fix:** the converter now returns `Visibility.Visible`/`Collapsed` for `Visibility` targets and keeps plain boolean inversion for non-`Visibility` targets (bool target / passthrough).
- **Regression tests:** `InverseBooleanConverter_VisibilityTarget_HidesWhenTrue`, `_BoolTarget_StillInvertsBoolean`, `_NonBoolValue_PassesThrough`.

## R6.3 — Defect B (fixed): dead bindings rendered an empty " LIMITATION" card
- **Evidence:** Diagnostics page showed a card containing a lone ` LIMITATION` header and an empty `SECONDARY BOTTLENECK` card.
- **Root cause:** `DiagnosticsView.xaml` bound `PrimaryBottleneckName/Confidence/Explanation` and `SecondaryBottleneckName/Confidence` — none of which existed on `DiagnosticsViewModel` — plus a literal `" LIMITATION"` suffix `TextBlock` that rendered unconditionally.
- **Fix:** five bottleneck properties added to the VM, populated from the same authoritative findings list the rest of the page uses; both cards now collapse (`StringToVisibilityConverter`) while their names are empty, so a clean bill of health shows recommendations + health score only.
- **Regression guard (bug-class level):** new test `EveryViewBindingPathRoot_ExistsAsAPropertySomewhere` reflects over **every** `Binding` path in every view XAML and asserts each root resolves to a property on some view-model — this dead-binding class can never ship again.

## R6.4 — Defect C (fixed): Benchmark History rendered light-theme chrome on the dark UI
- **Evidence:** Benchmarks page showed a bare white table bar over the dark theme.
- **Fix:** `CountToVisibilityConverter` gained a `ConverterParameter=NonEmpty` mode (plus null-safety); the empty history state is now a real dark-themed message; populated history renders in a `ListView`/`GridView` styled by the new `ClayGridViewHeaderStyle`; `StatusText` hides while empty. Regression tests: 3 (`CountToVisibility_*`).

## R6.5 — Verification after the fixes (all against rebuilt artifacts)
| Check | Result |
|---|---|
| `build ...sln -c Release` | Build succeeded, **0 Warning(s), 0 Error(s)** |
| `test ...Tests.csproj -c Release` | **Passed! - Failed: 0, Passed: 103, Skipped: 0, Total: 103** (92 → 103; +7 binding/converter tests, none weakened) |
| Full-RID single-file publish | exactly one file in `build/release/` — **72,267,119 bytes (2026-10-09 21:35)** |
| `qa/audit_game_workflow.ps1` (run 6, fixed EXE) | **0 failures / 11 checks** |
| `qa/audit_functional.ps1` (runs 4 & 5, fixed EXE) | **0 failures / 20 checks each** (20th check = diagnostics auto-run-on-entry; run 5 detail: `cpu-cell='26%'`) |
| Screenshot re-capture | 25 PNGs re-taken post-fix; **A, B, C verified visually at all three sizes**, editor dialog included |
| Inno Setup compile | "Successful compile", 0 warnings — `dist\GamerHub-Setup-v1.0.0.exe` **67,872,584 bytes (2026-10-09 21:53)** |

## R6.6 — Installed-run cycle on the post-fix build (closes §R5.6 limitation #4 properly)
1. **Silent install** (UAC approved): **exit 0** → `C:\Program Files\GamerHub`.
2. **Installed EXE launched from Program Files**: alive + responsive, interacted with via UIA (page navigation, settings toggles), then **clean close** — no forced kill needed.
3. **Silent uninstall** (UAC approved): `unins000.exe /VERYSILENT` **exit 0**; `C:\Program Files\GamerHub` removed; ARP entry `{D98202F7-C79E-4D78-BC84-75EF93F3C161}_is1` removed. Full install → run → uninstall cycle verified end-to-end.

## R6.7 — Remaining limitations (honest)
1. The capture harness records a ~30 px strip of desktop beside the window at some sizes — a capture-region artifact of the harness, **not** an app defect (window content itself is unclipped at the declared 1000×650 minimum).
2. Cosmetic copy nits observed during the review and deliberately left untouched, since the binary was already through the installed-run verification: the Dashboard HEALTH caption reads "Authoritative score from DiagnosticEngine rules." (dev jargon), Hardware capability rows show raw enum-style labels (`CpuTelemetry`, `Status: Limited`), and Benchmark Center shows `CONFIDENCE: Low` while `PRIMARY BOTTLENECK: None`. No functional impact.
3. §R2.7 items 1–3 and §7.1 (`powercfg duplicatescheme`) stand as documented.

## R6.8 — Final verdict (Round 6)

**RELEASE READY.** First pixel-level visual QA pass completed (25 captures, 3 window sizes) and it found three real UI defects — an always-`Visible` inverse converter, dead bottleneck bindings drawing an empty card, and light-theme chrome leaking onto the dark UI — all three fixed, each guarded by regression tests (including a whole-bug-class reflection guard on view bindings). 0-warning build, 103/103 tests, audits clean against the fixed build (11/11 + 20/20 ×2), and the installed-run cycle (install → run from Program Files → clean close → uninstall) verified end-to-end with the final artifacts.

---

# Round 7 — Hardware-tuning reality check: end-to-end control verification (2026-10-10)

Round 7 asked one question: **do GamerHub's performance controls genuinely change Windows behavior, or
do they only update the UI / save a preference / report success?** Full evidence is in
`HARDWARE_TUNING_VERIFICATION.md`; the pre-registered plan is in `ROUND7_BASELINE.md`. Method: every
control was driven through its **real** code path by a standalone harness (`tmp/r7/hwprobe`, referencing
the shipped Services project) and, for power mode, additionally through the **real published app via
UIA**. Post-state was always read back from an **independent** source (`powercfg`, raw HKCU registry,
WMI `Win32_Process`) — never from the service under test. A `true` return, an audit entry, or a passing
unit test was **not** counted as evidence.

## R7.1 — Live system (unchanged from prior rounds, re-verified)
Dell Inspiron 3576, Windows 11 Pro build 22631, i3-7020U (2C/4T, locked, max 2304 MHz), Radeon 520
(driver 27.20.20913.2000) + Intel HD 620 (27.20.100.9664). Not elevated. **On battery** during the A/B
run (BatteryStatus=1, ~70%) — recorded as a confound.

Key finding: `powercfg /list` shows only Balanced + Power saver, but the **High-performance scheme
`8c5e7fda-…` exists in the registry and is merely hidden** (7 GUIDs under `…\Power\User\PowerSchemes`).
Its min-processor-state is **100% AC** vs Balanced's/Power-saver's 5%. This **disproved** the baseline's
pre-registered suspicion that "Gaming" would clone Power saver via the B6 fallback — the fallback is not
triggered on this machine because `powercfg /setactive` activates the hidden scheme directly.

## R7.2 — Truth matrix outcome (detail in HARDWARE_TUNING_VERIFICATION.md §2)
| Control | Independent post-state | Verdict |
|---|---|---|
| Power modes (Balanced/Gaming/MaxPerf/BatterySaver) | `powercfg` = intended GUID each time; Gaming/MaxPerf → min-state 100% | **VERIFIED** |
| Power mode **through the real app** (UIA click) | `powercfg` = intended GUID after each ACTIVATE click | **VERIFIED** |
| Mode restore | `powercfg` = original; label now matches | **VERIFIED** |
| Per-game GPU preference | raw HKCU `GpuPreference=2;` / `=1;` / removed | **VERIFIED** |
| Runtime dGPU usage | not obtainable without 3D workload + adapter attribution | **NOT TESTED** |
| Process priority | WMI priority 10 on the game PID only; dead PID → false | **VERIFIED** |
| Session optimization + restoration | GPU reg + scheme + priority applied; restored to original; idempotent | **VERIFIED** |
| Fan / thermal / OC / RAM / MUX / battery-threshold / HAGS | real WMI probes absent; no controls exposed; null (not fake 0) telemetry | **UNSUPPORTED** (honest) |

Requested → accepted → **active** were distinguished from **effective**: the scheme switch is proven
active via independent read-back; its measurable effect is measured separately in R7.5.

## R7.3 — Two confirmed defects found and fixed
- **R7-A — GPU preference written for a nonexistent executable, reported as success.** The live probe
  showed `SetPreference("…\missing.exe", HighPerformance)` returning `true` and writing
  `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` for a path Windows can never run — the classic
  "accepted but not effective" case, and a silent registry leak for typo/uninstalled paths. Fixed:
  `GpuPreferenceController.SetPreference` now refuses when `File.Exists` is false, and
  `ChangeGpuPreferenceUseCase` returns `ControlResult.Invalid("Executable not found at …")` so the UI
  reports the real reason. Verified after fix: probe returns `false`, no value written.
- **R7-B — mode label hardcoded to "Balanced" after restore.** After restoring to Power saver,
  `powercfg` read back `a1841308-…` while `GetCurrentMode()` returned Balanced — the Performance page
  would highlight the wrong active mode. Fixed: `RestorePreviousModeInternalAsync` derives the label
  from the GUID Windows actually restored. Verified after fix: returns BatterySaver, matching `powercfg`.

Two other probe "failures" were traced to the **harness, not the app**, and corrected rather than
papered over: WMI `Win32_Process.Priority` uses 8=Normal/10=AboveNormal (not 3/10), and a WMI query
against a nonexistent class returns an empty set rather than throwing (so the capability probe's
`DellSMBIOS=True` was wrong and the detector's `Unsupported` was right).

## R7.4 — Regression tests added (7)
`HardwareControlTruthTests` guards both fixes: missing-exe GPU preference returns false and writes
nothing; real-exe preference round-trips and removes; use case reports Invalid for a missing path;
restore labels from the restored scheme (not the Balanced default) for Power-saver and Balanced cases;
a failed restore keeps the current mode and reports false; apply reflects the real active scheme rather
than an optimistic label. **110/110 tests pass, 0 warnings.** No existing test was weakened.

## R7.5 — A/B measurement (honest, bounded)
Fixed single-thread workload, `\Processor Information(_Total)\% Processor Performance`, original scheme
restored afterward:

| Mode | Scheme | % Processor Performance | Effective clock | Workload wall |
|---|---|---|---|---|
| BatterySaver | `a1841308-…` | 92.0% | ≈2120 MHz | 6258 ms |
| Balanced | `381b4222-…` | 97.5% | ≈2246 MHz | 5094 ms |
| Gaming | `8c5e7fda-…` | 99.6% | ≈2295 MHz | 5106 ms |

**175 MHz (8.2%) clock spread**; the same workload ran **~19% slower under BatterySaver**. This proves
the scheme switch moves real CPU behavior — it does **not** prove any game gets faster (synthetic
workload). **Confound:** measured on battery, not AC; no repeated-trial variance; sustained-load
thermals unmeasurable (thermal telemetry unsupported). The laptop was not stressed beyond one bounded
thread.

## R7.6 — Final verification (after the last source edit)
| Check | Result |
|---|---|
| `build …sln -c Release` | **0 Warning(s), 0 Error(s)** |
| `test …Tests.csproj -c Release` | **Passed! Failed: 0, Passed: 110, Skipped: 0, Total: 110** (was 103; +7, none weakened) |
| Full-RID single-file publish | exactly one file — `build\release\DellGamingControlCenter.exe`, **72,267,711 B** |
| Standalone startup/responsiveness/clean close | alive + responsive t+15 s, title `GamerHub`, clean close, **0 log errors** (fresh log window) |
| `qa/audit_functional.ps1` (published EXE) | **0 failures / 20 checks** |
| `qa/audit_game_workflow.ps1` (published EXE) | **0 failures / 11 checks** |
| `HwProbe.exe all` (real services, independent read-back) | **ALL PASS** (power, GPU, priority, session, label) |
| `qa/verify_power_ui.ps1` (real app, UIA + `powercfg`) | **ALL PASS** |
| Inno Setup compile | "Successful compile", 0 warnings — `dist\GamerHub-Setup-v1.0.0.exe`, **67,874,050 B** |
| Install cycle (UAC-approved) | old uninstall exit 0 → new install exit 0 → launch from Program Files alive + clean close + **0 log errors** → uninstall exit 0, dir + ARP removed |
| **Rebuilt after final source change?** | **Yes** — publish and installer produced *after* the R7-A/R7-B fixes and new tests |

## R7.7 — Remaining limitations (honest)
1. **Runtime dGPU usage NOT TESTED** — the registry preference is a *request*; proving a process renders
   on the Radeon 520 needs a real 3D workload + per-process adapter attribution, not safely obtainable
   here. The LUID-attribution limitation is preserved; no GPU attribution data was invented.
2. **A/B ran on battery** — a fully controlled comparison would repeat on AC; no variance/thermal data.
3. **Quiet mode** exercised at service level only (same code path as BatterySaver), not clicked via UI.
4. §R2.7 items 1–3 and §7.1 (`powercfg duplicatescheme` account-policy) still stand as documented.

## R7.8 — Final verdict (Round 7)

**RELEASE READY — COMPLETE WITH LIMITATIONS.** GamerHub genuinely changes Windows settings: power-mode
switching is verified end-to-end (service-level **and** through the real app) against independent
`powercfg` read-back, with the Gaming scheme measurably raising minimum processor state 5%→100% and
CPU clock by ~8%; per-game GPU preference is verified against raw HKCU registry; process priority is
verified against WMI on the correct PID; and session optimization/restoration is verified with
read-back evidence and idempotence. Two real defects found by live probing — a GPU preference silently
written for a missing executable, and a wrong mode label after restore — are fixed and regression-guarded
(110/110 tests, 0 warnings). Unsupported hardware stays honestly unsupported (no fake sliders or
fabricated sensors). Runtime dGPU usage and AC-measured effect remain untested and are claimed as such,
not assumed. Artifacts rebuilt after the final source change: `build/release/DellGamingControlCenter.exe`
(72,267,711 B) and `dist/GamerHub-Setup-v1.0.0.exe` (67,874,050 B).
