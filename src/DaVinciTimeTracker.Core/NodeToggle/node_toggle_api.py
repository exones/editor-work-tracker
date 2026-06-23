"""
node_toggle_api.py — persistent stdin/stdout server for DaVinci node toggling.

The C# host starts this process once and keeps it alive.
Commands arrive as single-line JSON on stdin; responses are single-line JSON on stdout.
The DaVinci connection (fusionscript) is established once and reused.

Command envelope:  {"action": "toggle|on|off|list", "nodes": [...], "id": "optional-trace-id"}
Response envelope: {"status": "ok|error", "enabled": true|false, "nodes": [...], "message": "..."}
"""

import sys
import os
import json
import traceback

# Fix encoding for Windows
if sys.platform == 'win32':
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', line_buffering=True)
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', line_buffering=True)


def _dbg(*args):
    print(*args, file=sys.stderr, flush=True)


def _respond(obj: dict):
    """Write a single JSON response line to stdout."""
    print(json.dumps(obj), flush=True)


_dbg(f"[node_toggle_api] Python: {sys.executable} ({sys.version.split()[0]})")

# ── DLL search path (Windows, same fix as resolve_api.py) ─────────────────────
if sys.platform == 'win32' and hasattr(os, 'add_dll_directory'):
    resolve_install_dir = r"C:\Program Files\Blackmagic Design\DaVinci Resolve"
    if os.path.isdir(resolve_install_dir):
        os.add_dll_directory(resolve_install_dir)


def get_modules_path() -> str:
    if sys.platform.startswith('win') or sys.platform.startswith('cygwin'):
        return os.path.join(
            os.environ.get("PROGRAMDATA", r"C:\ProgramData"),
            "Blackmagic Design", "DaVinci Resolve",
            "Support", "Developer", "Scripting", "Modules"
        )
    elif sys.platform.startswith('darwin'):
        return "/Library/Application Support/Blackmagic Design/DaVinci Resolve/Developer/Scripting/Modules"
    else:
        return "/opt/resolve/Developer/Scripting/Modules"


# ── Bootstrap: connect to DaVinci once ────────────────────────────────────────
def bootstrap():
    modules_path = get_modules_path()
    if not os.path.isdir(modules_path):
        return None, f"modules_not_found:{modules_path}"
    sys.path.append(modules_path)

    try:
        import DaVinciResolveScript as dvr_script
        resolve = dvr_script.scriptapp("Resolve")
        if not resolve:
            return None, "resolve_not_running"
        _dbg(f"[node_toggle_api] Connected to DaVinci Resolve")
        return resolve, None
    except SystemError as e:
        return None, f"fusionscript_error:{e}"
    except Exception as e:
        return None, f"bootstrap_error:{e}"


# ── Node graph helpers (same logic as before) ─────────────────────────────────
def get_graph(resolve, level: str):
    ll = level.lower()
    try:
        pm = resolve.GetProjectManager()
        project = pm.GetCurrentProject() if pm else None
        if not project:
            return None, "no_project"
        timeline = project.GetCurrentTimeline()
        if not timeline:
            return None, "no_timeline"

        clip_item = None
        try:
            if hasattr(timeline, 'GetCurrentVideoItem'):
                clip_item = timeline.GetCurrentVideoItem()
        except Exception:
            pass

        if ll == "timeline":
            g = timeline.GetNodeGraph() if hasattr(timeline, 'GetNodeGraph') else None
            return g, None
        elif ll == "clip":
            g = clip_item.GetNodeGraph() if clip_item and hasattr(clip_item, 'GetNodeGraph') else None
            return g, None
        elif ll in ("preclip", "pre-clip"):
            if clip_item and hasattr(clip_item, 'GetColorGroup'):
                grp = clip_item.GetColorGroup()
                g = grp.GetPreClipNodeGraph() if grp and hasattr(grp, 'GetPreClipNodeGraph') else None
                return g, None
        elif ll in ("postclip", "post-clip"):
            if clip_item and hasattr(clip_item, 'GetColorGroup'):
                grp = clip_item.GetColorGroup()
                g = grp.GetPostClipNodeGraph() if grp and hasattr(grp, 'GetPostClipNodeGraph') else None
                return g, None
    except Exception as e:
        return None, str(e)
    return None, f"unsupported_level:{level}"


def collect_nodes(graph, level_name: str) -> list:
    results = []
    if not graph:
        return results
    try:
        n = graph.GetNumNodes()
        for i in range(1, n + 1):
            label = ""
            tools = []
            try:
                label = graph.GetNodeLabel(i) or ""
            except Exception:
                pass
            try:
                tools = graph.GetToolsInNode(i) or []
            except Exception:
                pass
            results.append({"nodeId": i, "title": label, "level": level_name,
                             "enabled": True, "tools": tools})
    except Exception as e:
        _dbg(f"[node_toggle_api] collect_nodes error: {e}")
    return results


