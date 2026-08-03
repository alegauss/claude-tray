<#
.SYNOPSIS
    Capture a live window's animation as a numbered PNG frame sequence — the raw material for a
    promo GIF/MP4.

.DESCRIPTION
    Unlike Capture-Window.ps1 (one static PNG), this launches an executable, finds its first visible
    top-level window *by process id* (works for borderless / ShowInTaskbar=False windows like the
    reset toast, whose MainWindowHandle is 0), and then copies that window's rectangle to disk once
    per frame for a fixed duration. The frames land in -OutDir as frame_0000.png, frame_0001.png, …
    ready to feed to ffmpeg (see Encode-Clip below) or ScreenToGif.

    Per-monitor-v2 DPI awareness is set so GetWindowRect / CopyFromScreen share the window's physical
    pixel space (correct capture at 125–200% scaling).

.PARAMETER Exe
    Executable to launch. Defaults to the Debug build's ClaudeTray.exe.

.PARAMETER AppArgs
    Arguments for the exe. Default reproduces the "early weekly reset" toast (clay/coral + confetti).
    Try: "--simulate-reset bonus", "--simulate-reset weekly", "--simulate-reset session".

.PARAMETER OutDir
    Folder for the PNG frames (created if missing, cleared of old frame_*.png first).

.PARAMETER Fps
    Frames per second to capture. 25 is smooth for the ~1.5s toast entrance + bar-fill.

.PARAMETER Seconds
    How long to capture. The toast's entrance+fill settle by ~1.5s; 2.6s also catches the confetti.

.PARAMETER Pad
    Extra physical pixels captured around the window rect (catches the soft drop shadow). Default 8.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Capture-Frames.ps1
    powershell -ExecutionPolicy Bypass -File scripts\Capture-Frames.ps1 -AppArgs "--simulate-reset bonus" -OutDir docs\_preview\bonus
#>
param(
    [string]$Exe     = "bin\Debug\net10.0-windows\win-x64\ClaudeTray.exe",
    [string]$AppArgs = "--simulate-reset unexpected",
    [string]$OutDir  = "docs\_preview\frames",
    [int]$Fps        = 25,
    [double]$Seconds = 2.6,
    [int]$Pad        = 8
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$src = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Win {
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public static readonly IntPtr PER_MONITOR_AWARE_V2 = new IntPtr(-4);
    public static void MakeDpiAware() {
        try { if (SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)) return; } catch {}
        try { SetProcessDPIAware(); } catch {}
    }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);

    // First visible top-level window owned by the given process that has a sane size.
    public static IntPtr FindWindowForPid(uint target) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            if (!IsWindowVisible(h)) return true;
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != target) return true;
            RECT r; GetWindowRect(h, out r);
            if (r.Right - r.Left < 40 || r.Bottom - r.Top < 40) return true; // skip tool/message windows
            found = h; return false;
        }, IntPtr.Zero);
        return found;
    }
}
"@
Add-Type -TypeDefinition $src
[Win]::MakeDpiAware()

if (Test-Path $OutDir) { Remove-Item (Join-Path $OutDir "frame_*.png") -ErrorAction SilentlyContinue }
else { New-Item -ItemType Directory -Force $OutDir | Out-Null }
$absOut = (Resolve-Path -LiteralPath $OutDir).Path

$proc = Start-Process -FilePath $Exe -ArgumentList $AppArgs -PassThru
try {
    # Wait for the toast's borderless window to appear (MainWindowHandle stays 0 for it).
    $h = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(6)
    while ($h -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 40
        $h = [Win]::FindWindowForPid([uint32]$proc.Id)
    }
    if ($h -eq [IntPtr]::Zero) { throw "No window appeared for $Exe $AppArgs" }

    $frameCount = [int]([math]::Round($Fps * $Seconds))
    $intervalMs = [int]([math]::Round(1000.0 / $Fps))
    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    for ($i = 0; $i -lt $frameCount; $i++) {
        $target = [double]$i * $intervalMs
        $wait = $target - $sw.Elapsed.TotalMilliseconds
        if ($wait -gt 1) { Start-Sleep -Milliseconds ([int]$wait) }

        $r = New-Object Win+RECT
        [Win]::GetWindowRect($h, [ref]$r) | Out-Null
        $x = $r.Left - $Pad; $y = $r.Top - $Pad
        $w = ($r.Right - $r.Left) + 2 * $Pad
        $hh = ($r.Bottom - $r.Top) + 2 * $Pad
        if ($w -le 0 -or $hh -le 0) { continue }

        $bmp = New-Object System.Drawing.Bitmap $w, $hh
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $hh))
        $name = "frame_{0:0000}.png" -f $i
        $bmp.Save((Join-Path $absOut $name), [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
    }
    Write-Host "Captured $frameCount frames ($Fps fps) -> $OutDir"
    Write-Host "Next: assemble with ffmpeg (see scripts\Encode-Clip.ps1) or drop the folder on ScreenToGif."
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
}
