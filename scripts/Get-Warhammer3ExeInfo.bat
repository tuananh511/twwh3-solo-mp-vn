@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Get-Warhammer3ExeInfo.ps1"

echo.
pause
