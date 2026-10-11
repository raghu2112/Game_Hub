# capture_dialogs.ps1 - open-state capture for the app's modal dialogs / popups.
#
# The window-rect harness in capture_screens.ps1 only ever sees the main window, so popup
# and dialog surfaces were never captured. This script opens each reachable dialog from the
# real UI, captures it as its own window rectangle, and closes it again.
#
# Inventory note: the application contains NO ComboBox, ContextMenu or Menu controls, and no
# autocomplete/suggestion panel - the only drop-down surface in the design system is the
# themed ComboBox template, which no view instantiates (verified by WpfSurfaceRuntimeTests).
# ToolTips are covered by unit test rather than capture, because a Tooltip cannot be held open
# reliably through UIA. The tray menu is a WinForms ContextMenuStrip drawn with OS chrome and
# is out of scope for the Claymorphism theme.
#
# Same method notes as capture_screens.ps1: run from a non-DPI-aware PowerShell so
# GetWindowRect and Graphics.CopyFromScreen share one coordinate space. ASCII only - this
# file has no BOM and Windows PowerShell 5.1 reads it as ANSI.
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$exe = 'E:\Projects\Game Hub\build\release\DellGamingControlCenter.exe'
$outDir = 'E:\Projects\Game Hub\tmp\dialogs'
$manifest = Join-Path $outDir 'manifest.txt'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShotDlg {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    public const int SW_RESTORE = 9;
}
'@

function Get-TopLevelWindows([int]$procId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)
    return @($root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond))
}

function Get-MainWindow([int]$procId) {
    foreach ($w in (Get-TopLevelWindows $procId)) {
        if ($w.Current.Name -like '*GamerHub*') { return $w }
    }
    return $null
}

# A modal WPF dialog is an OWNED window: the WPF UIA provider exposes it inside the OWNER's
# descendant subtree, and a ProcessId-filtered root query does not return it at all. Search the
# main window's descendants first, then fall back to a desktop-wide exact-name lookup (both
# locations verified against the live app while building this harness).
function Find-WindowElement([string]$expectedTitle, [int]$procId) {
    $main = Get-MainWindow $procId
    if ($main) {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window)
        foreach ($w in @($main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond))) {
            if ($w.Current.Name -eq $expectedTitle) { return $w }
        }
    }

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $byName = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $expectedTitle)
    $byType = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.AndCondition]::new($byName, $byType))
}

function Find-ByName($w, $name, $type) {
    if (-not $w) { return $null }
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    if ($type) {
        $typeCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)
        $cond = [System.Windows.Automation.AndCondition]::new($cond, $typeCond)
    }
    return $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Invoke-ByName($w, $name) {
    $b = Find-ByName $w $name ([System.Windows.Automation.ControlType]::Button)
    if (-not $b) { return $false }
    try {
        ($b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
        return $true
    } catch { return $false }
}

function Save-WindowShot($h, $path) {
    $r = New-Object ShotDlg+RECT
    if (-not [ShotDlg]::GetWindowRect($h, [ref]$r)) { return $false }
    $w = $r.Right - $r.Left
    $hh = $r.Bottom - $r.Top
    if ($w -le 0 -or $hh -le 0) { return $false }
    $bmp = New-Object System.Drawing.Bitmap($w, $hh)
    try {
        $captured = $false
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $hh)))
            $g.Dispose()
            $captured = $true
        } catch {
            $captured = $false
        }
        if (-not $captured) {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $hdc = $g.GetHdc()
            $pw = [ShotDlg]::PrintWindow($h, $hdc, 2)
            if (-not $pw) { [void][ShotDlg]::PrintWindow($h, $hdc, 0) }
            $g.ReleaseHdc($hdc)
            $g.Dispose()
        }
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)

        $colors = New-Object 'System.Collections.Generic.HashSet[string]'
        for ($y = 5; $y -lt $hh -and $colors.Count -lt 8; $y += [Math]::Max(1, [int]($hh / 7))) {
            for ($x = 5; $x -lt $w -and $colors.Count -lt 8; $x += [Math]::Max(1, [int]($w / 7))) {
                [void]$colors.Add($bmp.GetPixel($x, $y).ToArgb().ToString())
            }
        }
        return ($colors.Count -ge 3)
    } finally {
        $bmp.Dispose()
    }
}

