# Packages the Windows build into a zipped playtest kit for testers.
# Usage: powershell -ExecutionPolicy Bypass -File tools/playtest-kit/make-kit.ps1
# Build first with ClickDungeon -> Build Windows. The headless tests run first; -SkipTests skips them.
param(
    [string]$BuildDir = (Join-Path $PSScriptRoot '..\..\ClickDungeon\Builds\Windows'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\..\ClickDungeon\Builds\Playtest'),
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not (Test-Path (Join-Path $BuildDir 'ClickDungeon.exe'))) {
    throw "No Windows build found at $BuildDir. Run ClickDungeon -> Build Windows first."
}
$BuildDir = (Resolve-Path $BuildDir).Path

# There is no CI, so the kit is the gate: never hand testers a build whose rules fail their tests.
if (-not $SkipTests) {
    Write-Output 'Running headless tests (pass -SkipTests to skip)...'
    dotnet test (Join-Path $repo 'Sim\ClickDungeon.Sim.Tests\ClickDungeon.Sim.Tests.csproj') --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Headless tests failed. Fix them before packaging a kit.' }
}

# The build writes the git version it was made from; a missing stamp means an old or failed build.
$stampPath = Join-Path $BuildDir 'BUILD-VERSION.txt'
if (-not (Test-Path $stampPath)) { throw "No BUILD-VERSION.txt in $BuildDir. Rebuild with ClickDungeon -> Build Windows." }
$builtFrom = (Get-Content $stampPath -TotalCount 1).Trim()

$version = (git -C $repo describe --always --dirty).Trim()
# describe --dirty ignores untracked files, which still end up in the build.
if ((git -C $repo status --porcelain --untracked-files=normal) -and $version -notlike '*-dirty') { $version = "$version-dirty" }
$date = Get-Date -Format 'yyyy-MM-dd'
$name = "ClickDungeon-Playtest-$date-$version"

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$staging = Join-Path $OutDir $name
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory $staging | Out-Null

# Ship the player only (no debug-symbol or backup folders).
Get-ChildItem $BuildDir |
    Where-Object { $_.Name -notlike '*DoNotShip*' -and $_.Name -notlike '*BackUpThisFolder*' -and $_.Extension -ne '.pdb' } |
    Copy-Item -Destination $staging -Recurse
Copy-Item (Join-Path $PSScriptRoot 'PLAYTEST-README.txt'), (Join-Path $PSScriptRoot 'collect-logs.bat'), (Join-Path $PSScriptRoot 'collect-logs.ps1') $staging

$buildTime = (Get-Item (Join-Path $BuildDir 'ClickDungeon_Data\Managed\ClickDungeon.Unity.dll')).LastWriteTime.ToString('yyyy-MM-dd HH:mm')
@(
    'ClickDungeon playtest build'
    "Version (git): $version"
    "Player built from: $builtFrom"
    "Player built: $buildTime"
    "Kit packaged: $date"
) | Set-Content -Encoding utf8 (Join-Path $staging 'VERSION.txt')

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
if ($version -like '*-dirty') { Write-Warning 'Packaged from uncommitted changes. Commit and rebuild for a traceable kit.' }
if ($builtFrom -ne $version) { Write-Warning "The player was built from $builtFrom, but the kit is labelled $version. VERSION.txt records both." }
