# Zips ClickDungeon playtest logs next to this script so testers can send them back. Called by collect-logs.bat.
# Files are copied with shared access first, so collecting works even while the game still has its log open.
# Exit codes: 0 zip created, 2 no logs found, 1 anything else failed.
param(
    [string]$LogsDir = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Clickd\ClickDungeon\telemetry'),
    [string]$OutZip = (Join-Path $PSScriptRoot 'ClickDungeon-playtest-logs.zip')
)

$ErrorActionPreference = 'Stop'

function Open-Shared([string]$Path) {
    [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
}

$logs = @(Get-ChildItem -LiteralPath $LogsDir -Filter '*.jsonl' -File -ErrorAction SilentlyContinue)
if ($logs.Count -eq 0) {
    Write-Output 'No ClickDungeon playtest logs were found in:'
    Write-Output "  $LogsDir"
    Write-Output 'Play at least one run first, then run this again.'
    exit 2
}

$staging = Join-Path ([IO.Path]::GetTempPath()) ('clickdungeon-logs-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    foreach ($log in $logs) {
        $in = Open-Shared $log.FullName
        try {
            $out = [IO.File]::Create((Join-Path $staging $log.Name))
            try { $in.CopyTo($out) } finally { $out.Dispose() }
        }
        finally { $in.Dispose() }
    }

    # Unity's player logs explain crashes. They contain file paths, so the Windows profile path is replaced first.
    $gameDir = Split-Path -Parent $LogsDir
    foreach ($name in 'Player.log', 'Player-prev.log') {
        $path = Join-Path $gameDir $name
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $in = Open-Shared $path
        try { $text = (New-Object IO.StreamReader($in)).ReadToEnd() } finally { $in.Dispose() }
        foreach ($profile in @($env:USERPROFILE, ($env:USERPROFILE -replace '\\', '/'))) {
            $text = [regex]::Replace($text, [regex]::Escape($profile), '%USERPROFILE%', 'IgnoreCase')
        }
        [IO.File]::WriteAllText((Join-Path $staging $name), $text)
    }

    if (Test-Path -LiteralPath $OutZip) { Remove-Item -LiteralPath $OutZip -Force }
    try {
        Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $OutZip
    }
    catch {
        if (Test-Path -LiteralPath $OutZip) { Remove-Item -LiteralPath $OutZip -Force }
        throw
    }
    Write-Output "Created: $OutZip"
    exit 0
}
catch {
    Write-Output "Error: $($_.Exception.Message)"
    exit 1
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}
