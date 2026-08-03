<#
.SYNOPSIS
    The interaction loop: what a screenshot cannot see. Drives the real UI through UI Automation and
    asserts a pass/fail — keyboard input into a WPF window hosted the way the tray hosts it, and the
    tray menu's entries as they are when the menu actually opens.

.DESCRIPTION
    Every other verification loop in this repo is a *picture* (`Capture-Window.ps1`, `--capture-settings`,
    `--capture-stats`, the `preview-ui` skill). Pictures prove layout. They cannot prove a key press
    arrives: the WPF windows accepted no keyboard input at all from the day the first one shipped
    (T135) while every screenshot ever taken of them looked perfect, because mouse input travels
    `WndProc` and keyboard input travels `ComponentDispatcher`. This script closes the checking gap.

    Three cases, runnable separately:

      -Case Keyboard   Launch `--settings-tray` (WinForms pump — the only preview that can see a
                       keyboard bug), navigate to a page, type into a TextBox and read the value back
                       through ValuePattern, Tab out of it, and drive a Slider with an arrow key.
      -Case Panes      Launch `--main` and assert the report can be READ: the three tab headers, and
                       the selected pane's body — a used %, a reset caption, a live headline — inside
                       the accessibility tree. Needs no second profile, which is the whole point
                       (T176): behind `-Case Profiles`' two-profile skip this never ran on a
                       one-profile machine, and it is the only thing that would notice
                       `PART_SelectedContentHost` going missing again.
      -Case Profiles   Launch `--main`, walk the Statistics page's profile picker 0 -> 1 -> 0 through
                       the real ComboBox and read the report back at each stop: the used %, the reset
                       caption and the live headline. Nothing else in this repo *drives* the picker —
                       every capture renders one profile, which is structurally incapable of seeing
                       T164's three defects (all need a second switch; two need it to go back). Also
                       T166's timing: on the way back, the status line must never be observed at all.
      -Case Menu       Launch the real tray, open the notification-area icon's menu, read its entries,
                       and expand "Open Claude Code" to read the per-profile entries (this is what
                       verified T137).
      -Case Names      Launch `--main` and read back what the controls ANNOUNCE: the Statistics
                       picker, the method-note button, and the settings rows whose label is a
                       neighbouring element rather than their own content (T175). A picture cannot
                       see an accessible name, and an unnamed control is invisible to every other
                       check in this file.

    Exit code is 0 only if every check passed; any failure exits 1. A check that could not observe
    anything is a FAIL, never a pass — see "fail loudly" below.

.NOTES
    Five traps, all met while writing these checks by hand, all encoded here:

    1. The notification-area icon has NO CLICKABLE POINT — `GetClickablePoint()` throws
       `NoClickablePointException`. Use `Current.BoundingRectangle`. It may also sit inside the
       overflow flyout, which has to be opened before the icon exists in the tree at all.
       Worse on Windows 11's XAML taskbar: a *synthesised right-click* on the icon does not open the
       menu at all (observed on 10.0.26200). The route that works is UI Automation `SetFocus()` on the
       icon plus the Application key (VK_APPS) — the keyboard path a user has via Win+B. The
       right-click is kept as a fallback for shells where it does work.
    2. A COLLAPSED WPF PANE IS NOT IN THE UIA TREE. A control on a settings page that is not showing
       cannot be found by any id — navigate first. The sidebar items are bare `Border`s with no
       automation peer, so they are matched by the `TextBlock` inside them and disambiguated by
       x-position (the page title carries the same words).
    3. THE MENU DOES NOT ALWAYS OPEN on the first try. This script retries — and when it read nothing
       it FAILS. The first version of this check reported a pass having read zero menu entries, which
       is strictly worse than having no check at all.
    4. A PROFILE SWITCH LEGITIMATELY EMPTIES THE PANES for the length of a transcript scan (T164 clears
       the last-rendered pace on the way out, and T118's keep-it-up rule is about a refresh of the
       *same* profile). So a reading taken right after a switch is the "computing…" line, or worse the
       *previous* profile's numbers still on screen — the Profiles case waits that out before it reads.
    5. "READ NOTHING" MUST NOT BE SAID OF A WINDOW THAT WAS TALKING. The first version of the timed-out
       read reported *"no panes and no status line after 25s"* while the status line had been up for the
       whole 25 seconds saying "Computing your consumption pace…", and the real fault was elsewhere
       entirely (the missing `PART_SelectedContentHost`) — so the message pointed at timing and cost a
       throwaway tree-dumping script to get past. `Read-ProfileStop` now separates *working* (the line
       was up and nothing finished) from *blank* (nothing was ever in the tree), reports what it last
       saw and on how many polls, and prints the control view itself (T178).

    A menu item is never Invoked: invoking "Open Claude Code" would launch a terminal. Submenus are
    opened the way a keyboard user opens them (Down to the item, Right to expand), and read.

.PARAMETER Case
    Keyboard, Panes, Profiles, Menu, Names, or All (default).

.PARAMETER Exe
    The build under test. Defaults to the Debug build.

.PARAMETER Lang
    Display language forced for the run (`--lang`), so the labels this script matches are fixed
    regardless of the machine's saved preference. Default `en`.

.PARAMETER UseRunning
    Menu case only: drive the tray that is ALREADY running instead of launching `-Exe`. Convenient on
    a dev machine (the single-instance mutex makes a second launch exit silently), but it checks
    whatever binary is running, not the one you just built — so the script prints that path loudly.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1
    powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Keyboard
    powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Panes
    powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Profiles
    powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Names
    powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Menu -UseRunning
#>
param(
    [ValidateSet('All', 'Keyboard', 'Panes', 'Profiles', 'Menu', 'Names')]
    [string]$Case = 'All',
    [string]$Exe = "bin\Debug\net10.0-windows\win-x64\ClaudeTray.exe",
    [string]$Lang = "en",
    [switch]$UseRunning
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Native {
    // Per-monitor-v2, so BoundingRectangle (physical pixels) and SetCursorPos agree. Without it every
    // synthesised click lands somewhere else on a 150-200% display.
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public static void Dpi() {
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch {}
        try { SetProcessDPIAware(); } catch {}
    }
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, IntPtr e);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    // A background process cannot steal the foreground, so SetForegroundWindow silently does nothing
    // and every synthesised click lands on whatever is covering the window. HWND_TOPMOST puts it on
    // top regardless; UIA's SetFocus (called by the caller) then activates it.
    public static void Topmost(IntPtr h) { SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040); }

    public static void Click(int x, int y, bool right) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(150);
        mouse_event(0x0001, 0, 0, 0, IntPtr.Zero);          // MOVE, so hover state settles first
        System.Threading.Thread.Sleep(150);
        mouse_event(right ? 0x0008u : 0x0002u, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        mouse_event(right ? 0x0010u : 0x0004u, 0, 0, 0, IntPtr.Zero);
    }
    public static void Key(byte vk) {
        keybd_event(vk, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        keybd_event(vk, 0, 2, IntPtr.Zero);
        System.Threading.Thread.Sleep(140);
    }
}
"@
[Native]::Dpi()

