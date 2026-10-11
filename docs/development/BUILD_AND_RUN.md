# GamerHub Build & Run Guide

This document describes the environment prerequisites, commands, and deployment models for building, testing, and running GamerHub.

---

## 1. Prerequisites & Toolchain

- **Operating System:** Windows 10 (version 1809+) or Windows 11 (x64)
- **.NET SDK:** .NET 8.0 SDK (version `8.0.425` or later)
- **C# Language Version:** C# 12.0
- **Windows Desktop SDK:** Included with .NET 8 SDK (`net8.0-windows` targeting WPF and WinForms APIs)
- **Optional Tools:**
  - [Inno Setup 6.x](https://jrsoftware.org/isdl.php) (needed to compile `installer/GamerHub.iss` into `GamerHub-Setup-v1.0.0.exe`)

---

## 2. Build & Execution Commands

### Environment Configuration (PowerShell)
If using a user-local .NET SDK installation:
```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
```

### Clean
Purge previous build outputs and intermediate files:
```powershell
dotnet clean DellGamingControlCenter.sln -c Release
```

### Restore Dependencies
Restore NuGet packages across all projects:
```powershell
dotnet restore DellGamingControlCenter.sln
```

### Build
Build the entire solution in `Release` configuration:
```powershell
dotnet build DellGamingControlCenter.sln -c Release --no-restore
```
*Expected result:* `0 Warning(s), 0 Error(s)`.

### Run from Source
Launch the WPF application in development mode:
```powershell
dotnet run --project src/DellGamingControlCenter.App/DellGamingControlCenter.App.csproj -c Release
```

---

## 3. Automated Testing

Execute the test suite (182 automated tests):
```powershell
dotnet test tests/DellGamingControlCenter.Tests/DellGamingControlCenter.Tests.csproj -c Release --no-build
```

### Test Configuration Details
- Tests run with test parallelization disabled (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) to avoid race conditions on shared memory buffers, singleton mocks, and UI thread dispatchers.
- Test categories include:
  - Protocol tests for HWiNFO shared memory structure and offsets.
  - Hardware abstraction and fallback cascade tests.
  - Security authorization and power plan policy tests.
  - UI binding, value converters, and theme resource verification.
  - Game session lifecycle and state restoration tests.

---

## 4. Release Publishing & Packaging

GamerHub uses a **Self-Contained Single-File** deployment strategy for Windows x64.

### Publish Single-File Executable
```powershell
dotnet publish src/DellGamingControlCenter.App/DellGamingControlCenter.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none `
  -o build/release
```

- **Output Path:** `build/release/DellGamingControlCenter.exe`
- **Output Size:** ~74.2 MB (compressed from ~154 MB uncompressed assemblies)
- **Architecture:** Windows 64-bit (`win-x64`)
- **Dependencies:** Fully self-contained. All WPF frameworks, .NET 8 desktop runtimes, and native C++ binaries are packaged internally inside the executable. No external runtime installation is required.

### Portable Distribution Archive
```powershell
Compress-Archive -Path "build\release\DellGamingControlCenter.exe" -DestinationPath "dist\GamerHub-v1.0.0-win-x64.zip" -Force
```

### Inno Setup Installer (Optional)
If Inno Setup compiler (`ISCC.exe`) is installed:
```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/GamerHub.iss
```
This produces `dist/GamerHub-Setup-v1.0.0.exe` (~67.8 MB).

---

## 5. Troubleshooting & Common Issues

| Symptom | Cause | Solution |
| :--- | :--- | :--- |
| `dotnet: command not found` | .NET SDK is installed in a non-standard or user-local location. | Set `$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"` and prepend to `$env:PATH`. |
| Second instance fails to start or closes immediately | Single-instance guard active. | GamerHub enforces a single instance via mutex `Local\GamerHub.SingleInstance`. The running instance will be brought to the foreground automatically. |
| Hardware temperature reports `Unavailable` | HWiNFO64 is not running or shared memory is disabled. | Start HWiNFO64 and enable "Shared Memory Support" in HWiNFO settings. GamerHub never fabricates sensor values when low-level sensors are unreachable. |
| Test failures due to cross-thread COM access | Parallel test execution enabled. | Ensure `TestConfig.cs` with `[assembly: CollectionBehavior(DisableTestParallelization = true)]` is present in the test project. |
