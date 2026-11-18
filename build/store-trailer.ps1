# store-trailer.ps1 - O trailer da listagem da Store e a miniatura dele, em cada idioma (T464).
#
# Para cada idioma do build\msix\store-listing.md, escreve a especificacao que o --store-trailer le: o selo,
# a abertura, a fala de que tudo e calculado no PC, o fechamento, o aviso capturado naquele idioma e as
# capturas compostas que entram no meio. Saida: dist\store-trailer\<idioma>\trailer.mp4 e trailer.png (a
# miniatura que a Store pede ao lado de cada trailer), 1920 x 1080. Precisa do ffmpeg no PATH
# (winget install Gyan.FFmpeg) e das capturas do build\store-shots.ps1, que tem de rodar antes.
#
#   powershell -File build\store-trailer.ps1 [-Language pt-br] [-Exe <ClaudeTray.exe>]
param(
    [string]$Language,
    [string]$Exe
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $root 'bin\Debug\net10.0-windows\win-x64\ClaudeTray.exe' }
if (-not (Test-Path $Exe)) { throw "exe nao encontrado: $Exe (dotnet build -c Debug, ou -Exe)" }
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) { throw 'ffmpeg nao esta no PATH (winget install Gyan.FFmpeg)' }
. (Join-Path $PSScriptRoot 'store-listing-lib.ps1')
$langs = Read-StoreListing

# Do meio do trailer: semana, sessao de 5h, sessoes e vazao - as capturas 1, 2, 4 e 5. O aviso (3) ja esta
# na abertura, e os perfis (6) sao a funcao que menos gente usa.
$middle = @(1, 2, 4, 5)

$utf8 = New-Object Text.UTF8Encoding $false
foreach ($code in @($langs.Keys | Sort-Object | Where-Object { -not $Language -or $_ -eq $Language })) {
    $l = $langs[$code]
    $shots = Join-Path $root "dist\store-shots\$code"
    $screens = @($middle | ForEach-Object { Join-Path $shots "screenshot-$_.png" })
    $toast = Join-Path $shots 'raw\toast-danger.png'
    foreach ($f in @($screens) + $toast) { if (-not (Test-Path $f)) { throw "sem $f (rode build\store-shots.ps1 antes)" } }

    $dir = Join-Path $root "dist\store-trailer\$code"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $spec = [ordered]@{
        trayHeadline  = $l.Tray.Headline
        traySubline   = $l.Tray.Subline
        localHeadline = $l.Local.Headline
        localSubline  = $l.Local.Subline
        end           = $l.TrailerEnd
        badge         = $l.Badge
        toast         = $toast
        screenshots   = $screens
    }
    $specPath = Join-Path $dir 'trailer.json'
    [IO.File]::WriteAllText($specPath, ($spec | ConvertTo-Json -Depth 4), $utf8)
    $p = Start-Process $Exe -ArgumentList "--store-trailer `"$(Join-Path $dir 'trailer.mp4')`" `"$specPath`"" -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) { throw "o trailer de $code falhou ($($p.ExitCode))" }
}
Write-Host "trailers: $(Join-Path $root 'dist\store-trailer')"
