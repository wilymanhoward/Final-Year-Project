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


def one_per_run(rows):
    """Rows with the two headset rows of a team run (same pair, condition, case) reduced to the first one."""
    seen, out = set(), []
    for r in rows:
        run = (r["pair_code"], r["condition"], r["case"]) if r.get("team_run") == "1" else id(r)
        if run not in seen:
            seen.add(run)
            out.append(r)
    return out


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

    # Contamination is per person (one row per headset). Time, answer and evidence are shared by a team, so a team
    # run counts once (the first of its two headset rows), otherwise condition B would be double-weighted.
    people, teams = defaultdict(list), defaultdict(list)
    valid = [r for r in rows if r["valid"]]
    for r in valid:
        people[(r["condition"], r["case"])].append(r)
    for r in one_per_run(valid):
        teams[(r["condition"], r["case"])].append(r)
    print("\nper run (a team run counts once); contamination per person")
    print("condition case runs people  active_s  reason  evidence  correct  contam")
    for key in sorted(people):
        def m(rs, col):
            vals = [num(r.get(col)) for r in rs if num(r.get(col)) is not None]
            return f"{mean(vals):8.1f}" if vals else "       -"
        rs = teams[key]
        print(f"{key[0]:>9} {key[1]:>4} {len(rs):4d} {len(people[key]):6d} {m(rs, 'active_s')} {m(rs, 'reasoning_errors')} "
              f"{m(rs, 'evidence_score')} {m(rs, 'answer_correct')} {m(people[key], 'contamination_errors')}")


def self_check():
    """Two headset rows of one team run count once for team measures, twice for contamination."""
    import tempfile
    head = "pair_code,participant_id,condition,case,end_reason,team_run,active_s,contamination_errors\n"
    with tempfile.TemporaryDirectory() as d:
        for pid, contam in (("P01", 2), ("P02", 4)):
            with open(os.path.join(d, f"G01_{pid}_B_X_1_summary.csv"), "w", encoding="utf-8") as fh:
                fh.write(head + f"G01,{pid},B,X,Submitted,1,100,{contam}\n")
        rows = [load(p) for p in find_runs(d)]
        assert len(rows) == 2 and all(r["valid"] for r in rows)
        assert len(one_per_run(rows)) == 1
        solo = dict(rows[0], team_run="0")
        assert len(one_per_run([solo, dict(solo)])) == 2   # solo runs are never merged


if __name__ == "__main__":
    if sys.argv[1:] == ["--self-check"]:
        self_check()
        print("self-check ok")
    else:
        main()
