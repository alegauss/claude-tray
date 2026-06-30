# =============================================================================
# update-winget.ps1 - Regenera os manifestos winget a partir da versao unica
# definida em ClaudeTray.csproj (<Version>) e do instalador ja gerado em
# dist\ClaudeTray-Setup.exe.
#
# Atualiza, em TODOS os YAMLs de winget\ (inclui os locales pt-BR, en-US, etc.):
#   PackageVersion, InstallerUrl, DisplayVersion, ReleaseNotesUrl,
#   InstallerSha256 (hash do instalador) e ReleaseDate (data de hoje).
#
# Uso local:  build-installer.cmd   (gera dist\ClaudeTray-Setup.exe)
#             powershell -File update-winget.ps1
#
# Uso no CI:  powershell -File update-winget.ps1 -Version 1.3.14 -Sha <SHA256>
#   -Version  sobrepoe a versao lida do csproj (default: <Version> do csproj)
#   -Sha      sobrepoe o hash (default: SHA256 de dist\ClaudeTray-Setup.exe)
#   -Date     sobrepoe a data    (default: hoje, yyyy-MM-dd)
# =============================================================================
param(
    [string]$Version,
    [string]$Sha,
    [string]$Date
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# --- 1) Versao: -Version, senao <Version> do ClaudeTray.csproj ---------------
if (-not $Version) {
    $csproj = Get-Content (Join-Path $root 'ClaudeTray.csproj') -Raw
    if ($csproj -notmatch '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>') {
        throw "Nao foi possivel ler <Version> de ClaudeTray.csproj"
    }
    $Version = $Matches[1]
}
$version = $Version

# --- 2) SHA256: -Sha, senao hash do instalador gerado -----------------------
if (-not $Sha) {
    $setup = Join-Path $root 'dist\ClaudeTray-Setup.exe'
    if (-not (Test-Path $setup)) {
        throw "Instalador nao encontrado: $setup`nRode build-installer.cmd primeiro (ou passe -Sha)."
    }
    $Sha = (Get-FileHash $setup -Algorithm SHA256).Hash.ToUpperInvariant()
}
$sha = $Sha.ToUpperInvariant()

# --- 3) Data: -Date, senao hoje ---------------------------------------------
if (-not $Date) { $Date = (Get-Date).ToString('yyyy-MM-dd') }
$date = $Date

Write-Host "Versao : $version"
Write-Host "SHA256 : $sha"
Write-Host "Data   : $date"

# --- 3) Reescreve os campos dinamicos nos manifestos -------------------------
# Le/escreve sempre em UTF-8 (sem BOM): no Windows PowerShell 5.1, Get-Content/-Raw usa o
# codepage ANSI por padrao e corromperia acentos e travessoes (—) dos manifestos.
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Read-Utf8([string]$path)  { [System.IO.File]::ReadAllText($path, $utf8) }
function Write-Utf8([string]$path, [string]$text) { [System.IO.File]::WriteAllText($path, $text, $utf8) }

$dir = Join-Path $root 'winget'

foreach ($file in Get-ChildItem -Path $dir -Filter '*.yaml') {
    $path = $file.FullName
    $c = Read-Utf8 $path

    $c = $c -replace '(?m)^PackageVersion:\s*.+$', "PackageVersion: $version"
    $c = $c -replace 'releases/download/v[0-9]+\.[0-9]+\.[0-9]+/', "releases/download/v$version/"
    $c = $c -replace 'releases/tag/v[0-9]+\.[0-9]+\.[0-9]+', "releases/tag/v$version"
    $c = $c -replace '(?m)^(\s*)DisplayVersion:\s*.+$', "`${1}DisplayVersion: $version"
    $c = $c -replace '(?m)^(\s*)InstallerSha256:\s*.+$', "`${1}InstallerSha256: $sha"
    $c = $c -replace '(?m)^ReleaseDate:\s*.+$', "ReleaseDate: $date"

    Write-Utf8 $path $c
    Write-Host "atualizado: winget\$($file.Name)"
}

Write-Host "`nManifestos winget atualizados para v$version."
