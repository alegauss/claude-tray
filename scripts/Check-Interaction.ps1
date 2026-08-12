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

    The cases, runnable separately — one bullet each, and `--selftest` asserts this list against the
    `ValidateSet` below, so no count is written here for a later case to make wrong (T364):

      -Case Keyboard   Launch `--settings-tray` (WinForms pump — the only preview that can see a
                       keyboard bug), navigate to a page, type into a TextBox and read the value back
                       through ValuePattern, Tab out of it, and drive a Slider with an arrow key.
      -Case Panes      Launch `--main` and assert the report can be READ: the three tab headers, and
                       the selected pane's body — a used %, a reset caption, a live headline — inside
                       the accessibility tree. Needs no second profile, which is the whole point
                       (T176): behind `-Case Profiles`' two-profile skip this never ran on a
                       one-profile machine, and it is the only thing that would notice
                       `PART_SelectedContentHost` going missing again.
      -Case Sessions   Launch `--main`, select the Sessions pane and drill into it: wait for the list
                       to finish its scan, assert a row reads back, click one and assert the task
                       lines it expands into, then open the pane's own method note and assert its
                       text. The rows carry a money column (T346), and a figure nobody has driven is
                       a figure that renders once and rots — this is the only case that opens the
                       note, which is a separate top-level window a capture cannot see (T359).
      -Case Profiles   Launch `--main`, walk the Statistics page's profile picker 0 -> 1 -> 0 through
                       the real ComboBox and read the report back at each stop: the used %, the reset
                       caption and the live headline. Nothing else in this repo *drives* the picker —
                       every capture renders one profile, which is structurally incapable of seeing
                       T164's three defects (all need a second switch; two need it to go back). Also
                       T166's timing: on the way back, the status line must never be observed at all.
      -Case Menu       Launch the real tray, open the notification-area icon's menu, read its entries,
                       and expand "Open Claude Code" to read the per-profile entries (this is what
                       verified T137).
      -Case Switch     Launch a tray and drive the Profile submenu entry that changes which account
                       the ICON follows, then read back what a switch is supposed to have done: the
                       check mark on the profile picked and on no other, and the icon's own tooltip
                       naming it. Not the picker `-Case Profiles` walks — that one chooses whose
                       numbers the Statistics page draws, and until this case the path that rewrites
                       the setting, re-keys the stores and takes the other account's token ran under
                       no check at all (T294). Refused under `-UseRunning`: the resident tray is not
                       observing, so a pick there would repoint the icon for real.
      -Case Names      Launch `--main` and read back what the controls ANNOUNCE: the Statistics
                       picker, the method-note button, and the settings rows whose label is a
                       neighbouring element rather than their own content (T175). A picture cannot
                       see an accessible name, and an unnamed control is invisible to every other
                       check in this file.

    Three outcomes, three exit codes (T193):

      0  every assertion ran and passed
      2  DEGRADED — everything that ran passed, but at least one assertion could not be evaluated on
         this machine or this run (one profile registered, no report rendered, a picker route that
         needed more than one selection change, a tray already resident so the menu case must refuse).
         Named and listed in the summary, never an Info line:
         a green tick over an assertion that silently stopped running is the defect T169 and T176 both
         name, and T166's switch-back timing had been dropped that way.
      1  at least one assertion FAILED

    A check that could not observe anything it was pointed at is a FAIL, never a pass — see "fail
    loudly" below. `Unchecked` is for the narrower case where the *precondition* is absent, and it is
    deliberately not available for an assertion that can never run here (the Settings window binds
    nothing to Esc, so there is no Esc behaviour to lose).

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

    It is a LAUNCH argument, which is the whole of its reach: with `-UseRunning` there is no command
    line, so the labels are matched against the language the resident tray actually resolved instead
    (T220). An explicitly given `-Lang` is refused there rather than overridden — see `-UseRunning`.

.PARAMETER SampleEnv
    Drive the run against a SAMPLED `CLAUDE_CONFIG_DIR` (`--sample-env`, T231) instead of this machine's:
    `agrees`, `other`, `outside` or `unset`. The states the Profile submenu exists to report are the ones
    where the variable disagrees with the icon, and on a developer's machine it never does — so without
    this the environment mark (T172) is an assertion that is only ever `Unchecked`.

    Both the tray and the `--profiles` read-out the expectations come from are launched with it, or the
    check would compare a sampled menu against the real environment. Refused with `-UseRunning` for the
    reason `-Lang` is: it is a launch argument, and a resident tray did not get one.

.PARAMETER UseRunning
    Menu case only: drive the tray that is ALREADY running instead of launching `-Exe`. Convenient on
    a dev machine (the single-instance mutex makes a second launch exit silently), but it checks
    whatever binary is running, not the one you just built — so the script prints that path loudly.

    Deliberately NOT implied when a tray is found (§XX.6 asked). Implying it would silently move the
    check onto a binary nobody named — usually an installed release, while the point of the run is the
    build under `-Exe` — and "the thing being looked at was not the thing being checked" is the defect
    this file exists to catch, not one to automate. So the refusal stays, and it is `Unchecked`: the
    run says what it did not check and why, and the flag remains the caller's word that a different
    binary is what they meant.

    `-Lang` does not survive it either, which is the same point measured: the language is a launch
    argument, so a resident tray runs in its own saved one. Verifying T202 against a pt-BR tray with
    the default `-Lang en` produced four FAILs for labels that were all present, in Portuguese.

    So the labels are matched against what that tray resolved — `Resolve-TrayLang` reads the same two
    inputs the app read, saved preference then Windows display language — and the run says which and
    from where (T220). The default `-Lang en` is not a request and is simply replaced; a `-Lang` typed
    on the command line is, and one this tray cannot be in is `Unchecked` rather than quietly ignored.
    Checking a language nobody asked for is the same defect as checking a binary nobody named.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1
    powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Keyboard
    powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Panes
    powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Profiles
    powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Names
    powershell -ExecutionPolicy Bypass -File scripts\Check-Interaction.ps1 -Case Menu -UseRunning
#>
param(
    [ValidateSet('All', 'Keyboard', 'Panes', 'Sessions', 'Profiles', 'Menu', 'Names', 'Switch')]
    [string]$Case = 'All',
    [string]$Exe = "bin\Debug\net10.0-windows\win-x64\ClaudeTray.exe",
    [string]$Lang = "en",
    [switch]$UseRunning,
    [ValidateSet('agrees', 'other', 'outside', 'unset')]
    [string]$SampleEnv
)

$ErrorActionPreference = "Stop"

# Whether -Lang was TYPED, as opposed to defaulted (T220). Captured here and only here: inside a
# function $PSBoundParameters is that function's own, so asking there reads an empty table and the
# refusal below never fires - which is exactly how it first shipped.
$script:LangGiven = $PSBoundParameters.ContainsKey('Lang')

# The sampled-environment flag as it is passed to every launch, and to the read-out the expectations are
# derived from (T231). One string, so the two can never be given different modes.
$script:EnvArgs = if ($SampleEnv) { @('--sample-env', $SampleEnv) } else { @() }

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
    // Who owns the foreground, which is the precondition every synthesised input rests on: a key press
    // goes to the foreground window and a click lands on whatever is drawn at the point (T256).
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder s, int n);

    public static int ForegroundPid() {
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) return 0;
        int pid; GetWindowThreadProcessId(h, out pid); return pid;
    }
    public static string ForegroundTitle() {
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) return "";
        var s = new System.Text.StringBuilder(512);
        GetWindowTextW(h, s, s.Capacity);
        return s.ToString();
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
$VK_RETURN = 0x0D   # activating a focused menu entry, which is what the Switch case does (T294)
$VK_HOME = 0x24; $VK_END = 0x23
$AE   = [System.Windows.Automation.AutomationElement]
$ANY  = [System.Windows.Automation.Condition]::TrueCondition
$ROOT = $AE::RootElement

