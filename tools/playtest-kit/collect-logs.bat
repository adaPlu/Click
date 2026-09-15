@echo off
setlocal
rem Zips ClickDungeon playtest logs next to this script so testers can send them back.
rem The work happens in collect-logs.ps1 (it also works while the game is still open).
set "LOGS=%USERPROFILE%\AppData\LocalLow\Clickd\ClickDungeon\telemetry"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0collect-logs.ps1" -LogsDir "%LOGS%"
if errorlevel 3 goto failed
if errorlevel 2 goto nologs
if errorlevel 1 goto failed

echo.
echo Please send ClickDungeon-playtest-logs.zip (next to this file) back. Thank you for playing!
if not defined CD_NO_EXPLORER explorer /select,"%~dp0ClickDungeon-playtest-logs.zip"
pause
exit /b 0

:nologs
pause
exit /b 1

:failed
echo Could not create the zip. Please close the game, then copy this folder by hand:
echo   "%LOGS%"
pause
exit /b 1
