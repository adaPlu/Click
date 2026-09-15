@echo off
setlocal
rem Zips ClickDungeon playtest logs next to this script so testers can send them back.
set "LOGS=%USERPROFILE%\AppData\LocalLow\Clickd\ClickDungeon\telemetry"
set "OUT=%~dp0ClickDungeon-playtest-logs.zip"

if not exist "%LOGS%\*.jsonl" (
  echo No ClickDungeon playtest logs were found in:
  echo   "%LOGS%"
  echo Play at least one run first, then run this again.
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path (Join-Path $env:LOGS '*.jsonl') -DestinationPath $env:OUT -Force"
if errorlevel 1 (
  echo Could not create the zip. Please copy the log folder above by hand.
  pause
  exit /b 1
)

echo.
echo Created: "%OUT%"
echo Please send that file back. Thank you for playing!
if not defined CD_NO_EXPLORER explorer /select,"%OUT%"
pause
exit /b 0
