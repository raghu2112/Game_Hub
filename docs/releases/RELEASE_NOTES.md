# GamerHub — Release Notes (v1.0.0 Release Candidate)

**GamerHub** is a Windows desktop application that bridges the gap between hardware telemetry, performance optimization, and gaming UX for the Dell Inspiron 3576. 

## Features
* **Dashboard & Telemetry:** View realtime hardware statistics (CPU usage, frequency, RAM availability, and discrete GPU utilization). 
* **Game Center:** Manage your library of games natively within GamerHub. Includes automatic executable discovery.
* **Intelligent Game Profiles:** Configure automated pre-launch behaviors such as Windows power plan adjustments and Windows per-game GPU preferences to optimize supported Windows gaming policies for the configured game.
* **Session Analytics & Monitoring:** Automatically tracks total play time, analyzes background resource contention, and recovers system state gracefully on exit.
* **Performance Center:** An aggregate view of diagnostic snapshots and session history to review long-term system stability.* **UX First Design:** Claymorphism UI built in WPF — deep obsidian surfaces, muted violet accents, tactile extruded cards, and graphical telemetry.

## Hardware Honesty (Dell Inspiron 3576 Limitations)
Due to the OEM ACPI constraints of the Dell Inspiron 3576 platform, the following features are **unsupported** in GamerHub. Instead of failing silently or using unsafe bypass methods, GamerHub accurately labels these as *Unsupported* inside the UI:
- **Thermal Controls:** Fan curves, fan RPM, and Dell thermal profile switching are unavailable (BIOS restricted).
- **Overclocking/Undervolting:** The Intel i3-7020U (2C/4T) is locked by design.
- **Battery Thresholds:** Dell Command WMI extensions are not exposed on this model.
- **RAM Cleaners:** No aggressive memory trimming is performed. Only safe Windows memory metrics are monitored.

## Installation
1. Right-click `install.ps1` and select **Run with PowerShell** (Administrator privileges required to write to `Program Files`).
2. A shortcut will be created in your Start Menu as **GamerHub**.
3. All configuration, telemetry, and profiles will be saved locally to your user profile: `%LocalAppData%\DellGamingControlCenter`.

## Uninstallation
Delete the installation directory at `C:\Program Files\GamerHub` and remove the Start Menu shortcut from `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\GamerHub.lnk`. Configuration data is left in `%LocalAppData%\DellGamingControlCenter` to preserve your game profiles.

## Safety & Security Guarantee
GamerHub never injects into foreign processes, modifies sensitive Windows Registry keys (except natively through `powercfg`), or arbitrarily ends running processes. It uses explicit `UseShellExecute=false` bindings to launch your configured executables securely.
