# HARDWARE_TUNING_VERIFICATION.md — Round 7 (2026-10-10)

**Question this report answers:** do GamerHub's performance controls genuinely change Windows
behavior, or do they only update the UI / save a preference / report success?

**Method:** every control was driven through its real code path (no fakes) by a standalone harness
(`tmp/r7/hwprobe`, referencing the shipped `DellGamingControlCenter.Services` project) and, for power
mode, additionally through the **real published app via UIA**. Post-state was always read back from an
**independent** source (`powercfg`, raw HKCU registry, WMI `Win32_Process`) — never from the service
under test. All probe logs are in `tmp/r7/*.log`.

A method returning `true`, an audit entry, or a passing unit test was **not** counted as evidence.

---

## 1. Device and OS identification (live)

| Item | Live value |
|---|---|
| OS | Microsoft Windows 11 Pro, Build 22631 (10.0.22631) |
| Machine | Dell Inc. Inspiron 3576 |
| CPU | Intel Core i3-7020U @ 2.30 GHz — 2C/4T, max 2304 MHz (locked, no turbo) |
| GPU 0 | Radeon (TM) 520 (discrete), driver 27.20.20913.2000 |
| GPU 1 | Intel HD Graphics 620 (integrated), driver 27.20.100.9664 |
| Power source during A/B | **On battery** (Win32_Battery BatteryStatus=1, ~70%) — recorded as a confound |
| Active scheme at baseline | `a1841308-…` (Power saver) |
| Schemes in `powercfg /list` | **2** (Balanced, Power saver). The High-performance scheme `8c5e7fda-…` is **hidden** — present in `HKLM\…\Power\User\PowerSchemes` (7 GUIDs total) but omitted from `/list`. |

Power-scheme settings read via `powercfg /q` (AC indices):

| Scheme | PROCTHROTTLEMIN | PROCTHROTTLEMAX |
|---|---|---|
| Balanced `381b4222-…` | 5% (0x5) | 100% (0x64) |
| Power saver `a1841308-…` | 5% (0x5) | 100% (0x64) |
| High performance `8c5e7fda-…` (hidden) | **100% (0x64)** | 100% (0x64) |

---

## 2. Control-by-control truth matrix

| Control | UI action | Intended effect | Implementation | Pre-state | Action result | Post-state (independent) | Persistence | Restoration | Verdict |
|---|---|---|---|---|---|---|---|---|---|
| Power: Balanced | Performance → ACTIVATE | Active scheme = Balanced | `powercfg /setactive` + `PowerGetActiveScheme` read-back | Power saver | service `true` | `powercfg` = `381b4222-…` | survives (Windows-held) | n/a | **VERIFIED** |
| Power: Gaming | Performance → ACTIVATE | Higher-performance scheme active | same | Balanced | service `true` | `powercfg` = `8c5e7fda-…`; min-state **100%** vs Balanced's 5% | survives | n/a | **VERIFIED** |
| Power: MaximumPerformance | same | same as Gaming | same | BatterySaver | service `true` | `powercfg` = `8c5e7fda-…` (same GUID as Gaming) | survives | n/a | **VERIFIED** (see §5.1) |
| Power: BatterySaver | same | Active scheme = Power saver | same | Balanced | service `true` | `powercfg` = `a1841308-…` | survives | n/a | **VERIFIED** |
| Power: Quiet | same | Power saver | same | — | not clicked in UI run | same code path as BatterySaver | — | — | **PARTIALLY VERIFIED** (label only; see §5.2) |
| Power (through the real app) | UIA click ACTIVATE | Scheme changes in Windows | full UI→VM→use-case→service→OS | Power saver | app invoked | `powercfg` = intended GUID for Balanced and Gaming | survives | n/a | **VERIFIED** |
| Mode restore | session end / explicit | Previous scheme reactivated, label matches | `RestorePreviousModeAsync` | Power saver | service `true` | `powercfg` = original; **`GetCurrentMode()` now returns BatterySaver** | n/a | restores original | **VERIFIED** (after R7-B fix) |
| Per-game GPU preference | profile GPU field → session | HKCU `UserGpuPreferences` written | Registry write + read-back | `Auto` (no value) | service `true` | raw HKCU value = `GpuPreference=2;` (HP), `=1;` (iGPU) | survives (HKCU) | removed on restore | **VERIFIED** |
| GPU preference, missing exe | profile points at nonexistent path | Must NOT claim success | now `File.Exists`-guarded | (none) | **before fix: `true`; after fix: `false`** | **before fix: value written; after fix: no value** | n/a | n/a | **FAILED before → VERIFIED after** (R7-A) |
| Runtime dGPU usage | launch game with HP pref | Process renders on Radeon 520 | OS decides | — | — | not obtainable without a real 3D workload + per-process adapter attribution | — | — | **NOT TESTED** (§4) |
| Process priority | profile priority → session | Target PID raised | `Process.PriorityClass` | WMI priority 8 (Normal) | service `true` | WMI priority **10** (AboveNormal) on the **game PID only** | process lifetime | dies with process | **VERIFIED** |
| Priority, dead PID | game already exited | Must fail honestly | same | dead PID | service `false` | no throw, no side effect | — | — | **VERIFIED** |
| Session optimization | launch registered game | GPU pref + power mode applied | `PolicyEvaluator` pre/post-launch | Power saver, GPU `Auto` | applied=2 (pre) + 1 (post) | GPU reg written; `powercfg` = `8c5e7fda-…`; WMI priority 10 | session | — | **VERIFIED** |
| Session restoration | game exits | Original state restored | `RestoreProfileAsync` | (above) | returned `true` | `powercfg` = original; GPU reg removed | — | idempotent (2nd restore no-op) | **VERIFIED** |
| Partial application (missing exe) | profile w/ bad path | Honest partial result | `ProfileApplicationResult` | — | applied=1, **failed=1** | GPU policy reported failed, not silently applied | — | — | **VERIFIED** |
| Fan speed / curves | none exposed | — | `DellCapabilityDetector` | — | — | `DellSMBIOS` meta_class count=0; class does not exist | — | — | **UNSUPPORTED** (honest) |
| CPU/GPU temperature | read-only display | — | `ThermalTelemetryService` | — | — | `CpuTemperature=null`, `FanRpm=null` (no fake 0) | — | — | **UNSUPPORTED** (honest) |
| Overclock / undervolt | none exposed | — | — | — | — | no control exists | — | — | **UNSUPPORTED** (honest) |
| RAM optimization | none exposed as a control | — | — | — | — | no memory-writing control exists | — | — | **UNSUPPORTED** (honest) |
| MUX / GPU switching | none exposed | — | — | — | — | no MUX interface on this device | — | — | **UNSUPPORTED** (honest) |
| Battery charge thresholds | none exposed | — | `DellCapabilityDetector` | — | — | `DCIM_Battery` absent | — | — | **UNSUPPORTED** (honest) |
| HAGS / OS graphics features | none exposed | — | — | — | — | no control exists | — | — | **UNSUPPORTED** (honest) |

