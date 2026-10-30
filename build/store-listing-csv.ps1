# store-listing-csv.ps1 - Preenche o CSV de listagem do Partner Center a partir de build\msix\store-listing.md (T412).
#
# O Partner Center exporta a listagem como CSV (uma coluna por idioma) e importa o mesmo formato de volta.
# Este script le o CSV exportado, preenche em cada coluna de idioma a descricao, a descricao curta, os
# recursos, as palavras-chave, o copyright, o estudio, as legendas e os caminhos das imagens, e grava o
# resultado numa pasta junto com as imagens - e essa pasta que se importa. O texto vive so no .md; o CSV e
# gerado, nunca editado a mao.
#
#   powershell -File build\store-listing-csv.ps1 -Template <listingData-...csv>
#
# As imagens saem de dist\store-art (build-msix.cmd, ou ClaudeTray.exe --store-art dist\store-art).
param(
    [Parameter(Mandatory = $true)][string]$Template,
    [string]$Out,
    # So o texto: os campos de imagem ficam como vieram no CSV exportado. Serve para separar um erro de
    # importacao do texto de um erro das imagens.
    [switch]$TextOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path $root 'dist\store-listing' }
$art = Join-Path $root 'dist\store-art'
$utf8 = New-Object Text.UTF8Encoding $false

# --- 1) O texto de cada idioma, do .md -------------------------------------------
$md = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'msix\store-listing.md'), $utf8) -replace "`r`n", "`n"
$langs = @{}
foreach ($m in [regex]::Matches($md, '(?ms)^## [^\n]*\(([a-z]{2}-[a-z]{2})\)\n(.*?)(?=^## |\z)')) {
    $code = $m.Groups[1].Value
    $body = $m.Groups[2].Value
    # Blocos sob um cabecalho so em negrito: descricao curta, descricao, recursos, legendas - nesta ordem.
    $blocks = [regex]::Matches($body, '(?ms)^\*\*[^*\n:]+\*\*\n\n(.*?)(?=^\*\*|\z)') | ForEach-Object { $_.Groups[1].Value.Trim() }
    # Linhas "**Rotulo:** valor": palavras-chave e copyright - nesta ordem.
    $inline = [regex]::Matches($body, '(?m)^\*\*[^*\n]+?:\*\* (.+)$') | ForEach-Object { $_.Groups[1].Value.Trim() }
    if ($blocks.Count -ne 4 -or $inline.Count -ne 2) { throw "secao $code fora do formato: $($blocks.Count) blocos, $($inline.Count) rotulos" }

    # Paragrafos do .md sao quebrados em 100 colunas; na loja cada paragrafo e uma linha.
    $paras = ($blocks[1] -split "\n\n") | ForEach-Object { ($_ -split "\n") -join ' ' }
    $langs[$code] = @{
        Short       = (($blocks[0] -split "\n") -join ' ')
        Description = ($paras -join "`r`n`r`n")
        Features    = @(($blocks[2] -split "\n") | Where-Object { $_ -match '^- ' } | ForEach-Object { $_.Substring(2).Trim() })
        Captions    = @(($blocks[3] -split "\n") | Where-Object { $_ -match '^\d+\. ' } | ForEach-Object { $_ -replace '^\d+\. ', '' })
        Keywords    = @($inline[0] -split ',\s*')
        Copyright   = $inline[1]
    }
}

# --- 2) As imagens, ao lado do CSV --------------------------------------------------
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null
$images = @{
    'StoreLogo720x1080'        = 'poster-9x16-1440x2160.png'
    'StoreLogo1080x1080'       = 'box-1x1-2160x2160.png'
    'StoreLogo300x300'         = 'tile-300x300.png'
    'StoreLogoOverride150x150' = 'tile-150x150.png'
    'StoreLogoOverride71x71'   = 'tile-71x71.png'
    'PromoImage1920x1080'      = 'hero-16x9-3840x2160.png'
}
$shots = @(Get-ChildItem $art -Filter 'screenshot-*.png' | Sort-Object { [int]($_.Name -replace '^screenshot-(\d+)-.*$', '$1') })
if ($TextOnly) { $images = @{}; $shots = @() }
foreach ($f in @($images.Values) + @($shots | ForEach-Object Name)) {
    $src = Join-Path $art $f
    if (-not (Test-Path $src)) { throw "imagem ausente: $src (rode build-msix.cmd)" }
    Copy-Item $src $Out
}

# --- 3) O CSV ---------------------------------------------------------------------
$rows = Import-Csv $Template -Encoding UTF8
foreach ($row in $rows) {
    foreach ($code in $langs.Keys) {
        if (-not ($row.PSObject.Properties.Name -contains $code)) { continue }
        $l = $langs[$code]
        $f = $row.Field
        $value = $null
        switch -regex ($f) {
            '^Description$'                   { $value = $l.Description }
            '^ShortDescription$'              { $value = $l.Short }
            '^DevStudio$'                     { $value = 'Alegauss' }
            '^CopyrightTrademarkInformation$' { $value = $l.Copyright }
            '^OverrideLogosForWin10$'         { if (-not $TextOnly) { $value = 'True' } }
            '^Feature(\d+)$'                  { $i = [int]$Matches[1] - 1; $value = if ($i -lt $l.Features.Count) { $l.Features[$i] } else { '' } }
            '^SearchTerm(\d+)$'               { $i = [int]$Matches[1] - 1; $value = if ($i -lt $l.Keywords.Count) { $l.Keywords[$i] } else { '' } }
            '^DesktopScreenshot(\d+)$'        { if (-not $TextOnly) { $i = [int]$Matches[1] - 1; $value = if ($i -lt $shots.Count) { $shots[$i].Name } else { '' } } }
            '^DesktopScreenshotCaption(\d+)$' { if (-not $TextOnly) { $i = [int]$Matches[1] - 1; $value = if ($i -lt $l.Captions.Count -and $i -lt $shots.Count) { $l.Captions[$i] } else { '' } } }
        }
        if ($images.ContainsKey($f)) { $value = $images[$f] }
        if ($null -ne $value) { $row.$code = $value }
    }
}

# Gravado no formato exato que o Partner Center exporta, e nao com ConvertTo-Csv: a importacao recusou
# (sem dizer por que) o arquivo do ConvertTo-Csv, que poe aspas em todo campo e no cabecalho. Aqui: aspas so
# onde o campo precisa, LF dentro de um campo, CRLF entre linhas, BOM, e sem quebra de linha no fim.
function Cell([string]$v) {
    if ($null -eq $v) { return '' }
    $v = $v -replace "`r`n", "`n"
    if ($v -match '[",\n]') { return '"' + $v.Replace('"', '""') + '"' }
    return $v
}
$columns = $rows[0].PSObject.Properties.Name
$lines = @(($columns | ForEach-Object { Cell $_ }) -join ',')
foreach ($row in $rows) { $lines += (($columns | ForEach-Object { Cell $row.$_ }) -join ',') }
$file = Join-Path $Out (Split-Path -Leaf $Template)
[IO.File]::WriteAllText($file, ($lines -join "`r`n"), (New-Object Text.UTF8Encoding $true))
Write-Host "listagem: $file"
Write-Host "importe a pasta $Out no Partner Center (Listagens da Store > Importar)"
