# DaVinci Time Tracker - Delivery Pipeline Summary

## 🎯 Overview

Simple, maintainable deployment system for internal company use. No complex CI/CD needed!

## 📁 Files Created

### For You (Maintainer)

| File                    | Purpose                                                  |
| ----------------------- | -------------------------------------------------------- |
| `build-release.ps1`     | **Main build script** - Creates release packages         |
| `DEPLOYMENT-GUIDE.md`   | **Complete deployment guide** - All the details          |
| `CHANGELOG.md`          | **Version history** - Track changes                      |
| `Directory.Build.props` | **Build isolation** - Separates from parent build system |

### For Colleagues (End Users)

| File                | Purpose                                                             |
| ------------------- | ------------------------------------------------------------------- |
| `quick-install.ps1` | **One-click installer** - Downloads and installs from network share |
| `INSTALL.txt`       | **Installation instructions** - Auto-generated with each build      |

### Auto-Generated (Build Output)

| File                                     | Purpose                       |
| ---------------------------------------- | ----------------------------- |
| `releases/DaVinciTimeTracker-vX.X.X.zip` | **The application package**   |
| `releases/version.json`                  | **Version metadata**          |
| `releases/latest-version.txt`            | **Current version number**    |
| `releases/INSTALL.txt`                   | **Installation instructions** |

## 🚀 Quick Start Guide

### 1. Build a Release

```powershell
# Simple build (local only)
.\build-release.ps1 -Version "1.0.0"

# Build + Deploy to network share
.\build-release.ps1 -Version "1.0.0" -NetworkShare "\\fileserver\DaVinciTracker"
```

### 2. Distribute to Colleagues

**Option A: Network Share (Recommended)**

```powershell
# After building with -NetworkShare, tell colleagues:
"Download from: \\fileserver\DaVinciTracker\DaVinciTimeTracker-latest.zip"
```

**Option B: Direct Distribution**

```
Email/Slack the ZIP file from: .\releases\DaVinciTimeTracker-v1.0.0.zip
```

### 3. Colleagues Install

**Easy Way - Give them this script:**

```powershell
# quick-install.ps1
.\quick-install.ps1 -NetworkShare "\\fileserver\DaVinciTracker"
```

**Manual Way:**

1. Extract ZIP to `C:\Program Files\DaVinciTimeTracker\`
2. Run `DaVinciTimeTracker.App.exe`
3. Done!

## 📋 Complete Workflow Example

### Monthly Release Cycle

```powershell
# 1. Update code
cd c:\code\everix3\code\utils\davinci-time-tracker

# 2. Update changelog
notepad CHANGELOG.md  # Add new version section

# 3. Build and deploy
.\build-release.ps1 -Version "1.1.0" -NetworkShare "\\fileserver\DaVinciTracker"

# 4. Notify team
# Send email with: "New version 1.1.0 available at \\fileserver\DaVinciTracker"
```

### Team members update:

```powershell
# Run the quick installer (closes old version, installs new)
.\quick-install.ps1 -NetworkShare "\\fileserver\DaVinciTracker"
```

## 🎨 Customization Options

### Change Default Install Location

Edit `quick-install.ps1`:

```powershell
[string]$InstallPath = "C:\Tools\DaVinciTracker"  # Your preference
```

### Change Package Output Location

```powershell
.\build-release.ps1 -Version "1.0.0" -OutputPath "D:\Releases"
```

### Skip Build (Repackage Only)

```powershell
# Useful if you only changed documentation
.\build-release.ps1 -Version "1.0.1" -SkipBuild
```

## 📊 Version Management

### Semantic Versioning

- **1.0.0** → **1.0.1**: Bug fixes
- **1.0.0** → **1.1.0**: New features (backwards compatible)
- **1.0.0** → **2.0.0**: Breaking changes

### Check Deployed Version

```powershell
# What's on the network share?
Get-Content "\\fileserver\DaVinciTracker\latest-version.txt"

# All versions available
Get-ChildItem "\\fileserver\DaVinciTracker\DaVinciTimeTracker-v*.zip"
```

### Rollback

```powershell
# Copy old version to latest.zip
$oldVersion = "\\fileserver\DaVinciTracker\DaVinciTimeTracker-v1.0.0.zip"
$latest = "\\fileserver\DaVinciTracker\DaVinciTimeTracker-latest.zip"
Copy-Item $oldVersion $latest -Force
```

## 🔧 Troubleshooting

### Build Fails

**Error: "Directory.Build.props not found"**

- Fixed! We created a local `Directory.Build.props` to isolate the project

**Error: "dotnet command not found"**

```powershell
# Install .NET 9 SDK
https://dotnet.microsoft.com/download/dotnet/9.0
```

### Network Share Issues

**Can't access share**

```powershell
# Test access
Test-Path "\\fileserver\DaVinciTracker"