$VK_APPS = 0x5D; $VK_DOWN = 0x28; $VK_RIGHT = 0x27; $VK_LEFT = 0x25; $VK_ESC = 0x1B; $VK_UP = 0x26
$AE   = [System.Windows.Automation.AutomationElement]
$ANY  = [System.Windows.Automation.Condition]::TrueCondition
$ROOT = $AE::RootElement

$script:Failures = 0
function Pass($msg) { Write-Host "[PASS] $msg" -ForegroundColor Green }
function Fail($msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; $script:Failures++ }
function Info($msg) { Write-Host "       $msg" -ForegroundColor DarkGray }
function Head($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# ---------------------------------------------------------------- UIA helpers

function ById($root, $id, $timeoutMs = 4000) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    do {
        $el = $root.FindFirst('Descendants', $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    return $null
}

<# One FindFirst and no retry - for a loop that does its own waiting and must not have the helper's
   200ms retry sleep folded into every miss. Used by the timing observation, where the granularity of
   the poll IS the resolution of the answer. #>
function ByIdNow($root, $id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $root.FindFirst('Descendants', $cond)
}

<# By UIA Name rather than AutomationId - for controls whose identity is their label (the nav strip's
   destinations are RadioButtons whose Name is the localized text). #>
function ByName($root, $name, $timeoutMs = 4000) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    do {
        $el = $root.FindFirst('Descendants', $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    return $null
}

function WindowOfProcess($processId, $timeoutMs = 15000) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    do {
        $el = $ROOT.FindFirst('Children', $cond)
        if ($el) { return $el }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function ClickCentre($element, [switch]$Right) {
    $r = $element.Current.BoundingRectangle
    if ($r.Width -le 0 -or $r.Height -le 0) { throw "element has an empty bounding rectangle" }
    [Native]::Click([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2), $Right.IsPresent)
}

function Start-App($appArgs) {
    if (-not (Test-Path $Exe)) { throw "$Exe not found - run: dotnet build -c Debug" }
    Start-Process -FilePath $Exe -ArgumentList $appArgs -PassThru
}

<#
  The label a control actually carries, read from the same lang\<code>.json the app reads, so the
  checks are not pinned to English and cannot drift from the shipped strings.
#>
$script:LangText = @{}
function Label($key) {
    # Read by regex, not ConvertFrom-Json: the lang files carry `//` section comments, which are not
    # JSON. Falls back to en.json for a missing key, exactly as the app does.
    foreach ($code in @($Lang, 'en')) {
        if (-not $script:LangText.ContainsKey($code)) {
            $file = Join-Path (Split-Path -Parent $PSScriptRoot) "lang\$code.json"
            if (-not (Test-Path $file)) { throw "no language file for '$code' ($file)" }
            $script:LangText[$code] = Get-Content -LiteralPath $file -Encoding UTF8 -Raw
        }
        $pattern = '"' + [regex]::Escape($key) + '"\s*:\s*"((?:[^"\\]|\\.)*)"'
        if ($script:LangText[$code] -match $pattern) {
            return ($Matches[1] -replace '\\"', '"' -replace '\\\\', '\')
        }
    }
    throw "no lang file defines '$key'"
}

# ---------------------------------------------------------------- keyboard case

<#
  Typing, Tab and an arrow key, under the tray's own hosting. `--settings` would prove nothing: it
  runs a WPF pump, a different input environment from the WinForms one the tray runs (T135).
#>
function Invoke-KeyboardCase {
    Head "Keyboard - the WPF window under the tray's WinForms pump (--settings-tray)"

    $proc = Start-App "--lang $Lang --settings-tray"
    try {
        $win = WindowOfProcess $proc.Id
        if (-not $win) { Fail "the Settings window never appeared"; return }
        Start-Sleep -Milliseconds 900   # let WPF finish its first layout pass
        [Native]::Topmost([IntPtr]$win.Current.NativeWindowHandle)
        try { $win.SetFocus() } catch { Info "window SetFocus threw - $($_.Exception.Message)" }
        Start-Sleep -Milliseconds 500

        # Trap 2, stated as an observation rather than an assertion: the window opens on General, so
        # the Claude Code pane is collapsed and none of its controls exist in the tree yet.
        if (ById $win 'DirectoryBox' 600) {
            Info "note: the Claude Code pane's controls are reachable without navigating"
        } else {
            Info "collapsed pane is absent from the UIA tree (expected) - navigating to it"
        }

        # The sidebar items are Borders with no automation peer: match the TextBlock inside, and take
        # the leftmost, because the page title carries the same words.
        $textCond = New-Object System.Windows.Automation.PropertyCondition(
            $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
        $navLabel = Label 'settings.nav.claudeCode'
        $nav = @($win.FindAll('Descendants', $textCond) |
                 Where-Object { $_.Current.Name -eq $navLabel } |
                 Sort-Object { $_.Current.BoundingRectangle.X })
        if ($nav.Count -eq 0) { Fail "no sidebar item named '$navLabel' (is -Lang $Lang right?)"; return }
        ClickCentre $nav[0]
        Start-Sleep -Milliseconds 700

        $box = ById $win 'DirectoryBox'
        if (-not $box) { Fail "DirectoryBox not found after navigating to the Claude Code page"; return }
        Pass "navigated to the Claude Code page by clicking the sidebar"

        # --- typing reaches the control
        $vp = [System.Windows.Automation.ValuePattern]::Pattern
        $wasText = [string]$box.GetCurrentPattern($vp).Current.Value
        $marker = "T142check"
        $box.SetFocus()
        Start-Sleep -Milliseconds 250
        [System.Windows.Forms.SendKeys]::SendWait("^a")
        [System.Windows.Forms.SendKeys]::SendWait($marker)
        Start-Sleep -Milliseconds 400
        $after = [string]$box.GetCurrentPattern($vp).Current.Value
        if ($after -eq $marker) {
            Pass "typing reaches the WPF TextBox (DirectoryBox = '$after')"
        } else {
            Fail "typing did NOT reach the WPF TextBox - was '$wasText', now '$after', expected '$marker'"
        }

        # --- Tab moves focus off it
        [System.Windows.Forms.SendKeys]::SendWait("{TAB}")
        Start-Sleep -Milliseconds 400
        $focused = $AE::FocusedElement
        $focusedId = if ($focused) { $focused.Current.AutomationId } else { "<none>" }
        if ($focused -and $focused.Current.ProcessId -eq $proc.Id -and $focusedId -ne 'DirectoryBox') {
            Pass "Tab moves focus (DirectoryBox -> '$focusedId')"
        } else {
            Fail "Tab did not move focus - it is still on '$focusedId'"
        }

        # --- an arrow key drives a Slider (dead too, under the same bug)
        $slider = ById $win 'RetrySlider'
        if (-not $slider) {
            Fail "RetrySlider not found on the Claude Code page"
        } else {
            # Note the [double] casts: a pattern's `.Current` is a LIVE view, not a snapshot, so
            # holding on to it and comparing `before.Value` to `after.Value` compares the reading with
            # itself and can never fail. Copy the numbers out first.
            $rvp = [System.Windows.Automation.RangeValuePattern]::Pattern
            $before = [double]$slider.GetCurrentPattern($rvp).Current.Value
            $max    = [double]$slider.GetCurrentPattern($rvp).Current.Maximum
            $min    = [double]$slider.GetCurrentPattern($rvp).Current.Minimum
            $slider.SetFocus()
            Start-Sleep -Milliseconds 300
            $focusedId = $AE::FocusedElement.Current.AutomationId
            if ($focusedId -ne 'RetrySlider') { Info "focus is on '$focusedId', not the slider" }
            # At the maximum, Right is a legitimate no-op - press the other way instead.
            [System.Windows.Forms.SendKeys]::SendWait($(if ($before -ge $max) { "{LEFT}" } else { "{RIGHT}" }))
            Start-Sleep -Milliseconds 400
            $now = [double]$slider.GetCurrentPattern($rvp).Current.Value
            if ($now -ne $before) {
                Pass "an arrow key drives the Slider (RetrySlider $before -> $now)"
            } else {
                Fail "the arrow key did not move the Slider (RetrySlider stayed at $before of $min-$max)"
            }
        }

        # Esc is deliberately not checked: the Settings window binds nothing to it, so there would be
        # nothing to observe - and a check that cannot observe anything is the false green this
        # script exists to prevent. Add the check with the binding, not before it.
        Info "Esc: not checked - the Settings window binds no Esc action to observe"
    }
    finally {
        # WaitForExit, not just Kill: the menu case that may run next refuses to start while any
        # ClaudeTray process is alive, and a still-dying preview would look like the tray to it.
        if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) | Out-Null }
    }
}

# ---------------------------------------------------------------- profiles case

<# The label the picker is showing. A closed WPF ComboBox is a ContentPresenter over the selected item,
   so the visible text is the last resort rather than the first: ask the patterns, then read the box. #>
function Combo-SelectedText($combo) {
    try {
        $v = [string]$combo.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($v) { return $v }
    } catch { }
    try {
        $sel = @($combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection())
        if ($sel.Count -gt 0 -and $sel[0].Current.Name) { return [string]$sel[0].Current.Name }
    } catch { }
    $textCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $t = $combo.FindFirst('Descendants', $textCond)
    if ($t) { return [string]$t.Current.Name }
    return ""
}

<#
  T177: the status line, watched rather than sampled. T166's whole claim is a *timing* — the probe that
  measured it saw the line at 162 ms on a build without the per-profile cache, which is well inside the
  settling wait a switch already does — so a single look after the switch could miss it and report a
  pass. Both the settle and the read below record into these, and the caller clears them per stop.
#>
$script:StatusSeen = $false
$script:StatusSeenText = $null

<# Spend a wait watching the status line instead of sleeping through it. #>
function Watch-Status($win, $ms, $stepMs = 60) {
    $deadline = (Get-Date).AddMilliseconds($ms)
    do {
        if ($win) {
            $status = ByIdNow $win 'StatsStatusText'
            if ($status) {
                $txt = [string]$status.Current.Name
                if ($txt) { $script:StatusSeen = $true; $script:StatusSeenText = $txt }
            }
        }
        Start-Sleep -Milliseconds $stepMs
    } while ((Get-Date) -lt $deadline)
}

<#
  Select an index through the control itself, never through `SelectProfileForPreview` — the preview seam
  sets SelectedIndex directly, and it is the thing this case exists to stop trusting. Returns which route
  worked, so a pass says how the switch was made instead of implying the first one.

  `$win` is optional and only for the timing observation: given it, the post-selection settle is spent
  watching the status line rather than sleeping through the very interval it appears in.
#>
function Combo-Select($combo, [int]$index, $win = $null) {
    $ec = [System.Windows.Automation.ExpandCollapsePattern]::Pattern
    # WPF generates the item containers on the first drop-down, so before that there are no ListItems in
    # the tree to select at all.
    try { $combo.GetCurrentPattern($ec).Expand(); Start-Sleep -Milliseconds 500 } catch { }
    $itemCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $items = @($combo.FindAll('Descendants', $itemCond))
    if ($items.Count -gt $index) {
        try {
            $items[$index].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            if ($win) { Watch-Status $win 250 } else { Start-Sleep -Milliseconds 250 }
            try { $combo.GetCurrentPattern($ec).Collapse() } catch { }
            return "UIA"
        } catch { Info "SelectionItemPattern.Select threw - $($_.Exception.Message)" }
    }
    # Fallback, and a real user's route: a closed WPF ComboBox moves its selection with the arrow keys.
    # Kept because a dead UIA path here would report a red build for a bug in this script.
    try { $combo.GetCurrentPattern($ec).Collapse() } catch { }
    try { $combo.SetFocus() } catch { Info "combo SetFocus threw - $($_.Exception.Message)" }
    Start-Sleep -Milliseconds 250
    for ($i = 0; $i -lt ($items.Count + 4); $i++) { [Native]::Key($VK_UP) }
    for ($i = 0; $i -lt $index; $i++) { [Native]::Key($VK_DOWN) }
    return "keyboard"
}

<# "2h 00m" / "3d 4h" / "5m" as whole minutes. Dur() writes those unit letters itself, in every
   language, so this parse is not pinned to a locale. Null when there is no window to reset. #>
function Reset-Minutes($text) {
    if (-not $text) { return $null }
    $m = 0; $seen = $false
    if ($text -match '(\d+)\s*d') { $m += [int]$Matches[1] * 1440; $seen = $true }
    if ($text -match '(\d+)\s*h') { $m += [int]$Matches[1] * 60;   $seen = $true }
    if ($text -match '(\d+)\s*m') { $m += [int]$Matches[1];        $seen = $true }
    if ($seen) { return $m }
    return $null
}

<#
  What the window says about the profile now on screen, once it has settled. Four outcomes, and the
  last two are the point of T178:

    panes    a report - the pane body is in the tree and a used % came out of it
    status   a settled status line, e.g. a profile with no stored readings yet
    working  neither, but the window was TALKING the whole time: the status line said "Computing…"
             for the full timeout. A slow machine, a cold transcript cache - or a report that built
             and could not be read, which is what the missing PART_SelectedContentHost looked like.
    blank    neither, and nothing was ever in the tree to read. The window is not being read at all.

  It used to collapse the last two into one `nothing`, which the caller reported as "no panes and no
  status line after 25s" - and on the first real run of the Profiles case that sentence was false in
  the way that costs the most time: the line had been up for all 25 seconds saying "Computing your
  consumption pace…". So the read now carries what it last saw and how many of its polls saw it.

  The tab matters: a TabControl only realizes the selected tab, so the 5h pane's controls are absent
  while the weekly one is up. Whichever is there is read, and the suffix is returned - the default tab
  is picked once per window (`_defaultTabPicked`), so it must be the same at every stop.
#>
function Read-ProfileStop($win, $computing, $timeoutSec = 25) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    $polls = 0; $statusPolls = 0; $lastStatus = $null
    do {
        $polls++
        # The status line first, and recorded rather than acted on (T177): whether it was EVER up on the
        # way to the panes is the property, so it has to be sampled on every turn of this loop, not
        # looked at once the panes failed to appear.
        $status = ByIdNow $win 'StatsStatusText'
        $statusText = $null
        if ($status) {
            $statusText = [string]$status.Current.Name
            if ($statusText) {
                $script:StatusSeen = $true; $script:StatusSeenText = $statusText
                $statusPolls++; $lastStatus = $statusText
            }
        }

        foreach ($sfx in @('S', 'W')) {
            $used = ByIdNow $win "Used$sfx"
            if ($used -and [string]$used.Current.Name) {
                # Read, then build: Windows PowerShell has no `if` expression, and a null element here
                # must stay null rather than becoming an empty string a comparison would call equal.
                $reset = ById $win "Reset$sfx" 1000
                $live  = ById $win "LiveHead$sfx" 1000
                $resetText = $null; if ($reset) { $resetText = [string]$reset.Current.Name }
                $liveText  = $null; if ($live)  { $liveText  = [string]$live.Current.Name }
                return [pscustomobject]@{
                    Kind        = 'panes'
                    Suffix      = $sfx
                    Used        = [string]$used.Current.Name
                    Reset       = $resetText
                    Live        = $liveText
                    Status      = $null
                    Polls       = $polls
                    StatusPolls = $statusPolls
                    WaitedSec   = $timeoutSec
                }
            }
        }
        # "Computing…" is the switch still in flight, not an answer - keep waiting for one.
        if ($statusText -and $statusText -ne $computing) {
            return [pscustomobject]@{
                Kind        = 'status'
                Suffix      = $null
                Used        = $null
                Reset       = $null
                Live        = $null
                Status      = $statusText
                Polls       = $polls
                StatusPolls = $statusPolls
                WaitedSec   = $timeoutSec
            }
        }
        Start-Sleep -Milliseconds 80
    } while ((Get-Date) -lt $deadline)

    # Expired. Which of the two it was is the whole difference between a re-run and a defect hunt.
    return [pscustomobject]@{
        Kind        = $(if ($lastStatus) { 'working' } else { 'blank' })
        Suffix      = $null
        Used        = $null
        Reset       = $null
        Live        = $null
        Status      = $lastStatus
        Polls       = $polls
        StatusPolls = $statusPolls
        WaitedSec   = $timeoutSec
    }
}

<#
  What a timed-out read means, said the same way for both callers (T178), with the tree it failed to
  read printed underneath. Diagnosing the missing PART_SelectedContentHost took a throwaway script that
  dumped exactly this, which is work the check is supposed to have already done.
#>
function Report-NoReading($win, $stop, $where) {
    if ($stop.Kind -eq 'working') {
        Fail "$where did not finish in $($stop.WaitedSec)s - but the window was TALKING the whole time"
        Info "last seen: '$($stop.Status)' on $($stop.StatusPolls) of $($stop.Polls) polls."
        Info "So this is NOT a blank window. Either the report build has not finished (a slow machine, a"
        Info "cold transcript cache - re-run first), or it finished and its pane never entered the tree,"
        Info "which is what a template part going missing looks like (T165)."
    } else {
        Fail "$where read NOTHING in $($stop.WaitedSec)s - no pane body and no status line, ever"
        Info "$($stop.Polls) polls, none of which found anything to read. The window is not being read at"
        Info "all: this is the empty-read failure this script exists for (T142), not a timing problem."
    }
    Dump-ControlView $win
}

<#
  The window's control view, printed - id, type and name per element, which is the dump that made
  T165's defect obvious the moment somebody looked at it.
#>
function Dump-ControlView($win, $limit = 80) {
    $all = @()
    try { $all = @($win.FindAll('Descendants', $ANY)) }
    catch { Info "the tree could not be walked at all - $($_.Exception.Message)"; return }

    Info "control view: $($all.Count) element(s), first $([Math]::Min($limit, $all.Count)) below"
    $i = 0
    foreach ($el in $all) {
        if ($i -ge $limit) { Info "  ... $($all.Count - $limit) more"; break }
        $i++
        $id = [string]$el.Current.AutomationId; if (-not $id) { $id = '-' }
        $type = $el.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
        $name = Printable ([string]$el.Current.Name)
        if ($name.Length -gt 58) { $name = $name.Substring(0, 55) + '...' }
        Info ("  {0,-22} {1,-12} '{2}'" -f $id, $type, $name)
    }
}

<#
  The round trip: view a profile, switch away, switch back. That sequence is the whole point - all three
  defects T164 fixed need a second switch to appear, and two of them only when it returns to where it
  started, which is why a capture of one profile never saw them.

  `--main` rather than the tray: the same shell the tray opens, under the same WinForms pump, over the
  real discovered profiles - and its monitored reading is the synthetic one, so index 0 is a fixed number
  rather than whatever this machine's quota happens to be at the moment of the run.
#>
function Invoke-ProfilesCase {
    Head "Profiles - the picker walked 0 -> 1 -> 0, and the report read back at each stop"

    $count = Expected-ProfileCount
    if ($count -lt 2) {
        # Named, never silent: the round trip does not exist on a machine with one profile, and a Skip
        # that does not say what it failed to check is worse than no check (T161). What it costs is now
        # only the switch — the readable-panes property this case used to carry with it moved to
        # `-Case Panes`, which needs no second profile (T176).
        Info "SKIPPED - this check needs 2+ Claude Code profiles; --profiles reports $count."
        Info "          Nothing about the profile switch (T164) was verified on this run."
        Info "          The panes being readable at all is -Case Panes, which ran regardless."
        return
    }

    $computing = Label 'stats.computing'
    $liveOff   = Label 'stats.live.off'

    $proc = Start-App "--lang $Lang --main"
    try {
        $win = WindowOfProcess $proc.Id
        if (-not $win) { Fail "the main window never appeared"; return }
        Start-Sleep -Milliseconds 1200
        [Native]::Topmost([IntPtr]$win.Current.NativeWindowHandle)
        try { $win.SetFocus() } catch { Info "window SetFocus threw - $($_.Exception.Message)" }

        $combo = ById $win 'StatsProfileCombo' 8000
        if (-not $combo) {
            Fail "the profile picker is not in the tree although --profiles found $count profiles"
            return
        }
        Pass "the profile picker is on the Statistics page ($count profiles)"

        # 0 is where the window already opened, so the first stop is read without touching the picker.
        $stops = @()
        foreach ($step in @(0, 1, 0)) {
            # Cleared per stop, so the sighting belongs to this switch and not to the previous one.
            $script:StatusSeen = $false
            $script:StatusSeenText = $null
            $route = $null
            if ($stops.Count -gt 0) {
                $route = Combo-Select $combo $step $win
                Info "selected index $step through the $route route"
            }
            $stop = Read-ProfileStop $win $computing
            if ($stop.Kind -in @('working', 'blank')) {
                Report-NoReading $win $stop "stop $($stops.Count + 1) (index $step)"
                return
            }
            $stop | Add-Member -NotePropertyName Index -NotePropertyValue $step
            $stop | Add-Member -NotePropertyName Label -NotePropertyValue (Combo-SelectedText $combo)
            $stop | Add-Member -NotePropertyName Route -NotePropertyValue $route
            $stop | Add-Member -NotePropertyName SawStatus -NotePropertyValue $script:StatusSeen
            $stop | Add-Member -NotePropertyName SawStatusText -NotePropertyValue $script:StatusSeenText
            $stops += $stop
            if ($stop.Kind -eq 'panes') {
                Info "index $step '$($stop.Label)': used $($stop.Used), reset $($stop.Reset)"
                Info "  live: $($stop.Live)"
            } else {
                Info "index $step '$($stop.Label)': no report - '$($stop.Status)'"
            }
        }

        # This check's own precondition. If the picker never moved, every comparison below is a profile
        # against itself and they all pass - the exact false green a switch check must not be able to give.
        if ($stops[1].Label -eq $stops[0].Label) {
            Fail "the picker did not move: index 0 and index 1 both read '$($stops[0].Label)'"
            return
        }
        if ($stops[2].Label -ne $stops[0].Label) {
            Fail "the picker did not come back: index 0 read '$($stops[0].Label)', then '$($stops[2].Label)'"
            return
        }
        Pass "the picker walked '$($stops[0].Label)' -> '$($stops[1].Label)' -> '$($stops[0].Label)'"

        # The report changed with the profile. Two accounts CAN read the same percentage, so the switch is
        # judged on the pair: an identical used % and a reset caption to the same minute means the panes
        # were never repainted (or were repainted with the profile being left behind - T164's first defect).
        $moved = ($stops[1].Kind -ne $stops[0].Kind) -or
                 ($stops[1].Used -ne $stops[0].Used) -or
                 ((Reset-Minutes $stops[1].Reset) -ne (Reset-Minutes $stops[0].Reset))
        if ($moved) {
            Pass "the report follows the picker - '$($stops[1].Label)' is not '$($stops[0].Label)''s reading"
        } else {
            Fail "the switch to '$($stops[1].Label)' left the report unchanged: still used $($stops[0].Used), reset $($stops[0].Reset)"
        }

        # The round trip, which is the field report: come back and the same profile reads the same.
        if ($stops[2].Kind -ne $stops[0].Kind) {
            Fail "coming back changed what the window shows: '$($stops[0].Kind)' first, '$($stops[2].Kind)' on return"
        }
        elseif ($stops[0].Kind -eq 'status') {
            if ($stops[2].Status -eq $stops[0].Status) {
                Pass "the round trip is stable (no stored readings for '$($stops[0].Label)', same message both times)"
            } else {
                Fail "the status line changed over the round trip: '$($stops[0].Status)' -> '$($stops[2].Status)'"
            }
        }
        else {
            if ($stops[2].Suffix -ne $stops[0].Suffix) {
                Fail "the open tab changed over the round trip ('$($stops[0].Suffix)' -> '$($stops[2].Suffix)') - the readings are not comparable"
            }
            if ($stops[2].Used -eq $stops[0].Used) {
                Pass "the used % survives the round trip ($($stops[0].Used) both times)"
            } else {
                Fail "the used % changed over the round trip: $($stops[0].Used) -> $($stops[2].Used) for the same profile (T164)"
            }
            # A minute of tolerance, and only that: the caption counts down in real time, so a run that
            # straddles a minute boundary must not be a red build - but an hour of drift is another profile's
            # window, which is the defect.
            $a = Reset-Minutes $stops[0].Reset
            $b = Reset-Minutes $stops[2].Reset
            if ($null -eq $a -or $null -eq $b) {
                if ($stops[0].Reset -eq $stops[2].Reset) {
                    Pass "the reset caption survives the round trip ('$($stops[0].Reset)' both times)"
                } else {
                    Fail "the reset caption changed over the round trip: '$($stops[0].Reset)' -> '$($stops[2].Reset)'"
                }
            }
            elseif ([Math]::Abs($a - $b) -le 1) {
                Pass "the reset caption survives the round trip ('$($stops[0].Reset)' -> '$($stops[2].Reset)')"
            } else {
                Fail "the reset caption jumped over the round trip: '$($stops[0].Reset)' -> '$($stops[2].Reset)' for the same profile (T164)"
            }
        }

        # T177: T166's entire claim is a timing — coming back to a profile seen seconds ago must not blank
        # the panes — and the probe that measured it (status line at 162ms, panes back after 961ms without
        # the per-profile cache; line never shown, panes at 12ms with it) was a scratch file that is now
        # gone. Stated as "the status line was never observed", deliberately NOT as "the panes returned
        # within N ms": a deadline is the one assertion in this file that would go red on a slow machine
        # for a correct reason, and the property really is that the line is not shown at all.
        if ($stops[1].Kind -ne 'panes' -or $stops[2].Kind -ne 'panes') {
            Info "the switch stops did not both show a report, so there was no cached one to put back -"
            Info "  T166's timing was not checked on this run"
        }
        elseif ($stops[2].Route -ne 'UIA') {
            # The keyboard fallback moves the selection through every index on the way to the target, so
            # each one in between is a switch of its own and the line it shows is not the one under test.
            Info "the return was made by the $($stops[2].Route) route, which selects through every index"
            Info "  on the way - the status line that shows is not T166's, so the timing was not checked"
        }
        elseif ($stops[2].SawStatus) {
            Fail "coming back to '$($stops[0].Label)' showed the status line ('$($stops[2].SawStatusText)') - its report was not put back (T166)"
        }
        else {
            Pass "coming back to '$($stops[0].Label)' never showed the status line - its report was put back (T166)"
        }

        # The defect a percentage cannot see: the tail is disposed on the way out and restarted for the new
        # config dir, and `StartLive` ticks synchronously - so a headline still reading "unavailable" once a
        # stop has settled means the switch left the live strip watching nothing.
        $off = @($stops | Where-Object { $_.Kind -eq 'panes' -and $_.Live -eq $liveOff })
        if ($off.Count -gt 0) {
            Fail "the live headline reads '$liveOff' at $($off.Count) of the stops - the tail did not follow the switch (T164)"
        } else {
            $seen = @($stops | Where-Object { $_.Kind -eq 'panes' }).Count
            if ($seen -eq 0) {
                Info "no stop showed the panes, so the live headline was never on screen to read"
            } else {
                Pass "the live headline is a reading, not 'unavailable', at all $seen pane stops"
            }
        }
    }
    finally {
        # WaitForExit, not just Kill: the menu case refuses to start while any ClaudeTray process is alive,
        # and a still-dying `--main` would look like the tray to it.
        if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) | Out-Null }
    }
}

