# DaVinci Time Tracker - Deployment & Portability Guide

## ✅ Portability Status

The application is now **fully portable** and can be deployed to any Windows machine with minimal requirements.

### ✅ All Paths Are Safe

| Component               | Path Type                                       | Status  | Notes                                                                        |
| ----------------------- | ----------------------------------------------- | ------- | ---------------------------------------------------------------------------- |
| **Python Executable**   | Auto-detected                                   | ✅ SAFE | Searches common locations automatically                                      |
| **Python Script**       | Relative (`resolve_api.py`)                     | ✅ SAFE | Copied during build                                                          |
| **Database**            | User Data (`%LOCALAPPDATA%\DaVinciTimeTracker`) | ✅ SAFE | Created via `AppPaths.DatabasePath` in `%LOCALAPPDATA%\DaVinciTimeTracker\`  |
| **Logs**                | User Data (`%LOCALAPPDATA%\DaVinciTimeTracker`) | ✅ SAFE | Created via `AppPaths.LogsDirectory` in `%LOCALAPPDATA%\DaVinciTimeTracker\` |
| **WWWRoot**             | Relative (`wwwroot/`)                           | ✅ SAFE | Copied during build                                                          |
| **Auto-start Shortcut** | System API                                      | ✅ SAFE | Uses Windows `SpecialFolder.Startup`                                         |
| **DaVinci API**         | Environment-based                               | ✅ SAFE | Uses `%PROGRAMDATA%` environment variable                                    |

## 🚀 Deployment Instructions

### Option 1: Simple Copy (Recommended)

1. Build the application:

   ```powershell
   dotnet build src\DaVinciTimeTracker.App\DaVinciTimeTracker.App.csproj -c Release
   ```

2. Copy the entire output folder:

   ```
   src\DaVinciTimeTracker.App\bin\Release\net9.0-windows\
   ```

3. Paste it anywhere on the target machine (e.g., `C:\Tools\DaVinciTimeTracker\`)

4. Run `DaVinciTimeTracker.App.exe`

### Option 2: Self-Contained Publish (No .NET Runtime Required)

```powershell
dotnet publish src\DaVinciTimeTracker.App\DaVinciTimeTracker.App.csproj `
    -c Release `
    --self-contained true `
    -r win-x64 `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true
```

Output location: `src\DaVinciTimeTracker.App\bin\Release\net9.0-windows\win-x64\publish\`

## 📋 Target Machine Requirements

### Minimum Requirements

✅ **Operating System**: Windows 10 or later
✅ **Python**: 3.8 or later (any installation location supported)
✅ **DaVinci Resolve**: Studio version (Free version doesn't support scripting API)
✅ **.NET Runtime**: .NET 9 Desktop Runtime (only if not using self-contained build)

### Optional

- **DAVINCI_TRACKER_PYTHON** environment variable (if Python is in a non-standard location)

## 🔍 Python Auto-Detection

The application searches for Python in the following order:

1. **`DAVINCI_TRACKER_PYTHON` environment variable** (highest priority)

   ```powershell
   [System.Environment]::SetEnvironmentVariable('DAVINCI_TRACKER_PYTHON', 'C:\Custom\Path\python.exe', 'User')
   ```

2. **System PATH environment variable**

   - Looks for `python.exe` in any PATH directory

3. **Common installation locations**:

   - `C:\Program Files\PythonXX\python.exe`
   - `C:\Program Files (x86)\PythonXX\python.exe`
   - `%LOCALAPPDATA%\Programs\Python\PythonXX\python.exe`
   - `%LOCALAPPDATA%\Microsoft\WindowsApps\python.exe` (Microsoft Store)
   - `%LOCALAPPDATA%\Microsoft\WindowsApps\python3.exe`

4. **Directory scan** of Program Files folders

### Validation

After finding Python, the application validates it by running:

```
python.exe --version
```

## 📦 Packaging for Distribution

### Create a Portable ZIP

```powershell
# Build
dotnet build src\DaVinciTimeTracker.App\DaVinciTimeTracker.App.csproj -c Release

# Create ZIP
$outputDir = "src\DaVinciTimeTracker.App\bin\Release\net9.0-windows"
$zipPath = "DaVinciTimeTracker-Portable.zip"
Compress-Archive -Path "$outputDir\*" -DestinationPath $zipPath -Force

Write-Host "Created: $zipPath"
```

### Create an Installer (Optional)

Use tools like:

- **Inno Setup** (free) - https://jrsoftware.org/isinfo.php
- **WiX Toolset** (free) - https://wixtoolset.org/
- **Advanced Installer** (commercial) - https://www.advancedinstaller.com/

## 🧪 Testing on a Clean Machine

### Pre-Deployment Checklist

1. ✅ Test on a VM without development tools
2. ✅ Test with different Python versions (3.8, 3.9, 3.10, 3.11, 3.12)
3. ✅ Test with Python in different locations:
   - Program Files
   - %LOCALAPPDATA%
   - Custom directory with DAVINCI_TRACKER_PYTHON
4. ✅ Test without DaVinci Resolve (should still start, just log errors)
5. ✅ Test without Python (should show error message)
6. ✅ Test database creation in read-only directories
7. ✅ Test auto-start functionality
8. ✅ Verify web dashboard access

### Test Script

```powershell
# Test Python detection
$env:DAVINCI_TRACKER_PYTHON = ""  # Clear override
.\DaVinciTimeTracker.App.exe  # Should auto-detect

