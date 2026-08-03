<#
.SYNOPSIS
    Launch a window and capture it to a PNG — the visual feedback loop for UI work.

.DESCRIPTION
    Starts an executable (default: the Settings preview via `ClaudeTray.exe --settings`), waits for
    its main window, brings it to the foreground, and saves a PNG of just that window's rectangle.
    This is what lets the layout be verified by *looking* at it instead of guessing, and it is fully
    deterministic — no clicking through the tray menu.

    This capture is a **screen copy**: it reads the pixels currently on screen inside the window's
    rectangle. That is the whole reason `--capture-settings` / `--capture-stats` are preferred — they
    render off-screen and cannot photograph anything else. Use this script for the one case they
    cannot serve: a popup, which is its own top-level window that a RenderTargetBitmap over a page's
    content cannot see (`--stats method` plus this script).

    Because it is a screen copy, it verifies what it captured (T199). It had returned a PNG of another
    instance's *Settings* window when asked for Statistics, printed `Captured 1760 x 1200 -> …` and
    exited 0 — caught only because a person read the picture. Three assertions now stand between that
    and a green run, and the success line names the window and the pid so the next wrong capture
    reports itself:

      1. the window handle belongs to the process this script launched;
      2. no other ClaudeTray window is open, unless -IgnoreOtherInstances says to allow it;
      3. the pixels sampled across the window's own rectangle belong to that same process.

    (3) is the one that decides the file. Being in the foreground is only a proxy for it — a window can
    hold focus and still sit under a topmost one — and it is a proxy this script cannot even insist on,
    since Windows refuses SetForegroundWindow to a process that does not own focus (normally the editor
    this was launched from). So the window is raised *and* pushed topmost as best effort, and then the
    pixels are checked directly. Nothing is written if that check fails.

.PARAMETER Exe
    Path to the executable to launch. Defaults to the Debug build's ClaudeTray.exe.

.PARAMETER AppArgs
    Arguments passed to the exe. Defaults to "--settings".

.PARAMETER Out
    Output PNG path. Defaults to docs\_preview\settings.png.

.PARAMETER WaitMs
    Milliseconds to wait for the window to render before capturing. Default 1500.