$script:Failures = 0
$script:Unchecked = @()
# Every process Start-App creates, so nothing this run started can outlive it (T258).
$script:Launched = @()
function Pass($msg) { Write-Host "[PASS] $msg" -ForegroundColor Green }
function Fail($msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red; $script:Failures++ }
function Info($msg) { Write-Host "       $msg" -ForegroundColor DarkGray }
function Head($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

<#
  An assertion that COULD have run on this machine and did not (T193). Every one of these used to be an
  `Info` line, which is the defect: the run printed `OK - every interaction check passed` while one of the
  properties it exists to protect had quietly gone unobserved, and T169 and T176 are both that same shape.
  Named and counted here, and the summary refuses the word "every" when the list is non-empty.

  This is NOT for an assertion that can never run — the Settings window binds nothing to Esc, so there is
  no Esc behaviour to observe and nothing was lost. `Unchecked` means "should have been checked, wasn't".
#>
<#
  Whether this run owns the desktop it is about to type into (T256).

  Every assertion below rests on one thing the script does not control: a synthesised key press goes to
  the FOREGROUND window and a synthesised click lands on whatever is drawn at the point. Windows refuses
  `SetForegroundWindow` to a process that does not already own the foreground, so a run started from an
  editor that keeps focus drives somebody else's window - and the failures that produces are ordinary
  ones. Measured while verifying T229: the same case failed at `DirectoryBox not found after navigating`,
  then at `SetFocus` throwing, then at the first point again, and passed unchanged either side of a
  `git stash` of the code under test.

  So it is asked as a PRECONDITION and answered as `Unchecked`, not as a `Fail`. That is this script's
  own vocabulary for "should have been checked, wasn't" (T193), and it is deliberately not a retry: a
  case that passes on the second attempt cannot tell a busy desktop from a broken build, which is the
  reading this whole file exists to refuse. The intruder is named the way `Capture-Window.ps1` names the
  window it copied (T199), because "something else had focus" is not actionable and a title and a pid are.

  A hosted runner has no competing foreground, so on CI this either changes nothing or reports a
  condition that was being suffered silently. Which of the two is a reading nobody has taken.
#>
function Owns-Foreground($proc, $what) {
    $pid_ = [Native]::ForegroundPid()
    if ($pid_ -eq $proc.Id) { return $true }

    $who = try { (Get-Process -Id $pid_ -ErrorAction Stop).ProcessName } catch { "pid $pid_" }
    $title = [Native]::ForegroundTitle()
    Unchecked $what ("this run does not own the foreground, so a key press goes elsewhere and a click " +
                     "lands on whatever is drawn at the point: '$title' ($who, pid $pid_) has it. " +
                     "Nothing here is about the code - re-run with nothing else taking focus.")
    return $false
}

function Unchecked($what, $why) {
    Write-Host "[----] $what NOT CHECKED - $why" -ForegroundColor Yellow
    # Deduped by name in the SUMMARY only: the line above is printed every time, because where an
    # assertion did not run is part of the reading. The tally is a list of what is not covered, and the
    # environment sweep walks one submenu per mode (T238), so an assertion absent in all of them was
    # being counted three times and reading as three holes.
    if ($script:Unchecked -notcontains $what) { $script:Unchecked += $what }
}

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

<#
  Launch the binary under test, and REMEMBER having done it (T258).

  Every process this script creates comes through here, so the register is total by construction - there
  is no per-case list to keep, and a case that returns early down a path nobody thought about still has
  its tray stopped by `Stop-Launched` at the end of the run. The defect was the other shape: two trays a
  failing case had started were still alive afterwards, the next `dotnet build` died on
  `MSB3027 ... locked by: ClaudeTray (29700), ClaudeTray (49892)`, and - the part that matters - the
  command after THAT ran the previous .exe and reported on code that was not in the tree.
#>
function Start-App($appArgs) {
    if (-not (Test-Path $Exe)) { throw "$Exe not found - run: dotnet build -c Debug" }
    $p = Start-Process -FilePath $Exe -ArgumentList $appArgs -PassThru
    $script:Launched += $p
    return $p
}

<#
  Stop whatever is still running, whichever way the run ended. Named rather than silent: a tray left
  behind is a lock on the next build, so which ones were still up is a reading about this run and not
  housekeeping. A case that stopped its own process leaves nothing to do here, which is the ordinary case.
#>
function Stop-Launched {
    $alive = @($script:Launched | Where-Object { $_ -and -not $_.HasExited })
    if ($alive.Count -eq 0) { return }
    Info "stopping $($alive.Count) process(es) this run launched and a case did not: $(($alive | ForEach-Object { $_.Id }) -join ', ')"
    foreach ($p in $alive) { try { $p.Kill() } catch { } }
}

<#
  How the tray being driven differs from the build the caller named (T236), as the sentence to report -
  or null when they are the same file and there is nothing to say.

  `-UseRunning` prints the path it attached to, which was the whole of T202's answer, and a printed path
  is not a comparison. Executing Block AI this reported "OK - every interaction check passed" against a
  tray published the previous afternoon, before T171 shipped: the submenu entry being verified did not
  exist in that binary and the run was green about it.

  Two keys, because one is not enough. The file version catches the ordinary case. It cannot catch the
  one that actually happened - a Debug build and an installed Release carry the same `<Version>` between
  releases - so when the versions agree the write time is the only thing left that can tell two builds
  apart, and it is read second rather than instead: a version difference is the more useful sentence.
#>
function Binary-Drift($runningPath) {
    if (-not (Test-Path $Exe)) { return "-Exe '$Exe' is not on disk, so there is nothing to compare it to" }
    if (-not $runningPath)     { return "the running tray's path could not be read" }
    if (-not (Test-Path -LiteralPath $runningPath)) { return "the running tray's binary is gone from disk" }

    $mine = Get-Item -LiteralPath $Exe
    $theirs = Get-Item -LiteralPath $runningPath
    if ($mine.FullName -eq $theirs.FullName) { return $null }   # the same file: nothing can have drifted

    $mv = [string]$mine.VersionInfo.FileVersion
    $tv = [string]$theirs.VersionInfo.FileVersion
    if ($mv -ne $tv) { return "it is version $tv and -Exe is $mv" }

    $gap = [int][math]::Round(($mine.LastWriteTime - $theirs.LastWriteTime).TotalMinutes)
    if ($gap -ge 1)  { return "both are version $mv, but it was built $gap minute(s) before -Exe" }
    if ($gap -le -1) { return "both are version $mv, but it was built $([math]::Abs($gap)) minute(s) after -Exe" }
    return $null
}

<#
  The `--main` window, launched once and lent to whichever of Panes/Profiles/Names run together (T195).

  Three of the five cases drive the same window and each used to own its own process, so `-Case All`
  paid the launch, the first WPF layout pass and the wait for the first poll three times over - seconds
  each, for a window none of them leaves in a state the next would reject. `Panes` and `Names` only
  read (numbers out of the tree, accessible names); `Profiles` drives the picker but walks it 0 -> 1 -> 0,
  so it hands back the profile it was given.

  What is deliberately kept: a case run ALONE still owns its process, because the value of `-Case Names`
  is partly that it is ten seconds when a name is what you changed. Sharing is opt-in per invocation
  ($script:ShareMain), not a merge of the three cases into one.
#>
$script:ShareMain = $false
$script:MainProc  = $null
$script:MainWin   = $null
$script:MainLaunches = 0   # processes started
$script:MainAcquires = 0   # cases that asked for the window; the saving is the difference

<# The window, ready to read: launched if nothing is up, re-focused if something is. Null if it never
   appeared, and the caller Fails - an absent window is never a pass. #>
function Acquire-Main {
    $script:MainAcquires++
    if ($script:MainProc -and -not $script:MainProc.HasExited -and $script:MainWin) {
        Info "reusing the --main window from an earlier case (no launch, no first-poll wait)"
        [Native]::Topmost([IntPtr]$script:MainWin.Current.NativeWindowHandle)
        try { $script:MainWin.SetFocus() } catch { Info "window SetFocus threw - $($_.Exception.Message)" }
        return $script:MainWin
    }
    $script:MainLaunches++
    $script:MainProc = Start-App "--lang $Lang --main"
    $win = WindowOfProcess $script:MainProc.Id
    if (-not $win) { return $null }
    Start-Sleep -Milliseconds 1200
    [Native]::Topmost([IntPtr]$win.Current.NativeWindowHandle)
    try { $win.SetFocus() } catch { Info "window SetFocus threw - $($_.Exception.Message)" }
    $script:MainWin = $win
    return $win
}

<# End of one case. Keeps the window for the next one in a shared run; the runner closes it once. #>
function Release-Main {
    if (-not $script:ShareMain) { Close-Main }
}

<# WaitForExit, not just Kill: the menu case refuses to start while any ClaudeTray process is alive, and
   a still-dying `--main` would look like the tray to it. #>
function Close-Main {
    if ($script:MainProc -and -not $script:MainProc.HasExited) {
        $script:MainProc.Kill(); $script:MainProc.WaitForExit(5000) | Out-Null
    }
    $script:MainProc = $null
    $script:MainWin  = $null
}

<#
  The label a control actually carries, read from the same lang\<code>.json the app reads, so the
  checks are not pinned to English and cannot drift from the shipped strings.
#>
$script:LangText = @{}

<#
  The language the labels below are matched against. It starts as `-Lang`, because on every path that
  LAUNCHES the app that IS the language: it is passed as `--lang`. `-UseRunning` is the one path with
  no command line, and there it is replaced by what the resident tray is actually in (T220) - see
  Resolve-TrayLang. Kept as one variable rather than threaded through Label's callers, because the
  whole point is that every label in a run resolves against the same language.
#>
$script:LangCode = $Lang

<# One language file's raw text, read once. Asked for by code rather than reached for in the hashtable:
   `Label` stops at the first file that defines the key, so a table nobody asked for by name is a table
   that may never have been loaded - which is how the panel walk came to run over zero panels (T235). #>
function Lang-Table($code) {
    if (-not $script:LangText.ContainsKey($code)) {
        $file = Join-Path (Split-Path -Parent $PSScriptRoot) "lang\$code.json"
        if (-not (Test-Path $file)) { throw "no language file for '$code' ($file)" }
        $script:LangText[$code] = Get-Content -LiteralPath $file -Encoding UTF8 -Raw
    }
    return $script:LangText[$code]
}

function Label($key) {
    # Read by regex, not ConvertFrom-Json: the lang files carry `//` section comments, which are not
    # JSON. Falls back to en.json for a missing key, exactly as the app does.
    foreach ($code in @($script:LangCode, 'en')) {
        $pattern = '"' + [regex]::Escape($key) + '"\s*:\s*"((?:[^"\\]|\\.)*)"'
        if ((Lang-Table $code) -match $pattern) {
            return ($Matches[1] -replace '\\"', '"' -replace '\\\\', '\')
        }
    }
    throw "no lang file defines '$key'"
}

<#
  What language a tray THAT IS ALREADY RUNNING answers in (T220).

  `-Lang` is passed as `--lang` on the command line, so on every launching path it is the language by
  construction. `-UseRunning` exists precisely because there is no command line - it attaches to a
  process somebody else started - and that tray picked its language the way L.Detect/L.Resolve do: the
  saved preference in settings.json if it names a shipped code, otherwise the Windows display language,
  English when nothing matches. This mirrors that, and nothing more: it is a READING of the same two
  inputs the app read, not a second policy.

  What it cannot see is a resident tray that was itself launched with `--lang` (a process-only override
  that leaves the saved preference untouched). That is a developer running the app by hand, and it is
  why the answer is reported out loud rather than assumed - the run prints the language it resolved and
  where from, so a wrong one is visible in the log instead of arriving as four FAILs for labels that
  are all on screen.
#>
function Resolve-TrayLang {
    $codes = @('en', 'pt-BR', 'pt-PT', 'fr', 'es')   # L.Languages, in its own order

    $file = Join-Path $env:LOCALAPPDATA "ClaudeTray\settings.json"
    if (Test-Path $file) {
        try {
            $pref = (Get-Content -LiteralPath $file -Encoding UTF8 -Raw | ConvertFrom-Json).Language
            # "auto", absent, or a code no shipped language carries all mean the same to L.Resolve: fall
            # through to the OS. Matching that exactly is the point - a preference the app would have
            # rejected must not be honoured here either.
            if ($pref -and $codes -contains $pref) {
                return [pscustomobject]@{ Code = $pref; From = "the saved preference in settings.json" }
            }
        } catch {
            # A settings file that cannot be read is not a reason to guess English: fall to the OS, which
            # is what the app does when the preference is unusable.
        }
    }

    # L.Detect: pt-PT matched exactly so a generic pt-* still lands on pt-BR, then the two-letter names.
    try {
        $ui = (Get-UICulture)
        $two = $ui.TwoLetterISOLanguageName.ToLowerInvariant()
        $code = if ($ui.Name -ieq 'pt-PT') { 'pt-PT' }
                elseif ($two -eq 'pt')     { 'pt-BR' }
                elseif ($two -eq 'fr')     { 'fr' }
                elseif ($two -eq 'es')     { 'es' }
                else                       { 'en' }
        return [pscustomobject]@{ Code = $code; From = "the Windows display language ($($ui.Name))" }
    } catch {
        return [pscustomobject]@{ Code = 'en'; From = "the fallback (no preference, no readable UI culture)" }
    }
}

<#
  Every settings panel the page declares, derived rather than listed (T196). A panel is a sidebar item
  and a sidebar item carries a `settings.nav.*` label, so a panel added later is swept by the existing
  walk with no edit here - the failure mode of a hardcoded list is silently not checking the thing it
  was written for (§XV.3, and T161 before it).
#>
function Settings-PanelKeys {
    if ($script:PanelKeys) { return $script:PanelKeys }
    # English by name, because English is the file that has every key - a translation may be missing one
    # and the panel it names would silently drop out of the walk. Read through Lang-Table, not out of the
    # hashtable: `Label 'settings.nav.general'` loads whatever -Lang asked for and returns before it ever
    # touches `en`, so under a non-English run this used to read an unloaded table and derive nothing (T235).
    $keys = @()
    foreach ($m in [regex]::Matches((Lang-Table 'en'), '"(settings\.nav\.[A-Za-z]+)"\s*:')) {
        $keys += $m.Groups[1].Value
    }
    $script:PanelKeys = $keys
    return $keys
}

<#
  Click a settings sidebar item by its label. The items are bare `Border`s with no automation peer, so
  they are matched by the `TextBlock` inside and disambiguated by x-position - the page title carries the
  same words (trap 2). Returns whether the item was there to click.
#>
function Nav-Settings($win, $label) {
    $textCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $nav = @($win.FindAll('Descendants', $textCond) |
             Where-Object { $_.Current.Name -eq $label } |
             Sort-Object { $_.Current.BoundingRectangle.X })
    if ($nav.Count -eq 0) { return $false }
    ClickCentre $nav[0]
    Start-Sleep -Milliseconds 800
    return $true
}

<#
  The controls `SettingsRow` is responsible for naming, and only those: a `ComboBox`, `Slider` or
  `TextBox` has no content a name can be derived from, and a switch is a contentless `ToggleButton`
  (which reaches UIA as a Button carrying TogglePattern). Everything else on a panel is either a Button
  with its own text - `Save`, `Browse…`, `Add…`, which the rule must leave alone - or a framework
  template part: a Slider's `DecreaseLarge`/`IncreaseLarge` and a ScrollBar's `PageUp`/`PageDown` are
  plain unnamed Buttons, and they are excluded by *what they are* rather than by a list of their ids.
#>
<#
  The rows of whatever panel is on screen, as UIA Groups (T204). `SettingsRow` reaches the tree as a
  Group carrying its Header as the name, which is the element that says "this control and that text
  are one row" - without it a control can only be checked against "some non-empty string", and a rule
  handing every control the WRONG header reads exactly like one handing out the right ones.
  The class name is what separates a row from the other groups a panel carries.
#>
function Row-Groups($root) {
    $CT = [System.Windows.Automation.ControlType]
    return @($root.FindAll('Descendants', $ANY) | Where-Object {
        $_.Current.ControlType -eq $CT::Group -and $_.Current.ClassName -eq 'SettingsRow'
    })
}

# Takes any root, not just the window: the per-row pass below hands it one row's Group.
function Row-Controls($root) {
    $CT = [System.Windows.Automation.ControlType]
    $toggle = [System.Windows.Automation.TogglePattern]::Pattern
    return @($root.FindAll('Descendants', $ANY) | Where-Object {
        $t = $_.Current.ControlType
        if ($t -eq $CT::ComboBox -or $t -eq $CT::Slider -or $t -eq $CT::Edit) { return $true }
        if ($t -ne $CT::Button) { return $false }
        try { return $null -ne $_.GetCurrentPattern($toggle) } catch { return $false }
    })
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

        # Before anything is driven, and only here: every assertion below is about synthesised input, and
        # input goes to the foreground whoever owns it (T256). Asked after the raise, so what it reports
        # is a window that would not yield rather than one that had not been asked yet.
        if (-not (Owns-Foreground $proc "the keyboard path under the tray's own pump (T135)")) { return }

        # Trap 2, stated as an observation rather than an assertion: the window opens on General, so
        # the Claude Code pane is collapsed and none of its controls exist in the tree yet.
        if (ById $win 'DirectoryBox' 600) {
            Info "note: the Claude Code pane's controls are reachable without navigating"
        } else {
            Info "collapsed pane is absent from the UIA tree (expected) - navigating to it"
        }

        $navLabel = Label 'settings.nav.claudeCode'
        if (-not (Nav-Settings $win $navLabel)) {
            Fail "no sidebar item named '$navLabel' (is -Lang $Lang right?)"; return
        }

        $box = ById $win 'DirectoryBox'
        if (-not $box) { Fail "DirectoryBox not found after navigating to the Claude Code page"; return }
        Pass "navigated to the Claude Code page by clicking the sidebar"

        # --- typing reaches the control
        $vp = [System.Windows.Automation.ValuePattern]::Pattern
        $wasText = [string]$box.GetCurrentPattern($vp).Current.Value
        $marker = "T142check"
        # A SetFocus that throws mid-run is the same precondition failing later, not a different defect:
        # something took the foreground while this was driving. Ask, and let the answer decide which of
        # the two readings it is — owning the foreground and still being refused IS a failure (T256).
        try { $box.SetFocus() }
        catch {
            if (-not (Owns-Foreground $proc "typing under the tray's own pump (T135)")) { return }
            Fail "SetFocus on DirectoryBox threw while this run owned the foreground - $($_.Exception.Message)"
            return
        }
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
            try { $slider.SetFocus() }
            catch {
                if (-not (Owns-Foreground $proc "an arrow key driving a Slider (T135)")) { return }
                Fail "SetFocus on RetrySlider threw while this run owned the foreground - $($_.Exception.Message)"
                return
            }
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
  sets SelectedIndex directly, and it is the thing this case exists to stop trusting. Returns the route
  taken AND the number of selection changes it took, so a pass says how the switch was made instead of
  implying the first one — and so T166's timing stays observable on either route (T193).

  `$win` is optional and only for the timing observation: given it, the post-selection settle is spent
  watching the status line rather than sleeping through the very interval it appears in.

  **Why the hop count is part of the answer.** T166's claim is about *one* switch: coming back to a profile
  seen seconds ago must not blank its panes. A route that walks the selection through every index on the
  way makes each intermediate stop a switch of its own, so the status line that shows is some other
  profile's and the observation is void. The old fallback did exactly that — `UP`×n to normalise to the top,
  then `DOWN`×index — so the timing assertion was wired to the UIA route only and silently became a note
  whenever `Select()` threw, which is the whole reason that fallback exists. `Home` reaches index 0 in one
  keystroke and `End` the last one, so anchoring at whichever end is nearer costs at most one change for a
  two-profile picker and the assertion holds on both routes.
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
            return @{ Route = 'UIA'; Hops = 1 }
        } catch { Info "SelectionItemPattern.Select threw - $($_.Exception.Message)" }
    }
    # Fallback, and a real user's route: a closed WPF ComboBox moves its selection with the arrow keys.
    # Kept because a dead UIA path here would report a red build for a bug in this script.
    try { $combo.GetCurrentPattern($ec).Collapse() } catch { }
    try { $combo.SetFocus() } catch { Info "combo SetFocus threw - $($_.Exception.Message)" }
    Start-Sleep -Milliseconds 250

    # Where the selection is now, so the cheapest anchor can be chosen rather than assumed. Unknown (-1)
    # when the label cannot be matched, and then the top anchor is the only safe one.
    $count = $items.Count
    $atText = Combo-SelectedText $combo
    $at = -1
    for ($i = 0; $i -lt $count; $i++) { if ([string]$items[$i].Current.Name -eq $atText) { $at = $i; break } }

    # Three candidate routes, costed in selection changes; the arrow walk from where we already are is
    # available only when that position is known.
    $viaHome = if ($index -eq 0) { 1 } else { 1 + $index }
    $viaEnd  = if ($index -eq $count - 1) { 1 } else { 1 + ($count - 1 - $index) }
    $viaHere = if ($at -ge 0) { [Math]::Abs($index - $at) } else { [int]::MaxValue }

    $hops = [Math]::Min($viaHere, [Math]::Min($viaHome, $viaEnd))
    if ($hops -eq $viaHere) {
        $key = if ($index -gt $at) { $VK_DOWN } else { $VK_UP }
        for ($i = 0; $i -lt $viaHere; $i++) { [Native]::Key($key) }
    }
    elseif ($hops -eq $viaHome) {
        [Native]::Key($VK_HOME)
        for ($i = 0; $i -lt $index; $i++) { [Native]::Key($VK_DOWN) }
    }
    else {
        [Native]::Key($VK_END)
        for ($i = 0; $i -lt ($count - 1 - $index); $i++) { [Native]::Key($VK_UP) }
    }
    if ($win) { Watch-Status $win 250 } else { Start-Sleep -Milliseconds 250 }
    return @{ Route = 'keyboard'; Hops = $hops }
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
        Unchecked "the profile switch (T164, T166)" "this needs 2+ Claude Code profiles; --profiles reports $count"
        Info "The panes being readable at all is -Case Panes, which ran regardless."
        return
    }

    $computing = Label 'stats.computing'
    $liveOff   = Label 'stats.live.off'

    try {
        $win = Acquire-Main
        if (-not $win) { Fail "the main window never appeared"; return }

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
                Info "selected index $step through the $($route.Route) route, $($route.Hops) selection change(s)"
            }
            $stop = Read-ProfileStop $win $computing
            if ($stop.Kind -in @('working', 'blank')) {
                Report-NoReading $win $stop "stop $($stops.Count + 1) (index $step)"
                return
            }
            $stop | Add-Member -NotePropertyName Index -NotePropertyValue $step
            $stop | Add-Member -NotePropertyName Label -NotePropertyValue (Combo-SelectedText $combo)
            # Route and hop count flattened onto the stop: T166's guard below asks how many selection
            # changes THIS switch took, and a nested hashtable would read as a property that is always set.
            $stop | Add-Member -NotePropertyName Route -NotePropertyValue $(if ($route) { $route.Route } else { $null })
            $stop | Add-Member -NotePropertyName Hops  -NotePropertyValue $(if ($route) { [int]$route.Hops } else { 0 })
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
            Unchecked "T166's switch-back timing" ("the switch stops did not both show a report, so there " +
                      "was no cached one to put back")
        }
        elseif ($stops[2].Hops -gt 1) {
            # More than one selection change means the intermediate stops were switches of their own, so the
            # line that showed is some other profile's. `Combo-Select` now anchors on Home/End to keep this
            # at one hop on the keyboard route too (T193), so this is the genuinely-ambiguous case only.
            Unchecked "T166's switch-back timing" ("the return took $($stops[2].Hops) selection changes by " +
                      "the $($stops[2].Route) route, so the status line seen is not this switch's")
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
                Unchecked "the live headline following the switch (T164)" `
                          "no stop showed the panes, so the headline was never on screen to read"
            } else {
                Pass "the live headline is a reading, not 'unavailable', at all $seen pane stops"
            }
        }
    }
    finally { Release-Main }
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

    try {
        $win = Acquire-Main
        if (-not $win) { Fail "the main window never appeared"; return }

        # The headers first, and separately: T165's defect left every one of these perfectly readable
        # and took the whole body with them, so "headers but no body" is the exact shape to name.
        #
        # The set is DERIVED, not listed (T358). It used to name three keys by hand, and the window has
        # carried four since T328 — so the case reported "all three tab headers read" against a four-tab
        # window and the newest pane had never been asked whether it was in the tree at all. That is the
        # failure mode the automation-id check already avoids by deriving its types: a hardcoded list
        # silently stops covering the thing it was written for.
        #
        # From en.json rather than from the tree, and that is the point: the tree is what is being
        # asserted, so an expected set read out of it could never notice a header that had gone missing.
        # A tab added later ships a `stats.tab.*` string, and the cover follows it with no edit here.
        $tabKeys = @([regex]::Matches((Lang-Table 'en'), '"(stats\.tab\.[a-z]+)"\s*:') |
                     ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        if ($tabKeys.Count -lt 3) { Fail "only $($tabKeys.Count) stats.tab.* key(s) in en.json - the derivation is wrong, not the window"; return }
        $tabs = @($tabKeys | ForEach-Object { Label $_ })
        $missing = @($tabs | Where-Object { -not (ByName $win $_ 8000) })
        if ($missing.Count -gt 0) {
            Fail "these tab headers are not in the tree: $($missing -join ', ')"
        } else {
            Pass "all $($tabs.Count) tab headers read ($($tabs -join ' / '))"
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
            Unchecked "the pane body being in the tree (T165)" `
                      "the window shows the status line, not a report: '$($stop.Status)'"
        }
        else {
            Report-NoReading $win $stop "the Statistics page"
        }
    }
    finally { Release-Main }
}

