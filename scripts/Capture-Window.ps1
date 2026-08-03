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
      3. no foreign window in front of it overlaps the rectangle about to be copied.

    (3) was nine sampled points until T221, and nine points cannot cover a window: the capture taken to
    verify T217 passed all of them while carrying two windows of another process across its lower-right
    corner. More points only move the threshold — the number that finally covers a window is the number
    of pixels in it — so the question is now asked about the region. The Z order above the window is
    enumerated and each frame intersected with the copy rectangle, which answers for the whole area in
    one pass and names the intruder, its pid and the rectangle it covers.

    What it copies is the window's PAINTED frame (T206), not `GetWindowRect`: that one spans the
    invisible resize border and the drop-shadow margin, so every PNG this script made carried a strip of
    whatever was behind the window down its edges — measured while verifying T199, where slivers of the
    editor came back along the left, right and bottom. The run prints how much it trimmed.

    (3) is the one that decides the file. Being in the foreground is only a proxy for it — a window can
    hold focus and still sit under a topmost one — and it is a proxy this script cannot even insist on,
    since Windows refuses SetForegroundWindow to a process that does not own focus (normally the editor
    this was launched from). So the window is raised *and* pushed topmost as best effort, and then what
    is in front of it is checked directly. An overlap FAILS rather than cropping the copy around it:
    nothing is written, because a file quietly trimmed to dodge an intruder is a picture of something
    nobody asked for.

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
    [switch]$IgnoreOtherInstances,
    [string]$Expect
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$src = @"
using System;
using System.Collections.Generic;
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

    // T206: GetWindowRect spans a WPF window's INVISIBLE resize border and drop-shadow margin, so the
    // rectangle it reports is wider than the area the window paints and a screen copy of it carries a
    // strip of whatever is behind the window down its edges. DWMWA_EXTENDED_FRAME_BOUNDS is the visible
    // frame, in the same physical-pixel space per-monitor-v2 awareness already put us in.
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr,
        out RECT r, int size);
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// <summary>The rectangle the window actually paints, or false when DWM will not say - in which case
    /// the caller falls back to GetWindowRect and reports that it did, rather than silently going back
    /// to the wider rect.</summary>
    public static bool FrameRect(IntPtr h, out RECT r) {
        r = new RECT();
        try {
            int hr = DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out r,
                                           Marshal.SizeOf(typeof(RECT)));
            if (hr == 0 && r.Right > r.Left && r.Bottom > r.Top) return true;
        } catch {}
        r = new RECT();
        return false;
    }

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

    // T221: what it takes to ask about a REGION instead of a handful of coordinates. GW_HWNDPREV walks
    // one step up the Z order among top-level siblings, so from the target it enumerates exactly the
    // windows in front of it - and nothing below, which is the whole set that can cover a pixel.
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    public const uint GW_HWNDPREV = 3;
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);

    // A window can be visible by WS_VISIBLE and painted by nothing: DWM cloaks suspended UWP apps, the
    // shell's hidden host windows and a virtual desktop's other windows. Without this every run on a
    // stock Windows 11 reports a screenful of intruders that are not on screen.
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr,
        out int v, int size);
    public const int DWMWA_CLOAKED = 14;
    public static bool Cloaked(IntPtr h) {
        try { int v; if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out v, sizeof(int)) == 0) return v != 0; }
        catch {}
        return false;
    }

    /// <summary>Every foreign window in front of <c>target</c> whose painted frame overlaps <c>area</c>,
    /// nearest first, each named with the rectangle it covers. Empty means the region is clear - which is
    /// an answer about the whole area, not about the points somebody thought to sample.
    ///
    /// Own-process windows are skipped deliberately: a popup is its own top-level window and being in
    /// front of the page that opened it is what `-Expect` exists to require (T217).</summary>
    public static string[] Intruders(IntPtr target, RECT area, uint ownPid) {
        var found = new List<string>();
        for (IntPtr h = GetWindow(target, GW_HWNDPREV); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDPREV)) {
            if (!IsWindowVisible(h) || IsIconic(h) || Cloaked(h)) continue;
            if (PidOf(h) == ownPid) continue;
            RECT r;
            // The painted frame for the intruder too: its invisible resize border overlaps things it does
            // not cover, and failing a capture on a drop shadow is the false red of this same shape.
            if (!FrameRect(h, out r) && !GetWindowRect(h, out r)) continue;
            int l = Math.Max(r.Left, area.Left), t = Math.Max(r.Top, area.Top);
            int rt = Math.Min(r.Right, area.Right), b = Math.Min(r.Bottom, area.Bottom);
            if (rt <= l || b <= t) continue;
            found.Add(string.Format("'{0}' (pid {1}) covers ({2},{3})-({4},{5}), {6} x {7} px",
                                    TitleOf(h), PidOf(h), l, t, rt, b, rt - l, b - t));
        }
        return found.ToArray();
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

# The app's own account of what it drew (T217). Only redirected when something is expected, so the
# ordinary capture keeps behaving exactly as before and leaves no file behind.
$surfaceLog = $null
if ($Expect) { $surfaceLog = Join-Path ([IO.Path]::GetTempPath()) ("capture-surface-" + [guid]::NewGuid() + ".log") }

