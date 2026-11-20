"""
Replay viewer server: serves tools/viewer, pulls new study files from every Quest plugged in by USB, and lists all runs
in StudyData for the viewer's library. Standard library only.

    python tools/viewer_server.py            (options: --port 8000 --out StudyData --no-pull)
then open http://localhost:8000. Leave it running: plug a headset in after a test and the new run appears by itself.

- every 3 s: `adb devices`; for each authorised headset, every file *.csv / *.json in the app's data folder whose size or
  time changed since the last pull is copied to StudyData/<yyyy-mm-dd>/<serial>/ (same layout as tools/pull_data.sh).
  Never deletes anything on the headset. What was pulled is remembered in StudyData/pulled_index.json.
- GET /api/runs   -> every run (one per run name, newest copy wins, like analyze_runs.py) with its summary values
                    (+ "hands": url of {base}_hands.csv when the finger joints were logged)
- POST /api/runs/delete?name=<run CSV> -> archive that run's local files under StudyData/.deleted
- GET /api/status -> connected headsets + the last pull messages
- GET /data/<path under StudyData> -> a .csv/.json file
- GET /live?device=<adb id> -> live H.264 (Annex B) of that headset's screen (`adb exec-out screenrecord`, what the
  player sees: passthrough room + the app); the viewer decodes it with WebCodecs. Nothing is saved.
- GET /api/setup?device=<adb id> -> that headset's pair code, participant, case and condition (experiment_config.json)
- POST /api/setup?device=<adb id> with JSON {pairCode, participantId, caseId: X|Y, condition: A|B} -> writes them into
  the headset's experiment_config.json over adb (other settings kept); the app picks them up within ~2 s between runs
  (SessionManager watches the file), a running session takes them once it ends. Codes: 1-16 letters/digits/'-'.
- POST /api/wireless?device=<adb id> -> switches a USB headset to adb over Wi-Fi (tcpip 5555 + connect), so it can be
  watched live without a cable. Remembered in StudyData/wireless_headsets.json and reconnected by itself whenever the
  Wi-Fi link drops (adb restarts during Unity's Build and Run, the headset slept); after a headset reboot press it again.
Files still being written (changed in the last 15 s on the headset) are pulled once they settle, so a running session
isn't copied over and over. Listens on 127.0.0.1 only (study data stays on this laptop).
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
import tempfile
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, ".."))
REMOTE_DIR = "/sdcard/Android/data/com.xmum.fyp.detective/files"
UNITY_ADB = r"C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
SAFE_NAME = re.compile(r"^[A-Za-z0-9_\-][A-Za-z0-9_.\-]{0,127}\.(csv|json)$")
RUN_NAME = re.compile(r"^([A-Za-z0-9\-]+)_([A-Za-z0-9\-]+)_([AB])_([XY])_(\d{8}_\d{6})$")
RUN_SUFFIXES = (".csv", "_summary.csv", "_hands.csv", "_meta.json")
DELETED_DIR = ".deleted"
RUN_FILE_LOCK = threading.Lock()

STATE = {"devices": {}, "headsets": [], "messages": [], "version": 0}
INFO = {}   # adb id -> {"serial", "participant", "checked"}: real serial (Wi-Fi ids are ip:port) + code from the config
SETTLE_S = 15
RECONNECT_S = 10
# both eyes, same aspect as the 4128x2208 display, multiples of 16; the viewer asks for the big one and falls back
LIVE_SIZES = {"1920x1024": "8M", "1280x688": "4M"}
LOCK = threading.Lock()
CODE = re.compile(r"^[A-Za-z0-9\-]{1,16}$")   # ExperimentConfig.IsValidCode
CONFIG_PATH = f"{REMOTE_DIR}/experiment_config.json"


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


def pull_device(adb_path, out, dev, serial, index):
    r = adb(adb_path, "-s", dev, "shell", f"date +%s; stat -c '%s|%Y|%n' {REMOTE_DIR}/*.csv {REMOTE_DIR}/*.json")
    lines = r.stdout.splitlines()
    now = int(lines[0]) if lines and lines[0].strip().isdigit() else None
    remote = {}
    for line in lines[1:]:
        parts = line.strip().split("|", 2)
        if len(parts) == 3 and parts[0].isdigit():
            name = parts[2].rsplit("/", 1)[-1]
            if SAFE_NAME.match(name) and (now is None or now - int(parts[1]) >= SETTLE_S):   # still being written: later
                remote[name] = f"{parts[0]}|{parts[1]}"
    known = index.setdefault(serial, {})

    def deleted(name):
        return any(name.endswith(suffix) and os.path.isdir(os.path.join(out, DELETED_DIR, name[:-len(suffix)]))
                   for suffix in RUN_SUFFIXES)

    todo = [n for n, sig in remote.items() if known.get(n) != sig and not deleted(n)]
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
        res = adb(adb_path, "-s", dev, "pull", f"{REMOTE_DIR}/{name}", os.path.join(dest, name))
        if res.returncode == 0:
            known[name] = remote[name]
            pulled += 1
        else:
            note(f"{serial}: failed to pull {name}: {res.stderr.strip()[:120]}")
    save_index(out, index)
    if pulled:
        note(f"{serial}: pulled {pulled} file(s) to {os.path.basename(out)}/{datetime.date.today().isoformat()}/{serial}")
    return pulled


def headset_info(adb_path, dev):
    """Real serial and participant code of an adb device (cached; the code is re-read every 30 s)."""
    info = INFO.setdefault(dev, {"serial": None, "participant": "", "checked": 0})
    if info["serial"] is None:   # retried every loop until it answers (a headset that is just reconnecting may not)
        serial = adb(adb_path, "-s", dev, "shell", "getprop ro.serialno", timeout=10).stdout.strip()
        info["serial"] = serial if re.fullmatch(r"[A-Za-z0-9]{6,32}", serial) else None
    if time.time() - info["checked"] > 30:
        info["checked"] = time.time()
        try:
            cfg = json.loads(adb(adb_path, "-s", dev, "shell", f"cat {REMOTE_DIR}/experiment_config.json", timeout=10).stdout)
            info["participant"] = str(cfg.get("participantId", ""))
        except ValueError:
            pass
    return info


WIRELESS_FILE = "wireless_headsets.json"   # real serial -> "ip:5555" of headsets made wireless


def load_wireless(out):
    try:
        with open(os.path.join(out, WIRELESS_FILE), encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def save_wireless(out, known):
    with open(os.path.join(out, WIRELESS_FILE), "w", encoding="utf-8") as f:
        json.dump(known, f, indent=1, sort_keys=True)


def wifi_ip(adb_path, dev):
    out = adb(adb_path, "-s", dev, "shell", "ip -f inet addr show wlan0", timeout=10).stdout
    m = re.search(r"inet (\d+\.\d+\.\d+\.\d+)/", out)
    return m.group(1) if m else None


def reconnect(adb_path, out, devices, headsets):
    """Re-attach remembered wireless headsets that dropped off adb (a quick `adb connect` each)."""
    known = load_wireless(out)
    if not known:
        return
    for h in headsets:   # a headset on USB may have a new DHCP address: keep the remembered one current
        if not h["wireless"] and h["serial"] in known:
            ip = wifi_ip(adb_path, h["id"])
            if ip and known[h["serial"]] != f"{ip}:5555":
                known[h["serial"]] = f"{ip}:5555"
                save_wireless(out, known)
    for serial, addr in known.items():
        if devices.get(addr) == "device":
            continue
        if devices.get(addr):   # "offline" / "unauthorized": drop the stale entry first
            adb(adb_path, "disconnect", addr, timeout=5)
        try:
            adb(adb_path, "connect", addr, timeout=6)
        except subprocess.TimeoutExpired:
            pass   # headset off or on another network; try again later


def device_loop(out, pull):
    adb_path = find_adb()
    index = load_index(out)
    next_reconnect = 0.0
    while True:
        try:
            lines = adb(adb_path, "devices", timeout=15).stdout.splitlines()[1:]
            devices = dict(l.split()[:2] for l in lines if len(l.split()) >= 2)
            headsets = []
            for dev, st in devices.items():
                if st == "device":
                    i = headset_info(adb_path, dev)
                    if not i["serial"]:
                        continue   # never use an ip:port id as a folder name
                    headsets.append({"id": dev, "serial": i["serial"], "participant": i["participant"], "wireless": ":" in dev})
            with LOCK:
                STATE["devices"] = devices
                STATE["headsets"] = headsets
            if time.time() >= next_reconnect and not any(t.name == "reconnect" for t in threading.enumerate()):
                next_reconnect = time.time() + RECONNECT_S   # in the background: an asleep headset takes seconds to time out
                threading.Thread(target=reconnect, args=(adb_path, out, devices, headsets), name="reconnect", daemon=True).start()
            changed, done = 0, set()
            for h in sorted(headsets, key=lambda h: h["wireless"]):   # USB first; one pull per real headset
                if pull and h["serial"] not in done:
                    done.add(h["serial"])
                    with RUN_FILE_LOCK:
                        changed += pull_device(adb_path, out, h["id"], h["serial"], index)
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


def run_folders(out):
    for folder, dirs, files in os.walk(out):
        if DELETED_DIR in dirs:
            dirs.remove(DELETED_DIR)
        yield folder, files


def archive_run(out, name):
    stem = name[:-4]
    if not SAFE_NAME.fullmatch(name) or not name.endswith(".csv") or not RUN_NAME.fullmatch(stem):
        raise ValueError("Invalid run name.")
    archive = os.path.join(out, DELETED_DIR)
    target = os.path.join(archive, stem)
    if os.path.isdir(target):
        return
    names = {stem + suffix for suffix in RUN_SUFFIXES}
    paths = [os.path.join(folder, f) for folder, files in run_folders(out) for f in files if f in names]
    if not any(os.path.basename(path) == name for path in paths):
        raise FileNotFoundError("That run is no longer in the library.")
    os.makedirs(archive, exist_ok=True)
    staging = tempfile.mkdtemp(dir=archive)
    moved = []
    try:
        for path in paths:
            # The backup extension keeps archived runs out of CSV analysis and the data endpoint.
            backup = os.path.join(staging, os.path.relpath(path, out) + ".bak")
            os.makedirs(os.path.dirname(backup), exist_ok=True)
            os.rename(path, backup)
            moved.append((path, backup))
        os.rename(staging, target)
    except OSError:
        for path, backup in reversed(moved):
            os.rename(backup, path)
        shutil.rmtree(staging, ignore_errors=True)
        raise


def list_runs(out):
    newest = {}
    for folder, files in run_folders(out):
        for f in files:
            if not f.endswith(".csv") or not RUN_NAME.fullmatch(f[:-4]):
                continue
            if os.path.isdir(os.path.join(out, DELETED_DIR, f[:-4])):
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
        if os.path.exists(path[:-4] + "_hands.csv"):   # finger joints, logged since 2026-11-11
            runs[-1]["hands"] = runs[-1]["url"][:-4] + "_hands.csv"
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


def current_headset(dev):
    with LOCK:
        return next((h for h in STATE["headsets"] if h["id"] == dev), None)


def make_wireless(dev, out_dir):
    adb_path = find_adb()
    ip = wifi_ip(adb_path, dev)
    if not ip:
        return "The headset isn't on Wi-Fi - connect it to the same Wi-Fi as this laptop."
    adb(adb_path, "-s", dev, "tcpip", "5555", timeout=15)
    time.sleep(2)
    res = adb(adb_path, "connect", f"{ip}:5555", timeout=15)
    text = (res.stdout + res.stderr).strip()
    if "connected" in text and "cannot" not in text:
        h = current_headset(dev)
        if h:
            known = load_wireless(out_dir)
            known[h["serial"]] = f"{ip}:5555"
            save_wireless(out_dir, known)
        return f"Wireless at {ip}:5555 - you can unplug the cable. It reconnects by itself if the link drops."
    return f"Couldn't connect to {ip}:5555 ({text[:120]}). Same Wi-Fi? Campus Wi-Fi may block it - try a phone hotspot."


def setup_view(cfg):
    """The four values the setup card shows. JsonUtility writes enums as numbers (CaseId X=0/Y=1, StudyCondition A=0/B=1)."""
    return {"pairCode": str(cfg.get("pairCode", "")), "participantId": str(cfg.get("participantId", "")),
            "caseId": "Y" if cfg.get("caseId") == 1 else "X", "condition": "B" if cfg.get("condition") == 1 else "A"}


def merge_setup(cfg, body):
    """The config with the card's values put in, every other setting kept; ValueError with a reason if a value is bad."""
    if not isinstance(body, dict):
        raise ValueError("Send pairCode, participantId, caseId and condition.")
    pair, who = str(body.get("pairCode", "")).strip(), str(body.get("participantId", "")).strip()
    if not CODE.match(pair):
        raise ValueError("Pair code must be 1-16 letters, digits or '-' (no names).")
    if not CODE.match(who):
        raise ValueError("Participant must be 1-16 letters, digits or '-' (no names).")
    if body.get("caseId") not in ("X", "Y") or body.get("condition") not in ("A", "B"):
        raise ValueError("Case must be X or Y and condition A or B.")
    return {**cfg, "pairCode": pair, "participantId": who,
            "caseId": 1 if body["caseId"] == "Y" else 0, "condition": 1 if body["condition"] == "B" else 0}