# ---------------------------------------------------------------- panes case

<#
  That the window can be READ at all, with no second profile involved (T176).

  This assertion used to exist only inside `-Case Profiles`, which opens by asking `--profiles` for a
  count and skipping — loudly, but completely — below two. That is right for the round trip, which
  cannot exist with one profile. It is wrong for the property that made the round trip readable: the
  tab body being in the accessibility tree has nothing to do with profiles, and behind that skip it did
  not run on a single-profile machine, which is most machines and every CI runner.

  No screenshot can stand in for it. With the segmented tab template's content host unnamed (T165) the
  window renders perfectly and the entire pane — every number, legend and projection sentence — is
  absent from the tree, which is why that defect survived from T111 to T165.
#>
function Invoke-PanesCase {
    Head "Panes - the report is in the accessibility tree, not just on the screen"

    $computing = Label 'stats.computing'

    $proc = Start-App "--lang $Lang --main"
    try {
        $win = WindowOfProcess $proc.Id
        if (-not $win) { Fail "the main window never appeared"; return }
        Start-Sleep -Milliseconds 1200
        [Native]::Topmost([IntPtr]$win.Current.NativeWindowHandle)
        try { $win.SetFocus() } catch { Info "window SetFocus threw - $($_.Exception.Message)" }

        # The headers first, and separately: T165's defect left all three of these perfectly readable
        # and took the whole body with them, so "headers but no body" is the exact shape to name.
        $tabs = @('stats.tab.session', 'stats.tab.week', 'stats.tab.throughput') | ForEach-Object { Label $_ }
        $missing = @($tabs | Where-Object { -not (ByName $win $_ 8000) })
        if ($missing.Count -gt 0) {
            Fail "these tab headers are not in the tree: $($missing -join ', ')"
        } else {
            Pass "all three tab headers read ($($tabs -join ' / '))"
        }

        $stop = Read-ProfileStop $win $computing
        if ($stop.Kind -eq 'panes') {
            # Reading a number from inside the selected TabItem *is* the assertion that its body is
            # attached: `Used$sfx` is a TextBlock nested several levels inside the pane.
            Pass "the selected pane's body is in the tree - used $($stop.Used) (tab '$($stop.Suffix)')"
            foreach ($field in @(
                @{ Name = "the reset caption"; Value = $stop.Reset }
                @{ Name = "the live headline"; Value = $stop.Live }
            )) {
                if ($field.Value) { Pass "$($field.Name) reads '$($field.Value)'" }
                else { Fail "$($field.Name) is not readable although the pane's used % is" }
            }
        }
        elseif ($stop.Kind -eq 'status') {
            # Gated on the precondition, not on a weaker form of the assertion (T161): the pane is
            # Collapsed until a report renders, so with no report there is no body to look for. Said
            # out loud, with what went unchecked.
            Info "SKIPPED - the window shows the status line, not a report: '$($stop.Status)'"
            Info "          Nothing about the pane body being in the tree was verified on this run."
        }
        else {
            Report-NoReading $win $stop "the Statistics page"
        }
    }
    finally {
        # WaitForExit, not just Kill: the menu case that may run later refuses to start while any
        # ClaudeTray process is alive.
        if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) | Out-Null }
    }
}

