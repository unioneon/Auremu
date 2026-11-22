@echo off
if not exist "%~dp0dist\Auremu.exe" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if exist "%~dp0dist\Auremu.exe" start "" "%~dp0dist\Auremu.exe"