def read_config(adb_path, dev):
    r = adb(adb_path, "-s", dev, "shell", f"cat {CONFIG_PATH}", timeout=10)
    try:
        cfg = json.loads(r.stdout)
        return cfg if isinstance(cfg, dict) else {}
    except ValueError:
        return {}   # no file yet (app never saved one): the app fills in its defaults for everything else


def write_setup(dev, body):
    adb_path = find_adb()
    new = merge_setup(read_config(adb_path, dev), body)
    fd, tmp = tempfile.mkstemp(suffix=".json")
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:   # no BOM: Unity's JsonUtility reads it
            json.dump(new, f, indent=4)
        res = adb(adb_path, "-s", dev, "push", tmp, CONFIG_PATH, timeout=20)
    finally:
        os.remove(tmp)
    if res.returncode != 0:
        raise ValueError(f"Couldn't write to the headset: {(res.stderr or res.stdout).strip()[:160]}")
    if setup_view(read_config(adb_path, dev)) != setup_view(new):
        raise ValueError("Written, but the headset reads back something else - try again.")
    if dev in INFO:
        INFO[dev]["checked"] = 0   # the tile shows the new participant on the next loop
    v = setup_view(new)
    return (f"Sent: pair {v['pairCode']} · {v['participantId']} · case {v['caseId']} · condition {v['condition']}. "
            "The headset switches within a few seconds (after the run, if one is going).")


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
            with RUN_FILE_LOCK:
                runs = list_runs(self.out)
            return self.send_json(runs)
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
        if path == "/live":
            return self.stream_live(self.query("device"), self.query("size"))
        if path == "/api/setup":
            dev = self.query("device")
            if not current_headset(dev):
                return self.send_error(404)
            return self.send_json(setup_view(read_config(find_adb(), dev)))
        return super().do_GET()

    def query(self, key):
        from urllib.parse import parse_qs, urlsplit
        return (parse_qs(urlsplit(self.path).query).get(key) or [""])[0]

    def same_site(self):
        """POSTs change headsets: only from this viewer (no other web page, no DNS rebinding)."""
        port = self.server.server_address[1]
        hosts = {f"localhost:{port}", f"127.0.0.1:{port}"}
        origin = self.headers.get("Origin")
        return self.headers.get("Host") in hosts and (origin is None or origin in {"http://" + h for h in hosts})

    def do_POST(self):
        if not self.same_site():
            return self.send_error(403)
        if self.path.split("?", 1)[0] == "/api/runs/delete":
            if self.headers.get("Content-Type") != "application/json":
                return self.send_error(415)
            try:
                with RUN_FILE_LOCK:
                    archive_run(self.out, self.query("name"))
                    runs = list_runs(self.out)
                    with LOCK:
                        STATE["version"] += 1
            except (ValueError, FileNotFoundError) as e:
                return self.send_json({"ok": False, "message": str(e)})
            except OSError as e:
                note(f"Run archive failed: {e}")
                return self.send_json({"ok": False, "message": "Could not archive the run. Check the server log."})
            return self.send_json({"ok": True, "runs": runs})
        if self.path.split("?", 1)[0] == "/api/setup":
            dev = self.query("device")
            if not current_headset(dev):
                return self.send_error(404)
            if self.headers.get("Content-Type", "").split(";")[0].strip() != "application/json":
                return self.send_error(415)
            try:
                length = int(self.headers.get("Content-Length") or 0)
                body = json.loads(self.rfile.read(min(length, 4096)) or b"{}")
                return self.send_json({"ok": True, "message": write_setup(dev, body)})
            except ValueError as e:
                return self.send_json({"ok": False, "message": str(e)})
        if self.path.split("?", 1)[0] == "/api/wireless":
            dev = self.query("device")
            if not current_headset(dev):
                return self.send_error(404)
            return self.send_json({"message": make_wireless(dev, self.out)})
        return self.send_error(404)

    def stream_live(self, dev, size):
        """Pipes the headset's screen as raw H.264 until the browser closes the stream (only known adb devices)."""
        if not current_headset(dev):
            return self.send_error(404)
        size = size if size in LIVE_SIZES else "1280x688"
        proc = subprocess.Popen([find_adb(), "-s", dev, "exec-out", "screenrecord", "--output-format=h264",
                                 "--size", size, "--bit-rate", LIVE_SIZES[size], "--time-limit", "0", "-"],
                                stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        try:
            self.send_response(200)
            self.send_header("Content-Type", "video/h264")
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            while True:
                chunk = proc.stdout.read1(65536)
                if not chunk:
                    break
                self.wfile.write(chunk)
                self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass   # the browser stopped watching
        finally:
            proc.kill()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=8000)
    ap.add_argument("--out", default=os.path.join(REPO, "StudyData"))
    ap.add_argument("--no-pull", action="store_true", help="don't pull files from headsets (live view still works)")
    a = ap.parse_args()
    out = os.path.abspath(a.out)
    os.makedirs(out, exist_ok=True)
    threading.Thread(target=device_loop, args=(out, not a.no_pull), daemon=True).start()
    Handler.out = out
    handler = functools.partial(Handler, directory=os.path.join(HERE, "viewer"))
    srv = http.server.ThreadingHTTPServer(("127.0.0.1", a.port), handler)
    print(f"Replay viewer: http://localhost:{a.port}   (data: {out}; auto-pull {'off' if a.no_pull else 'on'})", flush=True)
    srv.serve_forever()


if __name__ == "__main__":
    main()
