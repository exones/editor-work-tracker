import sys
import os
import json

# Fix encoding for Windows before any output
if sys.platform == 'win32':
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')


def _dbg(*args):
    """Write to stderr — surfaced in logs on error."""
    print(*args, file=sys.stderr, flush=True)


_dbg(f"[node_toggle_api] Python: {sys.executable} ({sys.version.split()[0]})")

# Add DaVinci Resolve directory to DLL search path (Windows)
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


def get_graph(timeline, clip_item, level: str):
    """
    Return (graph, level_name) for the requested level.
    DaVinci Resolve API (index-based):
      graph.GetNumNodes()         -> int
      graph.GetNodeLabel(n)       -> str  (1-based)
      graph.SetNodeEnabled(n, bool)

    Levels:
      Timeline  — timeline.GetNodeGraph()
      Clip      — timelineItem.GetNodeGraph()
      PreClip   — timelineItem.GetColorGroup().GetPreClipNodeGraph()
      PostClip  — timelineItem.GetColorGroup().GetPostClipNodeGraph()
    """
    level_lower = level.lower()
    try:
        if level_lower == "timeline":
            if hasattr(timeline, 'GetNodeGraph'):
                return timeline.GetNodeGraph(), "Timeline"

        elif level_lower == "clip":
            if clip_item and hasattr(clip_item, 'GetNodeGraph'):
                return clip_item.GetNodeGraph(), "Clip"

        elif level_lower in ("preclip", "pre-clip", "pre_clip"):
            if clip_item and hasattr(clip_item, 'GetColorGroup'):
                group = clip_item.GetColorGroup()
                if group and hasattr(group, 'GetPreClipNodeGraph'):
                    return group.GetPreClipNodeGraph(), "PreClip"

        elif level_lower in ("postclip", "post-clip", "post_clip"):
            if clip_item and hasattr(clip_item, 'GetColorGroup'):
                group = clip_item.GetColorGroup()
                if group and hasattr(group, 'GetPostClipNodeGraph'):
                    return group.GetPostClipNodeGraph(), "PostClip"

    except Exception as e:
        _dbg(f"[node_toggle_api] WARN: get_graph({level}) failed: {e}")
    return None, level


def collect_nodes_from_graph(graph, level_name: str) -> list:
    """Enumerate all nodes in a graph and return their index, label and tools."""
    results = []
    if not graph:
        return results
    try:
        num = graph.GetNumNodes()
        for i in range(1, num + 1):
            label = ""
            try:
                label = graph.GetNodeLabel(i) or ""
            except Exception:
                pass
            tools = []
            try:
                tools = graph.GetToolsInNode(i) or []
            except Exception:
                pass
            # Skip completely unlabelled utility nodes (layer mixers etc.)
            results.append({
                "nodeId": i,        # 1-based stable index
                "title": label,
                "level": level_name,
                "enabled": True,    # GetNodeEnabled not available in this API
                "tools": tools
            })
    except Exception as e:
        _dbg(f"[node_toggle_api] WARN: collect_nodes_from_graph({level_name}) failed: {e}")
    return results


def find_node_index(graph, node_id, title: str):
    """
    Return the 1-based node index for the given identifier.
    nodeId is used first (direct index), title falls back to label match.
    """
    if not graph:
        return None
    try:
        num = graph.GetNumNodes()
        if node_id is not None and 1 <= node_id <= num:
            return node_id
        if title:
            for i in range(1, num + 1):
                label = graph.GetNodeLabel(i) or ""
                if label.lower() == title.lower():
                    return i
    except Exception as e:
        _dbg(f"[node_toggle_api] WARN: find_node_index failed: {e}")
    return None


# ── Bootstrap ─────────────────────────────────────────────────────────────────

