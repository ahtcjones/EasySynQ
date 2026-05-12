# Stress test for the EasySynQ test suite.
#
# Why this exists:
#   The test scaffolding has historically had concurrency-sensitive
#   problems that only surface intermittently when xUnit v3 runs test
#   classes in parallel. A single `dotnet test` invocation is therefore
#   not a reliable signal of suite health — a green run can coexist
#   with a 20%+ flake rate. This script runs the suite many times,
#   reports the run-level pass rate, and tallies which tests failed
#   how often, so flakes are visible instead of dice-rolled past.
#
# Canonical use:
#   pwsh -File scripts/stress-test.ps1
#
# Output:
#   Per-run lines and the final summary go to both stdout and
#   scripts/stress-test-output.txt (the latter is gitignored — it's a
#   transient artifact, not source).

[CmdletBinding()]
param(
    [int]$Iterations = 30
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $PSScriptRoot 'stress-test-output.txt'

# Reset the output file at the start of every invocation.
Set-Content -Path $outputPath -Value '' -Encoding UTF8

function Write-Both {
    param([string]$Line)
    Write-Host $Line
    Add-Content -Path $outputPath -Value $Line -Encoding UTF8
}

Write-Both "EasySynQ stress test — $Iterations iterations of 'dotnet test'"
Write-Both "Started: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Both "Repo:    $repoRoot"
Write-Both ('-' * 72)

# Track per-run results (zero-failure = pass) and per-test failure
# counts. The run-level pass rate is the metric that actually reflects
# suite health under parallel execution; counting individual failures
# would over-weight runs that happened to fail multiple tests.
$runsPassed   = 0
$runsFailed   = 0
$failureTally = @{}

for ($i = 1; $i -le $Iterations; $i++) {
    Push-Location $repoRoot
    try {
        $output = & dotnet test --nologo --verbosity normal 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -eq 0) {
        $runsPassed++
        Write-Both ("Run {0,3}: PASS" -f $i)
        continue
    }

    $runsFailed++

    # Pull the failing test names out of the verbose output. xUnit /
    # vstest prints `    Failed <FullyQualifiedTestName> [duration]`.
    $failedTests = @($output |
        Select-String -Pattern '^\s*Failed\s+EasySynQ' |
        ForEach-Object {
            ($_.ToString() -replace '^\s*Failed\s+', '') -replace '\s+\[.*$', ''
        })

    foreach ($t in $failedTests) {
        if ($failureTally.ContainsKey($t)) {
            $failureTally[$t]++
        } else {
            $failureTally[$t] = 1
        }
    }

    $names = if ($failedTests.Count -gt 0) { $failedTests -join ', ' } else { '<no test name parsed; check exit code>' }
    Write-Both ("Run {0,3}: FAIL  {1}" -f $i, $names)
}

# Summary.
$passRate = [math]::Round(($runsPassed / $Iterations) * 100, 2)

Write-Both ('-' * 72)
Write-Both 'Summary'
Write-Both ('-' * 72)
Write-Both ("Total runs:                  {0}" -f $Iterations)
Write-Both ("Runs that fully passed:      {0}" -f $runsPassed)
Write-Both ("Runs with any failure:       {0}" -f $runsFailed)
Write-Both ("Run-level pass rate:         {0}%" -f $passRate)

if ($failureTally.Count -gt 0) {
    Write-Both ''
    Write-Both 'Failure tally (test : run-count):'
    $failureTally.GetEnumerator() |
        Sort-Object -Property Value -Descending |
        ForEach-Object { Write-Both ("  {0,3}x  {1}" -f $_.Value, $_.Key) }
} else {
    Write-Both ''
    Write-Both 'No test failures observed across any run.'
}

Write-Both ('-' * 72)
Write-Both "Finished: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
