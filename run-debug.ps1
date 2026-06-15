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

# Launch the compiled exe in a new console window and tail the live log there
$exePath  = Resolve-Path "src\DaVinciTimeTracker.App\bin\Debug\net9.0-windows10.0.19041.0\DaVinciTimeTracker.App.exe"
$logDir   = "$env:LOCALAPPDATA\DaVinciTimeTracker\logs"
$logGlob  = "$logDir\davinci-tracker-*.log"

$script = @"
`$host.UI.RawUI.WindowTitle = 'DaVinci Time Tracker — Debug'
Write-Host '=== DaVinci Time Tracker — Debug ===' -ForegroundColor Cyan
Write-Host '  Dashboard : http://localhost:5555'  -ForegroundColor Yellow
Write-Host '  Ctrl+C    : stop the app'           -ForegroundColor Gray
Write-Host ''

`$proc = Start-Process -FilePath '$exePath' -PassThru
Write-Host "App started (PID `$(`$proc.Id))" -ForegroundColor Green
Write-Host ''

# Wait for the log file to appear, then tail it with colour-coding
`$deadline = (Get-Date).AddSeconds(10)
while (-not (Get-ChildItem '$logGlob' -ErrorAction SilentlyContinue) -and (Get-Date) -lt `$deadline) {
    Start-Sleep -Milliseconds 300
}
`$log = Get-ChildItem '$logGlob' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (`$log) {
    Get-Content `$log.FullName -Wait | ForEach-Object {
        if     (`$_ -match '\[ERR\]') { Write-Host `$_ -ForegroundColor Red     }
        elseif (`$_ -match '\[WRN\]') { Write-Host `$_ -ForegroundColor Yellow  }
        elseif (`$_ -match '\[DBG\]') { Write-Host `$_ -ForegroundColor DarkGray}
        else                           { Write-Host `$_                           }
    }
} else {
    Write-Host 'No log file found in $logDir' -ForegroundColor Red
    `$proc.WaitForExit()
}
"@

Start-Process pwsh -ArgumentList "-NoExit", "-Command", $script
Write-Host "✓ Debug window opened — live logs streaming there" -ForegroundColor Green
Write-Host "  Dashboard: http://localhost:5555" -ForegroundColor Cyan