# Test with custom Python
$env:DAVINCI_TRACKER_PYTHON = "C:\Python312\python.exe"
.\DaVinciTimeTracker.App.exe  # Should use custom path

# Test web dashboard
Start-Process "http://localhost:5555"

# Check log files in user data directory
$userDataPath = "$env:LOCALAPPDATA\DaVinciTimeTracker"
Get-Content "$userDataPath\logs\davinci-tracker-*.log" | Select-String "Python"

# Verify database location
Test-Path "$userDataPath\davinci-timetracker.db"
```

## 🐛 Common Deployment Issues

### Issue: "Python not found"

**Solution**:

1. Install Python from https://python.org
2. Or set `DAVINCI_TRACKER_PYTHON` environment variable
3. Or add Python directory to PATH

### Issue: "DaVinci API error"

**Solution**:

1. Ensure DaVinci Resolve **Studio** is installed (not free version)
2. Verify API path exists:
   ```
   C:\ProgramData\Blackmagic Design\DaVinci Resolve\Support\Developer\Scripting\Modules
   ```

### Issue: "Port 5555 already in use"

**Solution**:

1. Close other applications using port 5555
2. Or modify port in `Program.cs` before building

### Issue: Database file locked

**Solution**:

- Ensure only one instance of the application is running
- Check if antivirus is locking the database file at `%LOCALAPPDATA%\DaVinciTimeTracker\davinci-timetracker.db`
- Verify you have write permissions to the `%LOCALAPPDATA%\DaVinciTimeTracker` directory

## 📊 File Structure After Deployment

### Application Directory

Installation location (e.g., `C:\Tools\DaVinciTimeTracker\` or `C:\Program Files\DaVinciTimeTracker\`):

```
DaVinciTimeTracker/
├── DaVinciTimeTracker.App.exe          ← Main executable
├── DaVinciTimeTracker.Core.dll
├── DaVinciTimeTracker.Data.dll
├── DaVinciTimeTracker.Web.dll
├── resolve_api.py                       ← Python script (portable)
├── wwwroot/                             ← Web dashboard files
│   ├── index.html
│   ├── css/
│   │   └── styles.css
│   └── js/
│       └── dashboard.js
└── [Various DLL dependencies]
```

### User Data Directory

Per-user data location (`%LOCALAPPDATA%\DaVinciTimeTracker\`):

```
%LOCALAPPDATA%\DaVinciTimeTracker/
├── davinci-timetracker.db              ← SQLite database (created on first run)
└── logs/                                ← Log files (created on first run)
    └── davinci-tracker-YYYYMMDD.log
```

**Example**: `C:\Users\JohnDoe\AppData\Local\DaVinciTimeTracker\`

This separation follows Windows best practices:

- **Application binaries** can be installed to `Program Files` (read-only)
- **User data** is stored per-user in `%LOCALAPPDATA%` (writable)
- Multiple Windows users can run the application independently with separate databases

## 🔒 Security Considerations

### Safe Practices

✅ **No hardcoded credentials**
✅ **No network communication** (localhost only)
✅ **No admin privileges required**
✅ **Database stored locally**
✅ **Logs stored locally**

### Firewall

The application only listens on `localhost:5555` - no external network access needed.

### Data Privacy

- All data stays on the local machine in `%LOCALAPPDATA%\DaVinciTimeTracker\`
- No telemetry or analytics
- No internet connection required
- Each Windows user has isolated data (databases are not shared between users)

## 📈 Scaling & Multi-User

### Single User

- Database: SQLite (suitable for personal use)
- No conflicts

### Multiple Users (Same Machine)

Each Windows user gets their own data in `%LOCALAPPDATA%\DaVinciTimeTracker\`:

- Database file (`davinci-timetracker.db`)
- Log files (`logs/`)
- Auto-start configuration

The application binaries in `Program Files` or other installation directory are shared (read-only).

### Enterprise Deployment

For centralized database/statistics:

1. Replace SQLite with SQL Server/PostgreSQL
2. Modify connection strings in `Program.cs`
3. Implement user authentication

## 🆘 Support Information

Include in your distribution:

1. **README.md** - Installation and usage
2. **DEPLOYMENT.md** - This file
3. **LICENSE.txt** - Your license
4. **CHANGELOG.md** - Version history

## 📝 Version Information

Log the following on first start:

- Application version
- Python version and path
- DaVinci Resolve detection status
- OS version

This is already logged automatically by the application.

---

**Last Updated**: January 12, 2026
**Compatible With**: Windows 10+, Python 3.8+, DaVinci Resolve Studio 18+
