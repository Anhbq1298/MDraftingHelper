@echo off
rem Double-click to install MDraftingHeper for the current Windows user (AutoCAD must be closed).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-MDraftingHeper.ps1"
if errorlevel 1 (echo. & echo Installation FAILED - see the message above.) else (echo. & echo Installation complete.)
pause
