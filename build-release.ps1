#Requires -Version 7.0

<#
.SYNOPSIS
    Builds and packages DaVinci Time Tracker for distribution

.DESCRIPTION
    This script builds the application in Release mode, creates a ZIP package,
    and optionally copies it to a network share for distribution.
    Version is auto-generated as ISO date-time (yyyyMMdd-HHmmss).

.PARAMETER OutputPath
    Where to save the ZIP file. Default: .\releases\

.PARAMETER NetworkShare
    Network share path to copy the release. Default: F:\Linia\DaVinciTracker

.PARAMETER SkipBuild
    Skip the build step (use existing binaries)

.EXAMPLE
    .\build-release.ps1

.EXAMPLE
    .\build-release.ps1 -NetworkShare "F:\Linia\DaVinciTracker"
#>

param(
    [string]$OutputPath = ".\releases",
    [string]$NetworkShare = "F:\Linia\DaVinciTracker",
    [switch]$SkipBuild = $false
)

$ErrorActionPreference = "Stop"

# Colors
function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Success($text) { Write-Host "✓ $text" -ForegroundColor Green }
function Write-Info($text) { Write-Host "ℹ $text" -ForegroundColor Yellow }
function Write-Fail($text) { Write-Host "✗ $text" -ForegroundColor Red }

Write-Header "DaVinci Time Tracker - Release Build"

# Auto-generate version from current date/time
$Version = Get-Date -Format "yyyyMMdd-HHmmss"
Write-Info "Version: $Version"

$ProjectPath = "src\DaVinciTimeTracker.App\DaVinciTimeTracker.App.csproj"
$BuildOutputPath = "src\DaVinciTimeTracker.App\bin\Release\net9.0-windows"

# Create output directory
if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    Write-Success "Created output directory: $OutputPath"
}

# Build
if (-not $SkipBuild) {
    Write-Header "Building Application (Release Mode)"

    try {
        # Clean previous build
        Write-Info "Cleaning previous build..."
        dotnet clean $ProjectPath -c Release --nologo -v quiet

        # Build
        Write-Info "Building..."
        $buildOutput = dotnet build $ProjectPath -c Release --nologo 2>&1

        if ($LASTEXITCODE -ne 0) {
            Write-Fail "Build failed!"
            Write-Host $buildOutput
            exit 1
        }

        Write-Success "Build completed successfully"
    } catch {
        Write-Fail "Build failed: $_"
        exit 1
    }
} else {
    Write-Info "Skipping build (using existing binaries)"
}

# Verify build output exists
if (-not (Test-Path $BuildOutputPath)) {
    Write-Fail "Build output not found at: $BuildOutputPath"
    exit 1
}

# Create package
Write-Header "Creating Release Package"

$ZipFileName = "DaVinciTimeTracker-v$Version.zip"
$ZipFilePath = Join-Path $OutputPath $ZipFileName

# Remove existing ZIP if it exists
if (Test-Path $ZipFilePath) {
    Remove-Item $ZipFilePath -Force
    Write-Info "Removed existing package"
}

# Create ZIP
try {
    Write-Info "Packaging files..."
    Compress-Archive -Path "$BuildOutputPath\*" -DestinationPath $ZipFilePath -CompressionLevel Optimal

    $zipSize = [math]::Round((Get-Item $ZipFilePath).Length / 1MB, 2)
    Write-Success "Package created: $ZipFileName ($zipSize MB)"
} catch {
    Write-Fail "Failed to create package: $_"
    exit 1
}

# Create version info file
Write-Header "Creating Version Info"

$versionInfo = @{
    Version         = $Version
    BuildDate       = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    FileName        = $ZipFileName
    FileSize        = "$zipSize MB"
    RequiredPython  = "3.8+"
    RequiredDotNet  = ".NET 9 Desktop Runtime"
    RequiredDaVinci = "DaVinci Resolve Studio"
}

