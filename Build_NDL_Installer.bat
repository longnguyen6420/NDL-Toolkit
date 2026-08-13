@echo off
title NDL Tools Master Builder
color 0A
echo ==========================================================
echo        NDL AUTOMATED BUILD AND SETUP PACKAGER SYSTEM
echo ==========================================================
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0Build_NDL_Installer.ps1"
echo.
echo Hoan thanh! File NDL_Setup.exe da duoc tao tai D:\NDL\NDL_Setup.exe.
pause