def find_node_index(graph, node_id, title: str):
    if not graph:
        return None
    try:
        n = graph.GetNumNodes()
        if node_id is not None and 1 <= node_id <= n:
            return node_id
        if title:
            for i in range(1, n + 1):
                try:
                    lbl = graph.GetNodeLabel(i) or ""
                    if lbl.lower() == title.lower():
                        return i
                except Exception:
                    pass
    except Exception as e:
        _dbg(f"[node_toggle_api] find_node_index error: {e}")
    return None


# ── Command handlers ───────────────────────────────────────────────────────────
def handle_list(resolve):
    all_nodes = []
    for level_name in ("Timeline", "Clip", "PreClip", "PostClip"):
        graph, err = get_graph(resolve, level_name)
        if graph:
            all_nodes.extend(collect_nodes(graph, level_name))
    return {"status": "ok", "nodes": all_nodes}


def handle_toggle(resolve, node_defs: list, action: str):
    results = []
    new_state = None

    for nd in node_defs:
        node_id = nd.get("nodeId")
        title   = nd.get("title", "") or ""
        level   = nd.get("level", "Timeline")

        graph, err = get_graph(resolve, level)
        if not graph:
            _dbg(f"[node_toggle_api] no graph for level={level}: {err}")
            results.append({"nodeId": node_id, "title": title, "status": f"no_graph:{err}"})
            continue

        idx = find_node_index(graph, node_id, title)
        if idx is None:
            _dbg(f"[node_toggle_api] not found: nodeId={node_id} title={title!r}")
            results.append({"nodeId": node_id, "title": title, "status": "not_found"})
            continue

        if action == "on":
            target = True
        elif action == "off":
            target = False
        else:
            target = False if new_state is None else (not new_state)

        if new_state is None:
            new_state = target

        try:
            graph.SetNodeEnabled(idx, target)
            _dbg(f"[node_toggle_api] SetNodeEnabled({idx}, {target}) for '{title or node_id}'")
            results.append({"nodeId": node_id, "title": title, "status": "ok", "enabled": target})
        except Exception as e:
            _dbg(f"[node_toggle_api] SetNodeEnabled error: {e}")
            results.append({"nodeId": node_id, "title": title, "status": "error", "error": str(e)})

    any_ok = any(r["status"] == "ok" for r in results)
    if any_ok:
        return {"status": "ok", "enabled": new_state if new_state is not None else False, "results": results}
    return {"status": "error", "message": "all_nodes_failed", "results": results}


# ── Main loop ─────────────────────────────────────────────────────────────────
resolve, bootstrap_err = bootstrap()
if bootstrap_err:
    # Signal startup error and keep reading so C# can re-send after DaVinci starts
    _dbg(f"[node_toggle_api] Bootstrap failed: {bootstrap_err}")
    _respond({"status": "error", "message": f"bootstrap:{bootstrap_err}", "ready": False})
else:
    _respond({"status": "ok", "message": "ready", "ready": True})

_dbg("[node_toggle_api] Entering command loop (reading stdin)...")

for line in sys.stdin:
    line = line.strip()
    if not line:
        continue
    try:
        cmd = json.loads(line)
        action = cmd.get("action", "toggle")
        node_defs = cmd.get("nodes", [])

        # Re-connect if DaVinci was restarted
        if resolve is None or action == "reconnect":
            resolve, err = bootstrap()
            if err:
                _respond({"status": "error", "message": f"reconnect_failed:{err}", "ready": False})
                continue
            _respond({"status": "ok", "message": "reconnected", "ready": True})
            continue

        if action == "list":
            _respond(handle_list(resolve))
        elif action in ("on", "off", "toggle"):
            _respond(handle_toggle(resolve, node_defs, action))
        elif action == "get_page":
            # GetCurrentPage() only works reliably from a persistent connection (returns None in fresh processes)
            page = resolve.GetCurrentPage()
            _respond({"status": "ok", "page": page})
        elif action == "diagnose":
            # Full diagnostic snapshot — used by the in-app Troubleshooter (heavier than get_page)
            import struct as _struct
            try:
                product_name = resolve.GetProductName()
                version_string = resolve.GetVersionString()
                is_studio = "Studio" in (product_name or "")
                pm = resolve.GetProjectManager()
                project = pm.GetCurrentProject() if pm else None
                project_open = project is not None
                current_page = resolve.GetCurrentPage()
                _respond({
                    "status": "ok",
                    "product_name": product_name,
                    "is_studio": is_studio,
                    "version_string": version_string,
                    "scriptapp_ok": True,
                    "project_open": project_open,
                    "current_page": current_page,
                    "python_executable": sys.executable,
                    "python_version": f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}",
                    "is_64bit": _struct.calcsize("P") == 8,
                })
            except Exception as e:
                _respond({"status": "error", "message": str(e)})
        elif action == "ping":
            _respond({"status": "ok", "message": "pong"})
        else:
            _respond({"status": "error", "message": f"unknown_action:{action}"})

    except json.JSONDecodeError as e:
        _respond({"status": "error", "message": f"invalid_json:{e}"})
    except Exception as e:
        _dbg(f"[node_toggle_api] Unhandled error: {traceback.format_exc()}")
        _respond({"status": "error", "message": str(e)})

_dbg("[node_toggle_api] stdin closed, exiting.")
