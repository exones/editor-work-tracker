import sys
import os

# Fix encoding for Windows before any output
if sys.platform == 'win32':
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')


def _dbg(*args):
    """Write to stderr — only surfaced in logs when there is an error."""
    print(*args, file=sys.stderr, flush=True)


# Always emit a diagnostic header so logs show which Python is being used
_dbg(f"[resolve_api] Python:            {sys.executable}")
_dbg(f"[resolve_api] Version:           {sys.version.split()[0]}")
_dbg(f"[resolve_api] RESOLVE_SCRIPT_LIB:{os.environ.get('RESOLVE_SCRIPT_LIB', '(not set)')}")
_dbg(f"[resolve_api] PYTHONPATH:        {os.environ.get('PYTHONPATH', '(not set)')}")

try:
    # Python 3.8+ restricts DLL search paths (LOAD_LIBRARY_SEARCH_DEFAULT_DIRS).
    # Some Python.org builds fail with "initialization of fusionscript failed" because
    # they can't find fusionscript.dll's sibling DLLs in the DaVinci directory.
    # Microsoft Store / PyManager builds set this up automatically.
    if sys.platform == 'win32' and hasattr(os, 'add_dll_directory'):
        resolve_install_dir = r"C:\Program Files\Blackmagic Design\DaVinci Resolve"
        if os.path.isdir(resolve_install_dir):
            os.add_dll_directory(resolve_install_dir)

    # Verify fusionscript.dll is reachable before trying to import
    resolve_lib = os.environ.get('RESOLVE_SCRIPT_LIB', '')
    if resolve_lib and not os.path.exists(resolve_lib):
        print(f"ERROR:fusionscript.dll not found: RESOLVE_SCRIPT_LIB={resolve_lib}")
        sys.exit(2)

    # Add DaVinci scripting modules to path
    resolve_script_api_path = os.path.join(
        os.environ.get("PROGRAMDATA", "C:\\ProgramData"),
        "Blackmagic Design",
        "DaVinci Resolve",
        "Support",
        "Developer",
        "Scripting",
        "Modules"
    )

    if not os.path.isdir(resolve_script_api_path):
        print(f"ERROR:DaVinci scripting modules not found at {resolve_script_api_path}")
        sys.exit(2)

    _dbg(f"[resolve_api] Modules path:      {resolve_script_api_path}")
    sys.path.append(resolve_script_api_path)

    import DaVinciResolveScript as dvr_script

    resolve = dvr_script.scriptapp("Resolve")
    if resolve:
        pm = resolve.GetProjectManager()
        if pm:
            project = pm.GetCurrentProject()
            if project:
                project_name = project.GetName()
                # GetCurrentPage() returns None when DaVinci is minimised or not in foreground.
                # Only include page in output when we have a real value.
                page = resolve.GetCurrentPage()
                if page:
                    print(f"{project_name}|{page}")
                else:
                    print(project_name)
                sys.exit(0)

    print("NO_PROJECT")
    sys.exit(1)

except SystemError as e:
    # fusionscript.dll loaded into the process but PyInit_fusionscript() returned NULL.
    # This happens when DaVinci's IPC handshake fails, which can be caused by:
    #   - Python build incompatibility (Python.org builds vs Windows Store/PyManager)
    #   - External scripting disabled (Preferences → General → External scripting using)
    #   - DaVinci Resolve Free (not Studio)
    err_str = str(e)
    print(f"ERROR:{e}")
    if 'fusionscript' in err_str:
        _dbg(f"[resolve_api] HINT: fusionscript IPC failed — Python={sys.executable}")
        _dbg(f"[resolve_api] HINT: If using Python.org python.exe, try PyManager or Windows Store Python")
        _dbg(f"[resolve_api] HINT: Set DAVINCI_TRACKER_PYTHON=C:\\Program Files\\PyManager\\python.exe")
        _dbg(f"[resolve_api] HINT: Or: Preferences → General → External scripting using → 'Local'")
    sys.exit(2)

except Exception as e:
    print(f"ERROR:{e}")
    sys.exit(2)
