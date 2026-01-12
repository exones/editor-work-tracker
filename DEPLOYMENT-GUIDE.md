# DaVinci Time Tracker - Internal Deployment Guide

## Overview

This guide covers the easiest way to build and distribute DaVinci Time Tracker to your colleagues for internal company use.

## 🚀 Quick Start

### Option 1: Network Share (Recommended for Teams)

**One-time setup:**

```powershell
# Build and deploy to network share
.\build-release.ps1 -NetworkShare "\\fileserver\shared\apps\DaVinciTracker"
```

**Colleagues install with:**

```powershell
# Download and install (run this on their machine)
# See quick-install.ps1 below
```

### Option 2: Direct Distribution (Small Team)

```powershell
# Build locally
.\build-release.ps1

# Share the ZIP from .\releases\ folder via email/chat
```

## 📋 Build Script Options

### Basic Build

```powershell
# Creates ZIP in .\releases\ with date-based version
.\build-release.ps1
```

### Versioned Build

```powershell
# Specify version number
.\build-release.ps1 -Version "1.0.0"
```

### Build + Deploy to Network Share

```powershell
# Build and copy to network share automatically
.\build-release.ps1 -Version "1.0.0" -NetworkShare "\\server\apps\DaVinciTracker"
```

### Skip Build (Repackage Only)

```powershell
# Use existing binaries, just create new package
.\build-release.ps1 -SkipBuild -Version "1.0.1"
```

## 🌐 Network Share Setup

### 1. Create Shared Folder

**On file server:**

```powershell
# Create shared folder
New-Item -ItemType Directory -Path "C:\Shared\Apps\DaVinciTracker"

# Share it (PowerShell as Admin)
New-SmbShare -Name "DaVinciTracker" `
             -Path "C:\Shared\Apps\DaVinciTracker" `
             -ReadAccess "Domain Users"
```

**Or use existing share:**
- `\\fileserver\software\DaVinciTracker`
- `\\company-nas\apps\DaVinciTracker`

### 2. Deploy First Version

```powershell
.\build-release.ps1 -Version "1.0.0" -NetworkShare "\\fileserver\DaVinciTracker"
```

**This creates:**
```
\\fileserver\DaVinciTracker\
├── DaVinciTimeTracker-v1.0.0.zip      # Versioned package
├── DaVinciTimeTracker-latest.zip      # Always newest version
├── version.json                        # Version metadata
├── latest-version.txt                  # Current version number
└── INSTALL.txt                         # Installation instructions
```

### 3. Update Instructions for Colleagues

**Create a simple document or email:**

```
Subject: DaVinci Time Tracker - Download & Install

Hi team,

DaVinci Time Tracker is available on our network share:

📥 Download: \\fileserver\DaVinciTracker\DaVinciTimeTracker-latest.zip

📖 Instructions: See INSTALL.txt in the same folder

Or use the quick install script (see attachment: quick-install.ps1)

Requirements:
- Python 3.8+ (python.org)
- .NET 9 Runtime (dotnet.microsoft.com)
- DaVinci Resolve Studio

Questions? Let me know!
```

## 🔧 Build Script Details

### What It Does

1. **Builds** the application in Release mode
2. **Creates** a ZIP package with version number
3. **Generates** installation instructions
4. **Creates** version tracking files
5. **Optionally** copies everything to network share
6. **Creates** `DaVinciTimeTracker-latest.zip` (always newest)

### Output Files

