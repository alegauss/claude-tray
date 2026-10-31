# store-listing-lib.ps1 - Le build\msix\store-listing.md, a fonte do texto da listagem da Store (T412).
# Carregado com ". .\store-listing-lib.ps1" por store-listing-csv.ps1 e store-shots.ps1, para que os dois
# leiam o mesmo texto do mesmo jeito.

# Cada secao "## <Idioma> (xx-yy)" vira um item: descricao curta, descricao, recursos, legendas,
# palavras-chave e copyright.
function Read-StoreListing {
    $utf8 = New-Object Text.UTF8Encoding $false
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
    return $langs
}

# O codigo de idioma da listagem (pt-br) e o que o app entende em --lang (pt-BR).
$StoreAppLang = @{ 'en-us' = 'en'; 'pt-br' = 'pt-BR'; 'pt-pt' = 'pt-PT'; 'fr-fr' = 'fr'; 'es-es' = 'es' }
