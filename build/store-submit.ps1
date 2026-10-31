# store-submit.ps1 - Troca as capturas de tela da listagem da Store pela API de submissao (T412).
#
# A importacao de CSV do Partner Center recusa imagem citada por nome de arquivo (medido duas vezes em
# 30-31/10/2026, a segunda com o formato do CSV ja identico ao export), entao capturas por idioma so entram
# pela tela ou por esta API. O script:
#   1) pega um token do Microsoft Entra com as credenciais de um app Entra com funcao Gerente no Partner
#      Center, lidas de STORE_TENANT_ID, STORE_CLIENT_ID e STORE_CLIENT_SECRET (nunca do repositorio);
#   2) reaproveita a submissao pendente do app, ou cria uma nova (a API clona a ultima publicada);
#   3) em cada idioma da listagem, marca as capturas atuais para remocao e adiciona as de
#      dist\store-shots\<idioma> (build\store-shots.ps1), com a legenda daquele idioma do store-listing.md;
#   4) envia as imagens num ZIP para a URL que a submissao fornece, confirma a submissao e acompanha o status.
#
#   powershell -File build\store-submit.ps1 -DryRun   # so le: confere credenciais, app e submissao pendente
#   powershell -File build\store-submit.ps1           # faz a troca e envia para certificacao
param(
    [string]$AppId = '9P27BHWNT56D',
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'store-listing-lib.ps1')
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Env([string]$name) {
    $v = [Environment]::GetEnvironmentVariable($name, 'Process')
    if (-not $v) { $v = [Environment]::GetEnvironmentVariable($name, 'User') }
    if (-not $v) { throw "$name nao definido (veja o topo de build\store-submit.ps1)" }
    return $v
}

# --- 1) Token ---------------------------------------------------------------------------------------------
$token = (Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$(Env 'STORE_TENANT_ID')/oauth2/token" -Body @{
        grant_type    = 'client_credentials'
        client_id     = (Env 'STORE_CLIENT_ID')
        client_secret = (Env 'STORE_CLIENT_SECRET')
        resource      = 'https://manage.devcenter.microsoft.com'
    }).access_token
$headers = @{ Authorization = "Bearer $token" }
$base = "https://manage.devcenter.microsoft.com/v1.0/my/applications/$AppId"

function Api([string]$method, [string]$uri, $body) {
    $req = @{ Method = $method; Uri = $uri; Headers = $headers }
    if ($null -ne $body) {
        $json = $body | ConvertTo-Json -Depth 50
        $req.Body = [Text.Encoding]::UTF8.GetBytes($json)
        $req.ContentType = 'application/json; charset=utf-8'
    }
    return Invoke-RestMethod @req
}

# --- 2) Submissao -----------------------------------------------------------------------------------------
$app = Api 'Get' $base
Write-Host "app: $($app.primaryName) ($AppId)"
$pendingId = $app.pendingApplicationSubmission.id
if ($pendingId) { Write-Host "submissao pendente: $pendingId" } else { Write-Host 'nenhuma submissao pendente' }

if ($DryRun) {
    if ($pendingId) {
        $sub = Api 'Get' "$base/submissions/$pendingId"
        Write-Host "status da pendente: $($sub.status)"
        foreach ($p in $sub.listings.PSObject.Properties) {
            $shots = @($p.Value.baseListing.images | Where-Object { $_.imageType -eq 'Screenshot' })
            Write-Host "  $($p.Name): $($shots.Count) capturas"
        }
    }
    Write-Host 'DryRun: nada foi alterado.'
    return
}

if ($pendingId) {
    $sub = Api 'Get' "$base/submissions/$pendingId"
    if ($sub.status -ne 'PendingCommit') { throw "a submissao pendente esta em '$($sub.status)'; so uma em PendingCommit pode ser editada" }
} else {
    $sub = Api 'Post' "$base/submissions"
    Write-Host "submissao criada: $($sub.id)"
}

# --- 3) Capturas por idioma -------------------------------------------------------------------------------
$langs = Read-StoreListing
$zipDir = Join-Path $root 'dist\store-submit'
if (Test-Path $zipDir) { Remove-Item $zipDir -Recurse -Force }
New-Item -ItemType Directory -Force $zipDir | Out-Null
$zipPath = Join-Path $zipDir 'images.zip'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    foreach ($p in $sub.listings.PSObject.Properties) {
        $code = $p.Name.ToLowerInvariant()
        if (-not $langs.ContainsKey($code)) { Write-Host "  $code - sem texto no store-listing.md, mantido"; continue }
        $src = Join-Path $root "dist\store-shots\$code"
        $files = @(Get-ChildItem $src -Filter 'screenshot-*.png' -ErrorAction SilentlyContinue |
            Sort-Object { [int]($_.BaseName -replace '^screenshot-', '') })
        if ($files.Count -eq 0) { throw "sem capturas em $src (rode build\store-shots.ps1)" }

        $listing = $p.Value.baseListing
        $kept = @($listing.images | ForEach-Object {
            if ($_.imageType -eq 'Screenshot') { $_.fileStatus = 'PendingDelete' }
            $_
        })
        $captions = $langs[$code].Captions
        $added = for ($i = 0; $i -lt $files.Count; $i++) {
            # Barra normal no nome: e o separador de um ZIP, e o que a API compara com fileName.
            $entry = "$code/$($files[$i].Name)"
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $files[$i].FullName, $entry)
            [pscustomobject]@{
                fileName    = $entry
                fileStatus  = 'PendingUpload'
                imageType   = 'Screenshot'
                description = if ($i -lt $captions.Count) { $captions[$i] } else { '' }
            }
        }
        $listing.images = @($kept) + @($added)
        Write-Host "  $code - $($files.Count) capturas novas, $(@($kept | Where-Object { $_.imageType -eq 'Screenshot' }).Count) antigas removidas"
    }
}
finally { $zip.Dispose() }

$sub = Api 'Put' "$base/submissions/$($sub.id)" $sub

# --- 4) Imagens, commit e status --------------------------------------------------------------------------
Invoke-WebRequest -Method Put -Uri $sub.fileUploadUrl -InFile $zipPath -Headers @{ 'x-ms-blob-type' = 'BlockBlob' } -UseBasicParsing | Out-Null
Write-Host "imagens enviadas ($([int]((Get-Item $zipPath).Length / 1KB)) KB)"
Api 'Post' "$base/submissions/$($sub.id)/commit" $null | Out-Null
Write-Host 'commit pedido; acompanhando...'

do {
    Start-Sleep -Seconds 20
    $status = Api 'Get' "$base/submissions/$($sub.id)/status"
    Write-Host "  $($status.status)"
} while ($status.status -eq 'CommitStarted')

if ($status.status -eq 'CommitFailed') {
    $status.statusDetails.errors | ForEach-Object { Write-Host "  erro: $($_.code) - $($_.details)" }
    throw 'o commit falhou; a submissao continua pendente no Partner Center'
}
Write-Host "status final: $($status.status) - acompanhe a certificacao no Partner Center"
