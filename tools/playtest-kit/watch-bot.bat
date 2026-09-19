@echo off
rem Watch the bot play five runs on screen (it only knows what it has uncovered, like a player).
rem It alternates Sir Clickington and Dawnward and keeps coins, gear, levels and talents from run to run.
rem Nothing it does touches your own save or profile. Press Esc to stop.
start "" "%~dp0ClickDungeon.exe" -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -cdWatch 0.5 -cdRuns 5 -cdBot casual
