$ErrorActionPreference = "Stop"

# 1. Verify administrator privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Please run this script as an Administrator."
    exit 1
}

Write-Host "Uninstalling GamerHub..."

# 2. Stop GamerHub if running
$processName = "DellGamingControlCenter"
if (Get-Process -Name $processName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping running instance of GamerHub..."
    Stop-Process -Name $processName -Force
    Start-Sleep -Seconds 2
}

$installDir = "$env:ProgramFiles\GamerHub"
$startMenuPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\GamerHub.lnk"

# 3. Remove installed application files
if (Test-Path $installDir) {
    Write-Host "Removing application files at $installDir..."
    Remove-Item -Path $installDir -Recurse -Force
} else {
    Write-Host "Application directory $installDir not found. Skipping."
}

# 4. Remove the Start Menu shortcut
if (Test-Path $startMenuPath) {
    Write-Host "Removing Start Menu shortcut..."
    Remove-Item -Path $startMenuPath -Force
} else {
    Write-Host "Start Menu shortcut not found. Skipping."
}

# 5. NOT delete LocalAppData (Unless explicitly requested)
Write-Host ""
Write-Host "User data and profiles at %LocalAppData%\DellGamingControlCenter were NOT deleted to preserve your configuration."
Write-Host "If you wish to remove them, delete the folder manually."

# 6. Report what was removed
Write-Host ""
Write-Host "GamerHub has been successfully uninstalled."
