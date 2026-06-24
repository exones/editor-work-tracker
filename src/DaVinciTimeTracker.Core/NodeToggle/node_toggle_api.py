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
def _call(obj, method_name, *args):
    """Safely call obj.method_name(*args), returning None if the method is missing or not callable."""
    fn = getattr(obj, method_name, None)
    if not callable(fn):
        return None
    try:
        return fn(*args)
    except Exception as e:
        _dbg(f"[node_toggle_api] _call({method_name}) error: {e}")
        return None


def get_graph(resolve, level: str):
    ll = level.lower()
    try:
        pm      = _call(resolve, 'GetProjectManager')
        project = _call(pm, 'GetCurrentProject') if pm else None
        if not project:
            return None, "no_project"
        timeline = _call(project, 'GetCurrentTimeline')
        if not timeline:
            return None, "no_timeline"

        clip_item = _call(timeline, 'GetCurrentVideoItem')

        if ll == "timeline":
            return _call(timeline, 'GetNodeGraph'), None
        elif ll == "clip":
            g = _call(clip_item, 'GetNodeGraph') if clip_item else None
            return g, (None if g is not None else "clip_graph_unavailable")
        elif ll in ("preclip", "pre-clip"):
            grp = _call(clip_item, 'GetColorGroup') if clip_item else None
            g   = _call(grp, 'GetPreClipNodeGraph') if grp else None
            return g, (None if g is not None else "preclip_graph_unavailable")
        elif ll in ("postclip", "post-clip"):
            grp = _call(clip_item, 'GetColorGroup') if clip_item else None
            g   = _call(grp, 'GetPostClipNodeGraph') if grp else None
            return g, (None if g is not None else "postclip_graph_unavailable")
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


# ── Key injection helpers (Windows only, ctypes built-in) ─────────────────────

def _parse_vk_dict(d: dict):
    """Extract vk, optional mod, optional mod2 from {"vk": N, "mod": N, "mod2": N}."""
    vk   = d.get("vk")   if d else None
    mod  = d.get("mod")  if d else None
    mod2 = d.get("mod2") if d else None
    return vk, mod, mod2


_sendinput_defined = False
_INPUT_type        = None

def _ensure_sendinput_types():
    import ctypes
    global _sendinput_defined, _INPUT_type
    if _sendinput_defined:
        return
    class KEYBDINPUT(ctypes.Structure):
        _fields_ = [
            ("wVk",         ctypes.c_ushort),
            ("wScan",       ctypes.c_ushort),
            ("dwFlags",     ctypes.c_ulong),
            ("time",        ctypes.c_ulong),
            ("dwExtraInfo", ctypes.POINTER(ctypes.c_ulong)),
        ]
    class InputUnion(ctypes.Union):
        _fields_ = [("ki", KEYBDINPUT), ("_pad", ctypes.c_byte * 28)]
    class INPUT(ctypes.Structure):
        _fields_ = [("type", ctypes.c_ulong), ("u", InputUnion)]
    _INPUT_type = INPUT
    _sendinput_defined = True


def _press_key(user32, vk: int, mod=None, mod2=None):
    """
    Inject a single key (with up to two modifiers) using SendInput.
    SendInput is the modern replacement for keybd_event and is more
    reliably processed by applications like DaVinci Resolve.
    """
    import ctypes

    INPUT_KEYBOARD  = 1
    KEYEVENTF_KEYUP = 0x0002

    _ensure_sendinput_types()
    INPUT = _INPUT_type

    def make_key(vk_code, flags=0):
        inp = INPUT()
        inp.type         = INPUT_KEYBOARD
        inp.u.ki.wVk     = vk_code
        # Add hardware scan code — some apps (DaVinci) need it alongside the VK
        inp.u.ki.wScan   = ctypes.windll.user32.MapVirtualKeyW(vk_code, 0)
        inp.u.ki.dwFlags = flags
        return inp

    mods = [m for m in (mod, mod2) if m is not None]
    _dbg(f"[node_toggle_api] SendInput: vk=0x{vk:02X}({vk}) mods={[f'0x{m:02X}' for m in mods]}")

    events = []
    for m in mods:
        events.append(make_key(m))                # mod down
    events.append(make_key(vk))                   # key down
    events.append(make_key(vk, KEYEVENTF_KEYUP))  # key up
    for m in reversed(mods):
        events.append(make_key(m, KEYEVENTF_KEYUP))  # mod up

    arr  = (INPUT * len(events))(*events)
    sent = ctypes.windll.user32.SendInput(len(events), arr, ctypes.sizeof(INPUT))
    if sent != len(events):
        _dbg(f"[node_toggle_api] SendInput: WARNING only {sent}/{len(events)} events sent")


