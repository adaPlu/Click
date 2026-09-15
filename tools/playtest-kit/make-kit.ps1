# Packages the Windows build into a zipped playtest kit for testers.
# Usage: powershell -ExecutionPolicy Bypass -File tools/playtest-kit/make-kit.ps1
# Build first with ClickDungeon -> Build Windows.
param(
    [string]$BuildDir = (Join-Path $PSScriptRoot '..\..\ClickDungeon\Builds\Windows'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\..\ClickDungeon\Builds\Playtest')
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not (Test-Path (Join-Path $BuildDir 'ClickDungeon.exe'))) {
    throw "No Windows build found at $BuildDir. Run ClickDungeon -> Build Windows first."
}
$BuildDir = (Resolve-Path $BuildDir).Path

$version = (git -C $repo describe --always --dirty).Trim()
$date = Get-Date -Format 'yyyy-MM-dd'
$name = "ClickDungeon-Playtest-$date-$version"

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$staging = Join-Path $OutDir $name
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory $staging | Out-Null

# Ship the player only (no debug-symbol folders).
Get-ChildItem $BuildDir | Where-Object { $_.Name -notlike '*DoNotShip*' } | Copy-Item -Destination $staging -Recurse
Copy-Item (Join-Path $PSScriptRoot 'PLAYTEST-README.txt'), (Join-Path $PSScriptRoot 'collect-logs.bat') $staging

$buildTime = (Get-Item (Join-Path $BuildDir 'ClickDungeon_Data\Managed\ClickDungeon.Unity.dll')).LastWriteTime.ToString('yyyy-MM-dd HH:mm')
@(
    'ClickDungeon playtest build'
    "Version (git): $version"
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
if ($version -like '*-dirty') { Write-Warning 'Built from uncommitted changes. Commit and rebuild for a traceable kit.' }