# ---------------------------------------------------------------- names case

<#
  Assert one control's accessible name IS its label, not merely that it has one: a placeholder, an
  automation id echoed back, or the glyph codepoint itself would all satisfy "non-empty" and none of
  them is what a screen reader should say. Returns whether the control was in the tree at all, so the
  caller decides what an absence means - some of these are legitimately collapsed.
#>
function Assert-Name($win, $id, $expected, $what, $timeoutMs = 6000) {
    $el = ById $win $id $timeoutMs
    if (-not $el) { return $false }
    $name = [string]$el.Current.Name
    if ($name -eq $expected)  { Pass "$what announces '$name'" }
    elseif ($name)            { Fail "$what announces '$(Printable $name)', expected its label '$expected'" }
    else                      { Fail "$what announces NOTHING - unnamed in the automation tree (T175)" }
    return $true
}

<#
  A name the console cannot draw, drawn anyway. Without this the worst case in this whole check —
  a glyph-only button announcing its Segoe MDL2 codepoint, which is what T175 actually found — is
  reported as `announces ''` and reads as the empty case it is not.
#>
function Printable($text) {
    $out = ""
    foreach ($ch in $text.ToCharArray()) {
        if ([char]::IsControl($ch) -or [int]$ch -ge 0xE000) {
            $out += ('\u{0:X4}' -f [int]$ch)
        } else { $out += $ch }
    }
    return $out
}