# ---------------------------------------------------------------- sessions case

<#
  The Sessions pane, driven (T359).

  T358 taught the panes case to ask whether every tab is in the tree, so this pane is known to be
  *present*. Nothing touched it. Its whole reason to exist is what happens when you do, and all three of
  those paths were verified by nobody:

    a row click unfolds the call tree that produced the conversation (T329)
    the token header re-sorts the list
    the info dot opens the note explaining the list-price figure (T346)

  The last one is the case for this file existing at all. A WPF `Popup` is its own top-level window, so
  `--capture-stats` cannot photograph it - the flag catalogue says so in as many words - and no published
  screenshot ever will. Whether that note is readable at all is a question only the accessibility tree
  can answer.

  The pane renders asynchronously: the list arrives from a transcript scan, and a case that reads before
  it lands reads the placeholder. That is T343's trap for the capture path, inherited here - so this
  waits for a row and says so when none arrives, rather than asserting against "Reading your
  transcripts...".
#>
function Invoke-SessionsCase {
    Head "Sessions - the pane a capture cannot finish checking"
    try {
        $win = Acquire-Main
        if (-not $win) { Fail "the main window never appeared"; return }

        $tab = ById $win 'SessionsPane' 8000
        if (-not $tab) { Fail "the Sessions tab is not in the tree at all"; return }
        # Selected, then CONFIRMED selected, then clicked if it did not take. WPF builds a tab's
        # content on its first visit, so a Select() that silently does not land leaves `SessionList`
        # never realised - and the case then blames a 40-second scan for a tab it never opened. Seen
        # alternating pass/degrade while this case was being written, which is the shape that teaches a
        # reader to re-run rather than to look.
        $selected = $false
        foreach ($attempt in 1..3) {
            try { $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { }
            Start-Sleep -Milliseconds 400
            try {
                $selected = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
            } catch { $selected = $false }
            if ($selected) { break }
            try { ClickCentre $tab } catch { }
            Start-Sleep -Milliseconds 400
            try {
                $selected = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
            } catch { $selected = $false }
            if ($selected) { break }
        }
        if (-not $selected) { Fail "the Sessions tab never became the selected one, so its pane was never built"; return }

        # The scan, waited out rather than raced. A cold cache is seconds; the ceiling is generous
        # because the alternative is a case that fails on a slow machine and passes on a fast one.
        $list = ById $win 'SessionList' 40000
        $rows = @()
        if ($list) { $rows = @($list.FindAll('Children', $ANY)) }
        if ($rows.Count -eq 0) {
            Unchecked "everything this case drives (T359)" `
                      "no conversation row arrived within 40s - this profile's transcripts are empty, or the scan is still running, and a pane with no rows has nothing to click"
            return
        }
        Pass "the conversation list is in the tree ($($rows.Count) row(s))"

        # What a screen reader gets from one row. Not a re-assertion of the numbers a capture already
        # shows - the point is that they are *readable*, which a picture cannot tell you.
        $first = $rows[0]
        $cells = @(@($first.FindAll('Descendants', $ANY)) |
                   ForEach-Object { [string]$_.Current.Name } | Where-Object { $_ })
        if ($cells.Count -ge 4) {
            Pass "a row reads as text, not as a picture ($($cells.Count) fields, e.g. '$($cells[1])')"
        } else {
            Fail "a conversation row exposes only $($cells.Count) readable field(s) - a screen reader gets almost nothing"
        }

        # T329: the row is a disclosure, and what it discloses has to arrive in the tree.
        $before = $cells.Count
        ClickCentre $first
        # Polled, not slept. The tree is walked on the first expand, so how long it takes is a property
        # of the conversation that happens to be newest - and a fixed wait is either too short on the
        # day it matters or wasted on every other one.
        $after = @(); $taskLines = @()
        $w = [Diagnostics.Stopwatch]::StartNew()
        while ($w.ElapsedMilliseconds -lt 15000) {
            $after = @(@($first.FindAll('Descendants', $ANY)) |
                       ForEach-Object { [string]$_.Current.Name } | Where-Object { $_ })
            $taskLines = @($after | Where-Object { $_ -like '*TaskLine*' })
            if ($taskLines.Count -gt 0) { break }
            Start-Sleep -Milliseconds 250
        }
        if ($taskLines.Count -gt 0 -and $after.Count -gt $before) {
            Pass "clicking a row unfolds its call tree into the tree ($before -> $($after.Count) fields, $($taskLines.Count) task line(s))"
        } else {
            Fail "clicking a row added no call tree - $before field(s) before, $($after.Count) after, $($taskLines.Count) task line(s) (T329)"
        }

        # T346's note, behind the one control in this window whose content a capture provably cannot
        # reach. Asserted on the rate-card date it carries, because that is the sentence the figure
        # depends on: a note that has lost it is a figure with no provenance.
        $dot = ById $win 'SessionsMethodInfo' 8000
        if (-not $dot) {
            Fail "the Sessions method button is not in the tree (T346)"
        } else {
            ClickCentre $dot
            # Through `Label`, not through a literal (T361). It matched the English words 'list prices',
            # so the assertion found nothing in the other four languages and reported the note as
            # unreadable - loud here, but the same mistake one step over is an assertion that matches
            # nothing and passes. The string interpolates the date the rates were read, so what
            # identifies it is the fixed part before the placeholder; the date itself is what the
            # assertion is about and is checked as a pattern below.
            $noteHead = ((Label 'stats.sessions.method') -split '\{0\}')[0].Trim()
            $note = @()
            $w = [Diagnostics.Stopwatch]::StartNew()
            while ($w.ElapsedMilliseconds -lt 8000) {
                $note = @(@($win.FindAll('Descendants', $ANY)) |
                          ForEach-Object { [string]$_.Current.Name } |
                          Where-Object { $_ -like "*$noteHead*" })
                if ($note.Count -gt 0) { break }
                Start-Sleep -Milliseconds 200
            }
            if ($note.Count -gt 0) {
                $dated = @($note | Where-Object { $_ -match '\d{4}-\d{2}-\d{2}' })
                if ($dated.Count -gt 0) {
                    Pass "the list-price note opens and reads, carrying the date the rates were read"
                } else {
                    Fail "the note opened but names no rate-card date - the figure it explains has no provenance (T346)"
                }
            } else {
                Fail "the info dot opened no readable note - a popup a capture cannot see and a screen reader cannot either (T346)"
            }
            # Leave the window as it was found: the popup is a toggle and the next case shares this window.
            ClickCentre $dot
            Start-Sleep -Milliseconds 400
        }
    }
    finally { Release-Main }
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

  The settings rows are the same shape thirty-odd times over (`SettingsRow` = label left, control right),
  and the rule that names them was asserted on three controls of one panel out of six (T196). Now every
  panel the page declares is opened - the list comes from the `settings.nav.*` labels, so a panel added
  later is swept without an edit here - and every control the rule is responsible for is read. Three
  shapes still get an exact-label read, because the sweep can only see THAT a control is named, not that
  the name is its own row's header.

  Both branches of the rule are covered, which is the point: the one that must fire (a ComboBox, Slider,
  TextBox or contentless switch takes the header) and the one that must NOT (a field with a labelled
  Button beside it - the field takes the header, each Button keeps its own text). Getting the second wrong
  gives three controls in one row the same name, which is worse for a screen reader than one unnamed.

  `--main`, not `--settings`: one launch reaches both pages, and it is the shell the tray opens.
#>
function Invoke-NamesCase {
    Head "Names - what the controls announce to a screen reader"

    $read = 0
    try {
        $win = Acquire-Main
        if (-not $win) { Fail "the main window never appeared"; return }

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
            Unchecked "the profile picker's accessible name (T175)" `
                      "the profile card is collapsed on a $count-profile machine"
        }
        else {
            Fail "the profile picker is not in the tree although --profiles found $count profiles"
        }

        # The method button is collapsed until a report renders, so this waits out the first poll.
        if (Assert-Name $win 'MethodInfo' (Label 'stats.methodTitle') "the method-note button" 25000) {
            $read++
        } else {
            Unchecked "the method-note button's accessible name (T175)" `
                      "no report rendered within 25s (offline? cold transcript cache?), so it never left Collapsed"
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

            # Three exact-label reads first, on the panel the page opens on: one of each shape the rule
            # has to handle (an ItemsControl, a contentless ToggleButton, a Slider inside a StackPanel).
            # These assert the name is the RIGHT string, which the sweep below cannot - it has no way to
            # know which row a control belongs to.
            $rows = @(
                @{ Id = 'LanguageCombo'; Key = 'settings.general.appLanguage';    What = "the language picker" }
                @{ Id = 'StartupCheck';  Key = 'settings.general.startWithWindows'; What = "the start-with-Windows switch" }
                @{ Id = 'IntervalSlider'; Key = 'settings.general.interval';      What = "the refresh-interval slider" }
            )
            foreach ($row in $rows) {
                if (Assert-Name $win $row.Id (Label $row.Key) $row.What) { $read++ }
                else { Fail "$($row.What) ($($row.Id)) is not in the tree after navigating to Settings" }
            }

            # T196: the rule governs thirty-odd controls across six panels and used to be asserted on the
            # three above, all on one panel. Every panel is now visited and every control the rule is
            # responsible for is read, so a row added to a panel nobody checked is covered.
            # Opened and asserted counted apart: a panel with no row control was still visited, and
            # rolling the two together would report it as one that got away.
            # T235: the guard on the precondition, before the walk rather than after it. A derived list
            # that resolves to nothing makes every loop below run zero times and every assertion in them
            # report nothing at all - a clean run over no panels, which is §XV.3's defect reached through
            # the language flag instead of through a Skip. Gated on the list being empty, which is the
            # precondition itself and not a weaker form of the rule the panels are walked for.
            $panelKeys = @(Settings-PanelKeys)
            if ($panelKeys.Count -eq 0) {
                Fail ("no settings panel could be derived from lang\en.json - the walk below would " +
                      "report a clean run over zero panels (T235)")
            }
            $opened = 0
            $asserted = 0
            foreach ($key in $panelKeys) {
                $panel = Label $key
                if (-not (Nav-Settings $win $panel)) {
                    Unchecked "the row rule on the '$panel' panel (T175)" `
                              "no sidebar item carries that label - it was never opened"
                    continue
                }
                $opened++
                $controls = Row-Controls $win
                if ($controls.Count -eq 0) {
                    # Not a failure: About holds prose and links, and a panel is allowed to have no row
                    # the rule applies to. Said out loud so "0 read" is never mistaken for "0 asserted".
                    Info "'$panel': no ComboBox, Slider, TextBox or switch - nothing for the rule to name"
                    continue
                }
                $unnamed = @()
                foreach ($c in $controls) {
                    $nm = [string]$c.Current.Name
                    # A glyph codepoint satisfies "non-empty" and is not a name (T175's worst case), so
                    # the printable form is what gets compared and what gets reported.
                    if (-not $nm -or (Printable $nm) -ne $nm -or $nm -eq [string]$c.Current.AutomationId) {
                        $unnamed += "$($c.Current.AutomationId)='$(Printable $nm)'"
                    }
                }
                if ($unnamed.Count -gt 0) {
                    Fail "'$panel': $($unnamed.Count) of $($controls.Count) row control(s) announce nothing a screen reader can use (T175) - $($unnamed -join ', ')"
                } else {
                    Pass "'$panel': all $($controls.Count) row control(s) announce a label"
                    $read += $controls.Count
                }

                # T204: the same controls, now against the header of the row they are ACTUALLY in. The
                # sweep above proves a name exists; this proves it is the right one, which is the half
                # that was structurally impossible before the row had a peer.
                $groups = @(Row-Groups $win)
                if ($groups.Count -eq 0) {
                    Fail "'$panel': $($controls.Count) row control(s) and no SettingsRow group in the tree - the row's automation peer is gone, so nothing can be checked against its own header (T204)"
                } else {
                    # Every header on the panel, so "announces a header" can be told from "announces THE
                    # WRONG header" - the failure the flat sweep is blind to by construction.
                    $headers = @{}
                    foreach ($g in $groups) { $h = [string]$g.Current.Name; if ($h) { $headers[$h] = $true } }

                    $paired = 0; $ownHeader = 0; $ownText = 0; $wrong = @()
                    foreach ($g in $groups) {
                        $h = [string]$g.Current.Name
                        if (-not $h) { continue }
                        $inRow = @(Row-Controls $g)
                        # A row of prose, or one whose control the rule does not govern. Counted apart so
                        # the number reported is rows that actually paired, never rows that merely exist.
                        if ($inRow.Count -eq 0) { continue }
                        $paired++
                        foreach ($c in $inRow) {
                            $nm = [string]$c.Current.Name
                            if ($nm -eq $h) { $ownHeader++ }
                            elseif ($nm -and $headers.ContainsKey($nm)) {
                                # A Button keeping its own text is fine; a control wearing a NEIGHBOUR's
                                # header is the rule pairing the wrong two things.
                                $wrong += "$($c.Current.AutomationId)='$(Printable $nm)' sits in the '$h' row"
                            }
                            elseif ($nm) { $ownText++ }
                        }
                    }
                    if ($wrong.Count -gt 0) {
                        Fail "'$panel': $($wrong.Count) control(s) announce ANOTHER row's header (T204) - $($wrong -join ', ')"
                    } elseif ($paired -eq 0) {
                        Fail "'$panel': $($groups.Count) row group(s) in the tree but not one holds a row control - the controls are outside their rows, so no pairing was checked (T204)"
                    } else {
                        Pass "'$panel': $paired row(s) pair a control with their own header; $ownHeader announce it, $ownText carry their own text"
                    }
                }
                $asserted++
            }
            Info "$opened of $($panelKeys.Count) settings panel(s) opened, $asserted carried a row control"

            # The branch that must NOT fire, and the reason the rule tests the content rather than the
            # type: a row holding a field with a labelled Button beside it must give the header to the
            # field only, and each Button keeps its own text. Getting this wrong gives three controls in
            # one row the same name, which is worse for a screen reader than one unnamed control.
            if (Nav-Settings $win (Label 'settings.nav.claudeCode')) {
                $trio = @(
                    @{ Id = 'DirectoryBox';        Key = 'settings.cc.workDir';       What = "the working-directory field" }
                    @{ Id = 'BrowseButton';        Key = 'settings.cc.browse';        What = "the Browse button beside it" }
                    @{ Id = 'ProfileAddButton';    Key = 'settings.cc.profileAdd';    What = "the profile row's Add button" }
                    @{ Id = 'ProfileRemoveButton'; Key = 'settings.cc.profileRemove'; What = "its Remove button" }
                )
                $names = @()
                foreach ($t in $trio) {
                    if (Assert-Name $win $t.Id (Label $t.Key) $t.What) {
                        $read++
                        $el = ById $win $t.Id 1000
                        if ($el) { $names += [string]$el.Current.Name }
                    }
                }
                # Same row, so a header that leaked onto the buttons would show up as one name three times.
                $shared = @($names | Group-Object | Where-Object { $_.Count -gt 1 })
                if ($shared.Count -gt 0) {
                    Fail "a row's controls share a name: $(($shared | ForEach-Object { "'$($_.Name)' x$($_.Count)" }) -join ', ') - the header leaked onto a Button that had its own text"
                } elseif ($names.Count -gt 1) {
                    Pass "a field and the Buttons beside it keep $($names.Count) distinct names"
                }
            } else {
                Unchecked "the field-plus-Button branch of the row rule (T175)" `
                          "the Claude Code panel could not be opened"
            }
        }

        # The founding trap (T142): a green tick over an empty read is worse than no check at all.
        if ($read -eq 0) {
            Fail "not one accessible name was read - this case observed nothing, so it passes nothing"
        } else {
            Info "$read control name(s) read"
        }
    }
    finally { Release-Main }
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
function Expand-Item($menu, $label, $attempts = 3) {
    $items = Menu-Items $menu
    $target = $items | Where-Object { $_.Current.Name -eq $label } | Select-Object -First 1
    if (-not $target) { return $null }
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)

    # T233: bounded attempts, and the count is said out loud. One Down-walk and one read is a coin toss
    # against a shell that drops synthesised input - three of ten runs here reported '$label did not
    # expand' against a build with nothing wrong with it, wearing the wording of the T148 defect. What
    # is deliberately NOT done is retrying until it passes: the attempts are capped, so a submenu that
    # genuinely stopped expanding still goes red, it just no longer does so at random.
    for ($try = 1; $try -le $attempts; $try++) {
        for ($i = 1; $i -le ($items.Count + 2); $i++) {
            [Native]::Key($VK_DOWN)
            $focused = Menu-Items $menu | Where-Object { $_.Current.HasKeyboardFocus } | Select-Object -First 1
            if ($focused -and $focused.Current.Name -eq $label) {
                [Native]::Key($VK_RIGHT)
                # Polled to a deadline rather than slept at for a fixed 700ms: the submenu appearing in
                # the tree is an event, and a sleep long enough to be safe is a sleep paid on every run.
                # WinForms nests an opened submenu under the parent item, not as a second top-level menu.
                $deadline = (Get-Date).AddMilliseconds(2500)
                do {
                    $subs = @($target.FindAll('Descendants', $cond))
                    if ($subs.Count -gt 0) {
                        if ($try -gt 1) { Info "'$label' expanded on attempt $try of $attempts" }
                        return $subs
                    }
                    Start-Sleep -Milliseconds 150
                } while ((Get-Date) -lt $deadline)
                break   # Right landed and nothing came up: start the walk over rather than press on
            }
        }
        if ($try -lt $attempts) {
            # Nothing is pressed to "reset" between attempts, and that is deliberate: reaching here means
            # the poll above saw no submenu, so there is none to close - and Left on a top-level entry
            # dismisses the WHOLE menu, the same way the header records Right doing. Measured: retrying
            # after a Left walked a menu that was no longer there, and all three attempts failed.
            # The Down-walk wraps, so it re-targets on its own.
            if ((Menu-Items $menu).Count -eq 0) {
                Info "attempt $try : the menu closed under the walk, so there is nothing left to expand"
                return $null
            }
            Info "attempt $try : '$label' did not expand, retrying"
            Start-Sleep -Milliseconds 300
        }
    }
    return $null
}

<#
  The app's own account of the menu, read ONCE and shared by every expectation below (T230).

  It used to be a launch of `--profiles` per question, which is a chance for the machine to move between
  them - the auto-follow probe alone can repoint the icon between two reads, and then the count and the
  check mark are being compared against two different states. The prose is hardcoded English in
  ProfilesCli, so it is read in English whatever language the menu is rendered in.

  Cached PER environment, because the sweep below drives several (T238). `$script:EnvArgs` is the one
  variable that decides both what the tray is launched with and what this is read with, so the two
  cannot be given different modes - which is the whole reason the expectations are derived at all.
#>
$script:ProfilesOut = @{}
function Profiles-ReadOut {
    # The same --sample-env the tray was launched with, or the expectations would describe the real
    # environment while the menu renders a sampled one (T231).
    $key = ($script:EnvArgs -join ' ')
    if (-not $script:ProfilesOut.ContainsKey($key)) {
        $script:ProfilesOut[$key] = & $Exe "--lang" "en" @script:EnvArgs "--profiles" 2>&1 | Out-String
    }
    return $script:ProfilesOut[$key]
}

<# What the app itself says the menu should contain, so the check has something to be wrong about. #>
function Expected-ProfileEntries {
    $out = Profiles-ReadOut
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
    if ((Profiles-ReadOut) -match 'polled every interval:\s*\d+\s+of\s+(\d+)') { return [int]$Matches[1] }
    return 0
}

<#
  The state the Profile submenu is supposed to be RENDERING (T230), every field of it derived from the
  app rather than typed here: which profile the icon follows, which one the environment selects and
  whether those two agree, and the two toggles' positions. A hardcoded expectation would be a second
  source of truth for the thing being checked, and the one that drifts silently.
#>
function Expected-ProfileState {
    $out = Profiles-ReadOut
    # `menu     "Personal" -> claude` - the profile's own Label, which is an account or org name and so
    # is the same string in every language, unlike everything else on the entry.
    $labels = @([regex]::Matches($out, '(?m)^\s*menu\s+"([^"]+)"\s*->') | ForEach-Object { $_.Groups[1].Value })
    $icon   = if ($out -match '(?m)^icon follows:\s+(.+?)\s\s')        { $Matches[1] } else { $null }
    $env    = if ($out -match '(?m)^environment selects:\s+(.+?)\s\s') { $Matches[1] } else { $null }
    return [pscustomobject]@{
        Labels     = $labels
        Icon       = $icon
        Env        = $env
        EnvAgrees  = [bool]($out -match '- agrees with the icon')
        EnvOutside = ($env -eq 'none of these')
        SyncOn     = [bool]($out -match 'a pick reaches:.*\[x\]')
        FollowOn   = [bool]($out -match '(?m)^auto-follow:\s+on')
    }
}

<#
  A menu entry's toggle position: 'On', 'Off', or $null when it carries no toggle at all.

  A plain `ToolStripMenuItem` exposes TogglePattern only WHILE IT IS CHECKED - an unchecked one carries
  no pattern, so "off" and "not a toggle" were one reading and only On could be asserted. T234 gave the
  two switches `AccessibleRole.CheckButton`, which reports the pattern in both positions; so $null now
  means "this is not a switch", which is itself a failure for an entry that should be one.
#>
function Menu-Toggle($item) {
    try {
        return [string]$item.GetCurrentPattern(
            [System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState
    } catch { return $null }
}

<#
  Whether a menu entry is showing its check mark.

  Read off the announced description, not off TogglePattern. T234 gave the entries a custom accessible
  object - the only way a sentence beside the name reaches a client at all - and that costs the pattern
  the framework's own object supplies WHILE CHECKED. The state is announced as a word instead, in front,
  which loses nothing: the pattern was never readable in the off position anyway, and now both are.
#>
function Menu-Checked($item) { return Menu-Announces $item (Label 'menu.itemChecked') }

<#
  Whether an entry announces a state word. Matched as a PREFIX and only as a prefix: the state is written
  in front of the sentence for exactly this reason, since the sentence behind it is free text that can
  contain either word ("Turning it off puts the variable back the way it was").
#>
function Menu-Announces($item, $word) {
    $said = Menu-Description $item
    if (-not $said) { return $false }
    return $said.StartsWith($word, [System.StringComparison]::OrdinalIgnoreCase)
}

<# PowerShell 5.1 has no ternary; this keeps the two-branch label lookups on one line. #>
function if-else($cond, $a, $b) { if ($cond) { return $a } else { return $b } }

<#
  What an entry says to a screen reader beyond its own text.

  `ToolTipText` reaches no accessibility property, and it is where the Profile submenu states the SCOPE
  of a pick - the tray only, or the tray and the Windows user environment (T171), and the line about a
  running session keeping what it started with (T172). T234 mirrors it into `AccessibleDescription`,
  which arrives here as the LegacyIAccessible description; HelpText is read first in case a future
  framework maps it there instead.
#>
function Menu-Description($item) {
    foreach ($prop in @([System.Windows.Automation.AutomationElement]::HelpTextProperty)) {
        try { $v = [string]$item.GetCurrentPropertyValue($prop); if ($v) { return $v } } catch { }
    }
    try {
        return [string]$item.GetCurrentPattern(
            [System.Windows.Automation.LegacyIAccessiblePattern]::Pattern).Current.Description
    } catch { return '' }
}

<#
  The Profile submenu's contents, against what the app says they should be (T230).

  This submenu is where the profile work lives - the check mark, T139's pinned marker, T126's active-ago
  suffix, T171's machine-wide toggle, T172's environment mark - and until now the walk stopped at
  counting its entries. Every one of those is a string built in PopulateProfileMenu and shown to nobody
  but a person who opens the submenu, so the headless read-out covering the decisions behind them is not
  the same thing as this covering the rendering.

  The caller has already asserted the count, which is the order that matters: a submenu that failed to
  expand reads as zero entries, and zero entries is how a check passes by seeing nothing.
#>
function Assert-ProfileSubmenu($subs) {
    $want = Expected-ProfileState
    if ($want.Labels.Count -eq 0) {
        Fail "--profiles named no profile labels, so the submenu's entries can be compared to nothing"
        return
    }

    # One entry per profile, matched by the label the app itself printed. Identity, not arithmetic: the
    # count already passed above, and a count is satisfied by any N entries including the wrong ones.
    $byLabel = @{}
    foreach ($lab in $want.Labels) {
        $hit = @($subs | Where-Object { $_.Current.Name -like "$lab *" })
        if ($hit.Count -eq 0)     { Fail "no Profile submenu entry for '$lab', which --profiles lists" }
        elseif ($hit.Count -gt 1) { Fail "'$lab' has $($hit.Count) entries in the Profile submenu" }
        else                      { $byLabel[$lab] = $hit[0] }
    }
    if ($byLabel.Count -ne $want.Labels.Count) { return }
    Pass "every profile --profiles lists has one entry: $($want.Labels -join ', ')"

    # The check mark, on the profile the icon follows and on no other (T126/T139). Reading zero checks is
    # a FAIL and not a quiet pass - it is exactly what a Checked that stopped being set would look like.
    $checked = @($want.Labels | Where-Object { Menu-Checked $byLabel[$_] })
    if ($checked.Count -ne 1) {
        Fail ("$($checked.Count) of $($want.Labels.Count) profile entries carry the check mark, expected " +
              "exactly one - the icon's profile is '$($want.Icon)'")
    } elseif ($checked[0] -ne $want.Icon) {
        Fail "the check mark is on '$($checked[0])', but the icon follows '$($want.Icon)'"
    } else {
        Pass "the check mark is on '$($want.Icon)', the profile the icon follows, and on no other"
    }

    # T139's pinned marker and the entry that undoes it are one state rendered twice: PopulateProfileMenu
    # shows "resume following" exactly when the icon is pinned AND auto-follow is on. So they are checked
    # against each other - the only two-sided reading available, since --profiles does not print the pin.
    $pinMark = Label 'menu.profilePinned'
    $pinned  = ($want.Icon -and $byLabel[$want.Icon].Current.Name -like "*$pinMark*")
    $resume  = @($subs | Where-Object { $_.Current.Name -eq (Label 'menu.profileUnpin') })
    if ($pinned -and $resume.Count -eq 0) {
        Fail "'$($want.Icon)' is marked '$pinMark' but the submenu offers no way to undo it (T139)"
    } elseif (-not $pinned -and $resume.Count -gt 0) {
        Fail "the submenu offers '$(Label 'menu.profileUnpin')' while no entry carries '$pinMark' (T139)"
    } elseif ($pinned) {
        Pass "'$($want.Icon)' is marked '$pinMark' and the submenu offers the way back (T139)"
    } else {
        Unchecked "the pinned marker and its undo (T139)" `
                  "no profile is pinned here, so neither is rendered and there is nothing to compare"
    }

    # The two toggles, each present and each reading the position --profiles reports (T126, T171). The
    # machine-wide one is the half of a pick that is NOT the tray, and the reason it sits here at all is
    # that a user just told the pick stops at the tray must not have to go hunting for it.
    foreach ($t in @(@{ Key = 'menu.profileFollow';  On = $want.FollowOn; What = 'auto-follow (T126)' },
                     @{ Key = 'menu.profileEnvSync'; On = $want.SyncOn;   What = 'the machine-wide switch (T171)' })) {
        $lab  = Label $t.Key
        $item = @($subs | Where-Object { $_.Current.Name -eq $lab }) | Select-Object -First 1
        if (-not $item) { Fail "'$lab' is missing from the Profile submenu - $($t.What)"; continue }
        # Both positions, since T234. A `ToolStripMenuItem` announces a toggle only while checked, so the
        # position is said in words in the description instead - which is the form a reader hears anyway.
        # NOT $want: that name holds Expected-ProfileState for the whole function, and assigning it here
        # left every check after this loop comparing against a string.
        $wantState = Label (if-else $t.On 'menu.switchOn' 'menu.switchOff')
        $said      = Menu-Description $item
        if (-not $said) {
            Fail "'$lab' announces nothing at all - $($t.What) is invisible to a reader (T234)"
        } elseif (-not (Menu-Announces $item $wantState)) {
            Fail "'$lab' announces '$said', but --profiles reports $($t.What) as '$wantState'"
        } else {
            Pass "'$lab' announces '$wantState' - $($t.What), and off is now a reading (T234)"
        }
    }

    # T234. The sentence saying how far a pick reaches is the reason the tooltip exists: the icon moving
    # reads as "the machine is now on this profile", and that is only true under the machine-wide switch.
    # It lived in `ToolTipText`, which reaches no accessibility property at all, so the person who most
    # needs it was the one who could not have it. Asserted on the icon's own entry, and against the
    # scope --profiles reports, so a mirrored-but-stale sentence fails too.
    $scopeKey = if ($want.SyncOn) { 'menu.profileScopeWindows' } else { 'menu.profileScopeTray' }
    $scope    = Label $scopeKey
    $entry    = if ($want.Icon) { $byLabel[$want.Icon] } else { $null }
    if (-not $entry) {
        Fail "no entry for the icon's profile, so the scope sentence has nothing to be read off (T234)"
    } else {
        $said = Menu-Description $entry
        if (-not $said) {
            Fail ("'$($want.Icon)' announces no description - T171's scope sentence is drawn in the " +
                  "tooltip and reaches no screen reader (T234)")
        } elseif ($said -notlike "*$scope*") {
            Fail ("'$($want.Icon)' announces '$said', which does not carry the scope --profiles reports " +
                  "('$scope')")
        } else {
            Pass "'$($want.Icon)' announces how far a pick reaches: '$scope' (T171, T234)"
        }
    }

    # T172's environment mark: the submenu says so when the profile the variable selects is not the one
    # on the icon. Nothing to assert while they agree - which is the state this machine is always in, and
    # the gap T231 names.
    if ($want.EnvAgrees) {
        if ($script:EnvSweepWillRun) {
            # Covered a moment later on the fixture, so calling it Unchecked here would be a hole in the
            # tally that is not one - and a DEGRADED exit code the run does not deserve (T238). Said as
            # Info rather than passed over: the mark genuinely is not rendered in THIS environment.
            Info ("the environment and the icon both select '$($want.Icon)' here, so no mark is drawn - " +
                  "the fixture sweep below is what asserts it (T172)")
        } else {
            Unchecked "the environment mark on a profile entry (T172)" `
                      ("the environment and the icon both select '$($want.Icon)' here, so the mark this " +
                       "asserts is not rendered - re-run without -SampleEnv/-UseRunning for the sweep (T238)")
        }
    } elseif ($want.EnvOutside) {
        # The label is a format string; only the part before {0} is fixed text to match on.
        $stem = ((Label 'menu.profileEnvOutside') -split '\{0\}')[0].TrimEnd()
        $line = @($subs | Where-Object { $_.Current.Name -like "$stem*" })
        if ($line.Count -gt 0) { Pass "the variable names a folder no profile covers, and the submenu says so (T172)" }
        else { Fail "--profiles says the environment selects none of these, but no submenu line reports it (T172)" }
    } else {
        $envMark = Label 'menu.profileEnvSelects'
        if (-not $want.Env -or -not $byLabel.ContainsKey($want.Env)) {
            Fail "--profiles says the environment selects '$($want.Env)', which has no submenu entry (T172)"
        } elseif ($byLabel[$want.Env].Current.Name -like "*$envMark*") {
            Pass "'$($want.Env)' carries '$envMark' - the environment's profile is not the icon's (T172)"
        } else {
            Fail "the environment selects '$($want.Env)' but its entry carries no '$envMark' mark (T172)"
        }
    }
}

<#
  A tray of our own, launched from `-Exe` and carrying whatever `$script:EnvArgs` currently says (T237).

  It is ALWAYS `--second-tray`, and that is the whole of T240. Passing it conditionally meant deciding
  from a `Get-Process` reading taken a second earlier: a tray that starts in that gap, or one still
  holding the mutex while it exits, is not in the reading, so the launch went without the flag, exited
  silently, and the run reported "the tray menu never opened" - a FAIL naming the menu for a tray that
  never came up. That happened once while T237 was being verified, and it is the hazard this file
  already records one function along, where `Close-Main` waits for the exit rather than trusting the
  kill "because a still-dying `--main` looks like the tray to it".

  Unconditional costs nothing: the mutex is taken when it is free either way, so a run on a machine with
  no resident tray behaves exactly as before. What it buys, besides the race, is that the icon tag and
  the observer promise (T239) no longer depend on a guess about the machine - every tray a check starts
  writes nothing and is found by the same pattern.

  Not the move T202 refused. That was IMPLYING `-UseRunning`, which silently swaps the binary under test
  for whatever was already there; this launches `-Exe`, the binary the caller named, so the ambiguity
  T236 exists to catch cannot arise.

  Returns the process and the pattern that finds ITS icon, or null with the reason in
  `$script:TrayStartError`.
#>
$script:TrayStartError = $null
function Start-CheckTray {
    $proc = Start-App "--lang $Lang $($script:EnvArgs -join ' ') --second-tray"
    Start-Sleep -Milliseconds 1500
    $proc.Refresh()
    # The mutex can no longer be the reason, so this is the tray refusing on its own - a --sample-env
    # mode this machine cannot produce is the one that actually happens (T231's catalogue refusal).
    if ($proc.HasExited) {
        $script:TrayStartError = "the tray exited immediately after launch" +
            $(if ($script:EnvArgs.Count) { " (launched with $($script:EnvArgs -join ' '))" })
        return $null
    }
    return [pscustomobject]@{
        Proc = $proc
        # Unanchored on purpose: the shell's accessible name for an icon is the name it was REGISTERED
        # with followed by the current tooltip ("Claude Code - connecting... [check] Profile: ..."), so
        # anchoring to the start matches the tooltip the tray had at startup and never the live one.
        IconPattern = '\[check\]'
    }
}

function Invoke-MenuCase {
    Head "Menu - the tray icon's menu as it is when it opens"

    $proc = $null
    $ownProcess = $false
    # Which notification-area icon is ours. Only `-UseRunning` needs this value: it attaches to a tray
    # that carries no tag, and "Claude Code" is the first half of every tooltip in every language, so it
    # identifies that icon without pinning the check to one locale. A tray this script LAUNCHES is always
    # tagged (T240), and Start-CheckTray returns the pattern for it.
    $iconPattern = 'Claude Code'
    if ($UseRunning) {
        # Read here and not at the top of the function: the only question this answers is "which process
        # did the caller mean", and asking it a second earlier is what T240 is about.
        $running = @(Get-Process -Name ClaudeTray -ErrorAction SilentlyContinue)
        if ($running.Count -eq 0) { Fail "-UseRunning given but no ClaudeTray process is running"; return }
        $proc = $running[0]
        Info "driving the ALREADY RUNNING tray, not -Exe: $($proc.Path)"

        # T236: named and compared, not just named. This does NOT return - every assertion below still
        # runs and still has to pass, because a check of the installed copy is a real thing to want. What
        # it cannot be is a clean run: the claim "the build under -Exe was checked" is the one that could
        # not be evaluated here, so that is the claim marked Unchecked and the run comes out DEGRADED.
        $drift = Binary-Drift $proc.Path
        if ($drift) {
            Unchecked "the menu of the build under -Exe (T236)" `
                ("-UseRunning drove the resident tray and $drift. What ran passed, " +
                 "and it did not run against what was asked about - a tray older than the feature being " +
                 "verified passes a check for an entry it does not have. Quit it to check -Exe instead.")
        }

        # T220: this is the one path with no command line, so `-Lang` never reached the process. Match the
        # labels against what the tray is actually in, or every one of them is compared to a translation
        # nobody is looking at - four FAILs for entries that were all on screen, in Portuguese.
        # T231's flag is a launch argument too, so it cannot reach this process either - and unlike the
        # language there is nothing to read back off the machine, because the whole point of a sampled
        # environment is that the machine is not in it.
        if ($SampleEnv) {
            Unchecked "the tray icon's menu on a sampled environment (T231)" `
                ("-SampleEnv $SampleEnv was given with -UseRunning, but --sample-env is a launch " +
                 "argument and this tray was launched without it: it is reading the real variable. " +
                 "Drop -UseRunning so the check launches its own tray on the fixture.")
            return
        }

        $tray = Resolve-TrayLang
        if ($script:LangGiven -and $Lang -ne $tray.Code) {
            # Explicit and unhonourable: refuse rather than override, the shape T205 settled on. A default
            # `-Lang` is not a request and is simply replaced below; a typed one is the caller's word about
            # a language this run cannot produce, and silently checking a different one is the defect this
            # file exists to catch.
            Unchecked "the tray icon's menu (T137, T148, T158)" `
                ("-Lang $Lang was given with -UseRunning, but the language is a launch argument and this " +
                 "tray was launched by somebody else: it is in $($tray.Code), by $($tray.From). Drop " +
                 "-Lang to check it as it is, or drop -UseRunning to launch one in $Lang.")
            return
        }
        $script:LangCode = $tray.Code
        Info "matching labels against $($script:LangCode), by $($tray.From) - -Lang cannot reach a running tray"
    }
    else {
        $tray = Start-CheckTray
        if (-not $tray) {
            Unchecked "the tray icon's menu (T137, T148, T158)" $script:TrayStartError
            return
        }
        $proc = $tray.Proc
        $iconPattern = $tray.IconPattern
        $ownProcess = $true
    }

    try {
        $menu = Open-TrayMenu $iconPattern $proc.Id
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
            Unchecked "the Profile submenu expanding from the keyboard (T148)" `
                      "one profile, so the submenu is hidden and there is nothing to expand"
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
            if (-not $menu) { $menu = Open-TrayMenu $iconPattern $proc.Id }
            if (-not $menu) {
                Fail "the menu would not reopen for the Profile submenu check"
            }
            else {
                $profSubs = Expand-Item $menu $profLabel
                # One entry per profile, plus the two toggles the submenu carries: auto-follow (T126) and
                # the machine-wide switch (T171). A floor, because the pinned undo and the
                # environment-outside line are both conditional - Assert-ProfileSubmenu is what asks about
                # those by identity. The floor comes first all the same: an unexpanded submenu reads as
                # zero entries, and zero entries is how a check passes by seeing nothing.
                $least = $profileCount + 2
                if (-not $profSubs) {
                    Fail "'$profLabel' did not expand from the keyboard - empty when the menu opened? (T148)"
                }
                elseif ($profSubs.Count -lt $least) {
                    Fail "'$profLabel' expanded with $($profSubs.Count) entries, expected at least $least"
                }
                else {
                    Pass "'$profLabel' expands from the keyboard with $($profSubs.Count) entries"
                    foreach ($s in $profSubs) { Info "- $($s.Current.Name)" }
                    Assert-ProfileSubmenu $profSubs
}
            }
        }

        # T158: a LEFT-click on the icon is now the app's main entry point - it opens the one window on
        # the pacing report. Nothing else in this repo drives that path (`--main` builds the shell
        # directly, which is a different caller), and it is the one gesture most users make.
        [Native]::Key($VK_ESC); [Native]::Key($VK_ESC)   # the menu must be gone before clicking the icon
        Start-Sleep -Milliseconds 400
        $icon = Get-TrayIcon $iconPattern
        if (-not $icon) {
            Fail "the tray icon could not be found for the left-click check"
        }
        else {
            # T233: bounded attempts on the gesture, not a longer wait on its result. WindowOfProcess
            # already polls for twelve seconds, so a run that timed out was one where the CLICK never
            # arrived - twice in ten runs here, reported as "opened no window", which is the wording of
            # T158 being broken. A second click on the icon is harmless by construction: OpenMain
            # activates the window it already has rather than stacking another (T158's own rule).
            $win = $null
            for ($try = 1; $try -le 3 -and -not $win; $try++) {
                if ($try -gt 1) {
                    Info "attempt $try : no window from the left-click yet, clicking again"
                    $icon = Get-TrayIcon $iconPattern      # the overflow flyout may have closed
                    if (-not $icon) { break }
                }
                ClickCentre $icon
                $win = WindowOfProcess $proc.Id 5000
                if ($win -and $try -gt 1) { Info "the window opened on attempt $try of 3" }
            }
            if (-not $win) {
                Fail "a left-click on the icon opened no window after 3 attempts (T158)"
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

<#
  The environment states this machine is never in, driven rather than offered (T238).

  T231 built `--sample-env` so T172's mark could be rendered, and T230 wrote the assertion for it. An
  unattended run reached neither: the mark stayed `NOT CHECKED` unless somebody remembered to type
  `-SampleEnv`, which is most of the way back to the gap those two tasks were filed to close. An
  assertion nothing routine reaches is coverage nobody has, with a green tick's worth of reassurance on
  top of it.

  So the modes are swept here, one tray each. `agrees` is deliberately not swept: it is the state the
  ordinary pass above already ran in, and paying a launch to see it twice buys nothing.
#>
<#
  Walk an OPEN submenu to one entry and activate it, the way a keyboard user does (T294).

  Separate from `Expand-Item` because the gesture is a different one and so is the failure: that walks
  the TOP-LEVEL list to open a submenu, this walks the submenu itself to fire a command. Same bounded
  attempts and the same reason - the shell drops synthesised input often enough that one walk and one
  Enter is a coin toss, and a retry loop with no cap turns a broken command into a slow pass.

  Returns $true when the walk reached the entry and pressed Enter. It does NOT claim the command did
  anything: what a pick was supposed to do is asserted afterwards, against the app, by the caller.
#>
function Select-SubItem($target, $label, $attempts = 3) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
    for ($try = 1; $try -le $attempts; $try++) {
        $subs = @($target.FindAll('Descendants', $cond))
        if ($subs.Count -eq 0) { return $false }
        for ($i = 1; $i -le ($subs.Count + 2); $i++) {
            [Native]::Key($VK_DOWN)
            $focused = @($target.FindAll('Descendants', $cond)) |
                       Where-Object { $_.Current.HasKeyboardFocus } | Select-Object -First 1
            if ($focused -and $focused.Current.Name -like "$label*") {
                [Native]::Key($VK_RETURN)
                if ($try -gt 1) { Info "'$label' was activated on attempt $try of $attempts" }
                return $true
            }
        }
        if ($try -lt $attempts) { Info "attempt $try : the walk never focused '$label', retrying" }
    }
    return $false
}

<#
  The one action in this app that changes what a DIFFERENT process will do (T294).

  `-Case Profiles` walks a ComboBox and is the only check that drives a profile control at all - and it
  is not this one. That picker chooses whose numbers the Statistics page *draws*. This entry runs
  `AdoptMonitored`: it changes which account the tray icon follows, moves `MonitoredConfigDir`,
  re-keys the stores, drops the outgoing account's state (one assignment since T293) and takes the
  incoming account's token. T292 was a defect on that path, found by reading rather than by running.

  What is asserted is what a switch is supposed to have DONE, read back off the app: the check mark
  moves to the profile picked and to no other, and the icon's own tooltip names it. What is deliberately
  NOT asserted here is the settings file - `--second-tray` is an observing process and T239's promise is
  that it writes nothing, so the file must NOT move. That claim is already made, over this case too,
  by `Assert-StoreUntouched` at the end of the tray block; driving the one path that writes that field
  is the strongest place it has ever been made.
#>
function Invoke-SwitchCase {
    Head "Switch - the menu entry that changes which account the icon follows"

    # Refused outright, not degraded: the resident tray is NOT observing, so a pick driven through it
    # would rewrite the user's own MonitoredConfigDir and repoint the icon their tray follows. Every
    # other case here is a read; this one is the app's most consequential write.
    if ($UseRunning) {
        Unchecked "the profile switch (T294)" `
            ("-UseRunning drives the resident tray, which is not an observing process: a pick there " +
             "would rewrite this machine's MonitoredConfigDir and repoint the icon for real. Drop " +
             "-UseRunning so the check launches its own --second-tray.")
        return
    }

    $want = Expected-ProfileState
    if ($want.Labels.Count -lt 2) {
        Unchecked "the profile switch (T294)" `
                  "$($want.Labels.Count) profile registered, so there is nothing to switch to"
        return
    }
    if (-not $want.Icon) {
        Fail "--profiles named no followed profile, so there is no 'before' for a switch to move from"
        return
    }
    $other = @($want.Labels | Where-Object { $_ -ne $want.Icon }) | Select-Object -First 1
    Info "the icon follows '$($want.Icon)'; this case picks '$other'"

    $tray = Start-CheckTray
    if (-not $tray) { Unchecked "the profile switch (T294)" $script:TrayStartError; return }
    $proc = $tray.Proc

    try {
        $menu = Open-TrayMenu $tray.IconPattern $proc.Id
        if (-not $menu) { Fail "the tray menu never opened for the switch case (5 attempts)"; return }

        $profLabel = Label 'menu.profiles'
        $items = Menu-Items $menu
        $parent = $items | Where-Object { $_.Current.Name -eq $profLabel } | Select-Object -First 1
        $subs = Expand-Item $menu $profLabel
        if (-not $subs -or -not $parent) {
            Fail "'$profLabel' did not expand, so the switch could not be driven (T148's shape)"
            return
        }

        # The before-state read off the MENU rather than off --profiles: this tray is the one being
        # driven, and a second process's reading is a claim about a third state.
        $before = @()
        foreach ($lab in $want.Labels) {
            $hit = @($subs | Where-Object { $_.Current.Name -like "$lab *" }) | Select-Object -First 1
            if ($hit -and (Menu-Checked $hit)) { $before += $lab }
        }
        if ($before.Count -ne 1) {
            Fail "$($before.Count) profile entries carry the check mark before the switch, expected exactly one"
            return
        }
        Pass "before the switch the check mark is on '$($before[0])'"

        if (-not (Select-SubItem $parent $other)) {
            Fail "the walk never reached '$other' in the Profile submenu, so nothing was picked"
            return
        }
        # The menu closes on activation; the tray then adopts and repaints. Polled rather than slept at,
        # to a deadline: adopting re-keys stores and takes a token, and how long that takes is a fact
        # about the machine.
        Start-Sleep -Milliseconds 1200

        $menu = Open-TrayMenu $tray.IconPattern $proc.Id
        if (-not $menu) { Fail "the menu would not reopen to read the switch back"; return }
        $parent = (Menu-Items $menu) | Where-Object { $_.Current.Name -eq $profLabel } | Select-Object -First 1
        $subs2 = Expand-Item $menu $profLabel
        if (-not $subs2) { Fail "'$profLabel' would not expand to read the switch back"; return }

        $after = @()
        foreach ($lab in $want.Labels) {
            $hit = @($subs2 | Where-Object { $_.Current.Name -like "$lab *" }) | Select-Object -First 1
            if ($hit -and (Menu-Checked $hit)) { $after += $lab }
        }
        if ($after.Count -ne 1) {
            Fail "$($after.Count) profile entries carry the check mark after the switch, expected exactly one"
        } elseif ($after[0] -ne $other) {
            Fail ("the switch picked '$other' and the check mark is on '$($after[0])' - " +
                  "AdoptMonitored did not move the icon's profile")
        } else {
            Pass "the check mark moved to '$other', the profile that was picked, and to no other"
        }

        # And the icon says so itself - WHERE it can. The shell's accessible name for a notification-area
        # icon is the registered name followed by the live tooltip, and the profile line is the first thing
        # `Compose` writes... but only after `Data` arrives. With no reading it returns one sentence and
        # stops: connecting, not-signed-in, "usage will appear here", or the API's error. Measured here on
        # the first run of this case, which is how the premise got corrected rather than assumed - the
        # incoming account had no usable reading and the tooltip read "Once you start using Claude...".
        #
        # So the two outcomes are told apart rather than merged. A reading that names the wrong profile is
        # a FAIL; no reading at all is the precondition being absent, which is Unchecked and DEGRADED - the
        # distinction this file's header draws, and the reason a green tick is never printed over an
        # assertion that stopped running.
        # Only the LIVE half of that name. The shell concatenates the name the icon was REGISTERED with -
        # frozen at "Claude Code - connecting..." - and the current tooltip, so the registered half
        # contains `tip.connecting` on every run for ever. Testing the whole string for "no reading yet"
        # therefore matched always, and the first draft of this check turned a real wrong-profile FAIL into
        # a NOT CHECKED. Caught by injecting the defect and reading what came back, which is the only
        # reason it is not still in here.
        [Native]::Key($VK_ESC); [Native]::Key($VK_ESC)
        $stem  = ((Label 'tip.profile') -split '\{0\}')[0].TrimEnd()
        $mute  = @('tip.connecting', 'tip.notSignedIn', 'tip.willAppear') | ForEach-Object { Label $_ }
        $mute += ((Label 'tip.apiError') -split '\{0\}')[0].TrimEnd()
        $named = $false
        $said  = $null
        $deadline = (Get-Date).AddMilliseconds(12000)
        do {
            $icon = Get-TrayIcon $tray.IconPattern 3000
            if ($icon) {
                # `[check]` is the tag Start-CheckTray's tray registers with (T240); everything after it is
                # what Compose wrote on the last repaint.
                $parts = $icon.Current.Name -split '\[check\]', 2
                $said = if ($parts.Count -eq 2) { $parts[1].Trim() } else { $icon.Current.Name }
                if ($said -match [regex]::Escape("$stem $other")) { $named = $true; break }
            }
            Start-Sleep -Milliseconds 600
        } while ((Get-Date) -lt $deadline)

        $noReading = $said -and @($mute | Where-Object { $_ -and $said.Contains($_) }).Count -gt 0
        if ($named) {
            Pass "the icon's tooltip names '$other' as the profile it is now following"
        } elseif ($noReading) {
            Unchecked "the icon's tooltip naming the profile it switched to (T294)" `
                ("the incoming account has no usable reading yet, so the tooltip is a single sentence " +
                 "with no profile line in it: '$said'. The check mark above is what says the switch " +
                 "landed; this claim needs a reading the machine did not produce.")
        } else {
            Fail ("the icon's tooltip never named '$other' after the switch - it reads " +
                  "'$(if ($said) { $said } else { '<no icon found>' })'")
        }
    }
    finally {
        [Native]::Key($VK_ESC); [Native]::Key($VK_ESC)
        if ($proc -and -not $proc.HasExited) { $proc.Kill() }
    }
}

