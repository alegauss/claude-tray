# build-msix.ps1 - Monta o pacote da Microsoft Store a partir do exe ja publicado (T409).
#
# Chamado por build-msix.cmd depois do dotnet publish. Passos:
#   1) versao: <Version> do ClaudeTray.csproj, como X.Y.Z.0 (a Store exige a quarta parte 0)
#   2) layout em dist\msix\layout: o exe, o manifesto com a versao e os logos (--msix-assets)
#   3) makeappx pack -> dist\ClaudeTray.msix, sem assinatura (a Store assina no envio)
#   4) com -Register: instala o layout nesta maquina (Add-AppxPackage -Register), o unico jeito
#      de testar o pacote sem certificado; exige o Modo de desenvolvedor.
param(
    [switch]$Register
)
$ErrorActionPreference = 'Stop'

# Este script vive em build\; o csproj e dist\ estao na pasta acima.
$root = Split-Path -Parent $PSScriptRoot

# --- 1) Versao -----------------------------------------------------------------
$csproj = Get-Content (Join-Path $root 'ClaudeTray.csproj') -Raw
if ($csproj -notmatch '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>') {
    throw '<Version> nao encontrado em ClaudeTray.csproj'
}
$version = "$($Matches[1]).0"

# --- 2) Layout -----------------------------------------------------------------
$exe = Join-Path $root 'bin\Release\net10.0-windows\win-x64\publish\ClaudeTray.exe'
if (-not (Test-Path $exe)) { throw "exe publicado nao encontrado: $exe (rode build.cmd)" }

$dist = Join-Path $root 'dist'
$layout = Join-Path $dist 'msix\layout'
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $layout 'Assets') | Out-Null

Copy-Item $exe $layout
$manifest = Get-Content (Join-Path $PSScriptRoot 'msix\AppxManifest.xml') -Raw -Encoding UTF8
$manifest = $manifest.Replace('{VERSION}', $version)
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))

# Um WinExe nao escreve no console de quem o chama; -Wait e o codigo de saida bastam.
$p = Start-Process $exe -ArgumentList '--msix-assets', (Join-Path $layout 'Assets') -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0) { throw "--msix-assets saiu com $($p.ExitCode)" }

Write-Host "layout: $layout  (versao $version)"

# --- 3) makeappx ---------------------------------------------------------------
$kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$makeappx = Get-ChildItem $kits -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like '*\x64\*' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw 'makeappx.exe nao encontrado: instale o Windows SDK' }

$msix = Join-Path $dist 'ClaudeTray.msix'
& $makeappx.FullName pack /o /d $layout /p $msix | Out-Host
if ($LASTEXITCODE -ne 0) { throw "makeappx saiu com $LASTEXITCODE" }
Write-Host "pacote: $msix"

# --- 4) Teste local ------------------------------------------------------------
if ($Register) {
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
    Write-Host 'instalado a partir do layout: abra pelo menu Iniciar ou rode ClaudeTray.exe de qualquer shell'
}
