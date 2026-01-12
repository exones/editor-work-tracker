#Requires -Version 7.0

<#
.SYNOPSIS
  Diagnose DaVinci Resolve Python API (FusionScript) connectivity issues on Windows.

.DESCRIPTION
  Collects:
   - DaVinci Resolve process status
   - Scripting Modules folder contents
   - Python executables discovered (PATH, py launcher, common locations, DAVINCI_TRACKER_PYTHON)
   - For each Python: version, arch, sys.path, and import/connect test for DaVinciResolveScript
   - Runs the tracker script (resolve_api.py) and captures stdout/stderr/exit code

  Paste the output into chat for analysis.

.PARAMETER TrackerScriptPath
  Path to resolve_api.py (defaults to %LOCALAPPDATA%\DaVinciTimeTracker\resolve_api.py)

.PARAMETER ResolveModulesPath
  Path to DaVinci Resolve scripting Modules directory.
  Default: %PROGRAMDATA%\Blackmagic Design\DaVinci Resolve\Support\Developer\Scripting\Modules

.PARAMETER MaxSysPathLines
  Limit sys.path printing per Python to reduce noise.
#>

param(
    [string]$TrackerScriptPath = "$env:LOCALAPPDATA\DaVinciTimeTracker\resolve_api.py",
    [string]$ResolveModulesPath = "$(Join-Path ($env:PROGRAMDATA ?? 'C:\ProgramData') 'Blackmagic Design\DaVinci Resolve\Support\Developer\Scripting\Modules')",
    [int]$MaxSysPathLines = 35
)

$ErrorActionPreference = "Stop"

function Write-Section([string]$title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
}

function Write-KV([string]$k, [string]$v) {
    Write-Host ("{0}: {1}" -f $k, $v)
}

function Get-ExistingUniquePaths([string[]]$paths) {
    $paths |
        Where-Object { $_ -and (Test-Path $_) } |
        ForEach-Object { (Resolve-Path $_).Path } |
        Select-Object -Unique
}

function Invoke-Run([string]$exe, [string[]]$argList, [int]$timeoutSeconds = 20) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

    # IMPORTANT: Use ArgumentList (not a single Arguments string). This avoids quoting issues
    # that can cause WindowsApps python shims to drop into interactive mode (>>>).
    foreach ($a in $argList) {
        [void]$psi.ArgumentList.Add($a)
    }

    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $psi
    [void]$p.Start()

    $stdoutTask = $p.StandardOutput.ReadToEndAsync()
    $stderrTask = $p.StandardError.ReadToEndAsync()

    if (-not $p.WaitForExit($timeoutSeconds * 1000)) {
        try { $p.Kill($true) } catch {}
        return @{
            ExitCode = $null
            TimedOut = $true
            Stdout   = ($stdoutTask.Result ?? "")
            Stderr   = ($stderrTask.Result ?? "")
        }
    }

    return @{
        ExitCode = $p.ExitCode
        TimedOut = $false
        Stdout   = ($stdoutTask.Result ?? "")
        Stderr   = ($stderrTask.Result ?? "")
    }
}

function ConvertTo-PyRawString([string]$path) {
    # Return a python raw triple-quoted string literal for a Windows path.
    # Safe for backslashes and spaces.
    $escaped = $path -replace '"""', '\"\"\"'
    return ('r"""' + $escaped + '"""')
}

Write-Section "Environment"
Write-KV "Timestamp" (Get-Date).ToString("o")
Write-KV "User" $env:USERNAME
Write-KV "Computer" $env:COMPUTERNAME
Write-KV "OS" ([System.Environment]::OSVersion.VersionString)
Write-KV "PowerShell" $PSVersionTable.PSVersion.ToString()
Write-KV "DAVINCI_TRACKER_PYTHON" ($env:DAVINCI_TRACKER_PYTHON ?? "(not set)")
Write-KV "PYTHONPATH" ($env:PYTHONPATH ?? "(not set)")

