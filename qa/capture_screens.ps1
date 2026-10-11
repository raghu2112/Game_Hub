# capture_screens.ps1 - visual QA screenshot harness
# Captures every navigation destination at three window sizes so the layout can
# be reviewed (clipping, overflow, contrast, empty states) instead of only being
# asserted through the UIA tree.
#
# Method notes (host-verified):
#   * The capture runs in a non-DPI-aware PowerShell process, so GetWindowRect
#     and Graphics.CopyFromScreen speak the same logical-pixel space as
#     [System.Windows.Forms.Screen] (1536x864 here). Do not add SetProcessDPIAware
#     to this script: it would desynchronize the two coordinate spaces.
#   * Sizes must fit the work area - the window is moved to (30,30) and the
#     primary screen is 1536x864 logical, so the largest size is clamped.
#   * Only ASCII literals: this file has no BOM and Windows PowerShell 5.1 would
#     read it as ANSI.
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$exe = 'E:\Projects\Game Hub\build\release\DellGamingControlCenter.exe'
$outDir = 'E:\Projects\Game Hub\tmp\shots'
$manifest = Join-Path $outDir 'manifest.txt'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShotWin {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
}
'@

function Get-MainWindow($procId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)
    foreach ($w in ($root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond))) {
        if ($w.Current.Name -like '*GamerHub*') { return $w }
    }
    return $null
}

function Find-ByName($w, $name, $type) {
    if (-not $w) { return $null }
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    if ($type) {
        $typeCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)
        $cond = [System.Windows.Automation.AndCondition]::new($cond, $typeCond)
    }
    return $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Invoke-ByName($w, $name) {
    $b = Find-ByName $w $name ([System.Windows.Automation.ControlType]::Button)
    if (-not $b) { return $false }
    try { ($b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke(); return $true } catch { return $false }
}

# Capture the window's own rectangle. Returns $true when a non-flat image was
# written (an all-black frame means the window was not painted yet).
function Save-WindowShot($h, $path) {
    $r = New-Object ShotWin+RECT
    if (-not [ShotWin]::GetWindowRect($h, [ref]$r)) { return $false }
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
            $pw = [ShotWin]::PrintWindow($h, $hdc, 2)
            if (-not $pw) { [void][ShotWin]::PrintWindow($h, $hdc, 0) }
            $g.ReleaseHdc($hdc)
            $g.Dispose()
        }
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)

        # Flatness probe: sample a grid of pixels; a single unique color means the
        # capture caught an unpainted window rather than the real UI.
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

# ── setup ───────────────────────────────────────────────────────────────
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
Get-Process DellGamingControlCenter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

$work = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$maxW = [Math]::Min(1500, $work.Width - 40)
$maxH = [Math]::Min(860, $work.Height - 40)
$sizes = @(
    @{ w = 1000; h = 650 },
    @{ w = 1200; h = 750 },
    @{ w = $maxW; h = $maxH }
) | Where-Object { $_.w -le $maxW -and $_.h -le $maxH }

# Same section/chip logic as audit_functional.ps1: Sessions and Benchmarks are
# chips that only exist while the Gaming section is active.
$pages = @(
    @{ nav = 'Dashboard';   marker = 'GAMERHUB' },
    @{ nav = 'Game Center'; marker = 'ADD GAME' },
    @{ nav = 'Performance'; marker = 'POWER MODES' },
    @{ nav = 'Diagnostics'; marker = 'LIVE METRICS' },
    @{ nav = 'Sessions';    section = 'Game Center'; marker = 'VIEW REPORT' },
    @{ nav = 'Hardware';    marker = 'CAPABILITIES' },
    @{ nav = 'Benchmarks';  section = 'Game Center'; marker = 'Run Baseline Benchmark' },
    @{ nav = 'Settings';    marker = 'AUTOMATION' }
)

$shots = 0
$flat = 0
$lines = @()
Set-Content -LiteralPath $manifest -Value "visual QA captures - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

# ── launch ──────────────────────────────────────────────────────────────
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 14
$win = Get-MainWindow $proc.Id
if (-not $win) { Write-Output 'FATAL: main window not found'; exit 1 }
$h = if ($win) { [IntPtr]$win.Current.NativeWindowHandle } else { $proc.MainWindowHandle }

try {
    foreach ($sz in $sizes) {
        [void][ShotWin]::MoveWindow($h, 30, 30, $sz.w, $sz.h, $true)
        [void][ShotWin]::ShowWindow($h, 9)
        [void][ShotWin]::SetForegroundWindow($h)
        Start-Sleep -Seconds 2

        foreach ($p in $pages) {
            $w2 = Get-MainWindow $proc.Id
            if ($p.section) { [void](Invoke-ByName $w2 $p.section); Start-Sleep -Seconds 2 }
            $w2 = Get-MainWindow $proc.Id
            $clicked = Invoke-ByName $w2 $p.nav
            Start-Sleep -Seconds 3

            $w3 = Get-MainWindow $proc.Id
            $marker = $false
            if ($w3) { $marker = (Find-ByName $w3 $p.marker $null) -ne $null }

            $slug = ($p.nav -replace ' ', '').ToLower()
            $path = Join-Path $outDir ("{0}x{1}_{2}.png" -f $sz.w, $sz.h, $slug)
            $ok = $false
            try { $ok = Save-WindowShot $h $path } catch { $ok = $false }

            if ($ok -and $marker) { $shots++ } else { $flat++ }
            $line = "{0}x{1} {2} clicked={3} marker={4} shot={5} -> {6}" -f $sz.w, $sz.h, $p.nav, $clicked, $marker, $ok, (Split-Path -Leaf $path)
            $lines += $line
            Write-Output $line
        }
    }
} finally {
    Add-Content -LiteralPath $manifest -Value ($lines -join [Environment]::NewLine)
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
}

Write-Output "SUMMARY: $shots captures verified, $flat problem(s)"
exit $(if ($flat -gt 0) { 1 } else { 0 })