**Local (`.\releases\`):**
- `DaVinciTimeTracker-vX.X.X.zip` - The app package
- `version.json` - Metadata about this version
- `latest-version.txt` - Just the version number
- `INSTALL.txt` - User installation instructions

**Network Share (if specified):**
- Same as above, plus:
- `DaVinciTimeTracker-latest.zip` - Copy of newest version

## 📦 Package Contents

The ZIP file contains:

```
DaVinciTimeTracker-vX.X.X.zip
├── DaVinciTimeTracker.App.exe     # Main application
├── *.dll                          # Dependencies
├── resolve_api.py                 # Python script for DaVinci API
└── wwwroot/                       # Web dashboard files
    ├── index.html
    ├── css/styles.css
    └── js/dashboard.js
```

## 👥 End User Installation

### Method 1: Manual

1. Download `DaVinciTimeTracker-latest.zip` from network share
2. Extract to `C:\Program Files\DaVinciTimeTracker\`
3. Run `DaVinciTimeTracker.App.exe`
4. (Optional) Right-click tray icon → "Start with Windows"

### Method 2: Quick Install Script

Create `quick-install.ps1` for colleagues:

```powershell
# See quick-install.ps1 file
```

## 🔄 Updates

### Publishing New Version

```powershell
# 1. Make code changes
# 2. Build new version
.\build-release.ps1 -Version "1.0.1" -NetworkShare "\\fileserver\DaVinciTracker"

# 3. Notify colleagues
```

### Users Updating

**Easy way:** Rerun quick-install.ps1 (overwrites old version)

**Manual way:**
1. Close app (right-click tray icon → Exit)
2. Download new `DaVinciTimeTracker-latest.zip`
3. Extract over old installation
4. Restart app

**Note:** User data (database, logs) is in `%LOCALAPPDATA%\DaVinciTimeTracker\` and survives updates!

## 🎯 Best Practices

### For You (Maintainer)

1. **Use semantic versioning**: 1.0.0, 1.0.1, 1.1.0, 2.0.0
2. **Keep old versions**: Don't delete from network share (for rollback)
3. **Document changes**: Update CHANGELOG.md before building
4. **Test first**: Build and test locally before deploying to network

### For Colleagues (Users)

1. **Install in Program Files**: `C:\Program Files\DaVinciTimeTracker\`
2. **Enable auto-start**: Right-click tray → "Start with Windows"
3. **Check for updates**: Check network share periodically
4. **Report issues**: Include log files from tray menu

## 📊 Version Tracking

### Check Current Deployed Version

```powershell
Get-Content "\\fileserver\DaVinciTracker\latest-version.txt"
```

### View Version Details

```powershell
Get-Content "\\fileserver\DaVinciTracker\version.json" | ConvertFrom-Json
```

### List All Versions

```powershell
Get-ChildItem "\\fileserver\DaVinciTracker\DaVinciTimeTracker-v*.zip" | 
    Sort-Object LastWriteTime -Descending |
    Select-Object Name, @{N='Size';E={[math]::Round($_.Length/1MB,2)}}, LastWriteTime
```

## 🔍 Troubleshooting

### Build Fails

**Check:**
- .NET 9 SDK installed
- All code compiles in Visual Studio/Rider
- Run: `dotnet build src\DaVinciTimeTracker.App\DaVinciTimeTracker.App.csproj -c Release`

### Can't Access Network Share

**Check:**
- Network share exists: `Test-Path "\\server\share"`
- You have write access
- Try UNC path instead of mapped drive
- Check Windows Firewall

### Users Can't Install

**Common issues:**
- No Python: Install from python.org
- No .NET 9: Install from dotnet.microsoft.com
- Antivirus blocking: Add exception
- No DaVinci Studio: Need paid version (free doesn't have API)

## 🔐 Security Considerations

### Internal Network Only

- ✅ This is for **internal company use**
- ✅ Network share should be **internal only** (not internet-facing)
- ✅ If needed, add **authentication** to network share

### Code Signing (Optional)

For added trust, consider signing the executable:

```powershell
# Requires code signing certificate
signtool sign /f YourCert.pfx /p Password /t http://timestamp.digicert.com DaVinciTimeTracker.App.exe
```

## 📈 Scaling

### For Larger Organizations

If you grow beyond ~20 users, consider:

1. **Internal web server**: Host releases on internal website
2. **Auto-updater**: Add update checking to the app
3. **MSI installer**: Create proper Windows installer
4. **Group Policy**: Deploy via GPO
5. **Chocolatey**: Create internal Chocolatey package

## 📝 Maintenance Tasks

### Weekly

- Check if colleagues need help
- Review log files if issues reported

### Monthly

- Check for .NET/Python updates
- Consider version updates

### As Needed

- Build and deploy new versions
- Clean old versions from network share (keep last 3-5)

## 🎁 Bonus: Quick Commands

### Build and Deploy (One Command)

```powershell
# Add to your PowerShell profile for convenience
function Deploy-DaVinciTracker {
    param([string]$Version)
    
    .\build-release.ps1 -Version $Version -NetworkShare "\\fileserver\DaVinciTracker"
}

# Usage: Deploy-DaVinciTracker "1.0.2"
```

### Check Deployment Status

```powershell
$share = "\\fileserver\DaVinciTracker"
Write-Host "Current Version:" (Get-Content "$share\latest-version.txt")
Write-Host "Latest Build:" (Get-Item "$share\DaVinciTimeTracker-latest.zip").LastWriteTime
```

## 📞 Support Template

**For your colleagues:**

```
DaVinci Time Tracker - Support

Installation: \\fileserver\DaVinciTracker\INSTALL.txt
Current Version: [Check latest-version.txt]
Issues: Email me with:
  1. Description of problem
  2. Log files (right-click tray icon → "View Latest Log")
  3. Screenshot if applicable

Common Fixes:
- App won't start: Check Python and .NET 9 are installed
- No tracking: Ensure DaVinci Resolve Studio (not free version)
- Port 5555 in use: Check with `netstat -ano | findstr :5555`
```

## ✅ Summary

**Simple internal deployment:**

1. Run: `.\build-release.ps1 -NetworkShare "\\server\share"`
2. Share the network path with colleagues
3. They extract and run
4. Update by rebuilding and notifying team

**That's it!** No complex CI/CD, no servers, just simple file sharing. Perfect for internal use!
