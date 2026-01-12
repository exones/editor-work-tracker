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

# Check if app is running and kill it automatically
Write-Header "Checking for Running Application"
$runningProcesses = Get-Process -Name "DaVinciTimeTracker.App" -ErrorAction SilentlyContinue
if ($runningProcesses) {
    $processCount = ($runningProcesses | Measure-Object).Count
    Write-Info "Found $processCount running instance(s) of DaVinci Time Tracker"
    Write-Info "Closing application..."
    
    try {
        # Stop all instances
        Stop-Process -Name "DaVinciTimeTracker.App" -Force -ErrorAction Stop
        
        # Wait for processes to fully terminate
        $maxWaitSeconds = 10
        $waited = 0
        while ((Get-Process -Name "DaVinciTimeTracker.App" -ErrorAction SilentlyContinue) -and ($waited -lt $maxWaitSeconds)) {
            Start-Sleep -Seconds 1
            $waited++
            Write-Host "." -NoNewline
        }
        Write-Host ""
        
        # Verify processes are gone
        $stillRunning = Get-Process -Name "DaVinciTimeTracker.App" -ErrorAction SilentlyContinue
        if ($stillRunning) {
            Write-Fail "Failed to close application after $maxWaitSeconds seconds"
            Write-Host "Please close the application manually and try again." -ForegroundColor Yellow
            exit 1
        }
        
        Write-Success "Application closed successfully"
        Start-Sleep -Seconds 1  # Extra delay to ensure file handles are released
    } catch {
        Write-Fail "Error closing application: $_"
        Write-Host "Please close the application manually and try again." -ForegroundColor Yellow
        exit 1
    }
} else {
    Write-Success "No running instances found"
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

    # Check Python Launcher (py.exe)
    Write-Info "Checking for Python Launcher (py.exe)..."
    $pyLauncherCheck = Get-Command py -ErrorAction SilentlyContinue
    if ($pyLauncherCheck) {
        Write-Success "Python Launcher found"
        try {
            $pyVersions = & py -0p 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Host "  Installed Python versions:" -ForegroundColor Gray
                $pyVersions -split "`n" | ForEach-Object { 
                    if ($_ -match "^\s*-") { Write-Host "    $_" -ForegroundColor Gray }
                }
            }
        } catch {
            Write-Host "  Note: Could not enumerate Python versions" -ForegroundColor Yellow
        }
    } else {
        Write-Info "Python Launcher (py.exe) not found"
        
        # Check if winget is available
        $wingetCheck = Get-Command winget -ErrorAction SilentlyContinue
        if ($wingetCheck) {
            Write-Host ""
            Write-Host "Python Launcher is recommended for managing Python versions." -ForegroundColor Yellow
            Write-Host "  • Allows selecting specific Python versions (e.g., py -3.11)" -ForegroundColor Gray
            Write-Host "  • Avoids WindowsApps python.exe issues" -ForegroundColor Gray
            $installPyLauncher = Read-Host "Would you like to install Python Launcher? (Y/N)"
            
            if ($installPyLauncher -eq 'Y' -or $installPyLauncher -eq 'y') {
                Write-Info "Installing Python Launcher via winget..."
                Write-Host "This may take a moment..." -ForegroundColor Gray
                
                try {
                    winget install Python.PythonInstallManager --silent --accept-package-agreements --accept-source-agreements 2>&1 | Out-Null
                    
                    if ($LASTEXITCODE -eq 0) {
                        Write-Success "Python Launcher installed successfully!"
                        Write-Info "Refreshing environment variables..."
                        
                        # Refresh PATH
                        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
                        
                        Start-Sleep -Seconds 2
                        
                        # Verify
                        $pyCheck = Get-Command py -ErrorAction SilentlyContinue
                        if ($pyCheck) {
                            Write-Success "Python Launcher is now available!"
                        } else {
                            Write-Info "Python Launcher installed but not yet in PATH"
                            Write-Host "  You may need to restart your terminal" -ForegroundColor Yellow
                        }
                    } else {
                        Write-Fail "Python Launcher installation failed"
                        Write-Host "  You can install manually from: https://www.python.org/downloads/" -ForegroundColor Yellow
                    }
                } catch {
                    Write-Fail "Error installing Python Launcher: $_"
                }
            } else {
                Write-Info "Python Launcher installation skipped"
            }
        } else {
            Write-Info "winget not available - cannot auto-install Python Launcher"
            Write-Host "  Install from: https://www.python.org/downloads/" -ForegroundColor Yellow
        }
    }

    # Check Python
    Write-Info "Checking for Python..."
    $pythonCheck = Get-Command python -ErrorAction SilentlyContinue
    $needsPython312 = $false
    
    if ($pythonCheck) {
        $pythonVersion = & python --version 2>&1
        Write-Success "Python found: $pythonVersion"
        
        # Parse version to check compatibility with DaVinci Resolve
        if ($pythonVersion -match 'Python (\d+)\.(\d+)\.(\d+)') {
            $pyMajor = [int]$matches[1]
            $pyMinor = [int]$matches[2]
            
            # Check if Python 3.13 or higher (incompatible with DaVinci Resolve)
            if ($pyMajor -eq 3 -and $pyMinor -ge 13) {
                Write-Host ""
                Write-Host "⚠ WARNING: Python $pyMajor.$pyMinor is NOT compatible with DaVinci Resolve!" -ForegroundColor Red
                Write-Host "  DaVinci Resolve 20.x requires Python 3.9 - 3.12" -ForegroundColor Yellow
                Write-Host "  Your current version: $pythonVersion" -ForegroundColor Yellow
                Write-Host "  Recommended version: Python 3.11 (tested & verified)" -ForegroundColor Green
                $needsPython312 = $true
            } elseif ($pyMajor -eq 3 -and $pyMinor -ge 9 -and $pyMinor -le 12) {
                Write-Success "Python version is compatible with DaVinci Resolve"
            } else {
                Write-Host "  Note: Python 3.9-3.12 is recommended for DaVinci Resolve" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Info "Python not found in PATH"
        
        # Check if winget is available
        $wingetCheck = Get-Command winget -ErrorAction SilentlyContinue
        if ($wingetCheck) {
            Write-Host ""
            Write-Host "Python is required for DaVinci Time Tracker to work." -ForegroundColor Yellow
            $installPython = Read-Host "Would you like to install Python automatically using winget? (Y/N)"
            
            if ($installPython -eq 'Y' -or $installPython -eq 'y') {
                Write-Info "Installing Python 3.11 via winget..."
                Write-Host "This may take a few minutes..." -ForegroundColor Gray
                
                try {
                    # Install Python 3.11 (tested & verified with DaVinci Resolve)
                    winget install Python.Python.3.11 --silent --accept-package-agreements --accept-source-agreements 2>&1 | Out-Null
                    
                    if ($LASTEXITCODE -eq 0) {
                        Write-Success "Python installed successfully!"
                        Write-Info "Refreshing environment variables..."
                        
                        # Refresh PATH environment variable
                        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
                        
                        # Verify installation
                        Start-Sleep -Seconds 2
                        $pythonCheckAfter = Get-Command python -ErrorAction SilentlyContinue
                        if ($pythonCheckAfter) {
                            $pythonVersion = & python --version 2>&1
                            Write-Success "Python is now available: $pythonVersion"
                        } else {
                            Write-Info "Python installed but not yet in PATH"
                            Write-Host "  You may need to restart your terminal or computer" -ForegroundColor Yellow
                            Write-Host "  Or set DAVINCI_TRACKER_PYTHON environment variable" -ForegroundColor Yellow
                        }
                    } else {
                        Write-Fail "Python installation failed (exit code: $LASTEXITCODE)"
                        Write-Host "  Please install manually from: https://www.python.org/downloads/" -ForegroundColor Yellow
                        Write-Host "  Or set DAVINCI_TRACKER_PYTHON environment variable" -ForegroundColor Yellow
                    }
                } catch {
                    Write-Fail "Error installing Python: $_"
                    Write-Host "  Please install manually from: https://www.python.org/downloads/" -ForegroundColor Yellow
                    Write-Host "  Or set DAVINCI_TRACKER_PYTHON environment variable" -ForegroundColor Yellow
                }
            } else {
                Write-Info "Python installation skipped"
                Write-Host "  Download manually from: https://www.python.org/downloads/" -ForegroundColor Yellow
                Write-Host "  Or set DAVINCI_TRACKER_PYTHON environment variable" -ForegroundColor Yellow
            }
        } else {
            Write-Info "winget not available - cannot auto-install Python"
            Write-Host "  Download from: https://www.python.org/downloads/" -ForegroundColor Yellow
            Write-Host "  Or set DAVINCI_TRACKER_PYTHON environment variable" -ForegroundColor Yellow
        }
    }
    
    # If Python 3.13+ detected, offer to install Python 3.11 alongside
    if ($needsPython312) {
        Write-Host ""
        $wingetCheck = Get-Command winget -ErrorAction SilentlyContinue
        if ($wingetCheck) {
            Write-Host "Would you like to install Python 3.11 (tested & verified version) alongside?" -ForegroundColor Cyan
            Write-Host "  • Python 3.13 will remain on your system" -ForegroundColor Gray
            Write-Host "  • Python 3.11 will be installed separately" -ForegroundColor Gray
            Write-Host "  • DaVinci Tracker will use Python 3.11" -ForegroundColor Gray
            Write-Host "  • This is the exact version used by the developer" -ForegroundColor Green
            $installPy311 = Read-Host "Install Python 3.11? (Y/N)"
            
            if ($installPy311 -eq 'Y' -or $installPy311 -eq 'y') {
                Write-Info "Installing Python 3.11 via winget..."
                Write-Host "This may take a few minutes..." -ForegroundColor Gray
                
                try {
                    winget install Python.Python.3.11 --silent --accept-package-agreements --accept-source-agreements 2>&1 | Out-Null
                    
                    if ($LASTEXITCODE -eq 0) {
                        Write-Success "Python 3.11 installed successfully!"
                        Write-Info "Refreshing environment variables..."
                        
                        # Refresh PATH
                        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
                        
                        Start-Sleep -Seconds 2
                        
                        # Verify - check for py launcher or python3.11
                        $py311Check = Get-Command py -ErrorAction SilentlyContinue
                        if ($py311Check) {
                            $py311Version = & py -3.11 --version 2>&1
                            if ($py311Version) {
                                Write-Success "Python 3.11 is now available: $py311Version"
                                Write-Info "The tracker will use Python 3.11 automatically"
                            }
                        }
                        
                        # Install NumPy 2.2.3 (same as developer's setup)
                        Write-Info "Installing NumPy 2.2.3 (verified version)..."
                        try {
                            & py -3.11 -m pip install "numpy==2.2.3" 2>&1 | Out-Null
                            if ($LASTEXITCODE -eq 0) {
                                Write-Success "NumPy 2.2.3 installed successfully!"
                            }
                        } catch {
                            Write-Info "NumPy will be installed automatically when needed"
                        }
                    } else {
                        Write-Fail "Python 3.11 installation failed"
                        Write-Host "  You can install manually from: https://www.python.org/downloads/" -ForegroundColor Yellow
                        Write-Host "  Make sure to select Python 3.11.x" -ForegroundColor Yellow
                    }
                } catch {
                    Write-Fail "Error installing Python 3.11: $_"
                }
            } else {
                Write-Host ""
                Write-Host "⚠ WARNING: DaVinci Time Tracker may not work with Python 3.13!" -ForegroundColor Red
                Write-Host "  If tracking doesn't work, install Python 3.11 manually" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  Install Python 3.11 manually from: https://www.python.org/downloads/" -ForegroundColor Yellow
            Write-Host "  Select version 3.11.x for DaVinci Resolve compatibility" -ForegroundColor Yellow
        }
    }

    # Check NumPy (verify it's installed with Python 3.11)
    Write-Info "Checking NumPy installation..."
    $pythonCheck = Get-Command python -ErrorAction SilentlyContinue
    if ($pythonCheck) {
        try {
            $numpyVersion = python -c "import numpy; print(numpy.__version__)" 2>&1
            if ($LASTEXITCODE -eq 0 -and $numpyVersion -match '^\d+\.\d+\.\d+') {
                Write-Success "NumPy found: $numpyVersion"
                Write-Host "  Verified: NumPy is compatible with Python 3.11" -ForegroundColor Green
            } else {
                Write-Info "NumPy not installed"
                Write-Host "  Recommended: NumPy 2.2.3 (tested & verified)" -ForegroundColor Yellow
                $installNumpy = Read-Host "Install NumPy 2.2.3? (Y/N)"
                
                if ($installNumpy -eq 'Y' -or $installNumpy -eq 'y') {
                    Write-Info "Installing NumPy 2.2.3..."
                    try {
                        python -m pip install "numpy==2.2.3" 2>&1 | Out-Null
                        if ($LASTEXITCODE -eq 0) {
                            Write-Success "NumPy 2.2.3 installed successfully!"
                        }
                    } catch {
                        Write-Info "NumPy installation will be retried automatically if needed"
                    }
                }
            }
        } catch {
            Write-Info "Could not check NumPy - it will be installed automatically if needed"
        }
    }

    # Check .NET
    Write-Info "Checking for .NET 9 Desktop Runtime..."
    $dotnetCheck = Get-Command dotnet -ErrorAction SilentlyContinue
    $dotnetDesktopFound = $false
    
    if ($dotnetCheck) {
        $dotnetVersions = & dotnet --list-runtimes 2>&1 | Select-String "Microsoft.WindowsDesktop.App 9"
        if ($dotnetVersions) {
            Write-Success ".NET 9 Desktop Runtime found"
            $dotnetDesktopFound = $true
        }
    }
    
    if (-not $dotnetDesktopFound) {
        Write-Info ".NET 9 Desktop Runtime not found"
        
        # Check if winget is available
        $wingetCheck = Get-Command winget -ErrorAction SilentlyContinue
        if ($wingetCheck) {
            Write-Host ""
            Write-Host ".NET 9 Desktop Runtime is required for DaVinci Time Tracker." -ForegroundColor Yellow
            $installDotnet = Read-Host "Would you like to install it automatically using winget? (Y/N)"
            
            if ($installDotnet -eq 'Y' -or $installDotnet -eq 'y') {
                Write-Info "Installing .NET 9 Desktop Runtime via winget..."
                Write-Host "This may take a few minutes..." -ForegroundColor Gray
                
                try {
                    # Install .NET 9 Desktop Runtime
                    winget install Microsoft.DotNet.DesktopRuntime.9 --silent --accept-package-agreements --accept-source-agreements 2>&1 | Out-Null
                    
                    if ($LASTEXITCODE -eq 0) {
                        Write-Success ".NET 9 Desktop Runtime installed successfully!"
                        Write-Info "Refreshing environment variables..."
                        
                        # Refresh PATH environment variable
                        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
                        
                        # Verify installation
                        Start-Sleep -Seconds 2
                        $dotnetCheckAfter = Get-Command dotnet -ErrorAction SilentlyContinue
                        if ($dotnetCheckAfter) {
                            $dotnetVersionsAfter = & dotnet --list-runtimes 2>&1 | Select-String "Microsoft.WindowsDesktop.App 9"
                            if ($dotnetVersionsAfter) {
                                Write-Success ".NET 9 Desktop Runtime is now available"
                            } else {
                                Write-Info ".NET installed but verification pending"
                                Write-Host "  You may need to restart your terminal or computer" -ForegroundColor Yellow
                            }
                        } else {
                            Write-Info ".NET installed but not yet in PATH"
                            Write-Host "  You may need to restart your terminal or computer" -ForegroundColor Yellow
                        }
                    } else {
                        Write-Fail ".NET installation failed (exit code: $LASTEXITCODE)"
                        Write-Host "  Please install manually from: https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Yellow
                        Write-Host "  Required: '.NET 9 Desktop Runtime'" -ForegroundColor Yellow
                    }
                } catch {
                    Write-Fail "Error installing .NET: $_"
                    Write-Host "  Please install manually from: https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Yellow
                    Write-Host "  Required: '.NET 9 Desktop Runtime'" -ForegroundColor Yellow
                }
            } else {
                Write-Info ".NET installation skipped"
                Write-Host "  Download from: https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Yellow
                Write-Host "  Required: '.NET 9 Desktop Runtime'" -ForegroundColor Yellow
            }
        } else {
            Write-Info "winget not available - cannot auto-install .NET"
            Write-Host "  Download from: https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Yellow
            Write-Host "  Required: '.NET 9 Desktop Runtime'" -ForegroundColor Yellow
        }
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
