# TEST-50: turns a Unity `-runTests` run into a gate, the way test-gate.ps1 does for `dotnet test`.
#
# The kit gated the headless suite only, so the 488 EditMode and 3 PlayMode tests protected nothing that ships:
# every hero could go back to wearing Ironheart's face (D-065) or the whole PlayMode assembly could stop compiling,
# and a kit still packaged green. Unity writes an NUnit3 XML whose root carries the totals; a run that dies before
# writing it leaves no file at all, which is itself the failure this refuses to ignore.
#
# CI-20: "some tests ran" is not a gate. Its headless twin has had a floor under the count since CI-09 - this one
# checked `total > 0`, so an assembly that stopped being discovered would have taken hundreds of tests off the board
# and still packaged green, which is the same vacuous-assertion shape the suite itself was audited for.
#
# Usage: unity-gate.ps1 -Platform EditMode|PlayMode [-MinimumTests <n>] [-UnityExe <path>] [-ResultsFile <path>] [-LogFile <path>]
# On success it writes the test count to stdout and exits 0; otherwise it throws.
param(
    [Parameter(Mandatory = $true)][ValidateSet('EditMode', 'PlayMode')][string]$Platform,
    # The floor under the count. Zero only for a deliberate one-off run; make-kit.ps1 passes the real numbers.
    [int]$MinimumTests = 0,
    [string]$UnityExe,
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\..\ClickDungeon'),
    [string]$ResultsFile,
    [string]$LogFile
)

$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path $ProjectPath).Path

if (-not $UnityExe) {
    # The version the project is on; CLICKDUNGEON_UNITY overrides it for a machine that keeps its editors elsewhere.
    $UnityExe = $env:CLICKDUNGEON_UNITY
    if (-not $UnityExe) {
        $version = (Get-Content (Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt') -Raw) -replace '(?s).*m_EditorVersion:\s*([^\r\n]+).*', '$1'
        $UnityExe = "C:\Program Files\Unity\Hub\Editor\$($version.Trim())\Editor\Unity.exe"
    }
}
if (-not (Test-Path $UnityExe)) {
    throw "Unity gate: no editor at $UnityExe. Pass -UnityExe, or set CLICKDUNGEON_UNITY."
}

$stamp = [Guid]::NewGuid().ToString('N')
if (-not $ResultsFile) { $ResultsFile = Join-Path ([IO.Path]::GetTempPath()) "clickdungeon-$Platform-$stamp.xml" }
if (-not $LogFile) { $LogFile = Join-Path ([IO.Path]::GetTempPath()) "clickdungeon-$Platform-$stamp.log" }
if (Test-Path $ResultsFile) { Remove-Item -Force $ResultsFile }

# No -quit with -runTests: Unity quits before the run starts and writes no results file.
& $UnityExe -batchmode -nographics -projectPath $ProjectPath -runTests -testPlatform $Platform `
    -testResults $ResultsFile -logFile $LogFile | Out-Null
$unityExit = $LASTEXITCODE

if (-not (Test-Path $ResultsFile)) {
    throw "Unity gate: $Platform wrote no results (editor exit $unityExit). Nothing was verified. Log: $LogFile"
}

[xml]$results = Get-Content $ResultsFile -Raw
$run = $results.'test-run'
if ($null -eq $run) { throw "Unity gate: $Platform results at $ResultsFile have no test-run element. Log: $LogFile" }

$total = [int]$run.total
$failed = [int]$run.failed
if ($failed -gt 0) {
    throw "$Platform tests failed ($failed of $total). Fix them before packaging a kit. Results: $ResultsFile"
}
if ($total -le 0) {
    throw "Unity gate: $Platform discovered no tests, so nothing was verified. Results: $ResultsFile"
}
# The floor goes under PASSED, not under the total. NUnit counts a skipped case in the total, so a run in which every
# test was skipped reads exactly like one that passed - and the repo has 15 [Explicit] tuning aids that are skipped on
# purpose every time, so "skipped must be zero" is not the rule either. What has to hold is that the number of tests
# that actually executed and passed has not collapsed (CI-20).
$skipped = [int]$run.skipped
$passed = [int]$run.passed
if ($passed -lt $MinimumTests) {
    throw "Unity gate: only $passed $Platform tests passed ($total discovered, $skipped skipped), below the floor of $MinimumTests. The suite shrank or stopped being discovered -- do not package this kit until you know why. Results: $ResultsFile"
}
# CI-20, the other half: the editor's own exit code was read and never looked at. Failures are already out of the way above, so a
# non-zero code here is something the results file cannot describe - a crash partway, a licence refusal, an assembly
# that would not load - and a gate that ignores it reports on however much of the suite happened to run first.
if ($unityExit -ne 0) {
    throw "Unity gate: $Platform reported $total tests and no failures, but the editor exited $unityExit. Something ended the run that the results cannot show. Log: $LogFile"
}

Remove-Item -Force $ResultsFile, $LogFile -ErrorAction SilentlyContinue
if ($skipped -gt 0) { Write-Warning "${Platform}: $skipped tests were skipped (the [Explicit] tuning aids)." }
Write-Output $passed
