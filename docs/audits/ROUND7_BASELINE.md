# ROUND7_BASELINE.md — Hardware-Tuning Reality Check, Baseline (2026-10-10)

Round 7 baseline established **before any source change**, per the mandate. This document records the live
system, the audited control paths, and the pre-registered truth matrix that Round 7 verification will fill in.

## 1. Live system evidence (captured 2026-10-10, `Win32_OperatingSystem`/`Win32_Processor`/`Win32_VideoController`/`powercfg`)

| Item | Live value |
|---|---|
| OS | Microsoft Windows 11 Pro, Build 22631 (10.0.22631) |
| Machine | Dell Inc. Inspiron 3576 (matches prior rounds; unchanged) |
| CPU | Intel(R) Core(TM) i3-7020U CPU @ 2.30GHz — 2 cores / 4 threads, MaxClockSpeed 2304 MHz (locked SKU, no turbo) |
| GPU 0 | Radeon (TM) 520 (discrete), driver 27.20.20913.2000 |
| GPU 1 | Intel(R) HD Graphics 620 (integrated), driver 27.20.100.9664 |
| Active power scheme at baseline | `a1841308-3541-4fab-bc81-f71556f20b4a` (**Power saver**) |
| Existing power schemes | **Only 2**: `381b4222-…` (Balanced) and `a1841308-…` (Power saver, active). **No `8c5e7fda-…` (High performance) scheme exists on this machine.** |
| Power saver processor settings (AC) | PROCTHROTTLEMIN = **5%**, PROCTHROTTLEMAX = 100% |
| Balanced processor settings (AC) | PROCTHROTTLEMIN = **5%**, PROCTHROTTLEMAX = 100% |
| Elevation of this session | Not elevated (UAC prompts are user-approved per-step) |

**Pre-registered critical expectation:** on this machine `PerformanceMode.Gaming` targets GUID
`8c5e7fda-…` which **does not exist**, so `PerformanceModeService.ApplyModeAsync` takes the B6 fallback
`TryCreateSchemeFrom`, whose source is `GetActivePowerSchemeGuid()` — i.e. **the currently active scheme**
(Power saver at baseline). If confirmed live, "GAMING" mode on this device is a *clone of Power saver*
activated under a new GUID: Windows accepts the change, the UI reports success, but no performance setting
differs from Power saver. This is exactly the "requested ≠ accepted ≠ active ≠ effective" gap Round 7 exists to catch.

## 2. Audited control paths (source inspection, unchanged in this baseline step)

### 2.1 Power mode (Performance page / game profiles)
`UI PerformanceView (ActivateModeCommand) → PerformanceViewModel → ChangePerformanceModeUseCase.ExecuteAsync
(authorize → validate enum) → PerformanceModeService.ApplyModeAsync → WindowsPowerPlanService.SetPowerSchemeAsync
("powercfg /setactive <guid>", GUID-format regex validated) → read-back via powrprof!PowerGetActiveScheme`.
- Fallback (B6): if the target GUID is not activatable, `powercfg /duplicatescheme <active> <newGuid>` then activate the clone. Clone remembered per mode in `_customSchemeByMode`.
- Honest failure path exists: if both attempts fail → restore previous scheme, return false, UI gets `ControlResult.Failed`.
- `GetCurrentMode()` syncs from the real active scheme on first call (unknown GUID → Balanced).
- Defect already visible statically: `RestorePreviousModeInternalAsync` sets `_currentMode = Balanced` unconditionally after a restore, regardless of the GUID actually restored.

### 2.2 Per-game GPU preference
`GameEditor/profile → ChangeGpuPreferenceUseCase (authorize, path non-empty check) → GpuPreferenceController
→ HKCU\Software\Microsoft\DirectX\UserGpuPreferences value "GpuPreference=1|2|0;" keyed by full exe path`.
- This is the genuine Windows 10/11 per-application graphics preference mechanism (Settings → Graphics).
- Static gaps: `SetPreference` returns true after `SetValue` with **no read-back**; `NormalizePath` (`Path.GetFullPath`) succeeds for nonexistent files, so a typo path is written to the registry and reported success. Round 7 §4.8 requires honest reporting for missing/invalid paths.
- Runtime adapter selection is a separate question from the registry preference (LUID attribution limitation preserved).

### 2.3 Process priority
`ChangeProcessPriorityUseCase (authorize) → ProcessController.SetPriority → System.Diagnostics.Process.PriorityClass`
(below/normal/above only — deliberately excludes High/RealTime). `GetPriority` maps unknown/inaccessible → Normal
(conflates "inaccessible" with "Normal"; recorded as a known honesty gap to probe).