# Map drive if needed
New-PSDrive -Name Z -PSProvider FileSystem -Root "\\fileserver\DaVinciTracker"
```

**Permission denied**

- Ensure you have write access to the share
- Contact IT to grant permissions

### Colleagues Can't Install

**Python not found**

- Install from: https://www.python.org/downloads/
- Or set: `$env:DAVINCI_TRACKER_PYTHON = "C:\Path\To\python.exe"`

**.NET 9 not found**

- Install from: https://dotnet.microsoft.com/download/dotnet/9.0
- Need: ".NET 9 Desktop Runtime"

## 📈 Scaling Up

### For Small Teams (5-10 people)

✅ Current setup is perfect!

- Network share
- Manual builds
- Email notifications

### For Medium Teams (10-50 people)

Consider:

- **Scheduled builds**: Weekly automated builds
- **Internal web server**: Host releases on internal website
- **Update notifications**: Add version check to the app

### For Large Teams (50+ people)

Consider:

- **CI/CD pipeline**: GitHub Actions or Azure DevOps
- **MSI installer**: Professional Windows installer
- **Auto-updater**: In-app update notifications
- **Group Policy deployment**: IT-managed rollout

## 🎁 Bonus Scripts

### Email Template Generator

```powershell
# Generate email for new release
$version = "1.1.0"
$changes = @"
- New: Export to CSV
- Fixed: Grace period bug
- Improved: Performance
"@

$email = @"
Subject: DaVinci Time Tracker v$version Available

Hi team,

New version of DaVinci Time Tracker is now available!

Version: $version
Download: \\fileserver\DaVinciTracker\DaVinciTimeTracker-latest.zip

What's new:
$changes

Installation:
1. Run quick-install.ps1 (attached) OR
2. Download and extract ZIP manually

Questions? Let me know!
"@

$email | Set-Clipboard
Write-Host "Email template copied to clipboard!" -ForegroundColor Green
```

### Cleanup Old Versions

```powershell
# Keep only last 5 versions on network share
$share = "\\fileserver\DaVinciTracker"
$allVersions = Get-ChildItem "$share\DaVinciTimeTracker-v*.zip" |
               Where-Object { $_.Name -ne "DaVinciTimeTracker-latest.zip" } |
               Sort-Object LastWriteTime -Descending

if ($allVersions.Count -gt 5) {
    $toDelete = $allVersions | Select-Object -Skip 5
    Write-Host "Cleaning up $($toDelete.Count) old versions..."
    $toDelete | Remove-Item -Force
}
```

## ✅ Checklist for Each Release

- [ ] Update `CHANGELOG.md` with new version
- [ ] Test locally first
- [ ] Run `build-release.ps1` with correct version
- [ ] Verify package created
- [ ] Test installation on clean machine (optional but recommended)
- [ ] Deploy to network share
- [ ] Notify team via email/Slack
- [ ] Update any internal documentation

## 📞 Support

### For You (Maintainer)

- Build issues: Check `Directory.Build.props` exists
- Network issues: Verify share permissions
- Version conflicts: Use semantic versioning

### For Colleagues (Users)

Point them to:

1. `INSTALL.txt` in the package
2. Logs via tray menu → "View Latest Log"
3. Network share for updates: `\\fileserver\DaVinciTracker`

## 🎉 Summary

**Your deployment pipeline:**

```
Code Changes → Update CHANGELOG → Build → Network Share → Notify Team
     ↓             ↓                 ↓          ↓              ↓
  Local        Edit file      build-release   Copy      Email/Slack
```

**Colleague installs:**

```
Network Share → quick-install.ps1 → Running App
      ↓                ↓                 ↓
    Browse       One command        System tray
```

**Simple, maintainable, perfect for internal use!**

## 📚 Related Documentation

- `DEPLOYMENT-GUIDE.md` - Detailed deployment guide
- `README.md` - User documentation
- `CHANGELOG.md` - Version history
- `build-release.ps1` - Build script (run with `-?` for help)
- `quick-install.ps1` - Install script for end users

---

**Created**: 2026-01-12
**Last Updated**: 2026-01-12
**Maintained by**: [Your Name]