---

## 3. Power-mode verification (requested → accepted → active → effective)

Distinguished explicitly, per mandate:

1. **Requested** — `ChangePerformanceModeUseCase.ExecuteAsync(mode)` returned `ControlResult.Success`.
2. **Accepted by Windows** — `powercfg /setactive` exit 0.
3. **Actually active** — **independent** `powercfg /getactivescheme` returned the intended GUID. Confirmed for all four modes, and separately through the real app by UIA (`tmp/r7/power_ui7.log`).
4. **Measurable effect** — see §6. The Gaming/High-performance scheme raises minimum processor state from 5%→100% (AC), a real, different configuration; the effect on clock is measured below.

Repeated activation (3× Balanced), switching between modes, and restoration were all exercised and
deterministic. No scheme was created by the B6 fallback on this machine — the High-performance scheme is
merely *hidden*, not absent, and `powercfg /setactive` activates it directly. This **disproved** the
baseline's pre-registered suspicion that "Gaming" would clone Power saver (`tmp/r7/power_probe2.log`,
`powercfg_deep.txt`).

The UI does not say "Applied" on a rejected change: `ApplyModeAsync` returns false on failure and the
use case maps that to `ControlResult.Failed`.

---

## 4. Per-game GPU preference — and the honest runtime gap

`GpuPreferenceController` writes the genuine Windows per-application graphics preference
(`HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, value `GpuPreference=2;` for high-performance).
Verified by **raw registry read-back** independent of the controller: set → `GpuPreference=2;`,
Integrated → `=1;`, remove → value gone. The user's pre-existing game preferences (Hitman, Euro Truck
Simulator 2, GTA Vice City, Car Mechanic Simulator 2021) were untouched throughout.

**Runtime adapter selection is NOT TESTED.** The registry preference is a *request* to Windows; whether a
given process actually renders on the Radeon 520 requires a real 3D workload plus per-process
adapter/LUID attribution, which was not safely obtainable here. GamerHub's own GPU telemetry attributes
utilization by a documented LUID heuristic; that limitation is preserved and no GPU attribution data was
invented. A preference set to "High Performance" is **not** claimed as proof the discrete GPU is in use.

---

## 5. Defects found and fixed

### 5.1 R7-A — GPU preference written for a nonexistent executable, reported as success
- **Found by:** `HwProbe.exe gpu` (`tmp/r7/gpu_probe.log`): `SetPreference("…\missing.exe", HighPerformance)` returned `True` and wrote `GpuPreference=2;` to HKCU for a path Windows can never run. This is the "accepted but not effective" failure class — and it also meant a mistyped/uninstalled game path silently accumulated a dead registry value.
- **Fix:** `GpuPreferenceController.SetPreference` now refuses when `File.Exists(normalizedPath)` is false (logs a warning, returns false); `ChangeGpuPreferenceUseCase` returns `ControlResult.Invalid("Executable not found at …")` so the UI reports the real reason instead of a generic failure.
- **Verified after fix:** probe returns `false`, raw HKCU value absent (`tmp/r7/gpu_probe3.log`).
- **Regression tests:** `GpuPreference_SetOnMissingExecutable_ReturnsFalse_AndWritesNothing`, `GpuUseCase_MissingExecutable_ReportsInvalid_NotSilentSuccess`.

### 5.2 R7-B — mode label hardcoded to "Balanced" after restore
- **Found by:** `HwProbe.exe label` (`tmp/r7/label_probe.log`): after restoring to Power saver, `powercfg` read back `a1841308-…` (Power saver) while `GetCurrentMode()` returned **Balanced** — the Performance page would highlight the wrong active mode.
- **Fix:** `RestorePreviousModeInternalAsync` now derives the label from the GUID Windows actually restored (High-perf→Gaming, Power-saver→BatterySaver, Balanced→Balanced, else the per-mode clone map) instead of assigning `Balanced`.
- **Verified after fix:** probe returns BatterySaver, matching `powercfg` (`tmp/r7/label_probe2.log`).
- **Regression tests:** `RestorePreviousMode_LabelsFromRestoredScheme_NotBalancedDefault`, `RestorePreviousMode_BalancedLabel_WhenBalancedRestored`, `RestorePreviousMode_Failure_KeepsCurrentMode_AndReportsFalse`, `ApplyMode_ReflectsRealActiveScheme_NotOptimisticLabel`.

Two earlier probe "failures" were traced to the harness, not the app, and corrected rather than papered over: WMI `Win32_Process.Priority` uses **8=Normal / 10=AboveNormal** (not 3/10), and a WMI query against a nonexistent class returns an **empty set rather than throwing** — so my capability probe's `DellSMBIOS=True` was wrong and the detector's `Unsupported` was right.

---

## 6. A/B measurement — does the tuning produce a measurable effect?

Fixed single-thread CPU-bound workload (~5 s), sampled via `\Processor Information(_Total)\% Processor
Performance` (effective ÷ nominal clock), one mode at a time, original scheme restored afterward.

| Mode | Active scheme | Avg % Processor Performance | Avg effective clock | Workload wall time |
|---|---|---|---|---|
| BatterySaver | `a1841308-…` | 92.0% | ≈2120 MHz | 6258 ms |
| Balanced | `381b4222-…` | 97.5% | ≈2246 MHz | 5094 ms |
| Gaming | `8c5e7fda-…` | 99.6% | ≈2295 MHz | 5106 ms |

**Spread:** 2120 → 2295 MHz = **175 MHz (8.2%)** between lowest and highest mode; the same workload ran
**~19% slower under BatterySaver** than under Balanced/Gaming.

**What this proves:** the scheme switch moves real CPU clock behavior, and GamerHub's control reaches
Windows. **What it does not prove:** that any game gets faster. This is a synthetic single-thread
workload, not a game, and it must not be presented as game FPS evidence.

**Confound (recorded honestly):** the machine was **on battery** (`BatteryStatus=1`, ~70%) during
measurement, not AC. Battery state can compress the differences between schemes; a fully controlled
comparison would repeat this on AC. No repeated-trial variance was collected, and no thermal-throttling
data is available on this device (thermal telemetry unsupported), so sustained-load behavior under
Gaming cannot be characterized. The laptop was not stressed beyond a single bounded thread.

---

## 7. Session restoration evidence

Driven through the real `PolicyEvaluator` + `GameSessionManager` services against a self-owned benign
process (`ping -t 127.0.0.1`), never a third-party game:

- **Pre-state:** scheme `a1841308-…`, GPU pref `Auto`, priority 8.
- **Pre-launch policies applied = 2** (GPU preference + performance mode): raw HKCU = `GpuPreference=2;`, `powercfg` = `8c5e7fda-…`.
- **Post-launch policy applied = 1** (process priority): WMI priority = **10** on the game PID.
- **On exit:** `RestoreProfileAsync` returned `true`; `powercfg` read back the **original** `a1841308-…`; GPU registry value **removed**.
- **Idempotence:** a second `RestoreProfileAsync` was a no-op — original scheme intact, no clobbering of unrelated state.
- **Applied-change counting:** only policies actually applied are counted; a missing-executable profile yields applied=1 / failed=1, so a failed GPU policy is **not** counted as applied.

"Restoration Successful" is therefore backed by read-back evidence for the changes that were actually
made (power scheme and GPU preference). Process priority needs no restoration — it dies with the
process — and GamerHub never force-kills the game: the exit watcher is event-driven with a 1 s poll
safety net.

---

## 8. Unsupported capabilities (kept honest)

`DellCapabilityDetector` probes real WMI namespaces. Live results: `IsDellSystem=True`
(Inspiron 3576), `DellSMBIOS` meta_class count=**0**, `DCIM_ThermalInformation`=**absent**,
`DCIM_Battery`=**absent** → Fan Control, Thermal Management, Battery Thresholds are all
**Unsupported**. `ThermalTelemetryService.GetMetrics()` returns `CpuTemperature=null` /
`FanRpm=null` — never a fabricated 0. The UI shows "Not supported on this device" labels and exposes no
fan, thermal, overclock, undervolt, RAM-optimization, MUX, charging-threshold, or HAGS controls. CPU
usage reports real values with `-1` as the unavailable sentinel; battery reports real firmware-derived
charge. No fake sliders, no placebo controls, no undocumented ACPI/EC/firmware writes, no security
bypass, no force-kill.

---

## 9. Failed, blocked, and untested scenarios

| Scenario | Outcome |
|---|---|
| Runtime dGPU adapter attribution | **NOT TESTED** — no real 3D workload + per-process adapter evidence safely obtainable (§4) |
| Power-mode A/B on AC | **NOT TESTED** — measurement ran on battery (confound recorded, §6) |
| Sustained-load thermal behavior | **NOT TESTED** — thermal telemetry unsupported on this device |
| Quiet mode via UI click | **NOT TESTED** (same code path as BatterySaver, exercised at service level) |
| Fan/thermal/OC/RAM/MUX/battery-threshold controls | **UNSUPPORTED** — correctly not exposed; no test applicable |

No existing test was weakened, skipped, or removed. Authorization was never bypassed; all
state-changing operations stayed on the established authorize→validate→execute path. The user's
existing power scheme and GPU preferences were restored after every scenario.

---

## 10. Final verification (after the last source change)

| Check | Result |
|---|---|
| `build DellGamingControlCenter.sln -c Release` | Build succeeded, **0 Warning(s), 0 Error(s)** |
| `test …Tests.csproj -c Release` | **Passed! Failed: 0, Passed: 110, Skipped: 0, Total: 110** (was 103; +7 R7 tests, none weakened) |
| Full-RID single-file publish | exactly one file — `build\release\DellGamingControlCenter.exe`, **72,267,711 bytes** |
| Standalone startup/responsiveness/clean close | alive + responsive at t+15 s, title `GamerHub`, clean close, **0 log errors** (fresh log window) |
| `qa/audit_functional.ps1` (published EXE) | **0 failures / 20 checks** |
| `qa/audit_game_workflow.ps1` (published EXE) | **0 failures / 11 checks** |
| Hardware-control probes (`HwProbe.exe all`) | **ALL PASS** (power, GPU, priority, session, label) |
| UI-level power verification (`qa/verify_power_ui.ps1`) | **ALL PASS** — real app clicks verified against `powercfg` |
| Inno Setup compile | "Successful compile", 0 warnings — `dist\GamerHub-Setup-v1.0.0.exe`, **67,874,050 bytes** |
| Install cycle (UAC-approved) | old build uninstall exit 0 → new install exit 0 → launch from Program Files alive + clean close + **0 log errors** → uninstall exit 0, dir and ARP entry removed |
| **Binary rebuilt after final source change?** | **Yes** — publish and installer were produced *after* the R7-A/R7-B fixes and the new tests. |

**Artifacts:** `build/release/DellGamingControlCenter.exe` (72,267,711 B), `dist/GamerHub-Setup-v1.0.0.exe` (67,874,050 B).

---

## 11. Verdict summary

- **VERIFIED:** power-mode switching (all four modes, service-level and through the real app), power-mode restoration with correct label, per-game GPU preference write/read/remove, GPU preference refusal on missing exe (post-fix), process priority with independent WMI read-back and correct PID targeting, dead-PID honest failure, session optimization and restoration with idempotence, honest partial application, unsupported-hardware honesty.
- **PARTIALLY VERIFIED:** Quiet mode (service path only, not clicked via UI).
- **FAILED (now fixed):** R7-A GPU preference on a missing executable used to report success.
- **UNSUPPORTED:** fan, thermal, overclock/undervolt, RAM optimization, MUX, battery thresholds, HAGS.
- **NOT TESTED:** runtime dGPU usage, A/B on AC, sustained-load thermals.
