#!/usr/bin/env bash
# Pulls study data from every connected Quest headset into StudyData/<date>/<serial>/ (gitignored).
# Copies only the study files (*.csv, *.json) - never deletes anything on the headset - plus a Unity log dump.
# Prints a short report per headset: runs found and any run whose summary still says "Interrupted".
#
# Usage (Git Bash, from the repo root):   bash tools/pull_data.sh
# adb: uses ADB if set, else adb on PATH, else the one bundled with Unity 6000.3.25f1.
set -u
export MSYS_NO_PATHCONV=1   # keep /sdcard/... paths as they are in Git Bash

REMOTE_DIR="/sdcard/Android/data/com.xmum.fyp.detective/files"
# Windows-style path (D:/...) when available: adb.exe can't read /d/... paths with MSYS_NO_PATHCONV set.
REPO_ROOT="$(cd "$(dirname "$0")/.." && (pwd -W 2>/dev/null || pwd))"
DATE="$(date +%Y-%m-%d)"

if [ -z "${ADB:-}" ]; then
  if command -v adb >/dev/null 2>&1; then
    ADB="adb"
  else
    ADB="/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
  fi
fi
if ! "$ADB" version >/dev/null 2>&1; then
  echo "adb not found. Set ADB=/path/to/adb and try again." >&2
  exit 1
fi

DEVICES=$("$ADB" devices | awk 'NR>1 && $2=="device" {print $1}')
UNAUTHORIZED=$("$ADB" devices | awk 'NR>1 && $2!="device" && NF>=2 {print $1" ("$2")"}')
if [ -n "$UNAUTHORIZED" ]; then
  echo "Skipping (allow USB debugging in the headset / replug): $UNAUTHORIZED"
fi
if [ -z "$DEVICES" ]; then
  echo "No headset connected." >&2
  exit 1
fi

for SERIAL in $DEVICES; do
  DEST="$REPO_ROOT/StudyData/$DATE/$SERIAL"
  mkdir -p "$DEST"
  echo "== $SERIAL -> StudyData/$DATE/$SERIAL"

  FILES=$("$ADB" -s "$SERIAL" shell ls "$REMOTE_DIR" 2>/dev/null | tr -d '\r' | grep -E '\.(csv|json)$' || true)
  if [ -z "$FILES" ]; then
    echo "   no study files on this headset"
    continue
  fi
  COUNT=0
  for F in $FILES; do
    if "$ADB" -s "$SERIAL" pull "$REMOTE_DIR/$F" "$DEST/$F" >/dev/null 2>&1; then
      COUNT=$((COUNT + 1))
    else
      echo "   FAILED to pull $F"
    fi
  done
  "$ADB" -s "$SERIAL" logcat -d -v time -s Unity:V > "$DEST/unity_log_$(date +%H%M%S).txt" 2>/dev/null

  RUNS=$(ls "$DEST" | grep -c '_summary\.csv$' || true)
  echo "   $COUNT file(s) pulled, $RUNS run(s) with a summary"
  for S in "$DEST"/*_summary.csv; do
    [ -e "$S" ] || continue
    REASON=$(tail -n 1 "$S" | cut -d, -f7)
    if [ "$REASON" = "Interrupted" ]; then
      echo "   WARNING: $(basename "$S") ended as Interrupted (app closed or crashed mid-run)"
    fi
  done
done
echo "Done. Data is in StudyData/$DATE/ (gitignored - back it up separately)."
