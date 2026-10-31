# store-shots.ps1 - As capturas de tela da listagem da Store, uma serie por idioma (T412).
#
# Para cada idioma da listagem: captura a interface naquele idioma com dados sinteticos (--lang + --sample,
# nunca os desta maquina) e compoe cada captura sob a sua legenda do store-listing.md (--store-shot).
# Saida: dist\store-shots\<idioma>\screenshot-<n>.png, na ordem das legendas. Sobe cada pasta no idioma
# dela no Partner Center; o store-listing-csv.ps1 liga as legendas depois.
#
#   powershell -File build\store-shots.ps1 [-Exe <ClaudeTray.exe>]
param(
    [string]$Exe
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $root 'bin\Debug\net10.0-windows\win-x64\ClaudeTray.exe' }
if (-not (Test-Path $Exe)) { throw "exe nao encontrado: $Exe (dotnet build -c Debug, ou -Exe)" }
. (Join-Path $PSScriptRoot 'store-listing-lib.ps1')
$langs = Read-StoreListing

# A ordem e a das legendas no .md: semana, sessao de 5h, aviso de ritmo, sessoes, vazao, perfis.
# Cada item: o arquivo que a captura escreve, relativo a pasta raw.
$order = @('stats-7d.png', 'stats-5h.png', 'toast-danger.png', 'sess-sessions.png', 'live-throughput.png', 'link.png')

function Run([string[]]$a) {
    # Start-Process do PowerShell 5.1 junta os argumentos sem aspas: uma legenda com espacos viraria varios.
    $quoted = $a | ForEach-Object { if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ } }
    $p = Start-Process $Exe -ArgumentList ($quoted -join ' ') -Wait -PassThru -NoNewWindow `
        -RedirectStandardOutput "$env:TEMP\store-shots.out" -RedirectStandardError "$env:TEMP\store-shots.err"
    if ($p.ExitCode -ne 0) { throw "$($a -join ' ') saiu com $($p.ExitCode): $(Get-Content "$env:TEMP\store-shots.out","$env:TEMP\store-shots.err" -Raw)" }
}

foreach ($code in ($langs.Keys | Sort-Object)) {
    $lang = $StoreAppLang[$code]
    $dir = Join-Path $root "dist\store-shots\$code"
    $raw = Join-Path $dir 'raw'
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force $raw | Out-Null

    # Cada aba com a variante feita para publicar: ghost desenha a semana anterior atras da curva, live e
    # sessions trocam o que leria desta maquina por dados inventados.
    Run @('--lang', $lang, '--capture-stats', (Join-Path $raw 'stats'), 'ghost', '--sample')
    Run @('--lang', $lang, '--capture-stats', (Join-Path $raw 'live'), 'live', '--sample')
    Run @('--lang', $lang, '--capture-stats', (Join-Path $raw 'sess'), 'sessions', '--sample')
    Run @('--lang', $lang, '--capture-toast', 'danger', (Join-Path $raw 'toast-danger.png'))
    # Enquadrado pelo titulo da secao (LinkHeader), nao pelo cartao: card=LinkCard corta o titulo, e um
    # scroll= fixo cairia num lugar diferente em cada idioma.
    Run @('--lang', $lang, '--capture-settings', (Join-Path $raw 'link.png'), 'ClaudeCode', 'card=LinkHeader', '--sample')

    $captions = $langs[$code].Captions
    if ($captions.Count -ne $order.Count) { throw "$code tem $($captions.Count) legendas para $($order.Count) capturas" }
    for ($i = 0; $i -lt $order.Count; $i++) {
        Run @('--store-shot', (Join-Path $raw $order[$i]), (Join-Path $dir "screenshot-$($i + 1).png"), $captions[$i])
    }
    Write-Host "$code -> $dir"
}
