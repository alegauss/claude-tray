# =============================================================================
# update-winget.ps1 - Regenera os manifestos winget a partir da versao unica
# definida em ClaudeTray.csproj (<Version>) e do instalador ja gerado em
# dist\ClaudeTray-Setup.exe.
#
# Atualiza, nos 3 YAMLs de winget\:
#   PackageVersion, InstallerUrl, DisplayVersion, ReleaseNotesUrl,
#   InstallerSha256 (hash do instalador) e ReleaseDate (data de hoje).
#
# Uso:  build-installer.cmd   (gera dist\ClaudeTray-Setup.exe)
#       powershell -File update-winget.ps1
# =============================================================================
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# --- 1) Versao: fonte unica em ClaudeTray.csproj -----------------------------
$csproj = Get-Content (Join-Path $root 'ClaudeTray.csproj') -Raw
if ($csproj -notmatch '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>') {
    throw "Nao foi possivel ler <Version> de ClaudeTray.csproj"
}
$version = $Matches[1]

# --- 2) SHA256 do instalador gerado ------------------------------------------
$setup = Join-Path $root 'dist\ClaudeTray-Setup.exe'
if (-not (Test-Path $setup)) {
    throw "Instalador nao encontrado: $setup`nRode build-installer.cmd primeiro."
}
$sha = (Get-FileHash $setup -Algorithm SHA256).Hash.ToUpperInvariant()
$date = (Get-Date).ToString('yyyy-MM-dd')

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
$rep = @{
    'alegauss.ClaudeCodeTray.yaml'            = $null
    'alegauss.ClaudeCodeTray.installer.yaml'  = $null
    'alegauss.ClaudeCodeTray.locale.en-US.yaml' = $null
}

foreach ($name in @($rep.Keys)) {
    $path = Join-Path $dir $name
    $c = Read-Utf8 $path

    $c = $c -replace '(?m)^PackageVersion:\s*.+$', "PackageVersion: $version"
    $c = $c -replace 'releases/download/v[0-9]+\.[0-9]+\.[0-9]+/', "releases/download/v$version/"
    $c = $c -replace 'releases/tag/v[0-9]+\.[0-9]+\.[0-9]+', "releases/tag/v$version"
    $c = $c -replace '(?m)^(\s*)DisplayVersion:\s*.+$', "`${1}DisplayVersion: $version"
    $c = $c -replace '(?m)^(\s*)InstallerSha256:\s*.+$', "`${1}InstallerSha256: $sha"
    $c = $c -replace '(?m)^ReleaseDate:\s*.+$', "ReleaseDate: $date"

    Write-Utf8 $path $c
    Write-Host "atualizado: winget\$name"
}

Write-Host "`nManifestos winget atualizados para v$version."