### 2.4 Session lifecycle
`GameCenterViewModel → GameSessionManager.StartSessionAsync → GameLauncher.LaunchGameAsync
(pre-launch policies via PolicyEvaluator: GPU preference + power mode) → process detection by name
(60×500 ms) → ApplyPostLaunchPoliciesAsync (process priority) → monitoring + rooted exit watcher
(event + 1 s poll safety net) → HandleProcessExitAsync → RestoreSessionAsync → PolicyEvaluator.RestoreProfileAsync
(GPU preference restored only if changed; power scheme restored only if mode differs) → history + report`.
- "Restoration Successful" is derived from restore call results; skipped restores count as success. Applied-change counting only counts policies actually applied (skips excluded).

### 2.5 Authorization & audit
`AuthorizationService` denies only elevation-requiring capabilities (thermal/fan/GPU-mode/network) when not elevated;
power mode and process priority and GPU *preference* are per-user and legitimately need no elevation.
Every authorize() writes an `[AUDIT]` entry via `ControlAuditLogger` into the activity log.

### 2.6 Telemetry honesty (already hardened in prior rounds)
CPU usage -1 = unavailable (never fake 0), GPU Engine counters real (Task-Manager source) with null-on-unsupported,
battery wear only from firmware capacities, thermal metrics return empty (unsupported). `DellCapabilityDetector`
probes `root\WMI DellSMBIOS`, `root\dcim\sysman` DCIM classes — all absent on this Inspiron → fan/thermal/battery-threshold = Unsupported, and the UI shows "Not supported on this device" labels (no fake sliders).

### 2.7 Existing test/QA surface (inspected before adding anything)
- 103 unit tests incl. `PerformanceModeTests` (mocked power plan service — a **fake-service test**, not hardware verification), `SecurityPolicyTests` (authorization paths), `HardwareAbstractionTests` (capability map), `GameSessionIntegrationTests` (real ping.exe session, mocked policies).
- QA harnesses: `qa/audit_functional.ps1` (20 UIA checks; its PERFORMANCE mode check clicks two activate buttons but **never independently reads the active scheme back**), `qa/audit_game_workflow.ps1` (11 checks), `qa/capture_screens.ps1`.

## 3. Control-by-control truth matrix (to be filled by live verification)

| Control | UI action | Intended effect | Implementation | Verdict to test |
|---|---|---|---|---|
| Power mode: Quiet/BatterySaver | Performance page activate button | Active scheme = Power saver (exists) | powercfg /setactive + read-back | to test |
| Power mode: Balanced | same | Active scheme = Balanced | same | to test |
| Power mode: Gaming/MaximumPerformance | same | Active scheme = High performance | **missing scheme → clones the ACTIVE scheme** (expect: clone of Power saver) | to test (pre-registered suspicion) |
| Mode restore after session | automatic on game exit | Previous scheme reactivated | RestorePreviousModeAsync + `_currentMode` label | to test (label-vs-reality) |
| Per-game GPU preference | Game editor GPU field → session launch | HKCU UserGpuPreferences written for exe | Registry write, no read-back | to test incl. invalid path |
| Runtime dGPU usage | launch game with HighPerformance pref | Process renders on Radeon 520 | OS decides | to test if safely possible; else NOT TESTED |
| Process priority | profile priority → session | Target PID priority changed | Process.PriorityClass | to test with read-back via independent source |
| Session restore | game exit / app close during session | GPU pref + power scheme restored | PolicyEvaluator | to test with independent read-back |
| Fan / thermal / OC / MUX / battery threshold | none exposed as controls | — | CapabilityDetector = Unsupported | audit only (keep honest) |
| Effective performance gain | A/B measurement | measurable difference | power schemes only | to test with safe synthetic workload |

## 4. Verification plan (ordered, safe)
1. Build a hardware-probe harness (`tmp/r7/hwprobe`) that drives the **real services** (no fakes) against the live OS and records pre-state, action result, and **independent** post-state (`powercfg`, `reg query`, WMI, separate processes).
2. Drive the **real app** via UIA for power-mode switching; independently read the active scheme with `powercfg` before/after each click.
3. Process-priority and session lifecycle against a benign self-owned long-running process (ping -t), never third-party processes.
4. A/B measurement with a fixed CPU-bound workload under each scheme; restore original scheme afterward.
5. Fix only confirmed defects; add regression tests; re-run full pipeline; rebuild artifacts last.
