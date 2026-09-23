# CI-09: turns a `dotnet test` run into a gate that can tell "all passed" from "none ran".
#
# `dotnet test` exits 0 when zero tests are discovered -- it prints "No test is available" /
# "No test matches the given testcase filter" and returns success. So the exit code alone is not
# a gate: a broken include glob, a renamed fixture or a stray --filter would silently package a
# kit whose rules were never checked. This script parses the runner's summary line and refuses
# anything below a floor, or a run with no summary line at all.
#
# Usage: test-gate.ps1 -OutputFile <captured dotnet test output> [-ExitCode <n>] [-MinimumTests <n>]
# MAINT-34: the floor is raised with the suite. At 280 against ~355 tests, 75 could vanish and still package green.
# On success it writes the total test count to stdout and exits 0; otherwise it throws.
param(
    [Parameter(Mandatory = $true)][string]$OutputFile,
    [int]$ExitCode = 0,
        # catch "the suite stopped running", not to be re-tuned every time a test is added.
    [int]$MinimumTests = 340
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $OutputFile)) { throw "Test gate: no runner output at $OutputFile." }
$text = Get-Content $OutputFile -Raw
if ($null -eq $text) { $text = '' }

if ($ExitCode -ne 0) {
    throw "Headless tests failed (dotnet test exited $ExitCode). Fix them before packaging a kit."
}

# e.g. "Passed!  - Failed:     0, Passed:   262, Skipped:     0, Total:   262, Duration: 19 s - X.dll (net10.0)"
$pattern = '(?m)^\s*\w+!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)'
$found = [regex]::Matches($text, $pattern)
if ($found.Count -eq 0) {
    $why = 'the runner printed no test summary line'
    if ($text -match 'No test is available|No test matches the given testcase filter') {
        $why = 'the runner discovered no tests at all'
    }
    throw "Test gate: $why, so nothing was verified. dotnet test exits 0 in that case -- treating it as a failure. Check the test project's include globs and that no --filter is in play."
}

$failed = 0
$total = 0
foreach ($m in $found) {
    $failed += [int]$m.Groups[1].Value
    $total += [int]$m.Groups[4].Value
}

if ($failed -gt 0) { throw "Headless tests failed ($failed failing). Fix them before packaging a kit." }
if ($total -lt $MinimumTests) {
    throw "Test gate: only $total tests ran, below the floor of $MinimumTests. The suite shrank or stopped being discovered -- do not package this kit until you know why."
}

Write-Output $total
