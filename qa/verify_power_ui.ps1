# qa/verify_power_ui.ps1 — Round 7 §3: drive the REAL app's Performance page via UIA
# and independently read the active power scheme with powercfg before/after each click.
# A mode click that returns no scheme change (or a scheme that is not the intended GUID)
# is reported as a failure — a UI label alone is never evidence.
param(
    [string]$Exe = "build\release\DellGamingControlCenter.exe"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

function Log($m, $ok) {
    $v = if ($ok -eq $true) { "PASS" } elseif ($ok -eq $false) { "FAIL" } else { "INFO" }
    Write-Output ("[{0,-4}] {1}" -f $v, $m)
}

function Active-Scheme {
    $o = powercfg /getactivescheme
    if ($o -match 'GUID:\s*([0-9a-fA-F-]{36})') { return $Matches[1].ToLower() } else { return "UNREADABLE" }
}

$BALANCED = "381b4222-f694-41f0-9685-ff5bb260df2e"
$POWERSAVER = "a1841308-3541-4fab-bc81-f71556f20b4a"
$HIGHPERF = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"

$failures = 0
$original = Active-Scheme
Log "pre-state: active scheme = $original"
Log "pre-state: scheme count = $((powercfg /list | Select-String 'Power Scheme GUID').Count)"

# A forgotten instance from a previous run blocks launch (single-instance guard) and
# corrupts the shared %LocalAppData% log. Kill any pre-existing instances first.
Get-Process DellGamingControlCenter -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

# Launch the real published EXE
$proc = Start-Process $Exe -PassThru
$root = [System.Windows.Automation.AutomationElement]::RootElement
$procId = $proc.Id

function Get-Windows($procId) {
    $r = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)
    return $r.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Get-MainWindow($procId) {
    foreach ($w in (Get-Windows $procId)) {
        if ($w.Current.Name -like '*GamerHub*') { return $w }
    }
    return $null
}

# The window hides itself on minimize (tray behavior) and vanishes from the UIA tree.
# Re-acquire — and restore via Win32 if hidden — before each step.
function Get-LiveWindow {
    $win = Get-MainWindow $proc.Id
    if (-not $win) {
        $h = $proc.MainWindowHandle
        if ($h -ne [IntPtr]::Zero) {
            Add-Type -Namespace Native -Name Win -MemberDefinition '
                [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
                [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);' -ErrorAction SilentlyContinue
            [Native.Win]::ShowWindow($h, 9) | Out-Null
            [Native.Win]::SetForegroundWindow($h) | Out-Null
            Start-Sleep -Milliseconds 800
            $win = Get-MainWindow $proc.Id
        }
    }
    return $win
}

$deadline = (Get-Date).AddSeconds(30)
$win = $null
while ((Get-Date) -lt $deadline) {
    $win = Get-MainWindow $proc.Id
    if ($win) { break }
    Start-Sleep -Milliseconds 500
}
if (-not $win) { Log "app window did not appear" $false; exit 1 }
Start-Sleep -Seconds 3
Log "app window appeared (pid $procId)"

function Find-ByName($parent, $name, $type) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    if ($type) {
        $cond2 = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)
        $cond = New-Object System.Windows.Automation.AndCondition($cond, $cond2)
    }
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Find-AllByName($parent, $name, $type) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    if ($type) {
        $cond2 = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)
        $cond = New-Object System.Windows.Automation.AndCondition($cond, $cond2)
    }
    return $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

# Navigate: Performance primary nav (Button control type, per audit_functional.ps1 pattern)
$win = Get-LiveWindow
$nav = Find-ByName $win "Performance" ([System.Windows.Automation.ControlType]::Button)
if ($nav) { ($nav.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke(); Start-Sleep -Seconds 3; Log "nav Performance clicked" }
else { Log "nav Performance not found" $false; $failures++ }

# Each mode card contains an identical ACTIVATE button, and the top nav also has a
# 'GAMING' button — so name-matching a mode title is ambiguous. Collect the ACTIVATE
# buttons in UIA tree order instead: the ItemsControl renders Modes in declaration
# order (Quiet, BatterySaver, Balanced, Gaming, MaximumPerformance).
$modeOrder = @("Quiet", "BatterySaver", "Balanced", "Gaming", "MaximumPerformance")

function Get-ActivateButtons($w) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, "ACTIVATE")
    $typeCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $and = New-Object System.Windows.Automation.AndCondition($cond, $typeCond)
    return $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $and)
}

function Find-ActivateForMode($w, $modeTitle) {
    $idx = [array]::IndexOf($modeOrder, $modeTitle)
    if ($idx -lt 0) { return $null }
    $btns = Get-ActivateButtons $w
    if ($btns.Count -le $idx) { return $null }
    return $btns[$idx]
}

# Collect activate buttons (mode cards). They are named by title + description.
$titles = @("BatterySaver", "Balanced", "Gaming", "MaximumPerformance")
$expected = @{
    "Balanced"            = $BALANCED
    "BatterySaver"       = $POWERSAVER
    "Gaming"              = $HIGHPERF
    "MaximumPerformance"  = $HIGHPERF
}

foreach ($t in $titles) {
    if (-not $expected.ContainsKey($t)) { continue }
    $win = Get-LiveWindow
    if (-not $win) { Log "window lost before '$t'" $false; $failures++; continue }
    $before = Active-Scheme
    # Activate the card whose title matches this mode.
    $btn = Find-ActivateForMode $win $t
    if (-not $btn) { Log "activate button for '$t' not found" $false; $failures++; continue }

    # If this mode is already active its ACTIVATE is correctly disabled (IsEnabled bound
    # to !IsActive) — invoking a disabled button throws. Skipping is the correct behavior.
    $want = $expected[$t]
    if ($before -eq $want) {
        Log "skip '$t': already active (scheme $after)" $null
        continue
    }

    try { ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke() }
    catch { Log "click '$t' threw: $($_.Exception.Message)" $false; $failures++; continue }
    Start-Sleep -Seconds 4

    $after = Active-Scheme
    $ok = ($after -eq $want)
    if (-not $ok) { $failures++ }
    Log "click '$t': scheme $before -> $after (want $want)" $ok
}

# Restore original scheme through the app by clicking the card matching it, else via powercfg fallback.
$restoredByApp = $false
if ($original -eq $BALANCED) { $t = "Balanced" } elseif ($original -eq $POWERSAVER) { $t = "BatterySaver" } else { $t = $null }
if ($t) {
    $win = Get-LiveWindow
    $btn = Find-ActivateForMode $win $t
    if ($btn) {
        ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
        Start-Sleep -Seconds 4
        $restoredByApp = ((Active-Scheme) -eq $original)
    }
}
if (-not $restoredByApp) {
    powercfg /setactive $original | Out-Null
    Start-Sleep -Seconds 1
}
$final = Active-Scheme
if ($final -ne $original) { $failures++ }
Log "final-restore: active = $final (want $original)" ($final -eq $original)
Log "final scheme count = $((powercfg /list | Select-String 'Power Scheme GUID').Count)"

# Clean close
try { $proc.CloseMainWindow() | Out-Null } catch {}
Start-Sleep -Seconds 4
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
Log "closed: exited=$($proc.HasExited)"

Write-Output ("RESULT: " + $(if ($failures -eq 0) { "ALL PASS" } else { "$failures FAILURE(S)" }))
exit $(if ($failures -eq 0) { 0 } else { 1 })
