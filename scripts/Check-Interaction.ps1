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

    Two cases, runnable separately:

      -Case Keyboard   Launch `--settings-tray` (WinForms pump — the only preview that can see a
                       keyboard bug), navigate to a page, type into a TextBox and read the value back
                       through ValuePattern, Tab out of it, and drive a Slider with an arrow key.
      -Case Menu       Launch the real tray, open the notification-area icon's menu, read its entries,
                       and expand "Open Claude Code" to read the per-profile entries (this is what
                       verified T137).

    Exit code is 0 only if every check passed; any failure exits 1. A check that could not observe
    anything is a FAIL, never a pass — see "fail loudly" below.

.NOTES
    Three traps, all met while writing these checks by hand, all encoded here:

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

    A menu item is never Invoked: invoking "Open Claude Code" would launch a terminal. Submenus are
    opened the way a keyboard user opens them (Down to the item, Right to expand), and read.

.PARAMETER Case
    Keyboard, Menu, or All (default).

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
    powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Menu -UseRunning
#>
param(
    [ValidateSet('All', 'Keyboard', 'Menu')]
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

$VK_APPS = 0x5D; $VK_DOWN = 0x28; $VK_RIGHT = 0x27; $VK_LEFT = 0x25; $VK_ESC = 0x1B
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
if ($Case -in @('All', 'Menu'))     { Invoke-MenuCase }

Write-Host ""
if ($script:Failures -eq 0) {
    Write-Host "OK - every interaction check passed." -ForegroundColor Green
    exit 0
}
Write-Host "FAILED - $($script:Failures) interaction check(s)." -ForegroundColor Red
exit 1
