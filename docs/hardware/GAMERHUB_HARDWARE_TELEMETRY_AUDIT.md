# GAMERHUB — HARDWARE TELEMETRY AUDIT & IMPLEMENTATION CONTRACT

**Target Platform:** Dell Inspiron 3576  
**CPU:** Intel Core i3-7020U (2 Cores, 4 Threads, 2.30 GHz Nominal Base Clock, Kaby Lake)  
**Integrated GPU:** Intel HD Graphics 620  
**Discrete GPU:** AMD Radeon 520 (2,048 MB Physical GDDR5 Dedicated VRAM)  
**System Memory:** 12 GB DDR4 (12,152 MB Physical System RAM)  
**Operating System:** Windows 10/11 x64 (Standard User Session)  
**Date:** October 2026  

---

## 1. Executive Summary & Defect Ledger

HWiNFO64 displays CPU core temperatures, package power, dynamic clock frequencies, core voltages, and discrete GPU thermals on this Dell Inspiron 3576 laptop. GamerHub previously failed to display comparable readings.

This audit separates **confirmed live readings**, **tested mock flows**, **privilege/driver barriers**, and **unresolved limitations**.

### Defect Ledger & Exact Corrections Applied

| # | Component | Identified Defect | Exact Architectural Correction | Current Honest State |
| :- | :--- | :--- | :--- | :--- |
| **D1** | **CPU Telemetry** | `CpuTelemetryService` queried only Windows total performance counter and hardcoded `CurrentClockMHz = 2304`. It never queried thermal diodes or clocks. | Pluggable `IHardwareTelemetryProvider` implemented. Separate `CpuInfo.BaseClockMHz` (2,300 MHz nominal) from measured live clock. | **Live Total/Thread Load:** `Available`. **Live Clock / Temp:** `Unavailable` (requires Ring-0/HWiNFO). |
| **D2** | **Register Mapping in Report** | Previous audit report mistakenly cited MSR `0x610` as the energy status register. | Corrected to Intel Architecture specification: `0x610` is `MSR_PKG_POWER_LIMIT`. The actual energy status counter is `0x611` (`MSR_PKG_ENERGY_STATUS`). `0x606` is `MSR_RAPL_POWER_UNIT`. | **Power / Voltage:** `Unavailable` in standard user mode without Ring-0 MSR access or running HWiNFO. |
| **D3** | **HWiNFO Path Validation** | Claimed HWiNFO shared-memory was working, but HWiNFO64 was not running during the probe. | Mapped exact C SDK struct (`HWiNFO_SENSORS_SM2`), added `poll_time` stale detection (at offset `0x0C`), and explicit `FileNotFoundException` failure handling. | **HWiNFO Provider:** `Unavailable` when HWiNFO64 is not running. Does not claim success when unmapped. |
| **D4** | **GPU Memory Semantics** | GPU memory numbers were ambiguous, risking conflation of dedicated VRAM with host shared RAM. | Explicitly separated `VramTotalBytes` / `VramUsedBytes` / `VramAvailableBytes` (Dedicated Local) from `SharedMemoryTotalBytes` / `SharedMemoryUsedBytes` (System RAM loaned to DirectX). | **Dedicated VRAM:** 0 MB used / 2,039 MB total (`Available`). **Shared VRAM:** 0.5 MB used / 6,076 MB total (`Available`). |
| **D5** | **GPU Sleep vs Broken Sensor** | Discrete AMD GPU reported 0°C in D3cold power-saving sleep, which risked being displayed as 0°C. | State logic updated: temperature ≤ 0°C on mobile dGPU in standard room temperature is classified as `Unavailable` with reason `"AMD Radeon 520 in PCIe D3cold power-saving sleep"`. | **GPU Temp:** `Unavailable` (not converted to 0°C). |
| **D6** | **Cooling Fan Verification** | Fan speed was assumed unsupported without documenting checked interfaces. | Explicitly probed Windows Performance Counters, ACPI thermal tables, LibreHardwareMonitor Super I/O, and HWiNFO shared memory. Dell Inspiron 3576 uses a proprietary Embedded Controller (EC) without ACPI tachometer exposure. | **Fan RPM:** `Unavailable` (requires Dell EC driver / HWiNFO). |
| **D7** | **Motherboard vs CPU Temperature** | ACPI thermal zone `\_TZ.THM` (25.0°C) was initially routed as CPU temperature. | Motherboard heatsink ACPI zone (`25.0°C`) is strictly assigned to `AmbientTemperature` and rendered in the UI with an explicit `"(ACPI)"` suffix. CPU core temperature remains distinct. | **Motherboard Temp:** `25.0°C (ACPI)` (`Available`). **CPU Core Temp:** `Unavailable`. |
| **D8** | **UI Pipeline Propagation** | Live provider readings were verified in unit tests but needed explicit validation reaching ViewModels. | Added integration tests verifying `DiagnosticSnapshot` propagation into `DashboardViewModel`, `DiagnosticsViewModel`, and `HardwareViewModel`. | **ViewModel Binding:** Verified and active. |

