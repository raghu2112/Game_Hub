$ErrorActionPreference = "Stop"

# 1. Verify administrator privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Please run this script as an Administrator."
    exit 1
}

$installDir = "$env:ProgramFiles\GamerHub"
$startMenuPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\GamerHub.lnk"
# 2. Locate the release publish directory
$repoRoot = Split-Path $PSScriptRoot -Parent
$candidatePaths = @(
    (Join-Path $repoRoot "build\release"),
    (Join-Path $repoRoot "publish\release-win-x64")
)
$sourcePath = $candidatePaths | Where-Object { Test-Path (Join-Path $_ "DellGamingControlCenter.exe") } | Select-Object -First 1
$executablePath = if ($sourcePath) { Join-Path $sourcePath "DellGamingControlCenter.exe" } else { $null }

# 3. Verify the published executable exists
if (-not $executablePath -or -not (Test-Path $executablePath)) {
    Write-Error "Published executable not found in build\release or publish\release-win-x64. Please build the project first."
    exit 1
}

Write-Host "Installing GamerHub to $installDir..."

# 4. Create C:\Program Files\GamerHub
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

# 5. Copy the release artifacts
Write-Host "Copying files..."
Copy-Item -Path "$sourcePath\*" -Destination $installDir -Recurse -Force

# 6. Create Start Menu shortcut
Write-Host "Creating Start Menu shortcut..."
$WshShell = New-Object -comObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($startMenuPath)
$Shortcut.TargetPath = "$installDir\DellGamingControlCenter.exe"
$Shortcut.WorkingDirectory = $installDir
$Shortcut.Description = "GamerHub (Dell Gaming Control Center)"
$Shortcut.Save()

# 7. Report success/failure clearly
Write-Host "GamerHub installed successfully!"
Write-Host "You can now launch GamerHub from the Start Menu."
