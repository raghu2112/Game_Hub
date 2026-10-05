$ErrorActionPreference = "Stop"

# Dell Gaming Control Center Uninstaller

Write-Host "Uninstalling Dell Gaming & Performance Control Center..."

$targetDir = "$env:ProgramFiles\Dell Gaming Control Center"
$startMenuDir = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Dell Gaming Control Center"
$desktopDir = [Environment]::GetFolderPath("DesktopDirectory")

# Stop process if running
$processName = "DellGamingControlCenter.App"
if (Get-Process -Name $processName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping application..."
    Stop-Process -Name $processName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

# Remove files
if (Test-Path $targetDir) {
    Write-Host "Removing application files..."
    Remove-Item -Path $targetDir -Recurse -Force
}

# Remove Start Menu shortcut
if (Test-Path $startMenuDir) {
    Write-Host "Removing Start Menu shortcuts..."
    Remove-Item -Path $startMenuDir -Recurse -Force
}

# Remove Desktop shortcut
$desktopShortcut = "$desktopDir\Dell Gaming Control Center.lnk"
if (Test-Path $desktopShortcut) {
    Write-Host "Removing Desktop shortcut..."
    Remove-Item -Path $desktopShortcut -Force
}

Write-Host "Uninstallation Complete!" -ForegroundColor Green
Write-Host "Note: User data in AppData/Local/DellGamingControlCenter has been preserved."
