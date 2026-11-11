"""
Replay viewer server: serves tools/viewer, pulls new study files from every Quest plugged in by USB, and lists all runs
in StudyData for the viewer's library. Standard library only.

    python tools/viewer_server.py            (options: --port 8000 --out StudyData --no-pull)
then open http://localhost:8000. Leave it running: plug a headset in after a test and the new run appears by itself.

- every 3 s: `adb devices`; for each authorised headset, every file *.csv / *.json in the app's data folder whose size or
  time changed since the last pull is copied to StudyData/<yyyy-mm-dd>/<serial>/ (same layout as tools/pull_data.sh).
  Never deletes anything on the headset. What was pulled is remembered in StudyData/pulled_index.json.
- GET /api/runs   -> every run (one per run name, newest copy wins, like analyze_runs.py) with its summary values
- GET /api/status -> connected headsets + the last pull messages
- GET /data/<path under StudyData> -> a .csv/.json file
Listens on 127.0.0.1 only (study data stays on this laptop).
"""
import argparse
import csv
import datetime
import functools
import http.server
import json
import os
import re
import shutil
import subprocess
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, ".."))
REMOTE_DIR = "/sdcard/Android/data/com.xmum.fyp.detective/files"
UNITY_ADB = r"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
SAFE_NAME = re.compile(r"^[A-Za-z0-9_\-][A-Za-z0-9_.\-]{0,127}\.(csv|json)$")
RUN_NAME = re.compile(r"^([A-Za-z0-9\-]+)_([A-Za-z0-9\-]+)_([AB])_([XY])_(\d{8}_\d{6})$")

STATE = {"devices": {}, "messages": [], "version": 0}
LOCK = threading.Lock()


def find_adb():
    if os.environ.get("ADB"):
        return os.environ["ADB"]
    return shutil.which("adb") or UNITY_ADB


def adb(adb_path, *args, timeout=60):
    return subprocess.run([adb_path, *args], capture_output=True, text=True, timeout=timeout,
                          creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))


def note(msg):
    with LOCK:
        STATE["messages"] = (STATE["messages"] + [time.strftime("%H:%M:%S ") + msg])[-8:]
    print(msg, flush=True)


