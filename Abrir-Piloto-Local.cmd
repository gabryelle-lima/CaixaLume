@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Abrir-Piloto-Local.ps1"
if errorlevel 1 pause
