<#
.SYNOPSIS
    Turn a folder of frame_*.png (from Capture-Frames.ps1) into a promo MP4 and/or GIF.

.DESCRIPTION
    MP4 is the recommended format for Reddit/X/LinkedIn: they autoplay + loop it like a GIF, but the
    reset toast is a smooth gradient + confetti, which a 256-colour GIF bands and dithers badly. A GIF
    is still produced (two-pass palettegen/paletteuse for the best possible quality) for places that
    need a real .gif — e.g. an inline animation in the GitHub README.

    Requires ffmpeg on PATH. Install once (no admin):  winget install Gyan.FFmpeg
    Zero-install alternative: open ScreenToGif, "Load recording" → point it at the frames folder.

.PARAMETER InDir     Folder holding frame_0000.png … (default docs\_preview\frames)
.PARAMETER OutBase   Output path without extension (default docs\_preview\reset-toast → .mp4 / .gif)
.PARAMETER Fps       Must match the capture fps (default 25)
.PARAMETER Gif       Also emit a .gif (default $true). MP4 is always emitted.
.PARAMETER MaxWidth  Downscale to this width for a smaller file (0 = keep source). GIF caps at 640.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Encode-Clip.ps1
#>
param(
    [string]$InDir   = "docs\_preview\frames",
    [string]$OutBase = "docs\_preview\reset-toast",
    [int]$Fps        = 25,
    [bool]$Gif       = $true,
    [int]$MaxWidth   = 0
)

$ErrorActionPreference = "Stop"

$ff = (Get-Command ffmpeg -ErrorAction SilentlyContinue)
if (-not $ff) {
    Write-Host "ffmpeg not found on PATH."
    Write-Host "  Install (no admin):  winget install Gyan.FFmpeg   then reopen the terminal."
    Write-Host "  Or use ScreenToGif and load the frames folder: $InDir"
    exit 1
}

if (-not (Test-Path (Join-Path $InDir "frame_0000.png"))) {
    throw "No frame_0000.png in $InDir — run Capture-Frames.ps1 first."
}
$pattern = Join-Path $InDir "frame_%04d.png"

# MP4 (H.264, yuv420p for universal playback; even dimensions required by the codec).
$vf = "scale=trunc(iw/2)*2:trunc(ih/2)*2"
if ($MaxWidth -gt 0) { $vf = "scale='min($MaxWidth,iw)':-2" }
$mp4 = "$OutBase.mp4"
& ffmpeg -y -framerate $Fps -i $pattern -vf $vf -c:v libx264 -pix_fmt yuv420p -movflags +faststart $mp4
Write-Host "wrote $mp4"

# GIF (two-pass palette for the best quality GIF can manage; capped at 640px wide).
if ($Gif) {
    $gifw = if ($MaxWidth -gt 0 -and $MaxWidth -lt 640) { $MaxWidth } else { 640 }
    $pal = Join-Path $env:TEMP "ct_palette.png"
    & ffmpeg -y -i $pattern -vf "fps=$Fps,scale=${gifw}:-1:flags=lanczos,palettegen=stats_mode=diff" $pal
    $gif = "$OutBase.gif"
    & ffmpeg -y -framerate $Fps -i $pattern -i $pal -lavfi "fps=$Fps,scale=${gifw}:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=3" $gif
    Write-Host "wrote $gif"
}