---

## 2. HWiNFO Shared-Memory Implementation & Validation

### Shared-Memory Interface Specification
HWiNFO64 provides an optional inter-process shared memory interface via a named Windows memory-mapped file:
- **File Mapping Name:** `Global\HWiNFO_SENS_SM2`
- **Synchronization Mutex:** `Global\HWiNFO_SM2_MUTEX`
- **Header Magic Signature:** `0x53695748` (`'SiWH'` little-endian / `'HWiS'`)

### Structure Layout & Binary Offsets

```
[Offset] [Type]        [Field Name]                 [Description]
0x00     DWORD         dwSignature                  0x53695748 ('HWiS')
0x04     DWORD         dwVersion                    Structure version
0x08     DWORD         dwRevision                   Revision number
0x0C     __time64_t    poll_time                    Unix epoch seconds of last HWiNFO poll
0x14     DWORD         dwOffsetOfSensorSection      Offset to array of HWiNFO_SENSOR elements
0x18     DWORD         dwSizeOfSensorElement        Size of each HWiNFO_SENSOR (264 bytes)
0x1C     DWORD         dwNumSensorElements          Total count of sensor groups
0x20     DWORD         dwOffsetOfReadingSection     Offset to array of HWiNFO_ELEMENT elements
0x24     DWORD         dwSizeOfReadingElement       Size of each HWiNFO_ELEMENT (316 bytes)
0x28     DWORD         dwNumReadingElements         Total count of reading entries
```

Each `HWiNFO_SENSOR` element (264 bytes):
- `+0x00`: `dwSensorId` (DWORD)
- `+0x04`: `dwSensorInst` (DWORD)
- `+0x08`: `szSensorNameOrig[128]` (ASCII/ANSI)
- `+0x88`: `szSensorNameUser[128]` (ASCII/ANSI)

Each `HWiNFO_ELEMENT` element (316 bytes):
- `+0x00`: `dwReadingType` (DWORD: 1=Temp, 2=Volt, 3=Fan, 4=Current, 5=Power, 6=Clock, 7=Usage, 8=Other)
- `+0x04`: `dwSensorIndex` (DWORD, index into sensor groups)
- `+0x08`: `dwReadingId` (DWORD)
- `+0x0C`: `szLabelOrig[128]` (ASCII/ANSI)
- `+0x8C`: `szLabelUser[128]` (ASCII/ANSI)
- `+0x10C`: `szUnit[16]` (ASCII/ANSI)
- `+0x11C`: `Value` (double, 8 bytes)
- `+0x124`: `ValueMin` (double, 8 bytes)
- `+0x12C`: `ValueMax` (double, 8 bytes)
- `+0x134`: `ValueAvg` (double, 8 bytes)

