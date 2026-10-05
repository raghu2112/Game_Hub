$ErrorActionPreference = "Stop"

# Dell Gaming Control Center Installer

Write-Host "Installing Dell Gaming & Performance Control Center..."

$sourceDir = Join-Path $PSScriptRoot "DellGamingControlCenter-v1.0.0-win-x64"
$targetDir = "$env:ProgramFiles\Dell Gaming Control Center"

if (!(Test-Path $sourceDir)) {
    Write-Host "Error: 'DellGamingControlCenter-v1.0.0-win-x64' folder not found. Please ensure the published binaries are in the same directory as this installer." -ForegroundColor Red
    Exit 1
}

# Create target directory
if (!(Test-Path $targetDir)) {
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
}

# Copy files
Write-Host "Copying files to $targetDir..."
Copy-Item -Path "$sourceDir\*" -Destination $targetDir -Recurse -Force

# Create Start Menu shortcut
$startMenuDir = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Dell Gaming Control Center"
if (!(Test-Path $startMenuDir)) {
    New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
}

Write-Host "Creating shortcuts..."
$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut("$startMenuDir\Dell Gaming Control Center.lnk")
$shortcut.TargetPath = "$targetDir\DellGamingControlCenter.App.exe"
$shortcut.WorkingDirectory = $targetDir
$shortcut.Description = "Dell Gaming & Performance Control Center"
$shortcut.Save()

# Create Desktop shortcut (optional)
$desktopDir = [Environment]::GetFolderPath("DesktopDirectory")
$desktopShortcut = $wshShell.CreateShortcut("$desktopDir\Dell Gaming Control Center.lnk")
$desktopShortcut.TargetPath = "$targetDir\DellGamingControlCenter.App.exe"
$desktopShortcut.WorkingDirectory = $targetDir
$desktopShortcut.Description = "Dell Gaming & Performance Control Center"
$desktopShortcut.Save()

Write-Host "Installation Complete!" -ForegroundColor Green
Write-Host "You can now launch the application from your Start Menu or Desktop."