$proc = if ($surfaceLog) {
    Start-Process -FilePath $Exe -ArgumentList $AppArgs -PassThru -RedirectStandardOutput $surfaceLog
} else {
    Start-Process -FilePath $Exe -ArgumentList $AppArgs -PassThru
}
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

        # The rectangle the window PAINTS, not the one it owns (T206). GetWindowRect is read too, only
        # to say by how much the two differ - that difference is exactly the strip of desktop every
        # capture used to carry. Measured here at 200%: 1760 x 1896 owned, 1738 x 1885 painted, so
        # L11 T0 R11 B11 - asymmetric, since the top has no invisible border to spare.
        # A maximized window needs no separate handling: SW_RESTORE above has already un-maximized it
        # by the time either rectangle is read, so the case cannot reach the copy.
        $outer = New-Object Win+RECT
        [Win]::GetWindowRect($h, [ref]$outer) | Out-Null
        $usedDwm = [Win]::FrameRect($h, [ref]$r)
        if (-not $usedDwm) {
            # Reported, never silent: falling back means the PNG has the old edges and whoever reads it
            # should know which rectangle produced it.
            Write-Host "  DWM would not give the extended frame bounds - falling back to GetWindowRect, so the edges may carry a strip of the desktop (T206)" -ForegroundColor Yellow
            $r = $outer
        }
        $w = $r.Right - $r.Left
        $hgt = $r.Bottom - $r.Top
        if ($w -le 0 -or $hgt -le 0) { throw "Bad window rect ($w x $hgt)" }

        # (3) The region — the assertion that decides the file. It used to be nine sampled points, and a
        # sample is not a cover: the capture taken to verify T217 was certified, named the right window
        # and carried two windows of another process across its lower-right corner, missed by every one
        # of them (T221). Adding points only moves the threshold — the number that finally covers a
        # window is the number of pixels in it — so the question is asked about the area instead: walk
        # the Z order above this window and intersect. One pass, and it names the intruder.
        $covered = @([Win]::Intruders($h, $r, [uint32]$proc.Id))
    } while ($covered.Count -gt 0 -and (Get-Date) -lt $raiseDeadline)

    if ($covered.Count -gt 0) {
        # Fail, never crop. An overlap on the edge is now inside real content — T206 made the copied
        # rectangle the painted frame, so there is no border left for a foreign window to hide in — and
        # a file quietly trimmed to dodge it is a picture of something nobody asked for.
        throw (("{0} window(s) in front of the one being captured overlap it, after 4s of raising it - " +
                "the copy would photograph them: {1}") -f $covered.Count, ($covered -join '; '))
    }

    # (4) The surface the flag was invoked FOR is inside what is about to be copied (T217). Three
    # captures of the method note came back green, named the right window, and showed no note: the
    # popup is its own top-level window, so nothing about copying the right window says the note is in
    # it. Only the app knows what it drew, so the app says so and this checks the claim.
    if ($Expect) {
        $rect = $null
        $deadline = (Get-Date).AddMilliseconds(6000)
        do {
            $line = @(Get-Content $surfaceLog -ErrorAction SilentlyContinue |
                      Where-Object { $_ -match "^preview-surface:\s+$([regex]::Escape($Expect))\s" }) |
                    Select-Object -Last 1
            if ($line) {
                $f = ($line -split '\s+')
                $rect = [pscustomobject]@{ L=[int]$f[2]; T=[int]$f[3]; W=[int]$f[4]; H=[int]$f[5] }
                break
            }
            Start-Sleep -Milliseconds 200
        } while ((Get-Date) -lt $deadline)

        if (-not $rect) {
            throw ("'$Expect' was never reported by $Exe $AppArgs - the preview drew no such surface, " +
                   "or it drew one and did not say so. Nothing is written: a capture that cannot prove " +
                   "it contains what it was taken for is the silent pass T217 is about.")
        }
        $inside = $rect.L -ge $r.Left -and $rect.T -ge $r.Top -and
                  ($rect.L + $rect.W) -le $r.Right -and ($rect.T + $rect.H) -le $r.Bottom
        if (-not $inside) {
            throw ("'$Expect' rendered at ($($rect.L),$($rect.T)) $($rect.W)x$($rect.H), outside the " +
                   "window being copied ($($r.Left),$($r.Top))-($($r.Right),$($r.Bottom)) - a popup is " +
                   "its own top-level window and this one does not overlap. The PNG would have been a " +
                   "correct copy of a window without it.")
        }
        Write-Host ("  '$Expect' is inside the copy: ($($rect.L),$($rect.T)) $($rect.W)x$($rect.H)") -ForegroundColor Green
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
    if ($usedDwm) {
        # Said out loud on every run, because the strip it names is what the picture used to include and
        # nobody could see the difference by looking at one file.
        Write-Host ("  painted frame, not the window rect: trimmed L{0} T{1} R{2} B{3} of invisible border ({4} x {5} -> {6} x {7})" -f
                    ($r.Left - $outer.Left), ($r.Top - $outer.Top),
                    ($outer.Right - $r.Right), ($outer.Bottom - $r.Bottom),
                    ($outer.Right - $outer.Left), ($outer.Bottom - $outer.Top), $w, $hgt)
    }
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
    if ($surfaceLog) { Remove-Item $surfaceLog -ErrorAction SilentlyContinue }
}