try:
    if len(sys.argv) < 2:
        print("ERROR:no_args")
        sys.exit(2)

    payload = json.loads(sys.argv[1])
    node_defs = payload.get("nodes", [])
    action = payload.get("state", "toggle")  # "on" | "off" | "toggle" | "list"

    _dbg(f"[node_toggle_api] action={action}, nodes={len(node_defs)}")

    modules_path = get_modules_path()
    if not os.path.isdir(modules_path):
        print(f"ERROR:modules_not_found:{modules_path}")
        sys.exit(2)

    sys.path.append(modules_path)
    import DaVinciResolveScript as dvr_script

    resolve = dvr_script.scriptapp("Resolve")
    if not resolve:
        print("ERROR:resolve_not_running")
        sys.exit(2)

    pm = resolve.GetProjectManager()
    project = pm.GetCurrentProject() if pm else None
    if not project:
        print("ERROR:no_project")
        sys.exit(2)

    timeline = project.GetCurrentTimeline()
    if not timeline:
        print("ERROR:no_timeline")
        sys.exit(2)

    clip_item = None
    try:
        if hasattr(timeline, 'GetCurrentVideoItem'):
            clip_item = timeline.GetCurrentVideoItem()
    except Exception as e:
        _dbg(f"[node_toggle_api] WARN: GetCurrentVideoItem failed: {e}")

    # ── LIST action ────────────────────────────────────────────────────────────
    if action == "list":
        all_nodes = []
        # All four levels via ColorGroup API for Pre/Post-clip
        for level_name in ("Timeline", "Clip", "PreClip", "PostClip"):
            graph, ln = get_graph(timeline, clip_item, level_name)
            all_nodes.extend(collect_nodes_from_graph(graph, ln))
        _dbg(f"[node_toggle_api] list: {len(all_nodes)} nodes found")
        print(f"NODES:{json.dumps(all_nodes)}")
        sys.exit(0)

    # ── TOGGLE / ON / OFF action ───────────────────────────────────────────────
    results = []
    new_state = None  # track direction for the whole group

    for node_def in node_defs:
        node_id = node_def.get("nodeId")
        title   = node_def.get("title", "") or ""
        level   = node_def.get("level", "Timeline")

        if level.lower() in ("clip", "preclip", "postclip") and not clip_item:
            _dbg(f"[node_toggle_api] WARN: no current clip for level={level}")
            results.append({"nodeId": node_id, "title": title, "status": "skipped_no_clip"})
            continue

        graph, _ = get_graph(timeline, clip_item, level)
        if not graph:
            _dbg(f"[node_toggle_api] WARN: no graph for level={level}")
            results.append({"nodeId": node_id, "title": title, "status": "skipped_no_graph"})
            continue

        idx = find_node_index(graph, node_id, title)
        if idx is None:
            _dbg(f"[node_toggle_api] WARN: not found — nodeId={node_id} title={title!r}")
            results.append({"nodeId": node_id, "title": title, "status": "not_found"})
            continue

        actual_label = graph.GetNodeLabel(idx) or title or f"#{idx}"
        _dbg(f"[node_toggle_api] found '{actual_label}' at index {idx} in {level}")

        # Determine target enabled state
        # "on"/"off" are explicit; "toggle" relies on the caller's tracked state
        # since GetNodeEnabled is not available in this API version.
        if action == "on":
            target = True
        elif action == "off":
            target = False
        else:  # "toggle" — caller should prefer sending "on"/"off" explicitly
            # Default: first press disables, second enables
            target = False if new_state is None else (not new_state)

        if new_state is None:
            new_state = target

        try:
            graph.SetNodeEnabled(idx, target)
            _dbg(f"[node_toggle_api] SetNodeEnabled({idx}, {target}) OK")
            results.append({"nodeId": node_id, "title": title, "status": "ok", "enabled": target})
        except Exception as e:
            _dbg(f"[node_toggle_api] ERROR: SetNodeEnabled({idx}, {target}): {e}")
            results.append({"nodeId": node_id, "title": title, "status": "error", "error": str(e)})

    if not results:
        print("ERROR:no_nodes_processed")
        sys.exit(2)

    any_ok = any(r["status"] == "ok" for r in results)
    skipped = [r for r in results if r["status"] != "ok"]
    if skipped:
        _dbg(f"[node_toggle_api] {len(skipped)} skipped/failed: {skipped}")

    if any_ok:
        final = new_state if new_state is not None else False
        print(f"OK:{'enabled' if final else 'disabled'}")
        sys.exit(0)
    else:
        print(f"ERROR:all_nodes_failed:{[r['status'] for r in results]}")
        sys.exit(2)

except SystemError as e:
    err_str = str(e)
    print(f"ERROR:{e}")
    if 'fusionscript' in err_str:
        _dbg("[node_toggle_api] HINT: fusionscript IPC failed — see resolve_api.py hints")
    sys.exit(2)

except Exception as e:
    print(f"ERROR:{e}")
    sys.exit(2)