<#
  What the window says it is, control by control. This is the one property in this file that no
  screenshot and no other case can observe: the T165 control-view dump found `StatsProfileCombo` and
  `MethodInfo` carrying empty names while every neighbouring button read fine, because a WPF control
  derives its name from its own content and both of these have none - one's label is a separate
  TextBlock, the other's content is a Segoe MDL2 codepoint.

  The settings rows are the same shape thirty-odd times over (`SettingsRow` = label left, control
  right), so two of them are read here as the witness for the rule the row now applies to all.

  `--main`, not `--settings`: one launch reaches both pages, and it is the shell the tray opens.
#>
function Invoke-NamesCase {
    Head "Names - what the controls announce to a screen reader"

    $read = 0
    $proc = Start-App "--lang $Lang --main"
    try {
        $win = WindowOfProcess $proc.Id
        if (-not $win) { Fail "the main window never appeared"; return }
        Start-Sleep -Milliseconds 1200
        [Native]::Topmost([IntPtr]$win.Current.NativeWindowHandle)
        try { $win.SetFocus() } catch { Info "window SetFocus threw - $($_.Exception.Message)" }

        # --- Statistics. The read order no longer decides which control this finds: every `x:Name` in
        # the shell is unique across the window (T192), asserted by `--selftest`, so `StatsProfileCombo`
        # is the Statistics one whether or not the Settings pane has been built.
        $count = Expected-ProfileCount
        if (Assert-Name $win 'StatsProfileCombo' (Label 'stats.profile') "the Statistics profile picker" 8000) {
            $read++
        }
        elseif ($count -lt 2) {
            # Stated, not silent: the card is collapsed below two profiles, so there is no control to
            # read - but the check must say which of the two reasons it is reporting (T161).
            Info "the profile card is collapsed ($count profile) - its picker was not read"
        }
        else {
            Fail "the profile picker is not in the tree although --profiles found $count profiles"
        }

        # The method button is collapsed until a report renders, so this waits out the first poll.
        if (Assert-Name $win 'MethodInfo' (Label 'stats.methodTitle') "the method-note button" 25000) {
            $read++
        } else {
            Info "no report rendered within 25s (offline? cold transcript cache?) - the method button"
            Info "  never left Collapsed, so its name was not read on this run"
        }

        # --- Settings: the SettingsRow shape, where the same defect repeats on every row.
        $navSettings = ById $win 'NavSettings'
        if (-not $navSettings) {
            Fail "the nav strip has no Settings destination - the row names were not read"
        }
        else {
            try { $navSettings.GetCurrentPattern(
                    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
            catch { ClickCentre $navSettings }
            Start-Sleep -Milliseconds 1200

            # All three live on the Settings page's own default sidebar page (General), so no second
            # navigation is needed - and they are one of each shape the row rule has to handle: an
            # ItemsControl, a contentless ToggleButton, and a Slider inside a StackPanel.
            $rows = @(
                @{ Id = 'LanguageCombo'; Key = 'settings.general.appLanguage';    What = "the language picker" }
                @{ Id = 'StartupCheck';  Key = 'settings.general.startWithWindows'; What = "the start-with-Windows switch" }
                @{ Id = 'IntervalSlider'; Key = 'settings.general.interval';      What = "the refresh-interval slider" }
            )
            foreach ($row in $rows) {
                if (Assert-Name $win $row.Id (Label $row.Key) $row.What) { $read++ }
                else { Fail "$($row.What) ($($row.Id)) is not in the tree after navigating to Settings" }
            }
        }

        # The founding trap (T142): a green tick over an empty read is worse than no check at all.
        if ($read -eq 0) {
            Fail "not one accessible name was read - this case observed nothing, so it passes nothing"
        } else {
            Info "$read control name(s) read"
        }
    }
    finally {
        # WaitForExit, not just Kill: the menu case that may run next refuses to start while any
        # ClaudeTray process is alive.
        if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) | Out-Null }
    }
}