$versionJsonPath = Join-Path $OutputPath "version.json"
$versionInfo | ConvertTo-Json | Set-Content $versionJsonPath -Encoding UTF8

$versionTextPath = Join-Path $OutputPath "latest-version.txt"
$Version | Set-Content $versionTextPath -Encoding UTF8 -NoNewline

Write-Success "Version info created"

# Create README for distribution
$readmePath = Join-Path $OutputPath "INSTALL.txt"
$installInstructions = @"
DaVinci Time Tracker - Installation Instructions
================================================

Version: $Version
Build Date: $(Get-Date -Format "yyyy-MM-dd HH:mm")

REQUIREMENTS
------------
1. DaVinci Resolve Studio (Free version doesn't support scripting API)
2. Python 3.8 or later
3. .NET 9 Desktop Runtime: https://dotnet.microsoft.com/download/dotnet/9.0
4. PowerShell 7+: https://aka.ms/powershell-release?tag=stable
5. Windows 10 or later

INSTALLATION
------------
1. Extract DaVinciTimeTracker-v$Version.zip to any folder
   Recommended: %LOCALAPPDATA%\DaVinciTimeTracker\

2. Run DaVinciTimeTracker.App.exe

3. The application will:
   - Appear in the system tray (near the clock)
   - Auto-detect Python (or set DAVINCI_TRACKER_PYTHON env variable)
   - Create user data in: %LOCALAPPDATA%\DaVinciTimeTracker\
   - Start web dashboard at: http://localhost:5555

FIRST TIME SETUP
----------------
1. Ensure DaVinci Resolve Studio is installed
2. Ensure Python is installed (check: python --version)
3. Right-click the tray icon and select "Open Dashboard"
4. (Optional) Enable "Start with Windows" from tray menu

USAGE
-----
- The app tracks time automatically when DaVinci Resolve is active
- View statistics at: http://localhost:5555
- Right-click tray icon for options:
  * Open Dashboard
  * View Latest Log
  * Open Logs Folder
  * Start with Windows (auto-start)
  * Exit

DATA LOCATION
-------------
Application and data are both stored in:
  %LOCALAPPDATA%\DaVinciTimeTracker\
  (typically: C:\Users\<YourName>\AppData\Local\DaVinciTimeTracker\)

This means:
  ✓ No administrator privileges required
  ✓ Each Windows user has their own installation
  ✓ Data survives application updates
  ✓ Easy to uninstall (just delete the folder)

TROUBLESHOOTING
---------------
1. Python not found
   - Install from: https://www.python.org/downloads/
   - Or set environment variable: DAVINCI_TRACKER_PYTHON=C:\Path\To\python.exe

2. Check logs:
   - Right-click tray icon > "View Latest Log"
   - Or browse to: %LOCALAPPDATA%\DaVinciTimeTracker\logs\

3. Port conflict (5555 already in use)
   - Close other applications using port 5555
   - Check which app: netstat -ano | findstr :5555

SUPPORT
-------
For issues, check:
1. Logs folder (via tray menu)
2. Ensure Python 3.8+ is installed
3. Ensure DaVinci Resolve Studio is running
4. Contact: [Your Support Contact]

"@

$installInstructions | Set-Content $readmePath -Encoding UTF8
Write-Success "Installation instructions created"

# Display summary
Write-Header "Build Summary"
Write-Host ""
Write-Host "  Version:       $Version" -ForegroundColor White
Write-Host "  Package:       $ZipFileName" -ForegroundColor White
Write-Host "  Size:          $zipSize MB" -ForegroundColor White
Write-Host "  Location:      $(Resolve-Path $OutputPath)" -ForegroundColor White
Write-Host ""

# Copy to network share if specified
if (-not [string]::IsNullOrEmpty($NetworkShare)) {
    Write-Header "Copying to Network Share"

    try {
        if (-not (Test-Path $NetworkShare)) {
            Write-Info "Creating network share directory..."
            New-Item -ItemType Directory -Path $NetworkShare -Force | Out-Null
        }

        # Copy ZIP
        $destZip = Join-Path $NetworkShare $ZipFileName
        Copy-Item $ZipFilePath $destZip -Force
        Write-Success "Copied ZIP to: $destZip"

        # Copy version info
        Copy-Item $versionJsonPath (Join-Path $NetworkShare "version.json") -Force
        Copy-Item $versionTextPath (Join-Path $NetworkShare "latest-version.txt") -Force
        Write-Success "Copied version info"

        # Copy install instructions
        Copy-Item $readmePath (Join-Path $NetworkShare "INSTALL.txt") -Force
        Write-Success "Copied installation instructions"

        # Copy quick-install script and bat wrapper
        $quickInstallPath = "quick-install.ps1"
        if (Test-Path $quickInstallPath) {
            Copy-Item $quickInstallPath (Join-Path $NetworkShare "quick-install.ps1") -Force
            Write-Success "Copied quick-install script"
        }

        $quickInstallBatPath = "quick-install.bat"
        if (Test-Path $quickInstallBatPath) {
            Copy-Item $quickInstallBatPath (Join-Path $NetworkShare "quick-install.bat") -Force
            Write-Success "Copied quick-install launcher"
        }

        # Copy diagnostics scripts
        $diagnosticsDir = "diagnostics"
        $diagnosticScript = Join-Path $diagnosticsDir "resolve-python-diagnose.ps1"
        if (Test-Path $diagnosticScript) {
            $destDiagnosticsDir = Join-Path $NetworkShare "diagnostics"
            if (-not (Test-Path $destDiagnosticsDir)) {
                New-Item -ItemType Directory -Path $destDiagnosticsDir -Force | Out-Null
            }
            Copy-Item $diagnosticScript (Join-Path $destDiagnosticsDir "resolve-python-diagnose.ps1") -Force
            Write-Success "Copied Resolve/Python diagnostic script"
        }

        # Create/update latest.zip symlink (always points to newest version)
        $latestZip = Join-Path $NetworkShare "DaVinciTimeTracker-latest.zip"
        if (Test-Path $latestZip) {
            Remove-Item $latestZip -Force
        }
        Copy-Item $destZip $latestZip -Force
        Write-Success "Created latest.zip link"

        Write-Host ""
        Write-Success "Network share updated successfully!"
        Write-Host "  Share location: $NetworkShare" -ForegroundColor White
        Write-Host "  Direct download: $latestZip" -ForegroundColor Cyan
    } catch {
        Write-Fail "Failed to copy to network share: $_"
        Write-Info "Local package is still available at: $ZipFilePath"
    }
}

# Final instructions
Write-Header "Distribution"
Write-Host ""
Write-Host "📦 Package ready for distribution!" -ForegroundColor Green
Write-Host ""
Write-Host "To distribute to colleagues:" -ForegroundColor Yellow

if (-not [string]::IsNullOrEmpty($NetworkShare)) {
    Write-Host "  1. They can download from: $NetworkShare\DaVinciTimeTracker-latest.zip" -ForegroundColor White
    Write-Host "  2. Or use this PowerShell one-liner:" -ForegroundColor White
    Write-Host ""
    $downloadCmd = "     Invoke-WebRequest -Uri `"file://$NetworkShare\DaVinciTimeTracker-latest.zip`" -OutFile `"~\Downloads\DaVinciTimeTracker.zip`""
    Write-Host $downloadCmd -ForegroundColor Cyan
} else {
    Write-Host "  1. Share the ZIP file: $ZipFilePath" -ForegroundColor White
    Write-Host "  2. Include: INSTALL.txt (in the same folder)" -ForegroundColor White
    Write-Host ""
    Write-Host "  Or run again with -NetworkShare to auto-deploy:" -ForegroundColor Yellow
    Write-Host "     .\build-release.ps1 -NetworkShare `"\\server\share\apps`"" -ForegroundColor Cyan
}

Write-Host ""
Write-Success "Build complete!"
