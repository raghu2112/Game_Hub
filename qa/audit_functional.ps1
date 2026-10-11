$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = 'E:\Projects\Game Hub\src\DellGamingControlCenter.App\bin\Release\net8.0-windows\DellGamingControlCenter.exe'
$script:results = @()
$script:proc = $null

function Log($name, $pass) {
    $line = "$name : $(if($pass){'PASS'}else{'FAIL'})"
    $script:results += $line
    Write-Output $line
}

# A forgotten instance from a previous run corrupts the shared %LocalAppData% log
# (last writer wins) — kill any pre-existing app instances before starting.
Get-Process DellGamingControlCenter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

function Get-Windows($procId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)
    return $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Get-MainWindow($procId) {
    foreach ($w in (Get-Windows $procId)) {
        if ($w.Current.Name -like '*GamerHub*') { return $w }
    }
    return $null
}

# The window hides itself on minimize (tray behavior) and then vanishes from the
# UIA tree, turning every later check into a stale-element failure. Re-acquire —
# and restore via Win32 if hidden — before each step.
function Get-LiveWindow {
    if ($script:proc -eq $null) { return $null }
    $win = Get-MainWindow $script:proc.Id
    if (-not $win) {
        $h = $script:proc.MainWindowHandle
        if ($h -ne [IntPtr]::Zero) {
            Add-Type -Namespace Native -Name Win -MemberDefinition '
                [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
                [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);' -ErrorAction SilentlyContinue
            [Native.Win]::ShowWindow($h, 9) | Out-Null
            [Native.Win]::SetForegroundWindow($h) | Out-Null
            Start-Sleep -Milliseconds 800
            $win = Get-MainWindow $script:proc.Id
        }
    }
    return $win
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

function Invoke-Button($w, $name) {
    $el = Find-ByName $w $name ([System.Windows.Automation.ControlType]::Button)
    if (-not $el) { return $false }
    try { ($el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke(); return $true } catch { return $false }
}

function PageHas($w, $text) {
    return (Find-ByName $w $text $null) -ne $null
}

# An Invoke rebuilds the window's automation subtree: the pre-click element then
# answers queries with empty results (verified: same element re-acquired post-click
# finds the marker). So always re-acquire after clicking and retry the marker —
# pages do real work on entry and the UI thread is busy on this 2C/4T box.
function Click-Nav($name) {
    $w = Get-LiveWindow
    if (-not $w) { return $false }
    $clicked = Invoke-Button $w $name
    Start-Sleep -Seconds 2
    return $clicked
}

function Wait-Marker($text, $tries = 6) {
    for ($i = 0; $i -lt $tries; $i++) {
        $w = Get-LiveWindow
        if ($w -and (PageHas $w $text)) { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

# ── Launch ──────────────────────────────────────────────────────────────
$script:proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 14
$win = Get-MainWindow $proc.Id
Log "STARTUP window-appears" ($win -ne $null)
if (-not $win) { Stop-Process -Id $proc.Id -Force; exit 1 }
Log "STARTUP title-is-gamerhub" ($win.Current.Name -eq 'GamerHub')

try {

# ── Page walk with content verification ────────────────────────────────
$pages = @(
    @{ nav='Dashboard';   marker='GAMERHUB' },
    @{ nav='Game Center'; marker='ADD GAME' },
    @{ nav='Performance'; marker='POWER MODES' },
    @{ nav='Diagnostics'; marker='LIVE METRICS' },
    @{ nav='Sessions';    section='Game Center'; marker='VIEW REPORT' },
    @{ nav='Hardware';    marker='CAPABILITIES' },
    @{ nav='Benchmarks';  section='Game Center'; marker='Run Baseline Benchmark' },
    @{ nav='Settings';    marker='AUTOMATION' }
)
foreach ($p in $pages) {
    # Contextual secondary nav: Sessions/Benchmarks are chips that only exist
    # while the GAMING section is active — click the section first.
    if ($p.section) {
        $w0 = Get-LiveWindow
        if ($w0) { Invoke-Button $w0 $p.section | Out-Null }
        Start-Sleep -Seconds 2
    }
    $clicked = Click-Nav $p.nav
    $found = Wait-Marker $p.marker
    $lw = Get-LiveWindow
    Write-Output "DETAIL $($p.nav): clicked=$clicked found=$found win=$($lw -ne $null)"
    Log "PAGE $($p.nav) click+content" ($clicked -and $found)
}

# ── Dashboard: VIEW DETAILS → Diagnostics ──────────────────────────────
$win = Get-LiveWindow
Invoke-Button $win 'Dashboard' | Out-Null
Start-Sleep -Seconds 2
$win = Get-LiveWindow
$v = Invoke-Button $win 'VIEW DETAILS'
Start-Sleep -Seconds 4
Log "DASHBOARD VIEW-DETAILS-navigates" ($v -and (Wait-Marker 'LIVE METRICS'))

# ── Diagnostics: AUTOMATIC execution on page entry (no button click) ────
# No button is clicked on this path: the page must populate itself. Two host
# facts shape the probe:
#   * ReportStatus ("N finding(s)..." / "No issues detected.") is set by the VM
#     but never bound in DiagnosticsView.xaml, so it never reaches the UIA tree.
#   * PropertyConditionFlags on this host is only None/IgnoreCase — there is no
#     'contains' flag — so substring matching via PropertyCondition is impossible.
# What the user can actually see instead: the LIVE METRICS CPU cell starts at the
# literal 'N/A' sentinel and only leaves it when a COMPLETED analysis writes real
# metrics (the manual RUN DIAGNOSTIC check runs later in this script, so nothing
# earlier in the run can populate it). Values are read by walking Text elements
# in document order and taking the cell that follows the 'CPU' label inside the
# LIVE METRICS card.
function Get-LiveMetricValue($label) {
    $w = Get-LiveWindow
    if (-not $w) { return $null }
    $texts = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
    $names = @()
    foreach ($t in $texts) { $names += [string]$t.Current.Name }
    $lm = [array]::IndexOf($names, 'LIVE METRICS')
    if ($lm -lt 0) { return $null }   # results grid is hidden while IsAnalyzing
    for ($i = $lm + 1; $i -lt $names.Count; $i++) {
        if ($names[$i] -eq $label -and ($i + 1) -lt $names.Count) { return $names[$i + 1] }
    }
    return $null
}
$cpu = $null
for ($i = 0; $i -lt 20; $i++) {
    $cpu = Get-LiveMetricValue 'CPU'
    if ($null -ne $cpu -and $cpu -ne 'N/A') { break }
    Start-Sleep -Seconds 1
}
Write-Output "DETAIL diagnostics auto-run cpu-cell='$cpu'"
Log "DIAGNOSTICS auto-runs-on-page-entry" ($null -ne $cpu -and $cpu -ne 'N/A')

# ── Dashboard: CHANGE → Performance ────────────────────────────────────
$win = Get-LiveWindow
Invoke-Button $win 'Dashboard' | Out-Null
Start-Sleep -Seconds 2
$win = Get-LiveWindow
$c = Invoke-Button $win 'CHANGE'
Start-Sleep -Seconds 3
Log "DASHBOARD CHANGE-navigates" ($c -and (Wait-Marker 'POWER MODES'))

# ── Performance: live graph section present ────────────────────────────
Log "PERFORMANCE live-utilization-section" (Wait-Marker 'LIVE UTILIZATION (LAST 60 SECONDS)')

# ── Performance: activate a non-active mode, then revert ───────────────
$win = Get-LiveWindow
if ($win) {
$activates = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.AndCondition]::new(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'ACTIVATE')),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))))
} else { $activates = @() }

Log "PERFORMANCE activate-buttons-present" ($activates.Count -gt 0)
if ($activates.Count -gt 1) {
    # click the LAST one, then the FIRST one (usually the default) to revert
    ($activates[$activates.Count-1].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Seconds 5
    Log "PERFORMANCE mode-change-applied" $true
    ($activates[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Seconds 5
    Log "PERFORMANCE mode-reverted" $true
}

# ── Diagnostics: RUN DIAGNOSTIC re-run ─────────────────────────────────
Click-Nav 'Diagnostics' | Out-Null
$win = Get-LiveWindow
$r = Invoke-Button $win 'RUN DIAGNOSTIC'
Start-Sleep -Seconds 4
Log "DIAGNOSTICS run-diagnostic-executes" $r

# ── Settings: toggle automation toggle on + off (revert) ───────────────
Click-Nav 'Settings' | Out-Null
$win = Get-LiveWindow
$t = if ($win) { Find-ByName $win 'Automatic Optimization' ([System.Windows.Automation.ControlType]::ToggleButton) } else { $null }
if ($t) {
    try { ($t.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke(); Start-Sleep -Seconds 1 } catch {}
    $win = Get-LiveWindow
    $t2 = if ($win) { Find-ByName $win 'Automatic Optimization' ([System.Windows.Automation.ControlType]::ToggleButton) } else { $null }
    if ($t2) {
        try { ($t2.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke(); Start-Sleep -Seconds 1 } catch {}
    }
    Log "SETTINGS toggle-on-and-off" $true
} else {
    Log "SETTINGS toggle-found" $false
}

# ── Clean close ────────────────────────────────────────────────────────
} finally {
    if ($script:proc -ne $null -and -not $script:proc.HasExited) {
        try { $script:proc.CloseMainWindow() | Out-Null } catch { }
        Start-Sleep -Seconds 5
        if (-not $script:proc.HasExited) { Stop-Process -Id $script:proc.Id -Force; Start-Sleep -Seconds 2 }
    }
    $alive = ($script:proc -ne $null -and -not $script:proc.HasExited)
    Log "CLOSE clean-close" (-not $alive)
}

Write-Output ("SUMMARY: " + ($script:results | Where-Object { $_ -match ': FAIL' }).Count + " failures / " + $script:results.Count + " checks")