Write-Section "DaVinci Resolve Process"
$resolveProcs = @()
foreach ($n in @("Resolve", "DaVinciResolve", "DaVinci Resolve")) {
    $resolveProcs += @(Get-Process -Name $n -ErrorAction SilentlyContinue)
}
$resolveProcs = $resolveProcs | Sort-Object Id -Unique
if ($resolveProcs.Count -eq 0) {
    Write-Host "Resolve process: NOT FOUND" -ForegroundColor Yellow
} else {
    Write-Host "Resolve process: RUNNING" -ForegroundColor Green
    $resolveProcs | Select-Object Id, ProcessName, Path, StartTime | Format-Table | Out-String | Write-Host
}

Write-Section "Resolve Scripting Modules Folder"
Write-KV "ResolveModulesPath" $ResolveModulesPath
if (-not (Test-Path $ResolveModulesPath)) {
    Write-Host "Modules folder NOT FOUND." -ForegroundColor Red
    Write-Host "Expected at: $ResolveModulesPath"
} else {
    Write-Host "Modules folder FOUND." -ForegroundColor Green
    Get-ChildItem -Path $ResolveModulesPath -Force |
        Sort-Object Name |
        Select-Object Name, Length, LastWriteTime |
        Format-Table | Out-String | Write-Host
}

Write-Section "Tracker Script"
Write-KV "TrackerScriptPath" $TrackerScriptPath
if (-not (Test-Path $TrackerScriptPath)) {
    Write-Host "Tracker script NOT FOUND at this path." -ForegroundColor Yellow
    Write-Host "If installed, it is typically at: %LOCALAPPDATA%\DaVinciTimeTracker\resolve_api.py"
} else {
    Write-Host "Tracker script FOUND." -ForegroundColor Green
}

Write-Section "Discover Python Executables"
$candidates = New-Object System.Collections.Generic.List[string]

# 1) DAVINCI_TRACKER_PYTHON env var
if ($env:DAVINCI_TRACKER_PYTHON) { [void]$candidates.Add($env:DAVINCI_TRACKER_PYTHON) }

# 2) where.exe python
try {
    $where = Invoke-Run "where.exe" @("python") 5
    if (-not $where.TimedOut -and $where.ExitCode -eq 0) {
        $where.Stdout -split "`r?`n" | ForEach-Object {
            if ($_ -match "python\.exe$") { [void]$candidates.Add($_.Trim()) }
        }
    }
} catch {}

# 3) py launcher enumeration
try {
    $py = Invoke-Run "py.exe" @("-0p") 5
    if (-not $py.TimedOut -and $py.ExitCode -eq 0) {
        $py.Stdout -split "`r?`n" | ForEach-Object {
            # Lines look like: -3.11-64 C:\...\python.exe
            if ($_ -match "([A-Za-z]:\\.*python\.exe)$") { [void]$candidates.Add($matches[1].Trim()) }
        }
    }
} catch {}

