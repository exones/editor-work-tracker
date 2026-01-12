@echo off
REM DaVinci Time Tracker - Quick Install Launcher
REM Double-click this file to install or update DaVinci Time Tracker

setlocal

echo.
echo ========================================
echo DaVinci Time Tracker - Quick Install
echo ========================================
echo.
echo Installing to: %LOCALAPPDATA%\DaVinciTimeTracker
echo (No administrator privileges required)
echo.

REM Get the directory where this batch file is located
set "SCRIPT_DIR=%~dp0"

REM Check if PowerShell 7+ is installed
where pwsh.exe >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: PowerShell 7+ is not installed!
    echo.
    echo This installer requires PowerShell 7 or later.
    echo Download from: https://aka.ms/powershell-release?tag=stable
    echo.
    pause
    exit /b 1
)

REM Check if quick-install.ps1 exists
if not exist "%SCRIPT_DIR%quick-install.ps1" (
    echo ERROR: quick-install.ps1 not found in the same directory!
    echo Expected location: %SCRIPT_DIR%quick-install.ps1
    echo.
    pause
    exit /b 1
)

REM Run the PowerShell script
echo Starting installation...
echo.

pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%quick-install.ps1"

if %errorLevel% neq 0 (
    echo.
    echo Installation failed or was cancelled.
) else (
    echo.
    echo Installation completed!
)

echo.
pause