# ---------------------------------------------------------------- menu case

<# The tray icon in the notification area, opening the overflow flyout first if it is hidden there. #>
function Get-TrayIcon($namePattern, $timeoutMs = 20000) {
    $iconCond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'NotifyItemIcon')
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    $openedOverflow = $false
    do {
        # Whole desktop, not just Shell_TrayWnd: the overflow flyout is a top-level window of its own.
        foreach ($icon in $ROOT.FindAll('Descendants', $iconCond)) {
            if ($icon.Current.Name -match $namePattern) { return $icon }
        }
        if (-not $openedOverflow) {
            # The chevron is the one SystemTrayIcon button of class SystemTray.NormalButton (network,
            # volume, clock and show-desktop each have their own class).
            $tray = $ROOT.FindFirst('Children',
                (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Shell_TrayWnd')))
            if ($tray) {
                $chev = $tray.FindAll('Descendants',
                    (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'SystemTrayIcon'))) |
                    Where-Object { $_.Current.ClassName -eq 'SystemTray.NormalButton' } | Select-Object -First 1
                if ($chev) {
                    Info "icon not on the taskbar - opening the overflow flyout"
                    try { $chev.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
                    catch { ClickCentre $chev }
                    $openedOverflow = $true
                    Start-Sleep -Milliseconds 800
                }
            }
        }
        Start-Sleep -Milliseconds 400
    } while ((Get-Date) -lt $deadline)
    return $null
}

<# Any open ToolStripDropDown belonging to $processId - the tray's own menu and nobody else's. #>
function Find-TrayMenu($processId) {
    foreach ($w in $ROOT.FindAll('Children', $ANY)) {
        if ($w.Current.ControlType -eq [System.Windows.Automation.ControlType]::Menu -and
            $w.Current.ProcessId -eq $processId) { return $w }
    }
    return $null
}

function Menu-Items($menu) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
    return @($menu.FindAll('Children', $cond))
}

