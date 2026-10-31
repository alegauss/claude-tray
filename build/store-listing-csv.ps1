# store-listing-csv.ps1 - Preenche o CSV de listagem do Partner Center a partir de build\msix\store-listing.md (T412).
#
# O Partner Center exporta a listagem como CSV (uma coluna por idioma) e importa o mesmo formato de volta.
# Este script le o CSV exportado e preenche em cada coluna de idioma a descricao, a descricao curta, os
# recursos, as palavras-chave, o copyright, o estudio e as legendas. O texto vive so no .md; o CSV e
# gerado, nunca editado a mao.
#
#   powershell -File build\store-listing-csv.ps1 -Template <listingData-...csv> [-ImagesFrom es-es]
#
# Imagens: a importacao recusa imagem citada por nome de arquivo (medido em 30/10/2026), entao elas sobem pela
# tela do Partner Center e voltam no CSV exportado como URL. As capturas de tela sao por idioma
# (build\store-shots.ps1) e sobem em cada idioma; este script so liga a legenda de cada captura que o idioma
# tem. Logos e arte de super-heroi valem para todos: -ImagesFrom copia as URLs de um idioma para os outros.
param(
    [Parameter(Mandatory = $true)][string]$Template,
    [string]$Out,
    [string]$ImagesFrom
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path $root 'dist\store-listing' }
. (Join-Path $PSScriptRoot 'store-listing-lib.ps1')
$langs = Read-StoreListing

$rows = Import-Csv $Template -Encoding UTF8
$columns = $rows[0].PSObject.Properties.Name
if ($ImagesFrom -and -not ($columns -contains $ImagesFrom)) { throw "coluna $ImagesFrom nao existe no CSV" }

# Quantas capturas cada idioma tem, para ligar so as legendas delas.
$shotCount = @{}
foreach ($code in $langs.Keys) {
    if ($columns -contains $code) {
        $shotCount[$code] = @($rows | Where-Object { $_.Field -match '^DesktopScreenshot\d+$' -and $_.$code }).Count
    }
}

$shared = '^(StoreLogo\w+|PromoImage\w+|OverrideLogosForWin10)$'
foreach ($row in $rows) {
    $f = $row.Field
    foreach ($code in $langs.Keys) {
        if (-not ($columns -contains $code)) { continue }
        $l = $langs[$code]
        $value = $null
        switch -regex ($f) {
            '^Description$'                   { $value = $l.Description }
            '^ShortDescription$'              { $value = $l.Short }
            '^DevStudio$'                     { $value = 'Alegauss' }
            '^CopyrightTrademarkInformation$' { $value = $l.Copyright }
            '^Feature(\d+)$'                  { $i = [int]$Matches[1] - 1; $value = if ($i -lt $l.Features.Count) { $l.Features[$i] } else { '' } }
            '^SearchTerm(\d+)$'               { $i = [int]$Matches[1] - 1; $value = if ($i -lt $l.Keywords.Count) { $l.Keywords[$i] } else { '' } }
            '^DesktopScreenshotCaption(\d+)$' { $i = [int]$Matches[1] - 1; $value = if ($i -lt $shotCount[$code] -and $i -lt $l.Captions.Count) { $l.Captions[$i] } else { '' } }
        }
        if ($ImagesFrom -and $code -ne $ImagesFrom -and $f -match $shared) { $value = $row.$ImagesFrom }
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
$lines = @(($columns | ForEach-Object { Cell $_ }) -join ',')
foreach ($row in $rows) { $lines += (($columns | ForEach-Object { Cell $row.$_ }) -join ',') }
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null
$file = Join-Path $Out (Split-Path -Leaf $Template)
[IO.File]::WriteAllText($file, ($lines -join "`r`n"), (New-Object Text.UTF8Encoding $true))
Write-Host "listagem: $file"