# 4) Common locations (mirrors app resolver)
$common = @(
    "C:\Program Files\Python312\python.exe",
    "C:\Program Files\Python311\python.exe",
    "C:\Program Files\Python310\python.exe",
    "C:\Program Files\Python39\python.exe",
    "C:\Program Files\Python38\python.exe",
    "C:\Program Files (x86)\Python312\python.exe",
    "C:\Program Files (x86)\Python311\python.exe",
    "C:\Program Files (x86)\Python310\python.exe",
    (Join-Path $env:LOCALAPPDATA "Programs\Python\Python312\python.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Python\Python311\python.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Python\Python310\python.exe"),
    (Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\python.exe"),
    (Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\python3.exe")
)
$common | ForEach-Object { [void]$candidates.Add($_) }

$pythonExes = Get-ExistingUniquePaths $candidates.ToArray()
if ($pythonExes.Count -eq 0) {
    Write-Host "No python.exe candidates found." -ForegroundColor Red
    exit 1
}

Write-Host "Found python executables:" -ForegroundColor Green
$pythonExes | ForEach-Object { Write-Host "  - $_" }

Write-Section "Per-Python Diagnostics"
foreach ($pyExe in $pythonExes) {
    Write-Host ""
    Write-Host ">>> Python: $pyExe" -ForegroundColor Magenta
    if ($pyExe -like "*\Microsoft\WindowsApps\python.exe") {
        Write-Host "NOTE: This is the WindowsApps alias/shim. It often points to Microsoft Store Python and may not be suitable." -ForegroundColor Yellow
    }

    # Probe: does this python accept args (vs dropping into interactive REPL)?
    $ver = Invoke-Run $pyExe @("--version") 7
    if ($ver.TimedOut) {
        Write-Host "ExitCode=null TimedOut=True" -ForegroundColor Yellow
        Write-Host "STDERR: (timed out running --version; likely an App Execution Alias / shim)" -ForegroundColor Yellow
        continue
    }
    $verText = (($ver.Stdout + $ver.Stderr).Trim())
    Write-Host ("--version: ExitCode={0} -> {1}" -f ($ver.ExitCode ?? "null"), ($verText ?? "(empty)"))

    # Basic info
    $basicCode = @"
import sys, platform, site
print('sys.executable=' + sys.executable)
print('sys.version=' + sys.version.replace('\n',' '))
print('arch=' + platform.architecture()[0])
print('maxsize=' + str(sys.maxsize))
sp = ';'.join(site.getsitepackages()) if hasattr(site,'getsitepackages') else 'n/a'
print('sitepackages=' + sp)
"@.Trim()
    $info = Invoke-Run $pyExe @("-c", $basicCode) 12
    Write-Host ("info: ExitCode={0} TimedOut={1}" -f ($info.ExitCode ?? "null"), $info.TimedOut)
    if ($info.Stdout) { Write-Host $info.Stdout.TrimEnd() }
    if ($info.Stderr) { Write-Host ("STDERR: " + $info.Stderr.TrimEnd()) -ForegroundColor Yellow }

    # sys.path (truncated)
    $sysPath = Invoke-Run $pyExe @("-c", "import sys; print('\n'.join(sys.path))") 12
    if ($sysPath.Stdout) {
        Write-Host "--- sys.path (first $MaxSysPathLines lines) ---" -ForegroundColor DarkCyan
        ($sysPath.Stdout -split "`r?`n" | Select-Object -First $MaxSysPathLines) | ForEach-Object { Write-Host $_ }
    }

    # Import test
    if (Test-Path $ResolveModulesPath) {
        $modLit = ConvertTo-PyRawString $ResolveModulesPath
        $impCode = "import sys; sys.path.append($modLit); import DaVinciResolveScript as d; print('DaVinciResolveScript=' + getattr(d,'__file__','(no __file__)')); r=d.scriptapp('Resolve'); print('scriptapp=' + ('OK' if r else 'NULL'))"
        $imp = Invoke-Run $pyExe @("-c", $impCode) 20
        Write-Host "--- import/connect test ---" -ForegroundColor DarkCyan
        Write-Host ("ExitCode={0} TimedOut={1}" -f ($imp.ExitCode ?? "null"), $imp.TimedOut)
        if ($imp.Stdout) { Write-Host $imp.Stdout.TrimEnd() }
        if ($imp.Stderr) { Write-Host ("STDERR: " + $imp.Stderr.TrimEnd()) -ForegroundColor Yellow }
    } else {
        Write-Host "Skipping import/connect test (Modules folder missing)." -ForegroundColor Yellow
    }

    # Run tracker script (if present)
    if (Test-Path $TrackerScriptPath) {
        $run = Invoke-Run $pyExe @($TrackerScriptPath) 20
        Write-Host "--- run tracker resolve_api.py ---" -ForegroundColor DarkCyan
        Write-Host ("ExitCode={0} TimedOut={1}" -f ($run.ExitCode ?? "null"), $run.TimedOut)
        if ($run.Stdout) { Write-Host ("STDOUT: " + $run.Stdout.TrimEnd()) }
        if ($run.Stderr) { Write-Host ("STDERR: " + $run.Stderr.TrimEnd()) -ForegroundColor Yellow }
    }
}

Write-Section "What to Send Back"
Write-Host "Paste ALL output from this script into chat." -ForegroundColor Green
Write-Host "If at least one Python shows scriptapp=OK, we can pin the tracker to that Python by setting DAVINCI_TRACKER_PYTHON." -ForegroundColor Green

