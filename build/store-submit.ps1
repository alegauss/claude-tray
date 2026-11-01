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
#
# Com -Package (T417), envia tambem dist\ClaudeTray.msix (build\build-msix.cmd) no mesmo ZIP, marcando os
# pacotes da ultima publicada para remocao; as capturas so sao trocadas junto se -Screenshots vier tambem.
#
#   powershell -File build\store-submit.ps1 -DryRun   # so le: confere credenciais, app e submissao pendente
#   powershell -File build\store-submit.ps1           # faz a troca e envia para certificacao
#   powershell -File build\store-submit.ps1 -Package  # envia o pacote novo, mantendo as capturas
param(
    [string]$AppId = '9P27BHWNT56D',
    [switch]$DryRun,
    # Envia dist\ClaudeTray.msix no lugar dos pacotes publicados (T417).
    [switch]$Package,
    # Troca as capturas; e o padrao quando -Package nao vem.
    [switch]$Screenshots,
    # So le: mostra o que a submissao pendente tem de diferente da ultima publicada.
    [switch]$Compare,
    # Apaga a submissao pendente e cria outra pela API. So serve para uma pendente que a propria API criou:
    # a API nao edita nem apaga uma criada pela tela do Partner Center ("can only update, delete, and commit
    # submissions that are created through the API", medido 31/10/2026) - essa se apaga pela tela, e depois o
    # script cria a sua. Rode -Compare antes: o que a pendente tem de diferente da publicada se perde.
    [switch]$ReplacePending
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
    # A API recusa ate um POST sem corpo que nao se diga JSON ("Only JSON content is accepted", medido ao
    # criar a submissao), entao todo POST e PUT vai como JSON, vazio quando nao ha o que mandar.
    if ($method -in 'Post', 'Put') {
        $json = if ($null -ne $body) { $body | ConvertTo-Json -Depth 50 } else { '' }
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

if (-not $Package) { $Screenshots = $true }
$msix = Join-Path $root 'dist\ClaudeTray.msix'
if ($Package) {
    # A Store recusa um pacote que nao sobe a versao, mas so depois do upload e da validacao; conferir antes.
    $csproj = [IO.File]::ReadAllText((Join-Path $root 'ClaudeTray.csproj'))
    if ($csproj -notmatch '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>') { throw '<Version> nao encontrado em ClaudeTray.csproj' }
    $newVersion = [version]"$($Matches[1]).0"
    if (-not (Test-Path $msix)) { throw "sem $msix (rode build\build-msix.cmd)" }
    if ((Get-Item $msix).LastWriteTime -lt (Get-Item (Join-Path $root 'ClaudeTray.csproj')).LastWriteTime) {
        throw "$msix e mais antigo que o ClaudeTray.csproj; rode build\build-msix.cmd de novo"
    }
    $published = Api 'Get' "$base/submissions/$($app.lastPublishedApplicationSubmission.id)"
    $live = @($published.applicationPackages | ForEach-Object { [version]$_.version })
    $highest = $live | Sort-Object -Descending | Select-Object -First 1
    Write-Host "pacote: $newVersion (publicado: $(($live | ForEach-Object { "$_" }) -join ', '))"
    if ($highest -and $newVersion -le $highest) { throw "a versao $newVersion nao e maior que a publicada $highest; suba <Version>" }
}

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

# O que a pendente tem de diferente da ultima publicada, campo a campo, antes de alguem decidir apaga-la.
if ($Compare) {
    if (-not $pendingId) { Write-Host 'nenhuma pendente para comparar'; return }
    $pub = Api 'Get' "$base/submissions/$($app.lastPublishedApplicationSubmission.id)"
    $pen = Api 'Get' "$base/submissions/$pendingId"
    function Flat($o, [string]$path, $acc) {
        if ($null -eq $o -or $o -is [string] -or $o -is [ValueType]) { $acc[$path] = "$o"; return }
        if ($o -is [Array]) { for ($i = 0; $i -lt $o.Count; $i++) { Flat $o[$i] "$path[$i]" $acc }; return }
        foreach ($p in $o.PSObject.Properties) { Flat $p.Value "$path.$($p.Name)" $acc }
    }
    $a = @{}; $b = @{}
    Flat $pub '' $a; Flat $pen '' $b
    $skip = '^\.(id|status|statusDetails|fileUploadUrl|friendlyName|targetPublishDate)|\.(fileName|id|fileStatus)$|\.applicationPackages'
    $keys = @($a.Keys) + @($b.Keys) | Sort-Object -Unique | Where-Object { $_ -notmatch $skip -and $a[$_] -ne $b[$_] }
    Write-Host "campos lidos: publicada $($a.Count), pendente $($b.Count) (publicada $($app.lastPublishedApplicationSubmission.id))"
    Write-Host "campos diferentes (fora ids, status e nomes de arquivo): $($keys.Count)"
    $keys | Select-Object -First 60 | ForEach-Object {
        $va = "$($a[$_])"; $vb = "$($b[$_])"
        Write-Host ("  {0}`n    publicada: {1}`n    pendente:  {2}" -f $_, $va.Substring(0, [Math]::Min(90, $va.Length)), $vb.Substring(0, [Math]::Min(90, $vb.Length)))
    }
    return
}

if ($pendingId -and $ReplacePending) {
    Api 'Delete' "$base/submissions/$pendingId" $null | Out-Null
    Write-Host "submissao pendente $pendingId apagada"
    $pendingId = $null
}
if ($pendingId) {
    $sub = Api 'Get' "$base/submissions/$pendingId"
    if ($sub.status -ne 'PendingCommit') { throw "a submissao pendente esta em '$($sub.status)'; so uma em PendingCommit pode ser editada" }
    # A pendente criada pela tela devolve PendingCommit no GET e recusa o PUT; avisar antes de montar o ZIP.
    Write-Host 'aviso: se esta pendente foi criada pela tela do Partner Center, a API vai recusa-la; apague-a pela tela e rode de novo'
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
    if ($Package) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $msix, 'ClaudeTray.msix')
        $old = @($sub.applicationPackages | ForEach-Object { $_.fileStatus = 'PendingDelete'; $_ })
        $sub.applicationPackages = @($old) + @([pscustomobject]@{
                fileName              = 'ClaudeTray.msix'
                fileStatus            = 'PendingUpload'
                minimumDirectXVersion = 'None'
                minimumSystemRam      = 'None'
            })
        Write-Host "  pacote $newVersion adicionado, $($old.Count) antigo(s) removido(s)"
    }
    foreach ($p in $(if ($Screenshots) { $sub.listings.PSObject.Properties })) {
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