<#
  Open the icon's menu. Right-click first for the shells where that works, then the route that
  actually works on Windows 11's XAML taskbar: UIA SetFocus on the icon + the Application key.
  Retries, because a single attempt genuinely does not always produce the menu.
#>
function Open-TrayMenu($namePattern, $processId, $attempts = 5) {
    for ($try = 1; $try -le $attempts; $try++) {
        $icon = Get-TrayIcon $namePattern
        if (-not $icon) { Info "attempt $try : tray icon not found"; Start-Sleep -Milliseconds 600; continue }
        try { $icon.GetClickablePoint() | Out-Null }
        catch { if ($try -eq 1) { Info "icon has no clickable point (expected) - using its bounding rectangle" } }

        if ($try % 2 -eq 1) {
            try { $icon.SetFocus() } catch { Info "attempt $try : SetFocus threw - $($_.Exception.Message)" }
            Start-Sleep -Milliseconds 500
            [Native]::Key($VK_APPS)
        } else {
            ClickCentre $icon -Right
        }

        for ($w = 0; $w -lt 12; $w++) {
            Start-Sleep -Milliseconds 200
            $menu = Find-TrayMenu $processId
            if ($menu) { return $menu }
        }
        Info "attempt $try : no menu yet"
    }
    return $null
}

<#
  Expand a submenu the way a keyboard user does - Down until the item has focus, then Right. Never
  Invoke(): invoking "Open Claude Code" launches a terminal, and invoking "Quit" ends the run.
