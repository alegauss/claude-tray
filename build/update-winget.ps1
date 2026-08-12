# =============================================================================
# update-winget.ps1 - Regenera os manifestos winget a partir da versao unica
# definida em ClaudeTray.csproj (<Version>) e do instalador ja gerado em
# dist\ClaudeTray-Setup.exe.
#
# Atualiza, em TODOS os YAMLs de winget\ (inclui os locales pt-BR, en-US, etc.):
#   PackageVersion, InstallerUrl, DisplayVersion, ReleaseNotesUrl,
#   InstallerSha256 (hash do instalador) e ReleaseDate (data de hoje).
#
# Uso local:  build\build-installer.cmd   (gera dist\ClaudeTray-Setup.exe)
#             powershell -File build\update-winget.ps1
#
# Uso no CI:  powershell -File build\update-winget.ps1 -Version 1.3.14 -Sha <SHA256>
#   -Version  sobrepoe a versao lida do csproj (default: <Version> do csproj)
#   -Sha      sobrepoe o hash (default: SHA256 de dist\ClaudeTray-Setup.exe)
#   -Date     sobrepoe a data    (default: a data do commit, NAO a de hoje - ver T351)
# =============================================================================
param(
    [string]$Version,
    [string]$Sha,
    [string]$Date
)
$ErrorActionPreference = 'Stop'
# Este script vive em build\ junto de winget\; o csproj e dist\ estao na pasta acima.
$root = Split-Path $PSScriptRoot -Parent

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
        throw "Instalador nao encontrado: $setup`nRode build\build-installer.cmd primeiro (ou passe -Sha)."
    }
    $Sha = (Get-FileHash $setup -Algorithm SHA256).Hash.ToUpperInvariant()
}
$sha = $Sha.ToUpperInvariant()

# --- 3) Data: -Date, senao a data do commit que esta sendo empacotado (T351) -
# O relogio era a fonte, e por isso este era o unico campo do manifesto que uma re-emissao
# da mesma tag NAO reproduzia: o .exe e o instalador saem byte a byte iguais (T350), e o
# ReleaseDate saia com a data do dia em que o job rodou. A data do commit responde a mesma
# pergunta e responde igual para sempre - `v1.6.2` da 2026-08-07, que e exatamente o que o
# manifesto publicado ja carrega.
#
# A tag primeiro e o HEAD depois, porque as duas respondem em situacoes diferentes: rodando
# localmente em main para regerar uma versao antiga, a tag e a unica que sabe a data certa;
# cortando uma versao nova, a tag ainda nao existe e o HEAD e o commit do bump. No CI as duas
# coincidem - build.yml roda sobre o proprio ref da tag.
# Um rev que nao existe nao e erro aqui, e a proxima fonte da lista - mas dizer isso custa duas
# protecoes, nao uma. No Windows PowerShell 5.1 o stderr de um .exe nativo vira um NativeCommandError
# e, com o $ErrorActionPreference = 'Stop' la de cima, esse registro TERMINA o script: `2>$null` nao
# evita isso. Entao a sondagem e `rev-parse -q`, que nao escreve nada quando falha, e mesmo assim o
# bloco baixa o preference - senao "cortar a versao antes de criar a tag", que e o fluxo local
# normal, derrubaria a geracao dos manifestos em vez de cair no HEAD.
function Get-CommitDate([string]$rev) {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $null = & git rev-parse -q --verify "$rev^{commit}" 2>$null
        if ($LASTEXITCODE -ne 0) { return $null }
        $out = & git log -1 --format=%cs $rev 2>$null
        if ($LASTEXITCODE -eq 0 -and $out -match '^\d{4}-\d{2}-\d{2}$') { return $out }
        return $null
    }
    catch { return $null }   # git ausente: nao ha data a extrair, e a lista continua
    finally { $ErrorActionPreference = $prev }
}

$dateFrom = 'parametro -Date'
if (-not $Date) {
    $Date = Get-CommitDate "v$version"
    $dateFrom = "commit da tag v$version"
}
if (-not $Date) {
    $Date = Get-CommitDate 'HEAD'
    $dateFrom = 'commit do HEAD'
}
# Sem git (um zip do codigo, por exemplo) nao ha data reproduzivel a extrair; o relogio volta a
# ser a fonte, e a linha abaixo diz que foi ele.
if (-not $Date) {
    $Date = (Get-Date).ToString('yyyy-MM-dd')
    $dateFrom = 'relogio - sem git, esta data nao se reproduz'
}
$date = $Date

Write-Host "Versao : $version"
Write-Host "SHA256 : $sha"
Write-Host "Data   : $date  ($dateFrom)"

# --- 3) Reescreve os campos dinamicos nos manifestos -------------------------
# Le/escreve sempre em UTF-8 (sem BOM): no Windows PowerShell 5.1, Get-Content/-Raw usa o
# codepage ANSI por padrao e corromperia acentos e travessoes (—) dos manifestos.
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Read-Utf8([string]$path)  { [System.IO.File]::ReadAllText($path, $utf8) }
function Write-Utf8([string]$path, [string]$text) { [System.IO.File]::WriteAllText($path, $text, $utf8) }

$dir = Join-Path $PSScriptRoot 'winget'

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
