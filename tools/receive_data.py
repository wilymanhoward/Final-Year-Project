"""
Receives the study files the headsets send after each run (DataUploader in the app) and saves them to
StudyData/<yyyy-mm-dd>/<device>/ next to what tools/pull_data.sh writes. Standard library only.

Run on the experimenter's laptop during a session (same Wi-Fi / hotspot as the headsets):
    python tools/receive_data.py            (options: --port 8765 --out StudyData)
Windows asks once to allow Python through the firewall: allow it on private networks.

Protocol (keep in sync with Assets/_Project/Scripts/Logging/DataTransfer.cs):
- every 2 s a UDP broadcast "FYPDATA1|<port>" to port 8766, so the headsets find this laptop by themselves;
- PUT /upload/<device>/<file> with header X-Sha256; the file is stored only if the body matches the checksum, then the
  reply is 200 "OK <sha256>" (the headset marks the file as sent only after that reply);
- GET /ping -> "FYPDATA1 receiver".
Plain HTTP on the local network (user decision, session 6); the files hold codes, never names. Nothing is ever deleted:
a file that arrives again with different content keeps the old version as <name>.old-<time>.
Every request is logged to <out>/receiver_log.csv.
"""
import argparse
import csv
import datetime
import glob
import hashlib
import http.server
import os
import re
import socket
import sys
import threading
import time

BEACON_PORT = 8766
BEACON_PREFIX = "FYPDATA1|"
MAX_BYTES = 200 * 1024 * 1024
SAFE_NAME = re.compile(r"^[A-Za-z0-9_\-][A-Za-z0-9_.\-]{0,127}$")
DEVICE = re.compile(r"^quest-[a-z0-9]{1,8}$|^quest-unknown$")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, ".."))
LOG_LOCK = threading.Lock()


def is_study_file(name):
    return bool(SAFE_NAME.match(name)) and (name.endswith(".csv") or name.endswith("_meta.json"))


def local_ipv4s():
    ips = set()
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ips.add(info[4][0])
    except socket.gaierror:
        pass
    try:   # the address used for outgoing traffic (no packet is sent)
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("10.255.255.255", 1))
        ips.add(s.getsockname()[0])
        s.close()
    except OSError:
        pass
    return sorted(ip for ip in ips if not ip.startswith("127.") and not ip.startswith("169.254."))


def beacon_loop(port, stop):
    message = (BEACON_PREFIX + str(port)).encode("ascii")
    while not stop.is_set():
        for ip in local_ipv4s():
            targets = ["255.255.255.255", ".".join(ip.split(".")[:3] + ["255"])]   # /24 directed broadcast too
            try:
                s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
                s.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
                s.bind((ip, 0))   # send out of this interface
                for t in targets:
                    try:
                        s.sendto(message, (t, BEACON_PORT))
                    except OSError:
                        pass
                s.close()
            except OSError:
                pass
        stop.wait(2.0)


def log(out_dir, client, device, name, size, sha, result):
    path = os.path.join(out_dir, "receiver_log.csv")
    with LOG_LOCK:
        new = not os.path.exists(path)
        with open(path, "a", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            if new:
                w.writerow(["utc", "client", "device", "file", "bytes", "sha256", "result"])
            w.writerow([datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"), client, device, name,
                        size, sha, result])


def make_handler(out_dir):
    class Handler(http.server.BaseHTTPRequestHandler):
        server_version = "FYPDATA1"

        def reply(self, code, text):
            body = text.encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, fmt, *args):   # keep the console for our own lines
            pass

        def do_GET(self):
            if self.path == "/ping":
                self.reply(200, "FYPDATA1 receiver")
            else:
                self.reply(404, "not found")

        def do_PUT(self):
            client = self.client_address[0]
            parts = self.path.split("/")
            if len(parts) != 4 or parts[1] != "upload" or not DEVICE.match(parts[2]) or not is_study_file(parts[3]):
                log(out_dir, client, "", self.path[:120], 0, "", "rejected_path")
                return self.reply(400, "bad path")
            device, name = parts[2], parts[3]
            try:
                length = int(self.headers.get("Content-Length", "-1"))
            except ValueError:
                length = -1
            expected = (self.headers.get("X-Sha256") or "").strip().lower()
            if length < 0 or length > MAX_BYTES or not re.match(r"^[0-9a-f]{64}$", expected):
                log(out_dir, client, device, name, length, expected, "rejected_header")
                return self.reply(400, "bad header")
            data = self.rfile.read(length)
            sha = hashlib.sha256(data).hexdigest()
            if len(data) != length or sha != expected:
                log(out_dir, client, device, name, len(data), sha, "checksum_mismatch")
                return self.reply(400, "checksum mismatch")

            # Already here (any day's folder) with the same content: nothing to write.
            for existing in glob.glob(os.path.join(out_dir, "*", device, name)):
                with open(existing, "rb") as f:
                    if hashlib.sha256(f.read()).hexdigest() == sha:
                        log(out_dir, client, device, name, length, sha, "already_had")
                        return self.reply(200, "OK " + sha)

            folder = os.path.join(out_dir, datetime.date.today().isoformat(), device)
            os.makedirs(folder, exist_ok=True)
            dest = os.path.join(folder, name)
            result = "saved"
            if os.path.exists(dest):   # same name, different content: keep the old one
                os.replace(dest, dest + ".old-" + time.strftime("%H%M%S"))
                result = "saved_replaced"
            tmp = dest + ".part"
            with open(tmp, "wb") as f:
                f.write(data)
            os.replace(tmp, dest)
            log(out_dir, client, device, name, length, sha, result)
            print(time.strftime("%H:%M:%S"), f"{device} {name} ({length} bytes) from {client} - {result}")
            self.reply(200, "OK " + sha)

    return Handler


def main():
    parser = argparse.ArgumentParser(description="Receive study files from the headsets.")
    parser.add_argument("--port", type=int, default=8765, help="HTTP port (default 8765)")
    parser.add_argument("--out", default=os.path.join(REPO, "StudyData"), help="output folder (default StudyData/)")
    args = parser.parse_args()
    out_dir = os.path.abspath(args.out)
    os.makedirs(out_dir, exist_ok=True)

    server = http.server.ThreadingHTTPServer(("0.0.0.0", args.port), make_handler(out_dir))
    stop = threading.Event()
    threading.Thread(target=beacon_loop, args=(args.port, stop), daemon=True).start()
    ips = local_ipv4s()
    print(f"Receiving study data on port {args.port}, saving to {out_dir}")
    print("This laptop: " + (", ".join(ips) if ips else "no network address found") +
          f"  (headsets find it by broadcast; fallback: set dataReceiverAddress in experiment_config.json)")
    print("Leave this window open during the session. Ctrl+C to stop.")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        stop.set()
        server.server_close()
        print("Stopped.")


if __name__ == "__main__":
    sys.exit(main())
