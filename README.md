# GamerHub — PC Gaming Control Center

**GamerHub** is a high-performance Windows desktop gaming and hardware telemetry management application built with C#, .NET 8, WPF, MVVM, and dependency injection. It delivers real-time hardware telemetry, per-game optimization profiles, diagnostic health analysis, session performance tracking, and hardware-safe system control.

---

## Key Features

- **Hardware Telemetry Pipeline:** Authoritative multi-tier sampling architecture (HWiNFO64 Shared Memory → LibreHardwareMonitor → Windows Standard Performance Counters & Win32 APIs). Real-time CPU, RAM, GPU, storage, battery, and thermal monitoring with strict hardware honesty (zero simulated or fake metrics).
- **Game Center & Library:** Automatic game executable detection, custom game addition, executable path validation, and library persistence.
- **Per-Game Optimization Profiles:** Configure Windows power plan schemes (Gaming / Balanced / Quiet), GPU preference policies (High Performance / Power Saving / Auto), and process priority assignments with automated launch enforcement and pre-launch policy restoration.
- **Session Tracking & Benchmarking:** Automated session duration tracking, background resource contention logging, rolling performance histories, and health score analytics.
- **Hardware-Honest Diagnostics:** Automated diagnostic rule engine detecting sustained resource spikes, thermal warnings, and hardware configuration mismatches without privileged driver drops or kernel bypasses.
- **Modern WPF Precision UI:** Deep neutral graphite, dark gunmetal surfaces, tactile recessed panels, and signal cyan accents with hardware-accelerated 3D CPU die visualization.

---

## Supported Operating Systems & Prerequisites

- **Operating System:** Windows 10 (version 1809+) or Windows 11 (x64)
- **Runtime Environment:**
  - Standard User execution (no administrator elevation required for ordinary monitoring or gaming sessions).
  - Self-contained release build bundles the .NET 8 desktop runtime and native libraries (no external .NET installation required).
- **Development Prerequisites:**
  - [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (SDK `8.0.425` or compatible .NET 8 SDK).
  - PowerShell 5.1+ or PowerShell 7+.
  - Optional: [HWiNFO64](https://www.hwinfo.com/) with "Shared Memory Support" enabled for low-level CPU junction temperature, live core clocks, and EC fan tachometers.

---

## Quick Start: Build, Test, Run & Publish

```powershell
# 1. Restore dependencies
dotnet restore DellGamingControlCenter.sln

# 2. Build in Release configuration
dotnet build DellGamingControlCenter.sln -c Release --no-restore

# 3. Execute automated test suite (182 tests)
dotnet test tests/DellGamingControlCenter.Tests/DellGamingControlCenter.Tests.csproj -c Release --no-build

# 4. Publish self-contained single-file executable
dotnet publish src/DellGamingControlCenter.App/DellGamingControlCenter.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none `
  -o build/release
```

To run the application directly from source:
```powershell
dotnet run --project src/DellGamingControlCenter.App/DellGamingControlCenter.App.csproj -c Release
```

---

## Repository Structure

```text
Game Hub/
├── DellGamingControlCenter.sln      # Primary Visual Studio solution
├── README.md                        # Project overview and quick start guide
├── .gitignore                       # Repository build and artifact exclusions
├── docs/                            # Comprehensive architectural and process documentation
│   ├── architecture/                # System architecture and project structure guides
│   ├── development/                 # Build instructions and environment configuration
│   ├── hardware/                    # Hardware capabilities, telemetry tiers, and sensor contracts
│   ├── testing/                     # Test strategy, execution guide, and regression reports
│   ├── releases/                    # Release engineering, packaging, and publish verification
│   └── audits/                      # Codebase audits, stabilization history, and baseline logs
├── src/                             # Production source code
│   ├── DellGamingControlCenter.App/ # WPF application entry point, views, viewmodels, themes
│   ├── DellGamingControlCenter.Core/# Core domain interfaces, models, and enums
│   └── DellGamingControlCenter.Services/ # Telemetry providers, HAL, policies, and background workers
├── tests/                           # Automated test suite
│   └── DellGamingControlCenter.Tests/# 182 xUnit tests (unit, protocol, UI, security)
├── scripts/                         # Installation, uninstallation, and developer tools
├── installer/                       # Inno Setup packaging script (GamerHub.iss)
├── qa/                              # Automated UI and QA verification scripts
├── assets/                          # Application icons and branding assets
├── build/                           # Build and publish output directory (build/release/)
└── dist/                            # Production deliverables (installer and portable zip)
```

---

## Current Hardware Capabilities & Limitations

1. **Hardware Honesty Guarantee:** In the absence of an active HWiNFO64 interactive session or dedicated vendor Ring-0 drivers, CPU junction core temperatures, live core clock multipliers, and fan tachometer RPM are marked **Unavailable**. GamerHub never fabricates or estimates sensor data.
2. **Motherboard ACPI Zone:** The ACPI thermal zone (`25.0°C (ACPI)`) is explicitly identified as motherboard ambient and never conflated with CPU core junction temperature.
3. **GPU Metrics:** AMD Radeon 520 GPU core utilization is captured via DXGI performance counters. Thermal reporting for mobile dGPUs without vendor SDKs remains marked unsupported.
4. **Data Preservation:** User profiles, game library configurations, and session histories are saved to `%LocalAppData%\DellGamingControlCenter\config.json` and are preserved across updates and uninstallation.

---

## Detailed Documentation

- [Application Architecture](docs/architecture/ARCHITECTURE.md)
- [Project Structure & Guidelines](docs/architecture/PROJECT_STRUCTURE.md)
- [Build and Run Guide](docs/development/BUILD_AND_RUN.md)
- [Hardware Capabilities & Telemetry](docs/hardware/HARDWARE_CAPABILITIES.md)
- [Testing Guide & Strategy](docs/testing/TESTING.md)
- [Codebase Cleanup Report](docs/audits/CODEBASE_CLEANUP_REPORT.md)
- [Release Process & Packaging](docs/releases/RELEASE_PROCESS.md)
