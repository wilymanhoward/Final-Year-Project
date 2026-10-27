# Session checklist (experimenter)

How to run one study session with the Quest 3 headsets, from setup to backing up the data. Controller buttons
are for the experimenter only; participants use their hands.

## Once per build

1. In Unity, open `Main.unity` and run, in this order:
   1. **FYP > Build Case Scenes (X + Y)**
   2. **FYP > Set Up Networking**
   3. **FYP > Set Up Evidence Board**
   4. **FYP > Set Up Headset UI**
   5. **FYP > Set Up Occlusion (Depth API)**
   6. **FYP > Set Up Briefing Detective** (after any new voice files)
2. Run **FYP > Prepare Study Build**. Every line must say OK, and it switches the debug messages off.
   **FYP > Restore Debug Settings** switches them back on for development.
3. Build And Run to **both** headsets: `…4M00Z6` = P01 (host), `…970B12` = P02 (client).

## Before the session (both headsets)

- [ ] Laptop on the **same Wi-Fi / hotspot** as the headsets; start the data receiver and leave its window open:
      `python tools/receive_data.py` (first time: allow Python through the Windows firewall on private networks).
      The setup panel's last line shows "Data: laptop <ip> ..." once a headset has found it.

- [ ] Batteries charged; the 5 x 5 m play area is clear; tape mark on the floor at the
      centre, with a strip showing which way the front wall is.
- [ ] Wi-Fi: both on the same network **with internet** (shared anchors go through Meta's cloud). If campus Wi-Fi
      fails, use a phone hotspot.
- [ ] Bluetooth on (team discovery).
- [ ] Settings > Privacy: "Share Point Cloud Data" / Enhanced Spatial Services on.
- [ ] **Floor height correct**: Settings > Physical space > Boundary > set the floor. A wrong floor puts the scene
      under or above the real floor; the app warns when your head looks too high or too low above the floor.
- [ ] Boundary: if the system boundary still shows, set up a large roomscale boundary.
- [ ] First launch of a new build: allow **spatial data** when the headset asks (needed for occlusion: a person in front
      of a desk hides it). If it was denied: Settings > Privacy > App permissions > Spatial data.

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
5. Hand over the headset(s). The card moves in front of the participant; they poke **Begin briefing**, Inspector Hart
   appears and explains the case (about a minute; **Replay** afterwards if needed), then they poke **Start** (team runs:
   Start appears once both partners have heard the briefing). Without a detective in the scene the card shows the story
   as text instead. (Fallback: **Start now** on the panel / hold the right stick - logged as `heard=0` if it skips it.)
6. During the run: don't intervene. If a partner disconnects, the run continues and an `incident` row is logged.
   To stop early: hold the **left stick** (= Aborted).
7. The run ends when the answer is submitted on the evidence board (or after 15 min). The card says "Case closed" /
   "Time is up"; it never says whether the answer was right.

## After the session

1. The run files arrive by themselves if the laptop receiver ran during the session (see "Before the session"):
   `StudyData/<date>/quest-<id>/`, one folder per headset, `StudyData/receiver_log.csv` lists every file. On each headset
   the setup panel's last line must say **0 waiting** before you switch it off.
   Backup / if something is still waiting: connect each headset by USB and run, from the repo root in Git Bash:

   ```
   bash tools/pull_data.sh
   ```

   Files go to `StudyData/<date>/<headset serial>/`. Neither way ever deletes anything on the headset.
2. Check the report: every run should have a summary, and no run should say **Interrupted**. If one does, the app was
   closed or crashed mid-run - note it in the session log.
3. Back up `StudyData/` (it is not in git).

## Quick checks in the data

- `session_start` has `start_source=participant_start` and a `run_armed` row follows it.
- `placement_check` at session start: `ok=1` and `head_m` about 1.5-1.8 (a standing participant).
- Team runs: `team=1` in `session_start`, `by=P01/P02` on clue and board rows, condition `B` in the filename.
- `briefing` row at session start: `heard=1` (and `partner_heard=1` in team runs); `replays` = how often it was replayed.
- `depth_occlusion` at session start: `status=active` (else `no_permission` = allow spatial data; `waiting` = no
  depth yet). Team runs: walk behind a desk - the desk should hide your partner's legs, not be drawn over them.
- Toss: a gentle toss flies about a metre, a hard throw is slowed to 2 m/s; both log `clue_thrown` (`capped=1` for
  the hard one). Tell participants not to run after objects.
- Clue gravity: a clue let go in the air drops onto the desk / floor and stays put; one dropped into a zone goes back
  home and logs `clue_reset` with `reason=in_zone`. Team runs: the partner sees it fall too.

- `board_order` row at session start: `left_to_right=` lists the 4 suspects; the culprit is not always first. The same
  pair/case/condition gives the same order on both headsets of a team.
- `hand_tracking_lost` rows: expect a few per run; dozens per minute means poor lighting or hands too fast/too close.
- Merge everything for analysis: `python -I tools/analyze_runs.py StudyData` -> `StudyData/all_runs.csv` plus a means table.

## First test after the Case Y / briefing changes (headset-untested)

Do these once, solo (Condition A) first, then once as a team:

- [ ] **Room-scale boundary** drawn on both headsets (Settings > Physical space). This is the best guess for the grey
      screen; if the grey screen still appears, tell me and I'll add a switch to turn boundary suppression off.
- [ ] **Case Y scene**: body outline with the blood pool at the head end, keris beside the body, smashed clock, logbook on
      the reception table, bloody footprints from the pool to the door (nothing sitting under furniture), broken vase.
- [ ] **Case X briefing**: the detective stands about 2 m right of the display case, points at it with the finger out,
      and a gold trophy outline blinks in the empty case. Is it large enough to see from the start mark?
- [ ] **Case Y briefing**: the detective stands on open floor about 1 m right of the start mark, not on the blood, the
      footprints, the body outline or the desk. Team: same spot in both headsets.
- [ ] **Red edge flash**: step into a zone (blood pool, footprints, vase / glass, coffee ...); the edges of the view
      flash red for about 1 s. It also fires when a hand enters. Both eyes, no flicker. `error_contamination` row logged.
      Partner's entry should NOT flash your view.
- [ ] **Suspect cards** are in a different order for different pair/case/condition combinations.
- [ ] **Hand tracking**: decide High (current, Fast Motion) vs Low; compare `hand_tracking_lost` counts per run.
- [ ] Full correct submission in each case: culprit + K1-K4 -> `answer_correct=1`, `perfect_submission=1`, evidence
      score 4 (Case Y keys: clock K1, logbook K2, keris K3, email K4).
- [ ] Team: taking a clue from the partner's hand (`clue_grab_taken`), a picked-up clue moves in the partner's view.

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