def handle_select(resolve, node_defs: list, append_key: dict, next_key: dict):
    """
    Navigate to a specific node using a temp-node anchor technique:
      1. Append a serial node (DaVinci auto-selects it — always last index).
      2. Press Backspace — DaVinci deletes the temp node and lands on the last real node.
      3. Press Next Node × idx — first press wraps to node 1; (idx-1) more reach the target.

    Requires:
      - append_key: { vk, mod } for 'Add Serial Node' (default Alt+S)
      - next_key:   { vk, mod } for 'Next Node' (default Alt+Shift+')
    """
    if not node_defs:
        return {"status": "error", "message": "no_nodes"}

    nd    = node_defs[0]
    level = nd.get("level", "Timeline")
    node_id = nd.get("nodeId")
    title   = nd.get("title", "") or ""

    graph, err = get_graph(resolve, level)
    if not graph:
        return {"status": "error", "message": f"no_graph:{err}"}

    total = graph.GetNumNodes()
    if total == 0:
        return {"status": "error", "message": "empty_graph"}

    idx = find_node_index(graph, node_id, title)
    if idx is None:
        return {"status": "error", "message": f"not_found:nodeId={node_id} title={title!r}"}

    append_vk, append_mod, append_mod2 = _parse_vk_dict(append_key)
    next_vk,   next_mod,   next_mod2   = _parse_vk_dict(next_key)

    _dbg(f"[node_toggle_api] select: target node idx={idx}/{total} level={level} "
         f"appendKey={append_key} nextKey={next_key}")
    _dbg(f"[node_toggle_api] select: parsed → "
         f"append vk=0x{(append_vk or 0):02X} mod={append_mod} mod2={append_mod2}  |  "
         f"next vk=0x{(next_vk or 0):02X} mod={next_mod} mod2={next_mod2}")

    if not append_vk or not next_vk:
        _dbg("[node_toggle_api] select: ERROR — missing VK codes, check shortcut config")
        return {"status": "error", "message": "missing_key_config"}

    try:
        import ctypes
        import time

        if not hasattr(ctypes, 'windll'):
            return {"status": "error", "message": "windows_only"}

        user32 = ctypes.windll.user32
        VK_BACK    = 0x08  # Backspace
        VK_CONTROL = 0x11
        VK_MENU    = 0x12  # Alt
        VK_SHIFT   = 0x10
        VK_LWIN    = 0x5B
        KEYEVENTF_KEYUP = 0x0002

        # Keystrokes go to whatever window currently has focus.
        # If DaVinci is not in focus when the hotkey fires, the injected keys
        # will go to the wrong window — but that's the caller's concern.
        # The C# side can guard with IsDaVinciResolveInFocus() before sending.

        # ── Release held modifiers, inject, then restore ─────────────────────────────
        # When triggered via a hotkey (e.g. Ctrl+Alt+D), those keys are physically
        # held. We release them so injected keys (e.g. Alt+S) don't combine with
        # them and fire other hotkeys. After injection we restore them so the user
        # can press the next hotkey (e.g. Ctrl+Alt+H) without having to physically
        # release and re-press the modifier keys.
        held_mods = [vk for vk in (VK_CONTROL, VK_MENU, VK_SHIFT, VK_LWIN)
                     if user32.GetAsyncKeyState(vk) & 0x8000]
        if held_mods:
            _dbg(f"[node_toggle_api] select: releasing held modifiers {[f'0x{v:02X}' for v in held_mods]}")
            for vk in held_mods:
                user32.keybd_event(vk, 0, KEYEVENTF_KEYUP, 0)
        time.sleep(0.03)  # let modifier releases settle

        BACKSPACE_DELAY = 0.05  # 50ms after backspace — node tree rebuild takes time

        # Step 1: Append temp serial node → DaVinci auto-selects it (always last)
        _dbg(f"[node_toggle_api] select: step 1 — append node vk=0x{append_vk:02X} mod={append_mod} mod2={append_mod2}")
        _press_key(user32, append_vk, append_mod, append_mod2)
        _dbg(f"[node_toggle_api] select: after step 1 — graph should now have {total+1} nodes")

        # Step 2: Backspace → delete temp node → DaVinci lands on last real node (total)
        _dbg(f"[node_toggle_api] select: step 2 — backspace vk=0x08")
        _press_key(user32, VK_BACK)
        time.sleep(BACKSPACE_DELAY)  # wait for node tree to settle after deletion
        _dbg(f"[node_toggle_api] select: after step 2 — should be on node {total} now")

        # Step 3: Next × idx → total→(wrap)→1→…→idx
        _dbg(f"[node_toggle_api] select: step 3 — {idx} × Next Node vk=0x{next_vk:02X} mod={next_mod} mod2={next_mod2}")
        for i in range(idx):
            _dbg(f"[node_toggle_api] select:   next press {i+1}/{idx}")
            _press_key(user32, next_vk, next_mod, next_mod2)

        _dbg(f"[node_toggle_api] select: DONE — navigated to node {idx}/{total} ({title or node_id!r})")

        # Restore modifiers that were held before we started, so the user can
        # immediately trigger the next hotkey without releasing and re-pressing them.
        if held_mods:
            _dbg(f"[node_toggle_api] select: restoring held modifiers {[f'0x{v:02X}' for v in held_mods]}")
            for vk in held_mods:
                user32.keybd_event(vk, 0, 0, 0)  # press

        return {"status": "ok", "nodeIndex": idx, "totalNodes": total}

    except Exception as e:
        _dbg(f"[node_toggle_api] select error: {e}")
        return {"status": "error", "message": str(e)}


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
        elif action == "select":
            append_key = cmd.get("appendNodeKey", {})
            next_key   = cmd.get("nextNodeKey",   {})
            _respond(handle_select(resolve, node_defs, append_key, next_key))
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
