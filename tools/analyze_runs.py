"""Merge all study runs into one table (one row per run) and print means per condition x case.

Usage (from the repo root):  python -I tools/analyze_runs.py [StudyData] [out.csv]
Reads every *_summary.csv under the folder (the same run pulled twice is counted once, newest copy wins),
adds the evidence score from *_meta.json and the card order / culprit position from the log CSV.
Standard library only. Runs that ended Interrupted or Aborted are kept but flagged valid=0.
"""
import csv
import json
import os
import sys
from collections import defaultdict
from statistics import mean

CULPRITS = {"X": "X_grace", "Y": "Y_farid"}
NUMERIC = ["elapsed_s", "active_s", "contamination_errors", "reasoning_errors", "evidence_score"]


def find_runs(root):
    runs = {}
    for folder, _, files in os.walk(root):
        for f in files:
            if not f.endswith("_summary.csv"):
                continue
            path = os.path.join(folder, f)
            if f not in runs or os.path.getmtime(path) > os.path.getmtime(runs[f]):
                runs[f] = path
    return [runs[k] for k in sorted(runs)]


def board_order(log_path):
    """left_to_right list from the board_order event row, or []."""
    if not os.path.exists(log_path):
        return []
    with open(log_path, newline="", encoding="utf-8") as fh:
        for row in csv.DictReader(fh):
            if row.get("event") == "board_order":
                for part in row["detail"].split(";"):
                    if part.startswith("left_to_right="):
                        return part.split("=", 1)[1].split("|")
    return []


def load(path):
    with open(path, newline="", encoding="utf-8") as fh:
        row = next(csv.DictReader(fh), None)
    if row is None:
        return None
    base = path[: -len("_summary.csv")]
    meta = {}
    if os.path.exists(base + "_meta.json"):
        with open(base + "_meta.json", encoding="utf-8") as fh:
            meta = json.load(fh)
    order = board_order(base + ".csv")
    culprit = CULPRITS.get(row["case"], "")
    row["evidence_score"] = meta.get("evidenceScore", "")
    row["evidence_score_max"] = meta.get("evidenceScoreMax", "")
    row["board_order"] = "|".join(order)
    row["culprit_position"] = order.index(culprit) + 1 if culprit in order else ""
    row["valid"] = int(row["end_reason"] in ("Submitted", "TimeCap"))
    return row


def num(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "StudyData"
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(root, "all_runs.csv")
    rows = [r for r in (load(p) for p in find_runs(root)) if r]
    if not rows:
        print("no summary files under", root)
        return
    fields = list(rows[0].keys())
    for r in rows:
        for k in r:
            if k not in fields:
                fields.append(k)
    with open(out, "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=fields, extrasaction="ignore")
        w.writeheader()
        w.writerows(rows)
    print(f"{len(rows)} runs ({sum(int(r['valid']) for r in rows)} valid) -> {out}")

    groups = defaultdict(list)
    for r in rows:
        if r["valid"]:
            groups[(r["condition"], r["case"])].append(r)
    print("\ncondition case    n  active_s  contam  reason  evidence  correct")
    for (cond, case), rs in sorted(groups.items()):
        def m(key):
            vals = [num(r.get(key)) for r in rs if num(r.get(key)) is not None]
            return f"{mean(vals):8.1f}" if vals else "       -"
        print(f"{cond:>9} {case:>4} {len(rs):4d} {m('active_s')} {m('contamination_errors')} "
              f"{m('reasoning_errors')} {m('evidence_score')} {m('answer_correct')}")


if __name__ == "__main__":
    main()
