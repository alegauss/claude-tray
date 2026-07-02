# =============================================================================
# update-release.ps1 - Sobe o numero de versao (release) em um unico passo.
#
# Atualiza:
#   - ClaudeTray.csproj                -> <Version>
#   - winget\*.yaml (todos os locales) -> PackageVersion, InstallerUrl (v...),
#                                          ReleaseNotesUrl (tag/v...), DisplayVersion
#
# NAO mexe em InstallerSha256 nem ReleaseDate: esses dependem do instalador
# efetivamente gerado e sao preenchidos por update-winget.ps1 na hora do release
# (build-installer.cmd -> update-winget.ps1).
#
# Uso:  update-release.cmd 1.4.1
#       powershell -File update-release.ps1 -Version 1.4.1
# =============================================================================
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# --- Valida o formato x.y.z --------------------------------------------------
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "Versao invalida: '$Version'. Use o formato x.y.z (ex.: 1.4.1)."
}

# Le/escreve sempre em UTF-8 (sem BOM): no Windows PowerShell 5.1, Get-Content/-Raw usa o
# codepage ANSI por padrao e corromperia acentos e travessoes (--) dos manifestos.
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Read-Utf8([string]$path)  { [System.IO.File]::ReadAllText($path, $utf8) }
function Write-Utf8([string]$path, [string]$text) { [System.IO.File]::WriteAllText($path, $text, $utf8) }

# --- 1) ClaudeTray.csproj: <Version> ----------------------------------------
$csprojPath = Join-Path $root 'ClaudeTray.csproj'
$csproj = Read-Utf8 $csprojPath
if ($csproj -notmatch '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>') {
    throw "Nao foi possivel encontrar <Version> em ClaudeTray.csproj"
}
$old = $Matches[1]
$csproj = $csproj -replace '<Version>\s*[0-9]+\.[0-9]+\.[0-9]+\s*</Version>', "<Version>$Version</Version>"
Write-Utf8 $csprojPath $csproj
Write-Host "ClaudeTray.csproj: $old -> $Version"

# --- 2) Manifestos winget (campos de versao) --------------------------------
$dir = Join-Path $root 'winget'
foreach ($file in Get-ChildItem -Path $dir -Filter '*.yaml') {
    $path = $file.FullName
    $c = Read-Utf8 $path

    $c = $c -replace '(?m)^PackageVersion:\s*.+$', "PackageVersion: $Version"
    $c = $c -replace 'releases/download/v[0-9]+\.[0-9]+\.[0-9]+/', "releases/download/v$Version/"
    $c = $c -replace 'releases/tag/v[0-9]+\.[0-9]+\.[0-9]+', "releases/tag/v$Version"
    $c = $c -replace '(?m)^(\s*)DisplayVersion:\s*.+$', "`${1}DisplayVersion: $Version"

    Write-Utf8 $path $c
    Write-Host "atualizado: winget\$($file.Name)"
}

Write-Host ""
Write-Host "Versao definida como v$Version (InstallerSha256 e ReleaseDate serao"
Write-Host "gravados por build-installer.cmd -> update-winget.ps1)."
