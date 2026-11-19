param(
    [Parameter(Mandatory=$true)][string]$Godot,
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [ValidateRange(1,100)][int]$Iterations = 5
)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new evidence directory.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$results = @()
$cases = @('scenario-catalog','character-follow','action-driven','board-battle','character-attributes')
foreach ($case in $cases) {
    foreach ($iteration in 1..$Iterations) {
        $log = Join-Path $OutputDirectory ($case+'-'+$iteration+'.log')
        # Native stderr is evidence, not a replacement for the actual process exit code.
        $ErrorActionPreference = 'Continue'
        & $Godot --headless --path $Project --log-file $log -- ('--'+$case+'-smoke') > ($log+'.stdout') 2>&1
        $code = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        $content = [IO.File]::ReadAllText($log)
        $passed = $code -eq 0 -and $content -match 'SMOKE_PASS' -and $content -notmatch 'SMOKE_FAIL|CrashHandlerException'
        $results += [pscustomobject]@{Case=$case;Iteration=$iteration;ExitCode=$code;Passed=$passed}
    }
}
$results | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'results.json') -Encoding UTF8
$results | Format-Table
if ($results.Where({-not $_.Passed}).Count) { exit 1 }
