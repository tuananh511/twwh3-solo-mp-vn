@echo off
chcp 65001 >nul
echo Dang khoi dong cong cu tim offset moi...
echo.
python "%~dp0auto_update_offsets.py"
pause
