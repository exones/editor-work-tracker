#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs DaVinci Time Tracker in Debug mode for development/testing

.DESCRIPTION
    Builds and runs the application in Debug configuration with:
    - 3-second Grace Start period (vs 30 sec in Release)
    - 5-second Grace End period (vs 10 min in Release)
    - Detailed logging
    
    Perfect for rapid testing of tracking behavior.

.EXAMPLE
    .\run-debug.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "=== DaVinci Time Tracker - Debug Mode ===" -ForegroundColor Cyan
Write-Host ""

# Check if already running
$runningProcesses = Get-Process -Name "DaVinciTimeTracker.App" -ErrorAction SilentlyContinue
if ($runningProcesses) {
    Write-Host "⚠ Warning: App is already running!" -ForegroundColor Yellow
    Write-Host "Existing instances will be killed..." -ForegroundColor Yellow
    $runningProcesses | Stop-Process -Force
    Start-Sleep -Seconds 1
}

Write-Host "🔨 Building in Debug mode..." -ForegroundColor Green
dotnet build src\DaVinciTimeTracker.App\DaVinciTimeTracker.App.csproj -c Debug

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✅ Build successful!" -ForegroundColor Green
Write-Host ""
Write-Host "📋 Debug Configuration:" -ForegroundColor Yellow
Write-Host "  - Grace Start: 3 seconds (vs 30 sec in Release)" -ForegroundColor Gray
Write-Host "  - Grace End: 5 seconds (vs 10 min in Release)" -ForegroundColor Gray
Write-Host "  - Dashboard: http://localhost:5555" -ForegroundColor Gray
Write-Host "  - Logs: $env:LOCALAPPDATA\DaVinciTimeTracker\logs" -ForegroundColor Gray
Write-Host ""
Write-Host "🚀 Starting app..." -ForegroundColor Green
Write-Host ""

# Run the app
dotnet run --project src\DaVinciTimeTracker.App\DaVinciTimeTracker.App.csproj -c Debug --no-build