def load_index(out):
    try:
        with open(os.path.join(out, "pulled_index.json"), encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def save_index(out, index):
    path = os.path.join(out, "pulled_index.json")
    with open(path + ".tmp", "w", encoding="utf-8") as f:
        json.dump(index, f, indent=0, sort_keys=True)
    os.replace(path + ".tmp", path)


def local_sizes(out, serial):
    """name -> sizes of copies already in StudyData/*/<serial>/ (pulled before this server existed)."""
    sizes = {}
    for folder, _, files in os.walk(out):
        if os.path.basename(folder) == serial:
            for f in files:
                sizes.setdefault(f, set()).add(os.path.getsize(os.path.join(folder, f)))
    return sizes


def pull_device(adb_path, out, serial, index):
    r = adb(adb_path, "-s", serial, "shell", f"stat -c '%s|%Y|%n' {REMOTE_DIR}/*.csv {REMOTE_DIR}/*.json")
    remote = {}
    for line in r.stdout.splitlines():
        parts = line.strip().split("|", 2)
        if len(parts) == 3 and parts[0].isdigit():
            name = parts[2].rsplit("/", 1)[-1]
            if SAFE_NAME.match(name):
                remote[name] = f"{parts[0]}|{parts[1]}"
    known = index.setdefault(serial, {})
    todo = [n for n, sig in remote.items() if known.get(n) != sig]
    if not todo:
        return 0
    have = local_sizes(out, serial) if any(n not in known for n in todo) else {}
    dest = os.path.join(out, datetime.date.today().isoformat(), serial)
    pulled = 0
    for name in sorted(todo):
        size = int(remote[name].split("|")[0])
        if name not in known and size in have.get(name, ()):
            known[name] = remote[name]   # same file already pulled by tools/pull_data.sh
            continue
        os.makedirs(dest, exist_ok=True)
        res = adb(adb_path, "-s", serial, "pull", f"{REMOTE_DIR}/{name}", os.path.join(dest, name))
        if res.returncode == 0:
            known[name] = remote[name]
            pulled += 1
        else:
            note(f"{serial}: failed to pull {name}: {res.stderr.strip()[:120]}")
    save_index(out, index)
    if pulled:
        note(f"{serial}: pulled {pulled} file(s) to {os.path.basename(out)}/{datetime.date.today().isoformat()}/{serial}")
    return pulled


def pull_loop(out):
    adb_path = find_adb()
    index = load_index(out)
    while True:
        try:
            lines = adb(adb_path, "devices", timeout=15).stdout.splitlines()[1:]
            devices = dict(l.split()[:2] for l in lines if len(l.split()) >= 2)
            with LOCK:
                STATE["devices"] = devices
            changed = 0
            for serial, st in devices.items():
                if st == "device":
                    changed += pull_device(adb_path, out, serial, index)
            if changed:
                with LOCK:
                    STATE["version"] += 1
        except FileNotFoundError:
            note(f"adb not found ({adb_path}); set ADB=... and restart. Auto-pull is off.")
            return
        except Exception as e:  # keep the loop alive (headset unplugged mid-pull etc.)
            note(f"pull error: {e}")
        time.sleep(3)


def read_summary(path):
    try:
        with open(path, newline="", encoding="utf-8-sig") as f:
            rows = list(csv.DictReader(f))
        return rows[-1] if rows else {}
    except (OSError, csv.Error):
        return {}


def list_runs(out):
    newest = {}
    for folder, _, files in os.walk(out):
        for f in files:
            if not f.endswith(".csv") or f.endswith("_summary.csv") or not RUN_NAME.match(f[:-4]):
                continue
            path = os.path.join(folder, f)
            if f not in newest or os.path.getmtime(path) > os.path.getmtime(newest[f]):
                newest[f] = path
    runs = []
    for f, path in newest.items():
        pair, pid, cond, case, stamp = RUN_NAME.match(f[:-4]).groups()
        s = read_summary(path[:-4] + "_summary.csv")
        runs.append({
            "name": f, "url": "/data/" + os.path.relpath(path, out).replace(os.sep, "/"),
            "folder": os.path.relpath(os.path.dirname(path), out).replace(os.sep, "/"),
            "pair": pair, "participant": pid, "condition": cond, "case": case, "stamp": stamp,
            "title": s.get("case_title", ""), "end": s.get("end_reason", "no summary"),
            "elapsed": s.get("elapsed_s", ""), "correct": s.get("answer_correct", ""),
            "found": s.get("clues_found", ""), "contamination": s.get("contamination_errors", ""),
            "reasoning": s.get("reasoning_errors", ""), "team": s.get("team_run", "") == "1",
        })
    # team partner = same pair/case/condition, other participant, started within 3 min
    for r in runs:
        if not r["team"]:
            continue
        t = datetime.datetime.strptime(r["stamp"], "%Y%m%d_%H%M%S")
        for o in runs:
            if (o is not r and o["team"] and o["pair"] == r["pair"] and o["case"] == r["case"]
                    and o["condition"] == r["condition"] and o["participant"] != r["participant"]
                    and abs((datetime.datetime.strptime(o["stamp"], "%Y%m%d_%H%M%S") - t).total_seconds()) < 180):
                r["partner"] = o["url"]
                break
    runs.sort(key=lambda r: r["stamp"], reverse=True)
    return runs


class Handler(http.server.SimpleHTTPRequestHandler):
    out = ""

    def log_message(self, fmt, *args):
        pass

    def end_headers(self):
        if not self.path.startswith(("/api/", "/data/")):
            self.send_header("Cache-Control", "no-cache")   # always serve the current viewer after an edit
        super().end_headers()

    def send_json(self, obj):
        body = json.dumps(obj).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        path = self.path.split("?", 1)[0]
        if path == "/api/runs":
            return self.send_json(list_runs(self.out))
        if path == "/api/status":
            with LOCK:
                return self.send_json(dict(STATE))
        if path.startswith("/data/"):
            rel = os.path.normpath(path[len("/data/"):].replace("/", os.sep))
            full = os.path.realpath(os.path.join(self.out, rel))
            if (not full.startswith(os.path.realpath(self.out) + os.sep) or not SAFE_NAME.match(os.path.basename(full))
                    or not os.path.isfile(full)):
                return self.send_error(404)
            with open(full, "rb") as f:
                body = f.read()
            self.send_response(200)
            self.send_header("Content-Type", "text/csv" if full.endswith(".csv") else "application/json")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            return self.wfile.write(body)
        return super().do_GET()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8000)
    ap.add_argument("--out", default=os.path.join(REPO, "StudyData"))
    ap.add_argument("--no-pull", action="store_true", help="only serve, don't pull from headsets")
    a = ap.parse_args()
    out = os.path.abspath(a.out)
    os.makedirs(out, exist_ok=True)
    if not a.no_pull:
        threading.Thread(target=pull_loop, args=(out,), daemon=True).start()
    Handler.out = out
    handler = functools.partial(Handler, directory=os.path.join(HERE, "viewer"))
    srv = http.server.ThreadingHTTPServer(("127.0.0.1", a.port), handler)
    print(f"Replay viewer: http://localhost:{a.port}   (data: {out}; auto-pull {'off' if a.no_pull else 'on'})", flush=True)
    srv.serve_forever()


if __name__ == "__main__":
    main()
