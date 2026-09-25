# TEST-50: turns a Unity `-runTests` run into a gate, the way test-gate.ps1 does for `dotnet test`.
#
# The kit gated the headless suite only, so the 488 EditMode and 3 PlayMode tests protected nothing that ships:
# every hero could go back to wearing Ironheart's face (D-065) or the whole PlayMode assembly could stop compiling,
# and a kit still packaged green. Unity writes an NUnit3 XML whose root carries the totals; a run that dies before
# writing it leaves no file at all, which is itself the failure this refuses to ignore.
#
# Usage: unity-gate.ps1 -Platform EditMode|PlayMode [-UnityExe <path>] [-ResultsFile <path>] [-LogFile <path>]
# On success it writes the test count to stdout and exits 0; otherwise it throws.
param(
    [Parameter(Mandatory = $true)][ValidateSet('EditMode', 'PlayMode')][string]$Platform,
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

Remove-Item -Force $ResultsFile, $LogFile -ErrorAction SilentlyContinue
Write-Output $total
