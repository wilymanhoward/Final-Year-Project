"""Sanity-check study logs: python -I tools/check_logs.py StudyData [--quiet]

For every run log ({base}.csv) it checks the file against its _summary.csv and _meta.json:
column count, time order, 10 Hz sampling gaps, unit quaternions, missing tracking = empty (never 0),
plausible head height, contamination 1 s cooldown per zone, event counts == summary totals.
A {base}_hands.csv (finger joints, 20 Hz) is checked too: columns, time order, a hand is all-or-nothing, rate,
plausible wrist-to-knuckle length.
Prints one line per problem; exit code 1 if any run has a problem (warnings don't count).
"""
import csv, json, math, sys
from collections import Counter, defaultdict
from pathlib import Path

COLS = 25


def detail_map(detail):
    out = {}
    for part in detail.split(";"):
        if "=" in part:
            k, v = part.split("=", 1)
            out[k] = v
    return out


def check_run(log):
    errs, warns = [], []
    base = log.with_suffix("")
    summ_path, meta_path = Path(f"{base}_summary.csv"), Path(f"{base}_meta.json")
    with open(log, newline="", encoding="utf-8") as fh:
        reader = csv.reader(fh)
        header = next(reader, None)
        rows = list(reader)
    if header is None or len(header) != COLS:
        return [f"header has {len(header or [])} columns, expected {COLS}"], warns
    idx = {h: i for i, h in enumerate(header)}
    for n, r in enumerate(rows, 2):
        if len(r) != COLS:
            errs.append(f"line {n}: {len(r)} columns")
    rows = [r for r in rows if len(r) == COLS]
    if not rows:
        return errs + ["no rows"], warns

    self_id = rows[0][idx["participant_id"]]
    t_prev, samples, sample_t = -1.0, 0, []
    events = Counter()
    contam_self, contam_all = 0, 0
    heights = []
    last_entry = {}  # (by, zone) -> time of last counted entry
    for r in rows:
        t = float(r[idx["session_time_s"]])
        if t < t_prev - 1e-6:
            errs.append(f"t={t}: time goes backwards (prev {t_prev})")
        t_prev = t
        for pt in ("head", "lhand", "rhand"):
            xyz = [r[idx[f"{pt}_{a}"]] for a in "xyz"]
            if all(xyz) and all(float(v) == 0.0 for v in xyz):
                warns.append(f"t={t}: {pt} position is exactly 0,0,0 (missing tracking should be empty)")
        q = [r[idx[c]] for c in ("head_qx", "head_qy", "head_qz", "head_qw")]
        if all(q):
            norm = math.sqrt(sum(float(x) ** 2 for x in q))
            if abs(norm - 1) > 0.01:
                errs.append(f"t={t}: head quaternion norm {norm:.3f}")
        hy = r[idx["head_y"]]
        if hy:
            heights.append(float(hy))
        if r[idx["row_type"]] == "sample":
            samples += 1
            sample_t.append(t)
        else:
            ev = r[idx["event"]]
            events[ev] += 1
            d = detail_map(r[idx["detail"]])
            if ev in ("error_contamination", "partner_contamination"):
                by = d.get("by", self_id)
                contam_all += 1
                contam_self += by == self_id
                key = (by, d.get("zone"))
                if key in last_entry and t - last_entry[key] < 1.0 - 1e-3:
                    errs.append(f"t={t}: zone {key[1]} entry {t - last_entry[key]:.2f} s after the last (cooldown 1 s)")
                last_entry[key] = t

    if heights:
        heights.sort()
        median = heights[len(heights) // 2]
        if not 1.2 < median < 1.95:  # same band as the floor guard
            warns.append(f"median head height {median:.2f} m above CaseRoot (floor estimate off?)")

    gaps = [b - a for a, b in zip(sample_t, sample_t[1:])]
    big = [g for g in gaps if g > 0.25]
    if samples and len(big) > max(3, samples * 0.01):
        warns.append(f"{len(big)} sample gaps > 0.25 s (max {max(big):.2f} s; pauses explain some)")
    if samples > 10:
        active = sum(g for g in gaps if g <= 1.0)
        rate = len([g for g in gaps if g <= 1.0]) / active if active else 0
        if not 9.0 < rate < 11.0:
            warns.append(f"sample rate {rate:.1f} Hz (expected 10)")
    for ev in ("session_start", "session_end"):
        if events[ev] != 1:
            (errs if ev == "session_start" else warns).append(f"{events[ev]} {ev} rows")
    if events["hand_tracking_lost"] - events["hand_tracking_regained"] not in (0, 1, 2):
        warns.append("hand_tracking_lost / regained don't pair up")

    if not summ_path.exists():
        warns.append("no _summary.csv")
        return errs, warns
    with open(summ_path, newline="", encoding="utf-8") as fh:
        s = next(csv.DictReader(fh))
    end_reason = s["end_reason"]
    if end_reason == "Interrupted":
        warns.append("summary says Interrupted (app killed/crashed)")
    team = s.get("team_run") == "1"
    if int(s["clues_found"]) != events["clue_found"]:
        errs.append(f"summary clues_found {s['clues_found']} vs {events['clue_found']} clue_found rows")
    if int(s["contamination_errors"]) != contam_self:
        errs.append(f"summary contamination_errors {s['contamination_errors']} vs {contam_self} own entry rows")
    if team and s.get("contamination_team") and int(s["contamination_team"]) != contam_all:
        errs.append(f"summary contamination_team {s['contamination_team']} vs {contam_all} entry rows")
    zones = Counter()
    for r in rows:
        if r[idx["event"]] == "error_contamination":
            d = detail_map(r[idx["detail"]])
            if d.get("by", self_id) == self_id:
                zones[d.get("zone")] += 1
    by_zone = dict(p.split("=") for p in s["contamination_by_zone"].split(";") if "=" in p)
    if {k: int(v) for k, v in by_zone.items() if int(v)} != {k: v for k, v in zones.items() if v}:
        errs.append(f"contamination_by_zone {s['contamination_by_zone']} vs rows {dict(zones)}")
    if s["submitted_suspect"] and events["answer_submitted"] != 1:
        errs.append(f"suspect submitted but {events['answer_submitted']} answer_submitted rows")
    if end_reason != "Interrupted" and abs(float(s["elapsed_s"]) - t_prev) > 2.0:
        warns.append(f"elapsed_s {s['elapsed_s']} vs last row t={t_prev:.1f}")
    if s["completed"] != ("1" if end_reason == "Submitted" else "0"):
        errs.append(f"completed={s['completed']} but end_reason={end_reason}")

    if meta_path.exists():
        try:
            m = json.loads(meta_path.read_text(encoding="utf-8"))
            if m.get("contaminationErrors") is not None and m["contaminationErrors"] != contam_self:
                errs.append(f"meta contaminationErrors {m['contaminationErrors']} vs {contam_self}")
            if m.get("endReason") and m["endReason"] != end_reason:
                errs.append(f"meta endReason {m['endReason']} vs summary {end_reason}")
        except json.JSONDecodeError as e:
            errs.append(f"meta JSON broken: {e}")
    else:
        warns.append("no _meta.json")
    return errs, warns


HAND_COLS = 2 + 2 * 25 * 3


def check_hands(path):
    """{base}_hands.csv: 25 joints x xyz per hand; an untracked hand is 75 empty cells."""
    errs, warns = [], []
    with open(path, newline="", encoding="utf-8") as fh:
        reader = csv.reader(fh)
        header = next(reader, None)
        rows = list(reader)
    if header is None or len(header) != HAND_COLS:
        return [f"hands: header has {len(header or [])} columns, expected {HAND_COLS}"], warns
    t_prev, times, odd_len, tracked = -1.0, [], 0, 0
    for n, r in enumerate(rows, 2):
        if len(r) != HAND_COLS:
            errs.append(f"hands line {n}: {len(r)} columns")
            continue
        t = float(r[0])
        if t < t_prev - 1e-6:
            errs.append(f"hands t={t}: time goes backwards")
        t_prev = t
        times.append(t)
        for h in range(2):
            cells = r[2 + h * 75: 2 + (h + 1) * 75]
            filled = sum(1 for c in cells if c != "")
            if filled not in (0, 75):
                errs.append(f"hands t={t}: {'left' if h == 0 else 'right'} hand half filled ({filled}/75)")
            elif filled:
                tracked += 1
                v = [float(c) for c in cells]
                wrist, knuckle = v[0:3], v[11 * 3: 11 * 3 + 3]   # wrist -> middle-finger-phalanx-proximal
                if not 0.05 <= math.dist(wrist, knuckle) <= 0.15:
                    odd_len += 1
    if tracked and odd_len > 0.05 * tracked:
        warns.append(f"hands: wrist-to-knuckle length implausible in {odd_len} of {tracked} hand rows")
    gaps = [b - a for a, b in zip(times, times[1:]) if b - a <= 1.0]
    if gaps and len(gaps) / sum(gaps) < 15:
        warns.append(f"hands: rate {len(gaps) / sum(gaps):.1f} Hz (expected 20)")
    return errs, warns


def main():
    root = Path(sys.argv[1] if len(sys.argv) > 1 else "StudyData")
    quiet = "--quiet" in sys.argv
    logs = sorted(p for p in root.rglob("*.csv")
                  if not p.name.endswith(("_summary.csv", "_hands.csv")) and p.name.count("_") >= 5)
    bad = 0
    for log in logs:
        errs, warns = check_run(log)
        hands = log.with_name(log.stem + "_hands.csv")
        if hands.exists():
            he, hw = check_hands(hands)
            errs, warns = errs + he, warns + hw
        bad += bool(errs)
        if errs or (warns and not quiet):
            print(f"\n{log.relative_to(root)}")
            for e in errs:
                print(f"  ERROR {e}")
            if not quiet:
                for w in dict.fromkeys(warns):
                    print(f"  warn  {w}")
    print(f"\n{len(logs)} runs checked, {bad} with errors")
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
