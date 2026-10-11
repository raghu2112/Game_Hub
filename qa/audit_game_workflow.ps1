# GameWorkflowAudit.ps1 — Post-Round-5 user-experience audit
# Covers: real .exe Add-Game via the actual file picker (spaces in path,
# non-system drive), derived name, save, card appears, per-game honest stale
# state, EDIT/REMOVE, and Sessions VIEW REPORT.
# Modal windows (owned dialogs) are located via Win32 EnumWindows and attached
# with UIA FromHandle — this host's root-scoped walk does not expose owned
# windows. The file-picker filename combo hosts no UIA Edit (Vista-style IFileDialog
# exposes only Pane elements here), so the path is typed via focused SendKeys.
param()

$ErrorActionPreference = 'Continue'
$exe = 'E:\Projects\Game Hub\build\release\DellGamingControlCenter.exe'
$testExe = 'E:\Projects\Game Hub\tmp\regress\game space test.exe'
$script:checks = 0
$script:fails = 0

function Log($name, $ok) {
    if ($null -eq $ok) { Write-Output "LIMIT   $name"; return }
    $script:checks++
    if ($ok) { Write-Output "PASS    $name" } else { $script:fails++; Write-Output "FAIL    $name" }
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; using System.Text; public static class WEc { public delegate bool CB(IntPtr h, IntPtr l); [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l); [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid); [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n); [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h); }'

function Get-TopWindowByTitle($procId, $title) {
    $script:foundHwnd = [IntPtr]::Zero
    $cb = [WEc+CB]{ param($h, $l) $p2 = 0; [void]([WEc]::GetWindowThreadProcessId($h, [ref]$p2)); if ($p2 -eq $procId -and [WEc]::IsWindowVisible($h)) { $sb = New-Object System.Text.StringBuilder 256; [void]([WEc]::GetWindowText($h, $sb, 256)); if ($sb.ToString() -eq $title) { $script:foundHwnd = $h } }; return $true }
    [void]([WEc]::EnumWindows($cb, [IntPtr]::Zero))
    if ($script:foundHwnd -ne [IntPtr]::Zero) { return [System.Windows.Automation.AutomationElement]::FromHandle($script:foundHwnd) }
    return $null
}

function Get-MainWindow($procId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Get-LiveWindow {
    $w = Get-MainWindow $script:proc.Id
    if (-not $w) {
        $h = $script:proc.MainWindowHandle
        if ($h -ne [IntPtr]::Zero) {
            Add-Type -Namespace Native -Name Win -MemberDefinition '[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);' -ErrorAction SilentlyContinue
            [Native.Win]::ShowWindow($h, 9) | Out-Null
            Start-Sleep -Milliseconds 700
            $w = Get-MainWindow $script:proc.Id
        }
    }
    return $w
}

function Find-In($el, $name, $type) {
    if (-not $el) { return $null }
    $conds = [System.Collections.Generic.List[System.Windows.Automation.Condition]]::new()
    $conds.Add([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name))
    if ($type) { $conds.Add([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)) }
    $cond = if ($conds.Count -eq 1) { $conds[0] } else { [System.Windows.Automation.AndCondition]::new($conds.ToArray()) }
    return $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Invoke-Button($w, $name) {
    $b = Find-In $w $name ([System.Windows.Automation.ControlType]::Button)
    if (-not $b) { return $false }
    try { ($b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke(); return $true } catch { return $false }
}

function Wait-For($text, $tries = 8) {
    for ($i = 0; $i -lt $tries; $i++) {
        Start-Sleep -Seconds 1
        $w = Get-LiveWindow
        if ($w -and (Find-In $w $text $null)) { return $true }
    }
    return $false
}

# Find a card's OWN remove button: walk UP from the card's name text until an
# ancestor subtree contains a 'Remove game' button. Without this scoping, a
# library with several cards would click some OTHER game's remove button (and
# the check would pass vacuously). Never walk up to the Window level — at that
# point the subtree contains every card's button.
function Remove-Card($w, $cardName) {
    if (-not $w) { return $false }
    $nameCond = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $cardName)
    $node = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
    if (-not $node) { return $false }
    $up = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $btnCond = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Remove game'),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))
    $cur = $node
    while ($cur) {
        $cur = $up.GetParent($cur)
        if ($null -eq $cur) { break }
        if ($cur.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) { break }
        try {
            $btn = $cur.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
            if ($btn) {
                try { ($btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke(); return $true } catch { return $false }
            }
        } catch { break }
    }
    return $false
}

function Confirm-RemoveDialog {
    Start-Sleep -Seconds 2
    $mb = Get-TopWindowByTitle $script:proc.Id 'Confirm Remove'
    if ($mb) {
        $yes = Find-In $mb 'Yes' ([System.Windows.Automation.ControlType]::Button)
        if ($yes) { try { [void]($yes.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()) } catch {} }
        Start-Sleep -Seconds 3
        return $true
    }
    return $false
}

# ── launch ──────────────────────────────────────────────────────────────
# Idempotent re-runs: the REMOVE step deletes the backing test exe, so recreate
# it before every run — otherwise the app would (correctly) reject the add.
if (-not (Test-Path -LiteralPath $testExe)) {
    $dir = Split-Path -Parent $testExe
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllBytes($testExe, [byte[]](0x4D,0x5A,0x90,0x00,0x03,0x00,0x00,0x00,0x04,0x00,0x00,0x00))
}
Get-Process DellGamingControlCenter -ErrorAction SilentlyContinue | Stop-Process -Force
$script:proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 14
$win = Get-LiveWindow
Log 'AUDIT window-appears' ($win -ne $null)
if (-not $win) { Stop-Process -Id $script:proc.Id -Force; exit 1 }

try {

# ── navigate to Game Center ────────────────────────────────────────────
[void](Invoke-Button $win 'Game Center')
Start-Sleep -Seconds 2
$win = Get-LiveWindow
Log 'GAMECENTER nav-opens' ($null -ne (Find-In $win 'ADD GAME' ([System.Windows.Automation.ControlType]::Button)))

# Pre-clean: a leftover test card from an interrupted earlier run would make the
# duplicate check reject the SAVE below — remove it through the app's OWN remove
# flow (config.json is never edited by this script).
$win = Get-LiveWindow
if (Find-In $win 'game space test' $null) {
    $preRemoved = Remove-Card $win 'game space test'
    if ($preRemoved) { [void](Confirm-RemoveDialog) }
    Log 'PRECLEAN leftover-test-card-removed' (-not (Find-In (Get-LiveWindow) 'game space test' $null))
}

# ── ADD GAME → editor window (modal; located via EnumWindows) ──────────
$editorOpened = Invoke-Button $win 'ADD GAME'
Start-Sleep -Seconds 3
$ed = Get-TopWindowByTitle $script:proc.Id 'Add Game'
Log 'EDITOR opens-from-ADD-GAME' ($editorOpened -and ($ed -ne $null))

if ($ed) {
    # BROWSE… → modal OpenFileDialog 'Select Game Executable'
    # NOTE: build the ellipsis from [char]0x2026 instead of a literal — this
    # script is read as ANSI by Windows PowerShell 5.1 when it has no BOM, and a
    # literal … would not match the button's UIA name.
    $browsed = Invoke-Button $ed ('BROWSE' + [char]0x2026)
    # The picker can take several seconds to appear on this low-powered host.
    $dlg = $null
    for ($i = 0; $i -lt 10 -and -not $dlg; $i++) { Start-Sleep -Seconds 1; $dlg = Get-TopWindowByTitle $script:proc.Id 'Select Game Executable' }
    Log 'PICKER opens' ($browsed -and ($dlg -ne $null))

    $pickerConfirmed = $false
    if ($dlg) {
        # Host-verified structure (UIA RawView dump of the Vista-style IFileDialog):
        #   Edit  AutomationId='1148' Name='File name:'  <- the filename field (1090 is
        #         only the static 'File name:' label on this host — targeting it made
        #         SetFocus/SendKeys type into nothing)
        #   Button AutomationId='1' Name='Open'
        $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
        $stack = New-Object System.Collections.Stack
        $stack.Push($dlg)
        $nameEdit = $null; $openBtn = $null
        while ($stack.Count -gt 0 -and -not ($nameEdit -and $openBtn)) {
            $e = $stack.Pop()
            if (-not $nameEdit -and $e.Current.AutomationId -eq '1148' -and
                $e.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit) {
                $nameEdit = $e
            }
            if (-not $openBtn -and $e.Current.AutomationId -eq '1' -and $e.Current.Name -eq 'Open') {
                $openBtn = $e
            }
            $c = $walker.GetFirstChild($e)
            while ($c -ne $null) { $stack.Push($c); $c = $walker.GetNextSibling($c) }
        }
        if ($nameEdit -and $openBtn) {
            try {
                $typed = $false
                $vp = $null
                if ($nameEdit.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) {
                    try { $vp.SetValue($testExe); $typed = $true } catch { $typed = $false }
                }
                if (-not $typed) {
                    $nameEdit.SetFocus()
                    Start-Sleep -Milliseconds 600
                    [System.Windows.Forms.SendKeys]::SendWait($testExe)
                    Start-Sleep -Milliseconds 1000
                    $typed = $true
                }
                ($openBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
                Start-Sleep -Seconds 3
                # The dialog must actually close on confirm — an invalid path would leave it open.
                $pickerConfirmed = -not (Get-TopWindowByTitle $script:proc.Id 'Select Game Executable')
            } catch { $pickerConfirmed = $false }
        }
    }
    Log 'PICKER path-typed-and-confirmed' ($browsed -and $pickerConfirmed)
    if (-not ($browsed -and $pickerConfirmed)) {
        Log 'PICKER host-cannot-drive-file-dialog' $null
        $ed2 = Get-TopWindowByTitle $script:proc.Id 'Add Game'
        if ($ed2) { [void](Invoke-Button $ed2 'CANCEL') }
        Start-Sleep -Seconds 2
    }

    # Editor after successful pick: name derived, then SAVE
    $ed = Get-TopWindowByTitle $script:proc.Id 'Add Game'
    if ($ed -and $pickerConfirmed) {
        # The editor is an OWNED MODAL window — NOT inside the main window's UIA
        # subtree — so searching the main window here always fails. Read the
        # derived name from the Edit's ValuePattern instead (a TextBox's UIA Name
        # is not its text content).
        $nameShown = $false
        for ($i = 0; $i -lt 4 -and -not $nameShown; $i++) {
            Start-Sleep -Seconds 1
            $edNow = Get-TopWindowByTitle $script:proc.Id 'Add Game'
            if ($edNow) {
                $w2 = [System.Windows.Automation.TreeWalker]::RawViewWalker
                $stk = New-Object System.Collections.Stack
                $stk.Push($edNow)
                while ($stk.Count -gt 0 -and -not $nameShown) {
                    $n2 = $stk.Pop()
                    if ($n2.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit) {
                        $vp2 = $null
                        if ($n2.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp2)) {
                            if ($vp2.Current.Value -eq 'game space test') { $nameShown = $true }
                        }
                    }
                    $cc = $w2.GetFirstChild($n2)
                    while ($cc -ne $null) { $stk.Push($cc); $cc = $w2.GetNextSibling($cc) }
                }
            }
        }
        Log 'EDITOR name-derived-from-metadata' ($nameShown)
        $saved = Invoke-Button $ed 'SAVE'
        Start-Sleep -Seconds 3
        Log 'EDITOR save-closes-editor' ($saved -and -not (Get-TopWindowByTitle $script:proc.Id 'Add Game'))
        # The card's name text is exactly 'game space test'; the path text is the
        # full path (there is no element named just 'game space test.exe').
        Log 'GAMECENTER card-appears-with-space-path' (Wait-For 'game space test' 6)
    }
}

# ── EDIT honest missing-state + REMOVE (only if card exists) ───────────
$win = Get-LiveWindow
if (Find-In $win 'game space test' $null) {
    # Count EXECUTABLE MISSING badges BEFORE deleting the exe: other library
    # cards may already be missing one, so a bare 'badge exists' check would
    # pass vacuously — the delta proves THIS card's honest state.
    $badgeBefore = 0
    if ($win) {
        $bc = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'EXECUTABLE MISSING')
        $badgeBefore = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc).Count
    }
    # Delete the backing exe, force page refresh: card must show EXECUTABLE MISSING.
    Remove-Item -LiteralPath $testExe -Force -ErrorAction SilentlyContinue
    [void](Invoke-Button $win 'Game Center'); Start-Sleep -Seconds 2
    $badgeAfter = 0
    for ($i = 0; $i -lt 6 -and $badgeAfter -le $badgeBefore; $i++) {
        Start-Sleep -Seconds 1
        $win = Get-LiveWindow
        if ($win) {
            $bc = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'EXECUTABLE MISSING')
            $badgeAfter = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc).Count
        }
    }
    Log 'GAMECENTER missing-exe-honest-state' ($badgeAfter -gt $badgeBefore)

    # REMOVE: the Remove button scoped to THIS card (every card has its own;
    # its UIA name is the explicit accessible name "Remove game" — the emoji
    # glyph is content, not an accessible name).
    $win = Get-LiveWindow
    $removed = Remove-Card $win 'game space test'
    if ($removed) {
        [void](Confirm-RemoveDialog)
        $w2 = Get-LiveWindow
        Log 'GAMECENTER remove-clears-card' (-not (Find-In $w2 'game space test' $null))
    } else {
        Log 'GAMECENTER remove-clears-card' $false
    }
} else {
    Log 'GAMECENTER card-side-skipped-picker-unavailable' $null
}

# ── Sessions VIEW REPORT (real window opens) ───────────────────────────
$win = Get-LiveWindow
[void](Invoke-Button $win 'Game Center'); Start-Sleep -Seconds 2
$win = Get-LiveWindow
[void](Invoke-Button $win 'Sessions'); Start-Sleep -Seconds 3
$win = Get-LiveWindow
$m = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'VIEW REPORT')))
if ($m) {
    [void]($m.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke())
    Start-Sleep -Seconds 4
    $reportWin = Get-TopWindowByTitle $script:proc.Id 'Session Report'
    Log 'SESSIONS view-report-opens-real-window' ($reportWin -ne $null)
    if ($reportWin) {
        $close = Find-In $reportWin 'Close' ([System.Windows.Automation.ControlType]::Button)
        if ($close) { [void]($close.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()) }
    }
} else {
    Log 'SESSIONS view-report-button-absent-empty-history' $null   # honest empty state
}

} finally {
    $script:proc | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Output "SUMMARY: $script:fails failures / $script:checks checks (+ recorded limitations)"
}