#>
function Expand-Item($menu, $label) {
    $items = Menu-Items $menu
    $target = $items | Where-Object { $_.Current.Name -eq $label } | Select-Object -First 1
    if (-not $target) { return $null }
    for ($i = 1; $i -le ($items.Count + 2); $i++) {
        [Native]::Key($VK_DOWN)
        $focused = Menu-Items $menu | Where-Object { $_.Current.HasKeyboardFocus } | Select-Object -First 1
        if ($focused -and $focused.Current.Name -eq $label) {
            [Native]::Key($VK_RIGHT)
            Start-Sleep -Milliseconds 700
            $cond = New-Object System.Windows.Automation.PropertyCondition(
                $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
            # WinForms nests an opened submenu under the parent item, not as a second top-level menu.
            return @($target.FindAll('Descendants', $cond))
        }
    }
    return $null
}

<# What the app itself says the menu should contain, so the check has something to be wrong about. #>
function Expected-ProfileEntries {
    $out = & $Exe "--lang" $Lang "--profiles" 2>&1 | Out-String
    if ($out -match 'submenu with (\d+) entries') {
        return [pscustomobject]@{ Count = [int]$Matches[1]; Why = "several profiles" }
    }
    # "Open Claude Code" stays a plain command for two different reasons - there is only one profile,
    # or one profile is the whole environment's and the submenu collapsed back (T146). The app names
    # which, so a pass can say the true one instead of guessing.
    $why = if ($out -match 'the picked profile is the environment') { "the picked profile is the environment's (T146)" }
           else { "only one profile" }
    return [pscustomobject]@{ Count = 0; Why = $why }
}

<# How many profiles the app sees at all - the Profile submenu is shown, and expandable, above one. #>
function Expected-ProfileCount {
    $out = & $Exe "--lang" $Lang "--profiles" 2>&1 | Out-String
    if ($out -match 'polled every interval:\s*\d+\s+of\s+(\d+)') { return [int]$Matches[1] }
    return 0
}

function Invoke-MenuCase {
    Head "Menu - the tray icon's menu as it is when it opens"

    $running = @(Get-Process -Name ClaudeTray -ErrorAction SilentlyContinue)
    $proc = $null
    $ownProcess = $false
    if ($UseRunning) {
        if ($running.Count -eq 0) { Fail "-UseRunning given but no ClaudeTray process is running"; return }
        $proc = $running[0]
        Info "driving the ALREADY RUNNING tray, not -Exe: $($proc.Path)"
    }
    elseif ($running.Count -gt 0) {
        # A second launch would exit silently on the single-instance mutex, and this check would then
        # read the *other* tray's menu and call it a pass. Refuse instead.
        Fail "a ClaudeTray is already running (pid $($running[0].Id)) - the single-instance mutex would make this launch exit silently. Quit it, or re-run with -UseRunning."
        return
    }
    else {
        $proc = Start-App "--lang $Lang"
        $ownProcess = $true
        Start-Sleep -Milliseconds 1500
        $proc.Refresh()
        if ($proc.HasExited) { Fail "the tray exited immediately after launch (single instance already held?)"; return }
    }

    try {
        # "Claude Code" is the first half of every tooltip in every language, so it identifies the icon
        # without pinning the check to one locale.
        $menu = Open-TrayMenu 'Claude Code' $proc.Id
        if (-not $menu) { Fail "the tray menu never opened (5 attempts)"; return }

        $items = Menu-Items $menu
        $labels = @($items | ForEach-Object { $_.Current.Name } | Where-Object { $_ })
        if ($labels.Count -eq 0) {
            # The failure this whole script is for: a green tick over an empty read.
            Fail "the menu opened but read ZERO entries - the check saw nothing, so it passes nothing"
            return
        }
        Pass "the menu opened with $($labels.Count) entries"
        foreach ($l in $labels) { Info "- $l" }

        # T158: the window is opened from here as well as by a left-click on the icon. The menu is the
        # only route a keyboard user has (the context-menu key opens this list and nothing else), so
        # the entry going missing would strand them with no way in at all.
        $openLabel = Label 'menu.open'
        if ($labels -contains $openLabel) { Pass "'$openLabel' opens the window from the menu" }
        else { Fail "'$openLabel' is missing - the menu is the only route to the window without a mouse (T158)" }

        $expect = Expected-ProfileEntries
        $subLabel = Label 'menu.openClaude'
        $expandedOpenClaude = $false
        if ($expect.Count -lt 2) {
            # Prefix, not equality: the collapsed command carries T137's "- sign in" marking when the
            # profile it aims at has no credentials on disk, and that is still the same entry. The
            # open-the-window entry is excluded by name, or a label that merely *starts* the same way
            # would satisfy this check for the wrong item (it did, when it read "Open Claude Code Tray").
            $plain = @($labels | Where-Object { $_ -like "$subLabel*" -and $_ -ne $openLabel })
            if ($plain.Count -gt 0) {
                Pass "$($expect.Why): '$($plain[0])' stays a plain command, as designed"
            } else {
                Fail "'$subLabel' is missing from the menu"
            }
        }
        else {
            $subs = Expand-Item $menu $subLabel
            if (-not $subs) {
                Fail "'$subLabel' did not expand - expected $($expect.Count) profile entries, read none"
            }
            elseif ($subs.Count -lt $expect.Count) {
                Fail "'$subLabel' listed $($subs.Count) profile entries, expected $($expect.Count)"
            }
            else {
                Pass "'$subLabel' lists $($subs.Count) profile entries"
                foreach ($s in $subs) { Info "- $($s.Current.Name)" }
            }
            $expandedOpenClaude = $true
        }

        # T148: the Profile submenu has to expand from the keyboard too. It only can if it is non-empty
        # when the menu opens - an empty ToolStripMenuItem exposes no ExpandCollapse pattern, draws no
        # arrow, and WinForms handles Right as "activate a plain command", dismissing the whole menu.
        # A mouse hover always worked, which is exactly why this went unnoticed until it was driven.
        $profileCount = Expected-ProfileCount
        $profLabel = Label 'menu.profiles'
        if ($profileCount -lt 2) {
            Info "one profile: the Profile submenu is hidden, nothing to expand"
        }
        elseif ($labels -notcontains $profLabel) {
            Fail "'$profLabel' is missing from the menu although $profileCount profiles were found"
        }
        else {
            # Esc only if the previous check actually expanded a submenu: on a machine where "Open
            # Claude Code" stays a plain command (T146) nothing is open, so the Esc closed the *menu*
            # and this check failed on its own doing.
            if ($expandedOpenClaude) {
                [Native]::Key($VK_ESC)   # close the submenu, keep the menu itself
                Start-Sleep -Milliseconds 400
            }
            $menu = Find-TrayMenu $proc.Id
            # And if it closed anyway, re-open it rather than report a Profile submenu that was never
            # asked the question. Only a menu that will not open at all is a failure here.
            if (-not $menu) { $menu = Open-TrayMenu 'Claude Code' $proc.Id }
            if (-not $menu) {
                Fail "the menu would not reopen for the Profile submenu check"
            }
            else {
                $profSubs = Expand-Item $menu $profLabel
                $least = $profileCount + 1   # one entry per profile, plus the auto-follow toggle
                if (-not $profSubs) {
                    Fail "'$profLabel' did not expand from the keyboard - empty when the menu opened? (T148)"
                }
                elseif ($profSubs.Count -lt $least) {
                    Fail "'$profLabel' expanded with $($profSubs.Count) entries, expected at least $least"
                }
                else {
                    Pass "'$profLabel' expands from the keyboard with $($profSubs.Count) entries"
                    foreach ($s in $profSubs) { Info "- $($s.Current.Name)" }
                }
            }
        }

        # T158: a LEFT-click on the icon is now the app's main entry point - it opens the one window on
        # the pacing report. Nothing else in this repo drives that path (`--main` builds the shell
        # directly, which is a different caller), and it is the one gesture most users make.
        [Native]::Key($VK_ESC); [Native]::Key($VK_ESC)   # the menu must be gone before clicking the icon
        Start-Sleep -Milliseconds 400
        $icon = Get-TrayIcon 'Claude Code'
        if (-not $icon) {
            Fail "the tray icon could not be found for the left-click check"
        }
        else {
            ClickCentre $icon
            $win = WindowOfProcess $proc.Id 12000
            if (-not $win) {
                Fail "a left-click on the icon opened no window (T158)"
            }
            else {
                # The nav strip's three destinations are the window's identity: a window with the right
                # title and no strip would pass a title-only check.
                $dests = @('main.nav.statistics', 'main.nav.context', 'main.nav.settings') | ForEach-Object { Label $_ }
                $missing = @($dests | Where-Object { -not (ByName $win $_ 3000) })
                if ($missing.Count -gt 0) {
                    Fail "the window opened but these destinations are missing from the nav strip: $($missing -join ', ')"
                } else {
                    Pass "a left-click on the icon opens the window, with $($dests -join ' / ')"
                }
            }
        }
    }
    finally {
        [Native]::Key($VK_ESC); [Native]::Key($VK_ESC)   # close menu and submenu, whatever is open
        if ($ownProcess -and $proc -and -not $proc.HasExited) { $proc.Kill() }
    }
}

# ---------------------------------------------------------------- run

Write-Host "Check-Interaction - $Exe (lang $Lang)" -ForegroundColor White
if ($Case -in @('All', 'Keyboard')) { Invoke-KeyboardCase }
# Before the menu case, and killed with WaitForExit: that one refuses to run while any ClaudeTray is alive.
if ($Case -in @('All', 'Panes'))    { Invoke-PanesCase }
if ($Case -in @('All', 'Profiles')) { Invoke-ProfilesCase }
if ($Case -in @('All', 'Names'))    { Invoke-NamesCase }
if ($Case -in @('All', 'Menu'))     { Invoke-MenuCase }

Write-Host ""
if ($script:Failures -eq 0) {
    Write-Host "OK - every interaction check passed." -ForegroundColor Green
    exit 0
}
Write-Host "FAILED - $($script:Failures) interaction check(s)." -ForegroundColor Red
exit 1
