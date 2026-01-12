#Requires -Version 7.0

<#
.SYNOPSIS
    Quick installer for DaVinci Time Tracker from network share

.DESCRIPTION
    Downloads and installs DaVinci Time Tracker from your company's network share.
    Can be shared with colleagues for easy installation.

.PARAMETER NetworkShare
    Network share path where releases are stored. Default: F:\Linia\DaVinciTracker

.PARAMETER InstallPath
    Where to install the application. Default: %LOCALAPPDATA%\DaVinciTimeTracker

.PARAMETER EnableAutoStart
    Automatically enable "Start with Windows"

.EXAMPLE
    .\quick-install.ps1

.EXAMPLE
    .\quick-install.ps1 -NetworkShare "\\other\server\DaVinciTracker"
#>

param(
    [string]$NetworkShare = "F:\Linia\DaVinciTracker",

    [string]$InstallPath = "$env:LOCALAPPDATA\DaVinciTimeTracker",

    [switch]$EnableAutoStart = $false
)

$ErrorActionPreference = "Stop"

# Colors
function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Success($text) { Write-Host "✓ $text" -ForegroundColor Green }
function Write-Info($text) { Write-Host "ℹ $text" -ForegroundColor Yellow }
function Write-Fail($text) { Write-Host "✗ $text" -ForegroundColor Red }

Write-Header "DaVinci Time Tracker - Quick Install"

# Check network share access
Write-Info "Checking network share access..."
if (-not (Test-Path $NetworkShare)) {
    Write-Fail "Cannot access network share: $NetworkShare"
    Write-Host "Please check:"
    Write-Host "  1. You are connected to company network"
    Write-Host "  2. Path is correct"
    Write-Host "  3. You have read access"
    exit 1
}
Write-Success "Network share accessible"

# Check for latest package
$latestZip = Join-Path $NetworkShare "DaVinciTimeTracker-latest.zip"
if (-not (Test-Path $latestZip)) {
    Write-Fail "Latest package not found: $latestZip"
    exit 1
}

# Get version info
$versionFile = Join-Path $NetworkShare "latest-version.txt"
$version = if (Test-Path $versionFile) { Get-Content $versionFile } else { "Unknown" }

Write-Success "Found version: $version"

# Check if app is running
$runningProcess = Get-Process -Name "DaVinciTimeTracker.App" -ErrorAction SilentlyContinue
if ($runningProcess) {
    Write-Info "Application is currently running..."
    $response = Read-Host "Close it and continue? (Y/N)"
    if ($response -eq 'Y' -or $response -eq 'y') {
        Stop-Process -Name "DaVinciTimeTracker.App" -Force
        Start-Sleep -Seconds 2
        Write-Success "Application closed"
    } else {
        Write-Info "Installation cancelled"
        exit 0
    }
}

# Create temp directory
$tempDir = Join-Path $env:TEMP "DaVinciTracker-Install-$(Get-Date -Format 'yyyyMMddHHmmss')"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