function Close-Window($w) {
    try {
        $pattern = $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
        $pattern.Close()
        return $true
    } catch { }
    try {
        SendKeys.SendWait("{ESC}")
        return $true
    } catch { return $false }
}

# ── setup ───────────────────────────────────────────────────────────────
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
Get-Process DellGamingControlCenter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

$lines = @()
$captured = 0
$problems = 0
Set-Content -LiteralPath $manifest -Value "dialog/popup open-state captures - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

function Log([string]$msg) {
    Write-Output $msg
    $script:lines += $msg
}

# ── launch ──────────────────────────────────────────────────────────────
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 14
$h = $proc.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Output 'FATAL: main window not found'; exit 1 }

[void][ShotDlg]::ShowWindow($h, [ShotDlg]::SW_RESTORE)
[void][ShotDlg]::SetForegroundWindow($h)
Start-Sleep -Seconds 2

try {
    # ── 1. Game Editor dialog (ADD GAME) ─────────────────────────────────
    # ADD GAME only exists on the Game Center page, so navigate there first.
    $main = Get-MainWindow $proc.Id
    $navOk = Invoke-ByName $main 'Game Center'
    Start-Sleep -Seconds 4

    $main = Get-MainWindow $proc.Id
    $clicked = Invoke-ByName $main 'ADD GAME'
    if (-not $clicked) { $clicked = Invoke-ByName $main 'Add Game to Library' }
    Start-Sleep -Seconds 4
    Log ("navigate-to-game-center: {0}" -f $navOk)

    $dlg = Find-WindowElement 'Add Game' $proc.Id
    $ok = $false
    if ($dlg) {
        $dh = [IntPtr]$dlg.Current.NativeWindowHandle
        [void][ShotDlg]::SetForegroundWindow($dh)
        Start-Sleep -Seconds 1
        $ok = Save-WindowShot $dh (Join-Path $outDir 'game_editor_open.png')
        Log ("ADD GAME: clicked={0} dialog-title='{1}' shot={2} controls={3}" -f `
            $clicked, $dlg.Current.Name, $ok,
            $dlg.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::IsEnabledProperty, $true))).Count)
        [void](Close-Window $dlg)
        Start-Sleep -Seconds 2
    } else {
        Log ("ADD GAME: clicked={0} but no dialog window appeared" -f $clicked)
        $problems++
    }
    if ($ok) { $captured++ } elseif ($dlg) { $problems++ }

    # ── 2. Profile editor dialog (PROFILE / EDIT) ────────────────────────
    $main = Get-MainWindow $proc.Id
    $clicked = Invoke-ByName $main 'PROFILE'
    if (-not $clicked) { $clicked = Invoke-ByName $main 'EDIT' }
    if (-not $clicked) { $clicked = Invoke-ByName $main 'Edit Game Profile' }
    Start-Sleep -Seconds 4

    $dlg = Find-WindowElement 'Edit Game' $proc.Id
    if (-not $dlg) { $dlg = Find-WindowElement 'Edit Profile' $proc.Id }
    $ok = $false
    if ($dlg) {
        $dh = [IntPtr]$dlg.Current.NativeWindowHandle
        [void][ShotDlg]::SetForegroundWindow($dh)
        Start-Sleep -Seconds 1
        $ok = Save-WindowShot $dh (Join-Path $outDir 'profile_editor_open.png')
        Log ("PROFILE: clicked={0} dialog-title='{1}' shot={2}" -f $clicked, $dlg.Current.Name, $ok)
        [void](Close-Window $dlg)
        Start-Sleep -Seconds 2
    } else {
        Log ("PROFILE: clicked={0} but no dialog window appeared (library may be empty)" -f $clicked)
    }
    if ($ok) { $captured++ }

    # ── 3. Shell is still healthy after the dialogs closed ───────────────
    $main = Get-MainWindow $proc.Id
    $shellOk = ($main -ne $null) -and ((Find-ByName $main 'GAMERHUB' $null) -ne $null)
    Log ("shell-intact-after-dialogs: {0}" -f $shellOk)
    if (-not $shellOk) { $problems++ }
}
finally {
    Add-Content -LiteralPath $manifest -Value ($lines -join [Environment]::NewLine)
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
}

Write-Output "SUMMARY: $captured dialog capture(s), $problems problem(s)"
exit $(if ($problems -gt 0) { 1 } else { 0 })
