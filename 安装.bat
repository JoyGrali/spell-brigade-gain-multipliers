@echo off
chcp 65001 >nul
setlocal
echo.
echo   Spell Brigade - Gain Multipliers / Stat Editor
echo   正在启动安装器...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
echo.
pause
