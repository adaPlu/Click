# Packages the Windows build into a zipped playtest kit for testers.
# Usage: powershell -ExecutionPolicy Bypass -File tools/playtest-kit/make-kit.ps1
# Build first with ClickDungeon -> Build Windows.
# The headless tests gate the kit (see test-gate.ps1); -SkipTests skips them and says so in VERSION.txt.
# The kit is named after the player's build stamp; -AllowVersionMismatch packages one that predates the tree.
param(
    [string]$BuildDir = (Join-Path $PSScriptRoot '..\..\ClickDungeon\Builds\Windows'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\..\ClickDungeon\Builds\Playtest'),
    [switch]$SkipTests,
    # TEST-50: the Unity suites need a batchmode editor, which refuses to start while the Editor has the project
    # open. This skips them and says so in VERSION.txt, the way -SkipTests does for the headless suite.
    [switch]$SkipUnityTests,
    # CI-10: packaging a build whose stamp disagrees with the working tree is a mistake by default.
    [switch]$AllowVersionMismatch
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not (Test-Path (Join-Path $BuildDir 'ClickDungeon.exe'))) {
    throw "No Windows build found at $BuildDir. Run ClickDungeon -> Build Windows first."
}
$BuildDir = (Resolve-Path $BuildDir).Path

# There is no CI, so the kit is the gate: never hand testers a build whose rules fail their tests.
# CI-09: a zero exit code is not enough -- dotnet test returns success when it discovers nothing.
# test-gate.ps1 insists on a summary line and a minimum count. The result is recorded in VERSION.txt.
if (-not $SkipTests) {
    Write-Output 'Running headless tests (pass -SkipTests to skip)...'
    $testLog = Join-Path ([IO.Path]::GetTempPath()) ("clickdungeon-kit-tests-" + [Guid]::NewGuid().ToString('N') + ".txt")
    # No 2>&1 here: in Windows PowerShell 5.1 that turns a native command's stderr into ErrorRecords,
    # which $ErrorActionPreference = 'Stop' would treat as a failure. The summary line is on stdout.
    dotnet test (Join-Path $repo 'Sim\ClickDungeon.Sim.Tests\ClickDungeon.Sim.Tests.csproj') --nologo --verbosity quiet |
        Tee-Object -FilePath $testLog
    $testExit = $LASTEXITCODE
    try {
        $testCount = [int](& (Join-Path $PSScriptRoot 'test-gate.ps1') -OutputFile $testLog -ExitCode $testExit)
    }
    catch {
        throw "$_ (runner output kept at $testLog)"
    }
    Remove-Item -Force $testLog
    $gateLine = "Test gate: passed, $testCount headless tests"
    Write-Output "Test gate: $testCount headless tests passed."
}
else {
    # CI-09: an ungated kit must say so, or it is indistinguishable from one that passed.
    $gateLine = 'Test gate: SKIPPED (-SkipTests) -- this kit was NOT verified'
    Write-Warning 'Packaging without running the headless tests. VERSION.txt will say so.'
}

# TEST-50: the headless suite is a third of what the repo checks. The EditMode suite is what proves every hero has
# the face the dialogue asks for (D-065), and the PlayMode suite is the only thing that can look at a drawn tile
# (D-066) - both of them protected nothing that shipped, because the kit never ran them.
if (-not $SkipTests -and -not $SkipUnityTests) {
    $unityLines = @()
    foreach ($platform in @('EditMode', 'PlayMode')) {
        Write-Output "Running $platform tests (pass -SkipUnityTests to skip)..."
        $count = [int](& (Join-Path $PSScriptRoot 'unity-gate.ps1') -Platform $platform)
        $unityLines += "$count $platform"
        Write-Output "Test gate: $count $platform tests passed."
    }
    $gateLine = "$gateLine, " + ($unityLines -join ', ')
}
elseif (-not $SkipTests) {
    $gateLine = "$gateLine; Unity suites SKIPPED (-SkipUnityTests) -- EditMode and PlayMode were NOT verified"
    Write-Warning 'Packaging without running the Unity suites. VERSION.txt will say so.'
}

# The build writes the git version it was made from; a missing stamp means an old or failed build.
$stampPath = Join-Path $BuildDir 'BUILD-VERSION.txt'
if (-not (Test-Path $stampPath)) { throw "No BUILD-VERSION.txt in $BuildDir. Rebuild with ClickDungeon -> Build Windows." }
$builtFrom = (Get-Content $stampPath -TotalCount 1).Trim()
if (-not $builtFrom) { throw "BUILD-VERSION.txt in $BuildDir is empty. Rebuild with ClickDungeon -> Build Windows." }
if ($builtFrom.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
    throw "BUILD-VERSION.txt in $BuildDir does not look like a git version: '$builtFrom'."
}

$version = (git -C $repo describe --always --dirty).Trim()
# describe --dirty ignores untracked files, which still end up in the build.
if ((git -C $repo status --porcelain --untracked-files=normal) -and $version -notlike '*-dirty') { $version = "$version-dirty" }

# CI-10: the kit is named after the commit the player was BUILT from, never the one that happens to be
# checked out at packaging time. A disagreement means the player predates the tree, so it is a failure
# and not a trailing warning -- packaging it anyway needs -AllowVersionMismatch.
$mismatch = $builtFrom -ne $version
if ($mismatch -and -not $AllowVersionMismatch) {
    throw "The player was built from $builtFrom, but the working tree is $version. Rebuild (ClickDungeon -> Build Windows) so the kit matches, or pass -AllowVersionMismatch to package it anyway."
}

$date = Get-Date -Format 'yyyy-MM-dd'
$name = "ClickDungeon-Playtest-$date-$builtFrom"

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$staging = Join-Path $OutDir $name
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory $staging | Out-Null

# Ship the player only (no debug-symbol or backup folders).
Get-ChildItem $BuildDir |
    Where-Object { $_.Name -notlike '*DoNotShip*' -and $_.Name -notlike '*BackUpThisFolder*' -and $_.Extension -ne '.pdb' } |
    Copy-Item -Destination $staging -Recurse
Copy-Item (Join-Path $PSScriptRoot 'PLAYTEST-README.txt'), (Join-Path $PSScriptRoot 'collect-logs.bat'), (Join-Path $PSScriptRoot 'collect-logs.ps1'), (Join-Path $PSScriptRoot 'watch-bot.bat') $staging

$buildTime = (Get-Item (Join-Path $BuildDir 'ClickDungeon_Data\Managed\ClickDungeon.Unity.dll')).LastWriteTime.ToString('yyyy-MM-dd HH:mm')
$versionLines = @(
    'ClickDungeon playtest build'
    "Player built from: $builtFrom"
    "Working tree at packaging (git): $version"
    "Player built: $buildTime"
    "Kit packaged: $date"
    $gateLine
)
if ($mismatch) {
    $versionLines += "WARNING: packaged with -AllowVersionMismatch; the player ($builtFrom) predates the packaging tree ($version)."
}
$versionLines | Set-Content -Encoding utf8 (Join-Path $staging 'VERSION.txt')

$zip = "$staging.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
# Windows PowerShell 5.1 (Compress-Archive and ZipFile.CreateFromDirectory) writes backslash entry names,
# which some unzip tools mishandle. Add entries one by one with standard forward-slash paths.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$parent = (Split-Path $staging -Parent).TrimEnd('\') + '\'
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $staging -Recurse -File | ForEach-Object {
        $entryName = $_.FullName.Substring($parent.Length).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $archive.Dispose()
}
$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Output "Kit folder: $staging"
Write-Output "Kit zip:    $zip ($sizeMb MB)"
if ($builtFrom -like '*-dirty') { Write-Warning 'The player was built from uncommitted changes. Commit and rebuild for a traceable kit.' }
if ($mismatch) { Write-Warning "Packaged with -AllowVersionMismatch: the player was built from $builtFrom, the packaging tree is $version. VERSION.txt records both." }
