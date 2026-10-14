# Session checklist (experimenter)

How to run one study session with the Quest 3 headsets, from setup to backing up the data. Controller buttons
are for the experimenter only; participants use their hands.

## Once per build

1. In Unity, open `Main.unity` and run, in this order:
   1. **FYP > Build Case Scenes (X + Y)**
   2. **FYP > Set Up Networking**
   3. **FYP > Set Up Evidence Board**
   4. **FYP > Set Up Headset UI**
2. Run **FYP > Prepare Study Build**. Every line must say OK, and it switches the debug messages off.
   **FYP > Restore Debug Settings** switches them back on for development.
3. Build And Run to **both** headsets: `…4M00Z6` = P01 (host), `…970B12` = P02 (client).

## Before the session (both headsets)

- [ ] Batteries charged; the room is clear (4 x 4 m, nothing in the central 3 x 3 m); tape mark on the floor at the
      centre, with a strip showing which way the front wall is.
- [ ] Wi-Fi: both on the same network **with internet** (shared anchors go through Meta's cloud). If campus Wi-Fi
      fails, use a phone hotspot.
- [ ] Bluetooth on (team discovery).
- [ ] Settings > Privacy: "Share Point Cloud Data" / Enhanced Spatial Services on.
- [ ] **Floor height correct**: Settings > Physical space > Boundary > set the floor. A wrong floor puts the scene
      under or above the real floor; the app warns when your head looks too high or too low above the floor.
- [ ] Boundary: if the system boundary still shows, set up a large roomscale boundary.

## Placing the scene (each headset, wearing it)

1. Open the app and wait a few seconds for tracking to settle.
2. Stand **straight on the tape mark, facing the front wall**.
3. Press **A (Place here)**. If you see "Floor looks wrong…", fix the floor (above), then press A again.
   B loads the last saved placement; X places it without an anchor (fallback).
4. Check the furniture stands on the real floor.

## Per participant / run

1. Press **☰ (left controller)** to open the setup panel.
2. Set **Pair** and **Participant** codes (– / +) and the **Case X / Y** of this run (counterbalancing sheet).
3. Choose **Condition A / B**:
   - **A (solo)**: no network running ("no partner" on the panel).
   - **B (team)**: host headset = **Host**; client headset = **Join**. Wait for "Host's placement loaded (shared
     anchor)" on the client and check that the scene lines up in both headsets. If the shared anchor fails: both press
     **X (Manual)** on the tape mark facing the same wall.
4. Press **ARM**. ARM is refused if the condition doesn't match the connection (the panel says why). The briefing card
   appears; in team runs it appears on both headsets.
5. Hand over the headset(s). The card moves in front of the participant; they read the story and poke **Start**.
   (Fallback: **Start now** on the panel / hold the right stick.)
6. During the run: don't intervene. If a partner disconnects, the run continues and an `incident` row is logged.
   To stop early: hold the **left stick** (= Aborted).
7. The run ends when the answer is submitted on the evidence board (or after 15 min). The card says "Case closed" /
   "Time is up"; it never says whether the answer was right.

## After the session

1. Connect each headset by USB (one or both) and run, from the repo root in Git Bash:

   ```
   bash tools/pull_data.sh
   ```

   Files go to `StudyData/<date>/<headset serial>/`. The script never deletes anything on the headset.
2. Check the report: every run should have a summary, and no run should say **Interrupted**. If one does, the app was
   closed or crashed mid-run - note it in the session log.
3. Back up `StudyData/` (it is not in git).

## Quick checks in the data

- `session_start` has `start_source=participant_start` and a `run_armed` row follows it.
- `placement_check` at session start: `ok=1` and `head_m` about 1.5-1.8 (a standing participant).
- Team runs: `team=1` in `session_start`, `by=P01/P02` on clue and board rows, condition `B` in the filename.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Scene under/over the floor | Re-set the Quest floor, place again with A |
| "Anchor failed - use Manual" | Press A again; or X (Manual) on the tape mark |
| Client "Could not reach …" | Same Wi-Fi? Host started? Campus client isolation -> phone hotspot |
| "Anchor sharing failed: no internet …" | The Wi-Fi needs internet; or both use X on the tape mark |
| "…'Share Point Cloud Data' is off" | Settings > Privacy > turn it on, then Host again |
| Panel doesn't open | ☰ is on the **left** controller; the panel is hidden during a run |
| ARM refused | Read the reason on the panel (condition vs. partner, placement, floor height) |
