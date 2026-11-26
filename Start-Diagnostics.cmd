@echo off
setlocal
title ONEXTweaks - Hardware Discovery
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Collect-DeviceInfo.ps1" -OutputDirectory "%~dp0Reports"
if errorlevel 1 (
    echo.
    echo Diagnostics failed. Please send the error text from this window.
    pause
    exit /b 1
)
echo.
echo Open the Reports folder next to this file and send the new JSON in the chat.
pause