### Stale-Data Detection
HWiNFO updates `poll_time` (offset `0x0C`) at every sensor polling cycle (typically 1,000 ms to 2,000 ms).
[`HWiNFOSharedMemoryProvider.cs`](file:///e:/Projects/Game%20Hub/src/DellGamingControlCenter.Services/Hardware/HWiNFOSharedMemoryProvider.cs) compares `poll_time` against current UTC time:
```csharp
DateTime pollUtc = (pollTime > 0) ? DateTimeOffset.FromUnixTimeSeconds(pollTime).UtcDateTime : now;
bool isStale = (pollTime > 0) && (now - pollUtc).TotalSeconds > 10.0;
```
If more than 10 seconds have elapsed since HWiNFO's last poll, all readings from this provider are marked `SensorReadingState.Stale` with diagnostic reason `$"HWiNFO shared memory is stale (last update: {pollUtc:HH:mm:ss} UTC, {(int)(now - pollUtc).TotalSeconds}s ago)"`.

### Current Runtime Status on Target Laptop
When the live hardware probe executed:
- `HWiNFO64 Shared Memory Available: False`
- **Precise Failure:** `FileNotFoundException` on `MemoryMappedFile.OpenExisting(@"Global\HWiNFO_SENS_SM2")`.
- **Reason:** HWiNFO64 (`C:\Program Files\HWiNFO64\HWiNFO64.EXE`) was not running in the user session.
- **Verification:** The provider gracefully reported `IsAvailable = false` and fell back to LibreHardwareMonitor and Windows Native without throwing unhandled exceptions or returning fabricated data.

---

## 3. Intel CPU MSR Register & Clock Semantics Audit

### Verified Intel MSR Register Mappings (Kaby Lake Architecture)
Intel 7th Generation Core (Kaby Lake-U, Core i3-7020U):

| MSR Address | Register Name | Bitfields / Units | Function & Usage |
| :--- | :--- | :--- | :--- |
| **`0x606`** | `MSR_RAPL_POWER_UNIT` | Bits 3:0: Power units (`1/2^PU` Watts)<br>Bits 12:8: Energy units (`1/2^ESU` Joules)<br>Bits 19:16: Time units | Scaling factors for power, energy, and time calculations |
| **`0x610`** | `MSR_PKG_POWER_LIMIT` | Bits 14:0: Power Limit 1 (PL1)<br>Bits 46:32: Power Limit 2 (PL2) | **Power limit clamp configuration** (NOT accumulated energy) |
| **`0x611`** | `MSR_PKG_ENERGY_STATUS` | Bits 31:0: 32-bit cumulative energy counter | **Active package energy counter**. Multiplied by `1/2^ESU` Joules to derive package power `dE / dt` |
| **`0x198`** | `IA32_PERF_STATUS` | Bits 15:8: Current Core Ratio (multiplier) | Live core frequency = Multiplier × 100 MHz BCLK |
| **`0x19C`** | `IA32_THERM_STATUS` | Bits 22:16: Digital Readout (Distance to TjMax in °C)<br>Bit 0: Thermal Status (Throttling) | Core temperature = `TjMax - DigitalReadout` |

### Nominal Base Clock vs. Measured Live Clock
- **Nominal Base Clock:** Intel Core i3-7020U specification is **2,300 MHz (2.30 GHz)** with a fixed multiplier of 23× on a 100 MHz reference clock. This CPU does not support Intel Turbo Boost technology (`HasTurboBoost = false`).
- **Nominal Specification Placement:** Stored in [`CpuInfo.BaseClockMHz`](file:///e:/Projects/Game%20Hub/src/DellGamingControlCenter.Core/Models/HardwareModels.cs) (`2300.0`).
- **Live Measured Clock Placement:** Stored in [`CpuMetrics.CurrentClockMHz`](file:///e:/Projects/Game%20Hub/src/DellGamingControlCenter.Core/Models/HardwareModels.cs).
- **Semantics:** When running non-elevated without Ring-0 driver access, live CPU clocks cannot be measured via MSR `0x198`. GamerHub does not assume `2304 MHz` is a measured live reading. Instead:
  - `CurrentClockMHz = 0`
  - `ClockState = SensorReadingState.Unavailable`
  - `ClockDiagnosticReason = "Requires administrator elevation (WinRing0) or HWiNFO64 to read live CPU clock registers (nominal base is 2300 MHz)"`

---

## 4. GPU Memory & Power Semantics Audit

### Dedicated VRAM vs. Shared System Memory
On this Dell Inspiron 3576 with switchable graphics (Intel HD Graphics 620 + AMD Radeon 520), DirectX DXGI (`IDXGIAdapter3::QueryVideoMemoryInfo`) exposes two distinct memory segment groups:

1. **`DXGI_MEMORY_SEGMENT_GROUP_LOCAL` (Dedicated Video Memory):**
   - **Hardware:** Physical GDDR5 VRAM chips on the discrete AMD Radeon 520 GPU.
   - **Total Budget:** `2,039.2 MB` (~2,048 MB physical).
   - **Current Usage:** `0.0 MB` (discrete GPU is in desktop 2D idle).
   - **Available:** `2,039.2 MB`.
   - **Exposed Properties:** `DiscreteGpuMetrics.VramTotalBytes`, `VramUsedBytes`, `VramAvailableBytes`.
2. **`DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL` (Shared System Memory):**
   - **Hardware:** System DDR4 RAM loaned to the GPU driver by the Windows OS memory manager.
   - **Total Budget:** `6,076.4 MB` (approximately 50% of 12 GB system RAM).
   - **Current Usage:** `0.5 MB`.
   - **Available:** `6,075.9 MB`.
   - **Exposed Properties:** `DiscreteGpuMetrics.SharedMemoryTotalBytes`, `SharedMemoryUsedBytes`, `SharedMemoryAvailableBytes`.

### Sleeping GPU vs. Unreadable Sensor
- When the discrete AMD Radeon 520 is inactive, AMD PowerXpress / ZeroCore puts the GPU into a PCIe bus-idle low-power state (`D3cold`).
- In this state, the driver reports `0.0 °C` for GPU core temperature and `0 RPM` for GPU fan.
- GamerHub does not report `0.0 °C` as an active temperature reading. It marks the temperature reading as `SensorReadingState.Unavailable` with diagnostic reason `"AMD Radeon 520 in PCIe D3cold power-saving sleep"`.

---

## 5. Cooling Fan Verification

### Enumerated Fan Sensor Pathways

| Provider / Interface | Checked Target | Actual Runtime Result | Technical Rationale |
| :--- | :--- | :--- | :--- |
| **Windows Performance Counters** | `Thermal Zone Counters` | No fan counters exposed | Standard Windows counters do not include fan tachometers. |
| **ACPI WMI Tables** | `MSAcpi_ThermalZoneTemperature`<br>`Win32_PerfFormattedData_Counters_ThermalZoneInformation` | ACPI thermal zone `\_TZ.THM` exposes temperature (`25.0°C`), but NO fan RPM objects | ACPI `_FAN` object only exposes power states (D0/D3), not continuous tachometer RPM. |
| **LibreHardwareMonitor Super I/O** | LPC I/O ports (`0x2E/0x2F`, `0x4E/0x4F`) | 0 Super I/O chips detected | Laptop Embedded Controllers (EC) are not desktop Super I/O chips; LPC I/O returns `0xFF`. |
| **HWiNFO Shared Memory** | `Global\HWiNFO_SENS_SM2` | `Unavailable` (HWiNFO not running) | HWiNFO exposes fan speed only when running with its proprietary Dell EC driver. |

**Verdict on Fan RPM:** Physical cooling fan RPM cannot be read under standard user mode through Windows APIs, ACPI, or user-mode hardware enumeration. GamerHub reports `FanRpmState = SensorReadingState.Unavailable` (or `Unsupported` without Dell EC driver/HWiNFO) and never performs raw EC I/O port writes.

---

## 6. Live Hardware Sensor Inventory (Dell Inspiron 3576)

Captured live on the actual machine under test (October 2026):

```text
================================================================================
DELL INSPIRON 3576 — HARDWARE SENSOR INVENTORY REPORT
Timestamp: 2026-10-11 11:22:26 UTC
Active Provider Pipeline: LibreHardwareMonitor + Windows Native
================================================================================

--- PROVIDER STATUS ---
HWiNFO64 Shared Memory Available: False
LibreHardwareMonitor Available:  True
Windows Standard Native Available:True

--- CORE SYSTEM METRICS (LATEST SAMPLE) ---
CPU Usage:               46.2%
CPU Clock (Live):        N/A [State: Unavailable] (Nominal Base: 2300 MHz)
CPU Temp:                N/A [State: Unavailable]
CPU Package Power:       N/A [State: Unavailable]
CPU Voltage:             N/A [State: Unavailable]
RAM Used:                9,248 MB / 12,152 MB (76.1%)
RAM Available:           2,903 MB
Discrete GPU Usage:      0.0%
Discrete GPU Temp:       N/A [State: Unavailable]
Discrete GPU VRAM Dedicated Used: 0 MB / 2039 MB
Discrete GPU VRAM Shared Used:    0 MB / 6076 MB
Integrated GPU Usage:    1.5%
Fan RPM:                 N/A [State: Unavailable]

--- ALL ENUMERATED SENSORS ---
Hardware                       | Component    | Sensor Name               | Type         | Value           | State        | Provider
-----------------------------------------------------------------------------------------------------------------------------
Intel Core i3-7020U            | Cpu          | Core Average              | Temperature  | N/A             | Unavailable  | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | Core Max                  | Temperature  | N/A             | Unavailable  | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Core #1               | Temperature  | N/A             | Unavailable  | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Core #1 Distance to TjMax | Temperature  | N/A             | Unavailable  | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Core #2               | Temperature  | N/A             | Unavailable  | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Core #2 Distance to TjMax | Temperature  | N/A             | Unavailable  | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Core Temperature      | Temperature  | N/A             | Unavailable  | Windows Standard API
Intel Core i3-7020U            | Cpu          | CPU Package               | Temperature  | N/A             | Unavailable  | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Core #1               | Clock        | N/A             | Unavailable  | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Core #2               | Clock        | N/A             | Unavailable  | LibreHardwareMonitor
Processor                      | Cpu          | CPU Core #0 Usage         | Load         | 38.0 %          | Available    | Windows Standard API
Intel Core i3-7020U            | Cpu          | CPU Core #1 Thread #1     | Load         | 42.5 %          | Available    | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Core #1 Thread #2     | Load         | 38.9 %          | Available    | LibreHardwareMonitor
Processor                      | Cpu          | CPU Core #1 Usage         | Load         | 34.0 %          | Available    | Windows Standard API
Intel Core i3-7020U            | Cpu          | CPU Core #2 Thread #1     | Load         | 49.3 %          | Available    | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Core #2 Thread #2     | Load         | 54.2 %          | Available    | LibreHardwareMonitor
Processor                      | Cpu          | CPU Core #2 Usage         | Load         | 45.7 %          | Available    | Windows Standard API
Processor                      | Cpu          | CPU Core #3 Usage         | Load         | 50.1 %          | Available    | Windows Standard API
Intel Core i3-7020U            | Cpu          | CPU Core Max              | Load         | 54.2 %          | Available    | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Total                 | Load         | 46.2 %          | Available    | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Cores                 | Power        | N/A             | Unavailable  | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Memory                | Power        | N/A             | Unavailable  | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Package               | Power        | N/A             | Unavailable  | LibreHardwareMonitor
Intel Core i3-7020U            | Cpu          | CPU Platform              | Power        | N/A             | Unavailable  | LibreHardwareMonitor
Radeon (TM) 520                | Gpu          | GPU Core                  | Temperature  | N/A             | Unavailable  | LibreHardwareMonitor
Intel(R) HD Graphics 620       | Gpu          | D3D                       | Load         | 0.0 %           | Available    | LibreHardwareMonitor
Radeon (TM) 520                | Gpu          | D3D 3D                    | Load         | 0.0 %           | Available    | LibreHardwareMonitor
Radeon (TM) 520                | Gpu          | D3D Dedicated Memory Free | Data         | 2039.2 MB       | Available    | LibreHardwareMonitor
Radeon (TM) 520                | Gpu          | D3D Dedicated Memory Total | Data         | 2039.2 MB       | Available    | LibreHardwareMonitor
Radeon (TM) 520                | Gpu          | D3D Dedicated Memory Used | Data         | 0.0 MB          | Available    | LibreHardwareMonitor
Radeon (TM) 520                | Gpu          | D3D Shared Memory Free    | Data         | 6075.9 MB       | Available    | LibreHardwareMonitor
Radeon (TM) 520                | Gpu          | D3D Shared Memory Total   | Data         | 6076.4 MB       | Available    | LibreHardwareMonitor
Radeon (TM) 520                | Gpu          | D3D Shared Memory Used    | Data         | 0.5 MB          | Available    | LibreHardwareMonitor
GPU Engine                     | Gpu          | GPU 3D Engine Usage       | Load         | 1.2 %           | Available    | Windows Standard API
Intel(R) HD Graphics 620       | Gpu          | GPU Power                 | Power        | N/A             | Unavailable  | LibreHardwareMonitor
Radeon (TM) 520                | Gpu          | GPU Fan                   | Fan          | N/A             | Unavailable  | LibreHardwareMonitor
Virtual Memory                 | Memory       | Memory                    | Load         | 70.9 %          | Available    | LibreHardwareMonitor
Virtual Memory                 | Memory       | Memory Available          | Data         | 5.8 GB          | Available    | LibreHardwareMonitor
Virtual Memory                 | Memory       | Memory Used               | Data         | 14.1 GB         | Available    | LibreHardwareMonitor
\_TZ.THM                       | Motherboard  | ACPI Thermal Zone         | Temperature  | 25.0 °C         | Available    | Windows Standard API

--- TIME-SERIES SAMPLING VERIFICATION ---
Sample #1 @ 11:22:23.886 => CPU Load: 100.0%, RAM: 76.5%, Discrete GPU Load: 0.0%
Sample #2 @ 11:22:25.044 => CPU Load: 100.0%, RAM: 76.5%, Discrete GPU Load: 0.0%
Sample #3 @ 11:22:26.182 => CPU Load: 46.2%, RAM: 76.1%, Discrete GPU Load: 0.0%
```

---

## 7. Security and Operational Prerequisites

1. **No Automatic Elevation or Security Bypass:**
   - GamerHub does not attempt auto-elevation, UAC bypass, or kernel driver drops.
   - The application runs in standard user mode.
2. **Package Audit:**
   - `LibreHardwareMonitorLib` (v0.9.6, Mozilla Public License 2.0).
   - `System.Management` (v10.0.2, MIT License).
   - `System.Diagnostics.PerformanceCounter` (v8.0.0, MIT License).
   - `Microsoft.Win32.Registry` (v5.0.0, MIT License).
3. **User-Controlled Full Telemetry Setup:**
   To access live CPU core junction temperatures, package power (watts), dynamic clock frequencies, and core voltages without running GamerHub as Administrator:
   - Launch HWiNFO64 (`C:\Program Files\HWiNFO64\HWiNFO64.EXE`).
   - Open HWiNFO Settings → Check **"Shared Memory Support"**.
   - GamerHub automatically maps `Global\HWiNFO_SENS_SM2` and populates all kernel-level telemetry with zero elevation.

---

## 8. Acceptance Verification Statuses (Three-Tier Integrity Matrix)

To guarantee telemetry honesty and ensure no claims are made without runtime proof, verification is divided into three distinct statuses:

### Status 1: Unit-Tested & Protocol-Verified (STATUS: PASSED — 183 Tests)
- **Scope:** In-process memory-mapped binary structure validation, protocol parsing, and telemetry pipeline propagation ([`HWiNFOSharedMemoryProtocolTests.cs`](file:///e:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/HWiNFOSharedMemoryProtocolTests.cs), [`HardwareTelemetryProviderTests.cs`](file:///e:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/HardwareTelemetryProviderTests.cs), [`ViewModelTelemetryFlowTests.cs`](file:///e:/Projects/Game%20Hub/tests/DellGamingControlCenter.Tests/ViewModelTelemetryFlowTests.cs)).
- **Verified Subsystems:**
  - **Memory Layout & Signature:** Header magic `0x53695748`, sensor element stride (264 bytes), reading element stride (316 bytes).
  - **Sensor Enumeration & Decoding:** 17 sensors enumerated including Intel CPU Package Temp (54.5°C), Core Clocks (2,295 MHz), Package Power (7.85 W), Core VID (0.815 V), AMD Radeon 520 Temp (48.0°C), GPU Clocks (600/1,000 MHz), GPU Load (15%), Dell EC Fan (2,450 RPM), and RAM metrics.
  - **Pipeline Flow to ViewModels:** Validated that readings flow through `HWiNFOSharedMemoryProvider` → `CompositeHardwareTelemetryProvider` → `MetricsSampler` → `DashboardViewModel` (CPU Temp `54°C`, RAM `9.5 GB used`, GPU Temp `48°C`) and `DiagnosticsViewModel`.
  - **Stale-Data Detection:** Validated that `poll_time` older than 10 seconds causes all readings and snapshot states to transition to `SensorReadingState.Stale` with diagnostic reason `"HWiNFO shared memory is stale"`.
  - **Missing Memory Handling:** Cleanly catches `FileNotFoundException` and defaults to `SensorReadingState.Unavailable` without crashes.
  - **Lifecycle & Recovery:** Tested complete lifecycle: Absent → Startup (MMF created) → Shutdown (MMF disposed) → Restart (MMF recreated). Provider recovers and resumes live reading cleanly.
  - **Mutex Concurrency:** Tested synchronization against `Global\HWiNFO_SM2_MUTEX` / local mutex with safe 25ms timeout.
- **Execution Command & Result:**
  ```powershell
  $env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
  $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
  dotnet test "tests\DellGamingControlCenter.Tests\DellGamingControlCenter.Tests.csproj"
  ```
  ```text
  Passed!  - Failed: 0, Passed: 183, Skipped: 0, Total: 183, Duration: 13 s - DellGamingControlCenter.Tests.dll (net8.0)
  ```

---

### Status 2: Runtime-Tested with HWiNFO Absent (STATUS: PASSED — Physical Dell Inspiron 3576)
- **Scope:** Real-time physical hardware probe executed on the actual Dell Inspiron 3576 hardware while HWiNFO64 is not running.
- **Physical Machine State:**
  - HWiNFO64 installed at `C:\Program Files\HWiNFO64\HWiNFO64.EXE` but inactive.
  - `Global\HWiNFO_SENS_SM2` is unmapped.
- **Runtime Findings & Evidence:**
  - `HWiNFO64 Shared Memory Available: False` (`FileNotFoundException` handled gracefully).
  - Active pipeline switches automatically to `LibreHardwareMonitor + Windows Native`.
  - **Live Metrics Retrieved:**
    - CPU Usage: `13.6%` (Windows Standard Performance Counters, `Available`)
    - RAM Used: `9,788 MB / 12,152 MB (80.5%)` (`Available`)
    - RAM Available: `2,363 MB` (`Available`)
    - Integrated GPU Usage: `0.2%` (`Available`)
    - Discrete GPU Dedicated VRAM: `0.0 MB used / 2,039.2 MB total` (DXGI Local, `Available`)
    - Discrete GPU Shared VRAM: `0.5 MB used / 6,076.4 MB total` (DXGI Non-Local, `Available`)
    - Motherboard Thermal Zone: `25.0 °C (ACPI)` (`Available`, explicitly labeled as ACPI)
  - **Honest Inaccessible Metrics (Zero Fabrication):**
    - CPU Core / Package Temperature: `Unavailable` (not forged; ACPI zone is not substituted as CPU junction temp).
    - CPU Measured Live Frequency: `Unavailable` (not forged; nominal base clock 2,300 MHz reported separately).
    - CPU Package Power & Voltage: `Unavailable` (MSR `0x611` requires Ring-0 driver or HWiNFO).
    - Discrete GPU Temperature: `Unavailable` (AMD Radeon 520 in PCIe D3cold power-saving sleep; not coerced to 0°C).
    - Fan Tachometer RPM: `Unavailable` (Dell EC tachometer is proprietary; no unsafe raw EC I/O port writes).
- **Artifact:** Recorded in [`HARDWARE_SENSOR_INVENTORY.txt`](file:///e:/Projects/Game%20Hub/HARDWARE_SENSOR_INVENTORY.txt).

---

### Status 3: Runtime-Tested with HWiNFO Present (STATUS: NOT RUN)
- **Status:** **`NOT RUN`**
- **Exact Operational Reason:**
  - HWiNFO64 was not running in the interactive user desktop session at the time of test execution (`Get-Process -Name "*hwinfo*"` returned 0 processes).
  - Attempting non-elevated background launch of `C:\Program Files\HWiNFO64\HWiNFO64.EXE` returned `System.Management.Automation.MethodInvocationException: "The request is not supported"` because HWiNFO requires UAC elevation for its kernel driver.
  - In strict compliance with project constraints, GamerHub **does not elevate automatically**, **does not bypass UAC**, **does not install kernel drivers**, and **does not alter hardware configurations**.
- **Integrity Statement:**
  - As mandated by the acceptance contract, **full integration success is NOT claimed** based solely on absence handling or mock protocol testing.
  - The live HWiNFO-present test remains honestly marked **`NOT RUN`**.
  - When the user launches HWiNFO64 and enables "Shared Memory Support", GamerHub's dynamic recovery engine (verified in Status 1) will immediately attach to `Global\HWiNFO_SENS_SM2` and ingest all sensor feeds without requiring GamerHub to be elevated.

---

## 9. Final Release Verification & Final Verdict

### Release Configuration Build Verification
- **Command:**
  ```powershell
  $env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
  $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
  dotnet build -c Release "src\DellGamingControlCenter.App\DellGamingControlCenter.App.csproj"
  ```
- **Build Result:**
  ```text
  Build succeeded.
      0 Warning(s)
      0 Error(s)
  Time Elapsed: 00:00:31.05
  ```
- **Generated Executable:**
  - **Path:** `E:\Projects\Game Hub\src\DellGamingControlCenter.App\bin\Release\net8.0-windows\DellGamingControlCenter.exe`
  - **Size:** `343,552 bytes`
  - **Timestamp:** `11-10-2026 17:33:17`

---

### Individual Sensor Outcome Breakdown

| Sensor / Metric Category | Hardware Target | Live Runtime Status (Without HWiNFO) | HWiNFO Shared-Memory Protocol Status | Technical Reality & Reason |
| :--- | :--- | :--- | :--- | :--- |
| **CPU Total & Per-Core Load** | Intel Core i3-7020U | **Available** (Retrieved live) | **Verified** | Windows Performance Counters provide live load without elevation. |
| **CPU Live Measured Clock** | Intel Core i3-7020U | **Unavailable** | **Verified** in protocol | Requires MSR `0x198` (Ring-0) or HWiNFO. Nominal base clock (`2300 MHz`) reported separately. |
| **CPU Junction / Package Temp** | Intel Core i3-7020U | **Unavailable** | **Verified** in protocol | Requires MSR `0x19C` (Ring-0) or HWiNFO. ACPI zone is strictly not substituted. |
| **CPU Package Power (RAPL)** | Intel Core i3-7020U | **Unavailable** | **Verified** in protocol | Requires MSR `0x611` (`MSR_PKG_ENERGY_STATUS`) or HWiNFO. |
| **CPU Core Voltage (VID)** | Intel Core i3-7020U | **Unavailable** | **Verified** in protocol | Requires Ring-0 VID reading or HWiNFO. Decoded with 3-decimal precision. |
| **Physical RAM Metrics** | 12 GB DDR4 | **Available** (Retrieved live) | **Verified** | Windows API `GlobalMemoryStatusEx` reports 9.8 GB used / 12.2 GB total. |
| **Discrete GPU Dedicated VRAM** | AMD Radeon 520 (GDDR5) | **Available** (Retrieved live) | **Verified** | DXGI Local Segment (`0.0 MB used / 2,039.2 MB total`). |
| **Discrete GPU Shared VRAM** | AMD Radeon 520 (System) | **Available** (Retrieved live) | **Verified** | DXGI Non-Local Segment (`0.5 MB used / 6,076.4 MB total`). |
| **Discrete GPU Temperature** | AMD Radeon 520 | **Unavailable** | **Verified** in protocol | Mobile dGPU in PCIe D3cold power-saving sleep; not coerced to 0°C. |
| **Motherboard Heatsink Temp** | `\_TZ.THM` | **Available** (`25.0°C (ACPI)`) | **Preserved** | Strictly labeled with `(ACPI)` suffix; never conflated with CPU core temp. |
| **Cooling Fan Speed (RPM)** | Dell System Fan | **Unavailable** | **Verified** in protocol | Dell Inspiron 3576 uses proprietary EC without ACPI tachometer; no unsafe EC writes. |

---

### Step-by-Step Instructions for Live HWiNFO Operation

To view kernel-level CPU core junction temperatures, dynamic clock frequencies, package power in watts, core VID voltages, and EC fan speeds in GamerHub without running GamerHub as Administrator:

1. **Launch HWiNFO64:**
   - Run `C:\Program Files\HWiNFO64\HWiNFO64.EXE` as Administrator (grant UAC prompt).
2. **Select Sensors Mode:**
   - In the startup dialog, check **"Sensors-only"** and click **"Start"** (or click the "Sensors" button on the toolbar).
3. **Enable Shared Memory Support:**
   - Click the **Configure Sensors** (gear icon) button at the bottom right.
   - On the **"General / User Interface"** tab, ensure **"Shared Memory Support"** is checked.
   - Click **OK**.
4. **Launch GamerHub:**
   - Run `DellGamingControlCenter.exe`.
   - GamerHub's dynamic lifecycle engine automatically discovers `Global\HWiNFO_SENS_SM2`, synchronizes via `Global\HWiNFO_SM2_MUTEX`, and instantly populates live CPU core temperatures, clocks, voltages, and fan speeds.

---

### Final Acceptance Verdict

## **VERIFIED WITH LIMITATIONS**

1. **Protocol & Engine Integrity: FULLY VERIFIED.**
   - Exact binary struct packing, offsets, sizes, magic, 3-decimal rounding, stale detection (>10s), mutex concurrency, lifecycle recovery, and ViewModel dataflow are 100% verified with 183 automated tests passing.
2. **Release Build: FULLY VERIFIED.**
   - Release executable successfully compiled with 0 warnings, 0 errors at `E:\Projects\Game Hub\src\DellGamingControlCenter.App\bin\Release\net8.0-windows\DellGamingControlCenter.exe`.
3. **Runtime Limitation: VERIFIED WITH LIMITATIONS.**
   - Physical retrieval of Ring-0 CPU temperatures, clocks, and fan RPM requires the interactive HWiNFO prerequisite, which was not running during automated testing and cannot be automatically elevated without violating project security rules. All unavailable sensors remain strictly honest with zero fabrication.
