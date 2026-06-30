"""
nas_sqlite_latency.py
Simulates DaVinci Resolve's SQLite access pattern against a NAS path.

Usage:
    python nas_sqlite_latency.py [NAS_PATH]

Default NAS path: F:\\_resolve_latency_test
Press Ctrl+C to stop. Results print every 10 seconds.
"""

import sys, os, sqlite3, time, statistics, shutil

NAS_PATH = sys.argv[1] if len(sys.argv) > 1 else r"F:\_resolve_latency_test"
DB_PATH  = os.path.join(NAS_PATH, "resolve_sim.db")

WARN_MS  = 50    # noticeably slow for Resolve
ALERT_MS = 500   # Resolve stalls visibly

# ── Setup ──────────────────────────────────────────────────────────────────────

def setup():
    os.makedirs(NAS_PATH, exist_ok=True)
    con = sqlite3.connect(DB_PATH)
    con.execute("PRAGMA journal_mode=WAL")
    con.execute("PRAGMA synchronous=NORMAL")
    con.execute("CREATE TABLE IF NOT EXISTS nodes (id INTEGER PRIMARY KEY, name TEXT, enabled INTEGER, data BLOB)")
    con.execute("CREATE TABLE IF NOT EXISTS stills (id INTEGER PRIMARY KEY, label TEXT, thumbnail BLOB, ts INTEGER)")
    for i in range(50):
        con.execute("INSERT OR IGNORE INTO nodes VALUES (?,?,?,?)", (i, f"Node_{i}", 1, bytes(256)))
    for i in range(10):
        con.execute("INSERT OR IGNORE INTO stills VALUES (?,?,?,?)", (i, f"Still_{i}", bytes(20480), int(time.time())))
    con.commit()
    con.close()
    print("DB: " + DB_PATH)
    print("Simulating Resolve access pattern. Ctrl+C to stop.\n")
    sys.stdout.flush()

# ── Measurements ───────────────────────────────────────────────────────────────

samples = {"write_node": [], "write_commit": [], "read_all": [], "read_stills": []}

def measure(key, fn):
    t0 = time.perf_counter()
    fn()
    ms = (time.perf_counter() - t0) * 1000
    samples[key].append(ms)

def op_write_node():
    def run():
        con = sqlite3.connect(DB_PATH, timeout=15)
        con.execute("PRAGMA journal_mode=WAL")
        con.execute("UPDATE nodes SET enabled=((enabled+1)%2) WHERE id=?", (int(time.time()) % 50,))
        con.commit()
        con.close()
    measure("write_node", run)

def op_write_commit():
    def run():
        con = sqlite3.connect(DB_PATH, timeout=15)
        con.execute("PRAGMA journal_mode=WAL")
        with con:
            for i in range(50):
                con.execute("UPDATE nodes SET enabled=((enabled+1)%2), data=? WHERE id=?", (bytes(256), i))
        con.close()
    measure("write_commit", run)

def op_read_all():
    def run():
        con = sqlite3.connect(DB_PATH, timeout=15)
        list(con.execute("SELECT * FROM nodes"))
        con.close()
    measure("read_all", run)

def op_read_stills():
    def run():
        con = sqlite3.connect(DB_PATH, timeout=15)
        list(con.execute("SELECT id, label, ts FROM stills"))
        con.close()
    measure("read_stills", run)

# ── Stats ──────────────────────────────────────────────────────────────────────

def tag(ms):
    if ms >= ALERT_MS: return "!!! "
    if ms >= WARN_MS:  return " >> "
    return "    "

def print_stats():
    print("-" * 65)
    print(f"  {'Operation':<22} {'n':>4}  {'min':>7}  {'avg':>7}  {'p95':>7}  {'max':>7}")
    print("-" * 65)
    worst_p95 = 0
    worst_key = None
    for key, data in samples.items():
        if not data:
            continue
        s = sorted(data)
        p95 = s[int(len(s) * 0.95)]
        avg = statistics.mean(data)
        print(f"{tag(p95)} {key:<22} {len(data):>4}  {min(data):>6.1f}ms  {avg:>6.1f}ms  {p95:>6.1f}ms  {max(data):>6.1f}ms")
        if p95 > worst_p95:
            worst_p95 = p95
            worst_key = key
    print("-" * 65)
    if worst_key:
        if worst_p95 >= ALERT_MS:
            print(f"  SEVERE: {worst_key} p95={worst_p95:.0f}ms -- Resolve will stall noticeably")
        elif worst_p95 >= WARN_MS:
            print(f"  SLOW:   {worst_key} p95={worst_p95:.0f}ms -- Resolve will feel sluggish")
        else:
            print(f"  OK:     NAS latency looks healthy (p95 < {WARN_MS}ms)")
    print()
    sys.stdout.flush()

# ── Main ───────────────────────────────────────────────────────────────────────

def cleanup():
    try:
        shutil.rmtree(NAS_PATH)
        print("Cleaned up " + NAS_PATH)
    except Exception as e:
        print("Cleanup failed: " + str(e))

if __name__ == "__main__":
    setup()
    last_print = time.time()
    cycle = 0
    try:
        while True:
            op_write_node();  time.sleep(0.5)
            op_read_stills(); time.sleep(0.5)
            op_write_node();  time.sleep(0.5)
            op_read_all();    time.sleep(0.5)
            cycle += 1
            if cycle % 5 == 0:
                op_write_commit()
            if time.time() - last_print >= 10:
                print_stats()
                last_print = time.time()
    except KeyboardInterrupt:
        print_stats()
        cleanup()
