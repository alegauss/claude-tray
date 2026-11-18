# store-listing-lib.ps1 - Le build\msix\store-listing.md, a fonte do texto da listagem da Store (T412, T464).
# Carregado com ". .\store-listing-lib.ps1" por store-listing-csv.ps1, store-shots.ps1, store-trailer.ps1 e
# store-submit.ps1, para que todos leiam o mesmo texto do mesmo jeito.

# Uma linha "manchete | linha menor" vira as duas partes; os [colchetes] da manchete ficam, porque marcam as
# palavras desenhadas na cor de destaque.
function Split-StoreLine([string]$line, [string]$code) {
    $parts = $line -split ' \| ', 2
    if ($parts.Count -ne 2) { throw "secao $code - '$line' nao tem ' | ' antes da linha menor" }
    return [pscustomobject]@{ Headline = $parts[0].Trim(); Subline = $parts[1].Trim() }
}

# Cada secao "## <Idioma> (xx-yy)" vira um item: descricao curta, descricao, recursos, capturas (manchete,
# linha menor e a legenda da Store, que e a manchete sem colchetes), palavras-chave, copyright, o selo e as
# tres falas do trailer.
function Read-StoreListing {
    $utf8 = New-Object Text.UTF8Encoding $false
    $md = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'msix\store-listing.md'), $utf8) -replace "`r`n", "`n"
    $langs = @{}
    foreach ($m in [regex]::Matches($md, '(?ms)^## [^\n]*\(([a-z]{2}-[a-z]{2})\)\n(.*?)(?=^## |\z)')) {
        $code = $m.Groups[1].Value
        $body = $m.Groups[2].Value
        # Blocos sob um cabecalho so em negrito: descricao curta, descricao, recursos, legendas - nesta ordem.
        $blocks = [regex]::Matches($body, '(?ms)^\*\*[^*\n:]+\*\*\n\n(.*?)(?=^\*\*|\z)') | ForEach-Object { $_.Groups[1].Value.Trim() }
        # Linhas "**Rotulo:** valor", nesta ordem: palavras-chave, copyright, selo, abertura do trailer, a fala
        # de que tudo e calculado no PC, e o fechamento.
        $inline = [regex]::Matches($body, '(?m)^\*\*[^*\n]+?:\*\* (.+)$') | ForEach-Object { $_.Groups[1].Value.Trim() }
        if ($blocks.Count -ne 4 -or $inline.Count -ne 6) { throw "secao $code fora do formato: $($blocks.Count) blocos de 4, $($inline.Count) rotulos de 6" }

        $shots = @(($blocks[3] -split "\n") | Where-Object { $_ -match '^\d+\. ' } | ForEach-Object {
                $s = Split-StoreLine ($_ -replace '^\d+\. ', '') $code
                $s | Add-Member -NotePropertyName Caption -NotePropertyValue (($s.Headline -replace '[\[\]]', '').Trim()) -PassThru
            })

        # Paragrafos do .md sao quebrados em 100 colunas; na loja cada paragrafo e uma linha.
        $paras = ($blocks[1] -split "\n\n") | ForEach-Object { ($_ -split "\n") -join ' ' }
        $langs[$code] = @{
            Short       = (($blocks[0] -split "\n") -join ' ')
            Description = ($paras -join "`r`n`r`n")
            Features    = @(($blocks[2] -split "\n") | Where-Object { $_ -match '^- ' } | ForEach-Object { $_.Substring(2).Trim() })
            Shots       = $shots
            Captions    = @($shots | ForEach-Object { $_.Caption })
            Keywords    = @($inline[0] -split ',\s*')
            Copyright   = $inline[1]
            Badge       = $inline[2]
            Tray        = (Split-StoreLine $inline[3] $code)
            Local       = (Split-StoreLine $inline[4] $code)
            TrailerEnd  = $inline[5]
        }
    }
    return $langs
}

# O codigo de idioma da listagem (pt-br) e o que o app entende em --lang (pt-BR).
$StoreAppLang = @{ 'en-us' = 'en'; 'pt-br' = 'pt-BR'; 'pt-pt' = 'pt-PT'; 'fr-fr' = 'fr'; 'es-es' = 'es'; 'de-de' = 'de'; 'zh-cn' = 'zh-Hans' }