function Invoke-EnvFixtureSweep {
    if ($SampleEnv) {
        Info "-SampleEnv $SampleEnv pins this run to one mode, so the sweep is the run itself (T238)"
        return
    }
    if ($UseRunning) {
        # Launching our own tray here would contradict the flag: the caller asked for the resident one,
        # and a sampled environment is a launch argument no resident tray was given (T231).
        Unchecked "the environment states behind --sample-env (T172, T238)" `
                  "-UseRunning asked for the resident tray, and the fixture only reaches a launched one"
        return
    }

    Head "Environment fixture - the states CLAUDE_CONFIG_DIR is never in here (T231, T238)"
    $saved = $script:EnvArgs
    try {
        foreach ($mode in @('other', 'outside')) {
            $script:EnvArgs = @('--sample-env', $mode)

            # `other` needs a second profile for the variable to name, and inventing one would put the
            # menu in a state it cannot reach - EnvironmentFixture refuses it rather than pretend. That
            # refusal is a legitimate absence on a one-profile machine, which is what CI is, so it is
            # Unchecked and never a failure: the same rule the profile picker's two-profile skip follows.
            if ($mode -eq 'other' -and (Expected-ProfileCount) -lt 2) {
                Unchecked "the environment mark on a profile entry (T172, --sample-env other)" `
                          "one profile here, so the variable has no second profile to name"
                continue
            }

            $tray = Start-CheckTray
            if (-not $tray) {
                Unchecked "the Profile submenu on --sample-env $mode (T172)" $script:TrayStartError
                continue
            }
            try {
                $menu = Open-TrayMenu $tray.IconPattern $tray.Proc.Id
                if (-not $menu) { Fail "the tray menu never opened on --sample-env $mode"; continue }
                $profSubs = Expand-Item $menu (Label 'menu.profiles')
                if (-not $profSubs) {
                    Fail "'$(Label 'menu.profiles')' did not expand on --sample-env $mode"
                    continue
                }
                Info "--sample-env $mode :"
                foreach ($s in $profSubs) { Info "- $($s.Current.Name)" }
                # The whole submenu, not just the environment arm: a sampled variable moves what the
                # check mark and the toggles are supposed to say too, and asserting only the mark would
                # let the fixture certify a submenu that is wrong everywhere else.
                Assert-ProfileSubmenu $profSubs
            }
            finally {
                [Native]::Key($VK_ESC); [Native]::Key($VK_ESC)
                if (-not $tray.Proc.HasExited) { $tray.Proc.Kill() }
                Start-Sleep -Milliseconds 500
            }
        }
    }
    finally { $script:EnvArgs = $saved }
}

