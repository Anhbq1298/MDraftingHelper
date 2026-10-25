@echo off
rem Double-click to remove MDraftingHeper for the current Windows user (AutoCAD must be closed).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-MDraftingHeper.ps1"
if errorlevel 1 (echo. & echo Uninstall FAILED - see the message above.) else (echo. & echo Uninstall complete.)
pause
