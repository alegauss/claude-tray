# T448. One turn stamped now, appended to the bench's transcript, so a launch right after this has a
# session running. The live chart's project strip opens the Sessions pane only for a session whose last
# turn is inside LiveRate.HistorySeconds (300s), and the bench writes its transcript once at the start
# of the run - so without this, whether the strip had anything to name depended on how far into the
# run its case landed.
#
# A fixture's `before` command, run with the run's environment: CLAUDE_CONFIG_DIR is the bench's first
# profile, which tests/ClaudeTray.Cases/Bench.cs set. The line's shape is Bench.Turn's, a third copy
# of it and a deliberate one for the reason Bench gives: a line the reader stops understanding renders
# nothing, and the case then fails naming this fixture. Token counts and ids only - no content, ever.
$ErrorActionPreference = 'Stop'

$config = $env:CLAUDE_CONFIG_DIR
if (-not $config) { Write-Error 'CLAUDE_CONFIG_DIR is not set, so there is no bench to stamp'; exit 1 }

$session = Join-Path $config 'projects\D--bench-winwright\session.jsonl'
if (-not (Test-Path -LiteralPath $session)) { Write-Error "no bench transcript at $session"; exit 1 }

$stamp = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ', [Globalization.CultureInfo]::InvariantCulture)
$line = '{"type":"assistant","timestamp":"' + $stamp + '","requestId":"bench-live-' + $stamp +
        '","cwd":"D:\\bench","message":{"id":"msg-bench-live-' + $stamp +
        '","model":"claude-bench","usage":{"input_tokens":150,"output_tokens":340,' +
        '"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}}'

[IO.File]::AppendAllText($session, $line + "`n", (New-Object Text.UTF8Encoding $false))
exit 0
