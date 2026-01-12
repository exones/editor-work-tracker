import sys
import os

# Fix encoding for Windows
if sys.platform == 'win32':
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

try:
    # Add DaVinci Resolve API to path
    resolve_script_api_path = os.path.join(
        os.environ.get("PROGRAMDATA", "C:\\ProgramData"),
        "Blackmagic Design",
        "DaVinci Resolve",
        "Support",
        "Developer",
        "Scripting",
        "Modules"
    )
    sys.path.append(resolve_script_api_path)

    import DaVinciResolveScript as dvr_script

    resolve = dvr_script.scriptapp("Resolve")
    if resolve:
        pm = resolve.GetProjectManager()
        if pm:
            project = pm.GetCurrentProject()
            if project:
                project_name = project.GetName()
                print(project_name)
                sys.exit(0)

    print("NO_PROJECT")
    sys.exit(1)
except Exception as e:
    print(f"ERROR:{e}")
    sys.exit(2)