try {
    # Download/Copy package
    Write-Header "Downloading Package"
    $tempZip = Join-Path $tempDir "package.zip"

    Write-Info "Copying from network share..."
    Copy-Item $latestZip $tempZip -Force

    $zipSize = [math]::Round((Get-Item $tempZip).Length / 1MB, 2)
    Write-Success "Downloaded ($zipSize MB)"

    # Extract
    Write-Header "Installing"

    # Check if install path exists
    if (Test-Path $InstallPath) {
        Write-Info "Updating existing installation at: $InstallPath"
    } else {
        Write-Info "Installing to: $InstallPath"
    }

    # Create install directory
    if (-not (Test-Path $InstallPath)) {
        New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
    }

    # Extract files
    Write-Info "Extracting files..."
    Expand-Archive -Path $tempZip -DestinationPath $InstallPath -Force
    Write-Success "Files extracted"

    # Verify installation
    $exePath = Join-Path $InstallPath "DaVinciTimeTracker.App.exe"
    if (-not (Test-Path $exePath)) {
        Write-Fail "Installation verification failed - executable not found"
        exit 1
    }

    Write-Success "Installation verified"

    # Create desktop shortcut (optional)
    Write-Info "Creating desktop shortcut..."
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcutPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "DaVinci Time Tracker.lnk"
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $exePath
        $shortcut.WorkingDirectory = $InstallPath
        $shortcut.Description = "DaVinci Time Tracker"
        $shortcut.Save()
        Write-Success "Desktop shortcut created"
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
    } catch {
        Write-Info "Could not create desktop shortcut (not critical)"
    }

    # Check requirements
    Write-Header "Checking Requirements"

    # Check Python
    Write-Info "Checking for Python..."
    $pythonCheck = Get-Command python -ErrorAction SilentlyContinue
    if ($pythonCheck) {
        $pythonVersion = & python --version 2>&1
        Write-Success "Python found: $pythonVersion"
    } else {
        Write-Info "Python not found in PATH"
        Write-Host "  Download from: https://www.python.org/downloads/" -ForegroundColor Yellow
        Write-Host "  Or set DAVINCI_TRACKER_PYTHON environment variable" -ForegroundColor Yellow
    }

    # Check .NET
    Write-Info "Checking for .NET 9..."
    $dotnetCheck = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCheck) {
        $dotnetVersions = & dotnet --list-runtimes 2>&1 | Select-String "Microsoft.WindowsDesktop.App 9"
        if ($dotnetVersions) {
            Write-Success ".NET 9 Desktop Runtime found"
        } else {
            Write-Info ".NET 9 Desktop Runtime NOT found"
            Write-Host "  Download from: https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Yellow
            Write-Host "  Required: '.NET 9 Desktop Runtime'" -ForegroundColor Yellow
        }
    } else {
        Write-Info ".NET not found"
        Write-Host "  Download from: https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Yellow
    }

    # Start application
    Write-Header "Starting Application"

    Start-Process -FilePath $exePath -WindowStyle Hidden
    Start-Sleep -Seconds 3

    $runningNow = Get-Process -Name "DaVinciTimeTracker.App" -ErrorAction SilentlyContinue
    if ($runningNow) {
        Write-Success "Application started successfully!"
        Write-Host ""
        Write-Host "  • System tray icon should appear (near the clock)" -ForegroundColor White
        Write-Host "  • Dashboard: http://localhost:5555" -ForegroundColor Cyan
        Write-Host "  • Right-click tray icon for options" -ForegroundColor White

        if ($EnableAutoStart) {
            Write-Info "Auto-start will be enabled via the tray menu"
            Write-Host "  Right-click tray icon → 'Start with Windows'" -ForegroundColor White
        }
    } else {
        Write-Info "Application may have started - check system tray"
    }

    # Installation complete
    Write-Header "Installation Complete"
    Write-Host ""
    Write-Host "📍 Installed to: $InstallPath" -ForegroundColor Green
    Write-Host "📊 Dashboard: http://localhost:5555" -ForegroundColor Cyan
    Write-Host "📂 Data location: $env:LOCALAPPDATA\DaVinciTimeTracker\data\" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Ensure DaVinci Resolve Studio is installed" -ForegroundColor White
    Write-Host "  2. Open dashboard to verify it's working" -ForegroundColor White
    Write-Host "  3. (Optional) Enable 'Start with Windows' from tray menu" -ForegroundColor White
    Write-Host ""
    Write-Success "Enjoy tracking your DaVinci projects!"

    # Open dashboard
    $openDashboard = Read-Host "`nOpen dashboard now? (Y/N)"
    if ($openDashboard -eq 'Y' -or $openDashboard -eq 'y') {
        Start-Process "http://localhost:5555"
    }
} catch {
    Write-Fail "Installation failed: $_"
    Write-Host ""
    Write-Host "Please check:"
    Write-Host "  1. You have permissions to write to: $InstallPath"
    Write-Host "  2. No antivirus is blocking the installation"
    Write-Host "  3. Network share is accessible"
    exit 1
} finally {
    # Cleanup temp directory
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
