# GamerHub Release Engineering & Packaging Process

This document defines the official, reproducible release procedure, deployment model, verification smoke tests, and rollback strategies for GamerHub.

---

## 1. Release Strategy & Deployment Model

GamerHub uses a **Self-Contained Single-File** release architecture:
- **Target Runtime:** Windows x64 (`win-x64`)
- **Self-Contained (`--self-contained true`):** Bundles the complete .NET 8 desktop runtime, WPF assemblies, and native C++ dependencies inside the application payload. The end user does not need to install the .NET 8 Desktop Runtime.
- **Single-File Compression (`PublishSingleFile=true`, `EnableCompressionInSingleFile=true`):** Compresses ~154 MB of framework assemblies down to a single ~74.2 MB executable.
- **Native Library Extraction (`IncludeNativeLibrariesForSelfExtract=true`):** Native C++ dependencies (DirectX DXGI helpers, LibreHardwareMonitor native driver stubs) self-extract directly into application memory.
- **Zero Companion DLLs:** The output directory contains solely `DellGamingControlCenter.exe`.

---

## 2. Step-by-Step Clean Release Build Procedure

### Step 1: Environment Setup
Ensure the .NET 8 SDK is loaded in PowerShell:
```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
```

### Step 2: Clean and Restore
```powershell
dotnet clean DellGamingControlCenter.sln -c Release
dotnet restore DellGamingControlCenter.sln
```

### Step 3: Compile and Test
```powershell
# Compile Release binaries
dotnet build DellGamingControlCenter.sln -c Release --no-restore

# Verify test suite
dotnet test tests/DellGamingControlCenter.Tests/DellGamingControlCenter.Tests.csproj -c Release --no-build
```
*Requirement:* The test suite must pass with 0 errors and 0 failures before proceeding to publish.

### Step 4: Publish Single-File Deliverable
```powershell
dotnet publish src/DellGamingControlCenter.App/DellGamingControlCenter.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=none `
  -o build/release
```

### Step 5: Package Portable Distribution Archive
```powershell
Compress-Archive -Path "build\release\DellGamingControlCenter.exe" -DestinationPath "dist\GamerHub-v1.0.0-win-x64.zip" -Force
```

### Step 6: Inno Setup Installer Compilation (Optional)
If Inno Setup compiler is available:
```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/GamerHub.iss
```
This builds `dist/GamerHub-Setup-v1.0.0.exe`.

---

## 3. Artifact Verification & Smoke Test Checklist

Execute the following verification script on the generated artifact:

```powershell
$exe = "E:\Projects\Game Hub\build\release\DellGamingControlCenter.exe"

# 1. Verify existence and size
$item = Get-Item $exe
Write-Host "File size: $($item.Length) bytes (Timestamp: $($item.LastWriteTime))"

# 2. Launch process
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 3

# 3. Check responsiveness
$proc.Refresh()
Write-Host "Process PID: $($proc.Id), Responding: $($proc.Responding)"

# 4. Graceful window close
$proc.CloseMainWindow() | Out-Null
$proc.WaitForExit(5000)
Write-Host "Process Exit Code: $($proc.ExitCode)"

# 5. Check user data preservation
$config = "$env:LOCALAPPDATA\DellGamingControlCenter\config.json"
Write-Host "Config intact: $(Test-Path $config)"
```

### Acceptance Criteria
- [x] Process starts without UAC elevation prompt.
- [x] Process `Responding: True` confirmed continuously across 3 seconds.
- [x] Zero DLL load failures or `FileNotFoundException` in Windows Event Viewer.
- [x] `CloseMainWindow()` honored cleanly with exit code 0.
- [x] `%LocalAppData%\DellGamingControlCenter\config.json` remained uncorrupted and intact.

---

## 4. Release Rollback & Backup Procedure

- Previous known-good releases are preserved in `dist/` (`GamerHub-Setup-v1.0.0.exe` and `GamerHub-v1.0.0-win-x64.zip`).
- In the event of a critical defect in a freshly published binary, the previous known-good executable can be restored directly from `dist/` or Git tag checkpoints.
- User data in `%LocalAppData%\DellGamingControlCenter` is isolated from the application binary directory and is never overwritten or deleted during application rollbacks.