.PARAMETER IgnoreOtherInstances
    Capture even though another ClaudeTray already has a window open. The deliberate-override shape
    `Check-Interaction.ps1`'s `-UseRunning` takes, for the same hazard: by default this script refuses,
    because another instance's window on top of this one is the wrong picture it came back with. A tray
    with no window does not count — that one is running on every developer machine here.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Capture-Window.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Capture-Window.ps1 `
      -AppArgs "--stats method" -Out docs\_preview\method.png
#>
param(
    [string]$Exe = "bin\Debug\net10.0-windows\win-x64\ClaudeTray.exe",
    [string]$AppArgs = "--settings",
    [string]$Out = "docs\_preview\settings.png",
    [int]$WaitMs = 1500,
    [switch]$IgnoreOtherInstances
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$src = @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Win {
    // Per-monitor-v2 DPI awareness so GetWindowRect/CopyFromScreen use the SAME physical-pixel
    // coordinate space the window actually lives in — otherwise the capture is offset/scaled on
    // high-DPI displays (e.g. 200%).
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public static readonly IntPtr PER_MONITOR_AWARE_V2 = new IntPtr(-4);
    public static void MakeDpiAware() {
        try { if (SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)) return; } catch {}
        try { SetProcessDPIAware(); } catch {}
    }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    // What the capture is checked against (T199): who owns a handle, what is actually in front, and
    // which window owns a given pixel.
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after,
        int x, int y, int cx, int cy, uint flags);
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    // SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE: change only the Z order. Activating is what Windows
    // refuses to a background process anyway, and it is not what the capture needs.
    public const uint SWP_NOMOVE_NOSIZE_NOACTIVATE = 0x0002 | 0x0001 | 0x0010;
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr h, StringBuilder text, int count);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    public static uint PidOf(IntPtr h) { uint pid; GetWindowThreadProcessId(h, out pid); return pid; }

    public static string TitleOf(IntPtr h) {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>The top-level window owning the pixel at (x, y): WindowFromPoint lands on whatever child
    /// control is there, and GA_ROOT (2) walks up to the window a person would name.</summary>
    public static IntPtr RootAt(int x, int y) {
        var p = new POINT(); p.X = x; p.Y = y;
        IntPtr h = WindowFromPoint(p);
        if (h == IntPtr.Zero) return IntPtr.Zero;
        IntPtr root = GetAncestor(h, 2);
        return root == IntPtr.Zero ? h : root;
    }
}
"@
Add-Type -TypeDefinition $src
[Win]::MakeDpiAware()

# Resolved before anything is launched, so a bad path fails before a window opens rather than after.
$outDir = Split-Path -Parent $Out
if (-not $outDir) { $outDir = "." }
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force $outDir | Out-Null }
$fullOut = Join-Path (Resolve-Path -LiteralPath $outDir).Path (Split-Path -Leaf $Out)

# Another instance's window on the desktop is the failure this script could not see — that is literally
# what came back when Statistics was asked for — so it is refused up front rather than detected after.
# Only a *windowed* instance counts: the tray itself is almost always running and shows no window, and a
# check that fired on it would make every routine preview capture take an override to work.
$others = @(Get-Process -Name ClaudeTray -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 })
if ($others.Count -gt 0 -and -not $IgnoreOtherInstances) {
    throw ("another ClaudeTray window is open (pid $($others[0].Id), '$($others[0].MainWindowTitle)') - " +
           "it can sit on top of the one being captured, and a screen copy would photograph it instead. " +
           "Close it, or re-run with -IgnoreOtherInstances.")
}

$proc = Start-Process -FilePath $Exe -ArgumentList $AppArgs -PassThru
try {
    # Wait for a non-zero main window handle. A timeout FAILS: falling through to whatever is in front
    # is how a capture of another application gets reported as a success.
    $deadline = (Get-Date).AddMilliseconds($WaitMs + 4000)
    while ($proc.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 100
        $proc.Refresh()
        if ($proc.HasExited) { throw "$Exe $AppArgs exited before showing a window (exit $($proc.ExitCode))" }
    }
    if ($proc.MainWindowHandle -eq 0) { throw "Window never appeared for $Exe $AppArgs" }

    Start-Sleep -Milliseconds $WaitMs   # let WPF finish first paint / Mica settle
    $h = $proc.MainWindowHandle

    # (1) The handle belongs to the process this script started, not to a window that merely looked like
    # the one asked for.
    $owner = [Win]::PidOf($h)
    if ($owner -ne $proc.Id) {
        throw ("the window handle 0x{0:X} belongs to pid {1}, not to the launched pid {2} ('{3}')" -f
               [int64]$h, $owner, $proc.Id, [Win]::TitleOf($h))
    }

    # (2) Get it uncovered. Raising it is best-effort by nature: Windows refuses SetForegroundWindow to a
    # process that does not own focus, which here is normally the editor this script was launched from —
    # and that refusal is how the reported failure happened, with the copy silently taking whatever was in
    # front. So the window is *also* pushed topmost, and being in the foreground is deliberately NOT the
    # assertion: foreground is a proxy, and the property that decides what lands in the file is that
    # nothing covers the pixels. That is checked directly, at the pixels, in (3).
    # The remedy is retried and the property is asserted, rather than the other way round: raising a
    # window is racy (the editor keeps focus, the Z-order change lands a frame later), so one look was
    # enough to fail a run whose window was uncovered 400 ms afterwards.
    $r = New-Object Win+RECT
    $w = 0; $hgt = 0
    $covered = $null
    $raiseDeadline = (Get-Date).AddMilliseconds(4000)
    do {
        [Win]::ShowWindow($h, 9) | Out-Null   # SW_RESTORE
        [Win]::SetForegroundWindow($h) | Out-Null
        [Win]::SetWindowPos($h, [Win]::HWND_TOPMOST, 0, 0, 0, 0,
                            [Win]::SWP_NOMOVE_NOSIZE_NOACTIVATE) | Out-Null
        Start-Sleep -Milliseconds 400

        [Win]::GetWindowRect($h, [ref]$r) | Out-Null
        $w = $r.Right - $r.Left
        $hgt = $r.Bottom - $r.Top
        if ($w -le 0 -or $hgt -le 0) { throw "Bad window rect ($w x $hgt)" }

        # (3) The pixels themselves — the assertion that decides the file. Sampled well inside the rect:
        # the edges are the invisible resize border and the drop shadow, which belong to no window in
        # particular.
        $covered = $null
        foreach ($f in @(@(0.5, 0.5), @(0.25, 0.25), @(0.75, 0.25), @(0.25, 0.75), @(0.75, 0.75))) {
            $x = $r.Left + [int]($w * $f[0])
            $y = $r.Top + [int]($hgt * $f[1])
            $at = [Win]::RootAt($x, $y)
            $atPid = if ($at -eq [IntPtr]::Zero) { 0 } else { [Win]::PidOf($at) }
            if ($atPid -ne $proc.Id) {
                $covered = [pscustomobject]@{ X = $x; Y = $y; Title = [Win]::TitleOf($at); Pid = $atPid }
                break
            }
        }
    } while ($covered -and (Get-Date) -lt $raiseDeadline)

    if ($covered) {
        throw (("({0}, {1}) inside the window belongs to '{2}' (pid {3}), not to the launched pid {4} - " +
                "it stayed covered after 4s of raising it, and the copy would be a picture of that") -f
               $covered.X, $covered.Y, $covered.Title, $covered.Pid, $proc.Id)
    }

    $bmp = New-Object System.Drawing.Bitmap $w, $hgt
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $hgt))
    $bmp.Save($fullOut, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    # Names what it captured, which is worth as much as the assertions: it makes a wrong capture
    # self-reporting rather than something a person has to notice.
    Write-Host ("Captured $w x $hgt -> $Out")
    Write-Host ("  window '{0}' (pid {1}, hwnd 0x{2:X}) <- {3} {4}" -f
                [Win]::TitleOf($h), $proc.Id, [int64]$h, $Exe, $AppArgs)
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
}