<#
  Every file the app owns, by size and write time (T241).

  The backstop under T239's promise. `--selftest` asserts it for the writers somebody listed, and the
  list is the weakness: five gates went in, the section went green, and a real run still changed
  `context-cache.json` because nobody had thought to drive the context scan. This asks the DIRECTORY
  instead - one question about a region rather than a list of methods, which is the move T221 made when
  nine sampled points stopped being a cover.
#>
function Store-Fingerprint {
    $dir = Join-Path $env:LOCALAPPDATA 'ClaudeTray'
    $map = @{}
    if (Test-Path -LiteralPath $dir) {
        foreach ($f in Get-ChildItem -LiteralPath $dir -Recurse -File -ErrorAction SilentlyContinue) {
            $map[$f.FullName] = "$($f.Length)|$($f.LastWriteTimeUtc.Ticks)"
        }
    }
    return $map
}

<#
  What the check's own trays left behind, compared against `$before` (T241).

  The reading is asymmetric, and that is what makes it worth running on a machine where the tray is
  resident - which is every developer's, and the reason gating this on "no other tray alive" would have
  made it an assertion that never runs (T238's lesson, one task along).

    nothing moved  -> the check wrote nothing. Sound whoever else is running: a write by the check's
                      trays would be in this comparison no matter what the resident one is doing.
    something moved -> ambiguous ONLY when another tray is alive, because it polls on its own cadence
                      and writes on its own schedule. With none alive there is nobody else it could
                      have been, so it is a failure; with one alive it is Unchecked, naming the files,
                      because "something changed" sends the reader nowhere.
#>
function Assert-StoreUntouched($before) {
    if ($null -eq $before) { return }
    $after = Store-Fingerprint
    $moved = @()
    foreach ($k in $after.Keys)  { if ($before[$k] -ne $after[$k]) { $moved += (Split-Path -Leaf $k) } }
    foreach ($k in $before.Keys) { if (-not $after.ContainsKey($k)) { $moved += "(gone) " + (Split-Path -Leaf $k) } }
    $moved = @($moved | Sort-Object -Unique)

    if ($moved.Count -eq 0) {
        Pass "the check's trays left all $($before.Count) file(s) under %LocalAppData%\ClaudeTray untouched (T239)"
        return
    }
    $others = @(Get-Process -Name ClaudeTray -ErrorAction SilentlyContinue)
    if ($others.Count -gt 0) {
        Unchecked "that the check's trays wrote nothing (T239, T241)" `
            ("$($moved.Count) file(s) moved - $($moved -join ', ') - but a ClaudeTray is resident " +
             "(pid $($others[0].Id)) and polls on its own cadence, so this cannot be pinned on the " +
             "check. Re-run with no other tray alive to get an answer.")
    } else {
        Fail ("the check's trays changed $($moved.Count) file(s) under %LocalAppData%\ClaudeTray with " +
              "no other tray alive to blame: $($moved -join ', ') - an observing tray persists nothing (T239)")
    }
}

# ---------------------------------------------------------------- run

Write-Host "Check-Interaction - $Exe (lang $Lang)" -ForegroundColor White

# T195: the three cases that drive `--main` share one launch when several of them run here, and own their
# own when run alone. Opted into for the whole invocation rather than decided per case, so `Release-Main`
# has one rule to follow and no case has to know which others are running.
$script:ShareMain = ($Case -eq 'All')

# Whether the fixture sweep is going to run (T238), decided once and here rather than by each assertion
# guessing: the submenu walk reports the environment mark differently when something downstream is about
# to cover it. Same conditions Invoke-EnvFixtureSweep returns early on.
$script:EnvSweepWillRun = ($Case -in @('All', 'Menu')) -and -not $SampleEnv -and -not $UseRunning

<#
  Whether `-Exe` is older than the code it is supposed to be (T258).

  The same reading T236 refuses by flag, arrived at by accident: a build that fails leaves the previous
  .exe in place, so the next command drives a binary that predates the change and passes about code that
  is not there. It happened here - a build died on a file lock, and the dump taken from the run after it
  was of the build before the edit, with only the pids in the error saying so.

  `Unchecked`, not `Fail`: everything that ran did run and did pass, on *a* binary. What could not be
  evaluated is the claim the caller came for - that the build under `-Exe` was checked - which is word for
  word what T236 marks for `-UseRunning`. Skipped under `-UseRunning`, which has its own comparison and
  says so more precisely.
#>
if (-not $UseRunning -and (Test-Path $Exe)) {
    $built = (Get-Item -LiteralPath $Exe).LastWriteTimeUtc
    $srcDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'src'
    $newest = if (Test-Path $srcDir) {
        Get-ChildItem -LiteralPath $srcDir -Recurse -File -Include *.cs, *.xaml -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    } else { $null }
    if ($newest -and $newest.LastWriteTimeUtc -gt $built) {
        Unchecked "the build under -Exe (T258)" `
            ("$Exe was written $($built.ToLocalTime().ToString('HH:mm:ss')) and " +
             "$($newest.Name) was edited $($newest.LastWriteTimeUtc.ToLocalTime().ToString('HH:mm:ss')) - " +
             "this run is about the PREVIOUS build. A build that fails leaves the old .exe in place; " +
             "rebuild and re-run.")
    }
}

try {

if ($Case -in @('All', 'Keyboard')) { Invoke-KeyboardCase }
if ($Case -in @('All', 'Panes'))    { Invoke-PanesCase }
if ($Case -in @('All', 'Sessions')) { Invoke-SessionsCase }
if ($Case -in @('All', 'Profiles')) { Invoke-ProfilesCase }
if ($Case -in @('All', 'Names'))    { Invoke-NamesCase }

# Before the menu case, always: that one refuses to run while any ClaudeTray is alive, and with the window
# now shared across three cases this is the single place it gets closed. `Close-Main` waits for the exit,
# because a still-dying `--main` looks like the tray to it.
Close-Main
if ($Case -in @('All', 'Menu')) {
    # Taken here rather than at the top of the run: the cases above drive `--main`, which is not an
    # observing process and is not what T239 promised anything about. What is being measured is the
    # trays this script LAUNCHES as trays - the Menu case's and the sweep's, all of them --second-tray.
    $storeBefore = Store-Fingerprint
    Invoke-MenuCase
    if ($Case -eq 'All') { Invoke-SwitchCase }
    Invoke-EnvFixtureSweep
    Assert-StoreUntouched $storeBefore
}

# Its own tray, and inside its own store bracket when it runs alone. Under `All` it rides the one above:
# the switch is the single path in this app that writes MonitoredConfigDir, so having it inside the
# T239 comparison is the strongest form that promise has ever been asserted in.
if ($Case -eq 'Switch') {
    $storeBefore = Store-Fingerprint
    Invoke-SwitchCase
    Assert-StoreUntouched $storeBefore
}

if ($script:MainAcquires -gt 0) {
    Info "$($script:MainAcquires) case(s) drove --main on $($script:MainLaunches) launch(es)"
}

}
finally {
    # Whichever way the run ended — a case that returned early, a terminating error, Ctrl-C. The summary
    # below still prints, because this is the end of the body and not of the script (T258).
    Stop-Launched
}

Write-Host ""
if ($script:Failures -gt 0) {
    Write-Host "FAILED - $($script:Failures) interaction check(s)." -ForegroundColor Red
    if ($script:Unchecked.Count -gt 0) {
        Write-Host "         and $($script:Unchecked.Count) more did not run: $($script:Unchecked -join '; ')" -ForegroundColor Yellow
    }
    exit 1
}

# T193: the word "every" is earned, not the default. A run where an assertion could not be evaluated is
# not the same run as one where all of them passed, and printing the same green line for both is how
# T166's timing was dropped in an Info line nobody reads. Three outcomes, three exit codes, so whatever
# runs this - a person or CI - can tell a degraded run from a clean one without parsing the output.
if ($script:Unchecked.Count -gt 0) {
    Write-Host "DEGRADED - everything that ran passed, but $($script:Unchecked.Count) assertion(s) did not run:" -ForegroundColor Yellow
    foreach ($u in $script:Unchecked) { Write-Host "  - $u" -ForegroundColor Yellow }
    Write-Host "This is not a failure and not a pass. Fix the reason above, or re-run where it can be observed." -ForegroundColor Yellow
    exit 2
}
Write-Host "OK - every interaction check passed." -ForegroundColor Green
exit 0
