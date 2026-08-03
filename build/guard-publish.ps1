<#
.SYNOPSIS
    Refuse the publish when something is running from the folder it is about to overwrite.

.DESCRIPTION
    `dotnet publish` writes a single self-contained .exe. If a process is already running from that
    file, the SDK fails inside its bundler with

        error MSB4018: Falha inesperada da tarefa "GenerateBundle".
        System.UnauthorizedAccessException: Access to the path '...\publish\ClaudeTray.exe' is denied.

    plus four frames of MSBuild - a stack trace about a bundler, on the one command that matters at
    release time, for a state that is not a bug: somebody who installed this app by building it runs
    the tray from exactly that folder. So the answer is not to make the state impossible, it is to
    name it (T266).

    T258 asked this question and deferred it: whether a build whose output is locked should say so as
    itself rather than as a failure of a copy. It fixed the half that WAS a bug - a check script
    leaking the trays it launched - and the question outlived it, because the remaining cause is a
    person's normal setup.

    It matches on the PATH, not on the process name. A tray running from bin\Debug is not in the way
    of a Release publish, and refusing it would be refusing the ordinary case: this repository's own
    dev loop keeps a Debug tray alive while building Release.

    It does NOT offer to stop the process, deliberately. The tray is the user's own running
    application; a build script that kills applications is worse than one that asks. Same shape as
    the refusals elsewhere here - Capture-Window names the window it copied (T199), --capture-settings
    names the page it will not write (T205) - a refusal is only actionable if it says what is in the way.

.PARAMETER PublishDir
    The folder the publish will write. Defaults to the one build.cmd uses.
#>
[CmdletBinding()]
param([string]$PublishDir)

$ErrorActionPreference = 'Stop'

# Resolved here and not as a param default: in PowerShell 5.1 `$PSScriptRoot` is empty while parameter
# defaults are being bound, so the default threw and the guard exited 1 - failing closed, which is the
# safe direction, and for a reason that had nothing to do with the build.
if (-not $PublishDir) {
    $PublishDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Release\net10.0-windows\win-x64\publish'
}

# Nothing published yet is nothing to be in the way of.
if (-not (Test-Path -LiteralPath $PublishDir)) { exit 0 }

$full = (Resolve-Path -LiteralPath $PublishDir).Path
$blocking = @()

foreach ($p in Get-Process -ErrorAction SilentlyContinue) {
    # A process whose path cannot be read is one this session has no rights over, and a build is not
    # the place to ask for them: it cannot be holding a file this checkout is about to write.
    $path = try { $p.Path } catch { $null }
    if (-not $path) { continue }
    if ($path.StartsWith($full, [System.StringComparison]::OrdinalIgnoreCase)) {
        $blocking += [pscustomobject]@{ Id = $p.Id; Path = $path }
    }
}

if ($blocking.Count -eq 0) { exit 0 }

Write-Host ''
Write-Host '*** The publish would overwrite a file that is running. ***' -ForegroundColor Red
foreach ($b in $blocking) { Write-Host ("    pid {0}  {1}" -f $b.Id, $b.Path) }
Write-Host ''
Write-Host 'Quit it and run this again, or run the tray from somewhere other than the publish folder.'
Write-Host 'Nothing was built, and nothing was stopped for you: that tray is your running app.'
Write-Host ''
exit 1
