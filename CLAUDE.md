# CLAUDE.md: FYP co-located MR detective game

Read this first in every session. Update **Current status** at the end of each session.

## Project context

- **What:** Final Year Project. A co-located Mixed Reality detective game for two Meta Quest 3
  headsets in the same room, used for an HCI user study.
- **Platform:** Meta Quest 3, Android build, passthrough MR (players see the real room).
- **Engine:** Unity **6000.3.25f1** (Unity 6.3 LTS), URP 17.3.0, Input System 1.20.0 (new Input System only,
  `activeInputHandler: 1`). The Meta XR All-in-One SDK is meant to be installed via the Project Setup Tool,
  but see **Current status**: it was not in the committed `Packages/manifest.json`.
- **Final system (NOT built yet):** 2 players see the same virtual crime scene in the same physical spot
  using Shared Spatial Anchors + Meta Multiplayer Building Blocks (Colocation block with
  "Use Colocation Session") + Unity Netcode for GameObjects. Design everything so it can become networked
  later: **all game state lives in one place, `CaseState`**, never scattered across objects.
- **Study design:** within-subjects, counterbalanced. Condition A = solo, Condition B = team of 2.
  Two different but equally difficult cases: Case X and Case Y.
- **Interaction:** Meta Interaction SDK for grabbing. **Never write a custom grab system.** Hook into
  its events instead. Supports both hand tracking and controllers (placeholder in the brief was unresolved,
  so this is the default; the user should confirm).
- **Physical room:** about **4 x 4 m** (default; user should confirm). The scene must fit inside it.
- **Performance target:** stable 72+ fps on Quest 3.

### Measures the code must support
- **Completion time:** timer starts on experimenter trigger, stops on answer submit or the time cap
  (default **15 min**, user should confirm). Record whether the case was completed (= submitted before the cap).
- **Contamination errors:** head floor position (x,z) or a hand entering an invisible exclusion zone around
  evidence. Count **entries, not frames**, with a **1 s cooldown per zone**.
- **Reasoning errors:** wrong suspect / wrong evidence submitted on the evidence board.
- **Continuous log at 10 Hz:** timestamp, head position + rotation, left/right hand positions, plus discrete
  events (`clue_found`, `clue_tagged`, `error_contamination`, `error_reasoning`, `session_start`, `session_end`).
- **Later (don't build yet):** interpersonal distance and shared head-gaze attention. Quest 3 has no eye
  tracking, so attention = head-forward raycast.

### Data
- CSV in `Application.persistentDataPath`
  (on Quest: `/sdcard/Android/data/<package id>/files/`).
- Filename: `{pairCode}_{participantId}_{condition}_{case}_{yyyyMMdd_HHmmss}.csv`, plus a
  `..._summary.csv` with one row of totals per run.
- Flushed to disk every 1 s, on every event, and on app pause/focus loss/quit.
- **No names, only codes.** Codes are restricted to letters, digits and `-`.

## Rules for Claude
- Before using any Meta XR SDK class or method, verify it exists in the installed package source
  (`Library/PackageCache`) or Meta's official docs. Don't invent APIs. If unsure, say so.
- Don't upgrade or change package versions without asking.
- Claude can edit files but cannot operate the Unity Editor. Anything that needs the Editor goes into
  `SETUP_CHECKLIST.md` for the user.
- Keep scripts small and single-purpose. Prefer plain C# classes for logic so it can be unit tested.
- Don't commit study data (`*.csv` is gitignored).

## Coding conventions
- Namespace: `FYP.Detective` for runtime code, `FYP.Detective.Tests` for tests.
- One public type per file (small companion types like enums/structs may share a file); file name = main type.
- **Pure logic in plain C# classes** (`CaseState`, `SessionTimer`, `ZoneEntryTracker`, `FloorFootprint`,
  `AnswerChecker`, `CsvFormatter`, `CsvLogWriter`, `EvidenceSelection`), with no `MonoBehaviour` and time passed in as a
  parameter so they are deterministic in EditMode tests.
- **MonoBehaviours are thin adapters**: read Unity state, call the pure logic, raise events.
- MonoBehaviour fields: `[SerializeField] private` camelCase, exposed through read-only properties.
  Plain data classes serialised by Unity (e.g. `ExperimentConfig`) use public camelCase fields.
- State changes only through `CaseState` methods; other components subscribe to its C# events.
  (Later: `CaseState` gets mirrored/authoritative over Netcode.)
- Don't use `?.` / `??` on `UnityEngine.Object` references (it bypasses Unity's null check).
- No `Find*`/`GetComponent` in `Update`. `FindFirstObjectByType` only as an `Awake` fallback when a
  reference isn't assigned.
- Keep per-frame allocations out of hot paths (Quest 72 fps). The 10 Hz logger reuses a `StringBuilder`.
- All number formatting written to files uses `CultureInfo.InvariantCulture`.
- Doc comments (`///`) on every public type; short inline comments only where the *why* isn't obvious.
- **No Meta SDK types in `FYP.Detective` (yet).** Meta components call our public methods through
  UnityEvents wired in the Inspector. That keeps the core assembly compiling and tests running without the SDK.
  If direct SDK references become necessary, put them in a separate `FYP.Detective.Meta` assembly.

## Architecture (Assets/_Project)

```
Scripts/ (FYP.Detective.asmdef, references Unity.InputSystem)
  Core/        StudyEnums, ExperimentConfig, CaseState, CaseDefinition (SO), AnswerChecker, PlayerRig
  Experiment/  SessionTimer, SessionManager, ExperimentConfigStore
  Logging/     PoseSample (+LogRow, LogEvents), CsvFormatter, CsvLogWriter, DataLogger
  Interaction/ ZoneEntryTracker, FloorFootprint, ExclusionZone, Clue
  UI/          EvidenceSelection, EvidenceBoard, ExperimenterPanel
Tests/ (FYP.Detective.Tests.asmdef, Editor-only, EditMode NUnit)
Scenes/ Prefabs/ Art/{Models,Materials,Textures}/ Cases/{CaseX,CaseY}/
```

Data flow:
`ExperimenterPanel → SessionManager (owns CaseState + SessionTimer) → events → DataLogger`
`Clue / ExclusionZone / EvidenceBoard → CaseState methods → CaseState events → DataLogger`
`PlayerRig` (CenterEyeAnchor / LeftHandAnchor / RightHandAnchor) is read by `DataLogger` and `ExclusionZone`.

### Design decisions (confirm with the user if in doubt)
- **Completed** = the session ended because an answer was submitted before the cap. Correctness is separate
  (`answer_correct` in the summary).
- **One submission per run**: submitting stops the timer and ends the session.
- **Reasoning errors** = 1 for a wrong suspect + 1 per submitted evidence item that isn't key evidence.
  Missing key evidence is logged (`missing_evidence`) but not counted as a reasoning error.
- **Contamination:** head and hands are both floor-projected (height ignored). A zone is "occupied" if any
  tracked point is inside. The cooldown is per zone and measured from the last *counted* entry. The body
  part that triggered it is logged. Only counted while a session is running.
- **Clue found** = first Interaction SDK select (grab) in a run. **Tagging implies found.**
- **Timing** uses `Time.realtimeSinceStartupAsDouble` (not affected by timeScale). Elapsed is clamped to the cap.
- Log rows = `sample` (10 Hz) or `event`, in one file. Event rows also carry the current pose.
  Missing tracking = empty cells, never zeros. `head_yaw_deg`/`head_pitch_deg` are computed from the head
  forward vector (for future head-gaze analysis). `frame_dt_ms` lets you check fps from the log.

### Verified Meta APIs (source checked)
Checked against a public mirror of the Oculus Integration / Interaction SDK source and Meta's reference
docs index. Meta's own npm registry and docs site are blocked from the Claude sandbox, so re-check
against `Library/PackageCache` once the SDK is in the manifest:
- `Oculus.Interaction.InteractableUnityEventWrapper` (MonoBehaviour): serialized `IInteractableView`
  field plus UnityEvents `WhenHover`, `WhenUnhover`, `WhenSelect`, `WhenUnselect`,
  `WhenInteractorsCountUpdated`, `WhenSelectingInteractorsCountUpdated`. Listed in Meta's reference docs
  for Interaction SDK v71–v85.
- `OVRCameraRig` child anchors: `TrackingSpace`, `CenterEyeAnchor`, `LeftHandAnchor`, `RightHandAnchor`,
  `LeftControllerAnchor`, `RightControllerAnchor` (properties `centerEyeAnchor`, `leftHandAnchor`, …).
- Not yet used/verified: Shared Spatial Anchors, Colocation building block, `OVRHand.IsTracked`, `OVRInput`.

## How to test
- Unity: Window > General > Test Runner > EditMode > Run All.
- Outside Unity (what Claude does): a scratch .NET 8 project compiles `Scripts/` + `Tests/` against minimal
  UnityEngine stubs and runs NUnit. This verifies pure logic only, not Unity/Meta integration.

## Current status

_Last updated: session 1 (project foundation, Week 1–2 scope)._

**Done**
- Explored the project. Unity 6000.3.25f1, URP 17.3.0, Input System 1.20.0, Test Framework 1.6.0.
  **The Meta XR SDK is NOT in `Packages/manifest.json`** (no `com.meta.xr.*`), and there's no XR Plugin Management/OpenXR.
- Git was already initialised with Unity `.gitignore`/`.gitattributes`. Added keystore/CSV/config ignores and
  extra LFS types (glb, gltf, usdz, flac, m4a, webm, aab).
- Folder structure, 2 asmdefs, 20 runtime scripts, 5 EditMode test files (60 tests, all passing in the
  stub harness; not yet run inside Unity).
- `SETUP_CHECKLIST.md` with the Editor steps.

**Open questions for the user**
1. Hand tracking, controllers or both? (Code supports both; default = both.)
2. Confirm room 4 x 4 m and time cap 15 min.
3. Is the Meta XR SDK installed locally but not committed? Push `Packages/manifest.json` + `packages-lock.json`.
4. Delete template content (`TutorialInfo/`, `Readme.asset`, `Scenes/SampleScene`)? Currently kept.
5. Confirm the reasoning-error rule above.

**Next up**
- User: work through `SETUP_CHECKLIST.md`, run the tests in Unity, build to Quest, check fps.
- Claude: verify Meta APIs against `Library/PackageCache` once the manifest is committed; blockout of Case X
  inside 4 x 4 m; counterbalancing schedule helper (Latin square for condition x case order); hand-tracking
  validity (e.g. `OVRHand.IsTracked`, after verifying) so untracked hands aren't logged at stale positions.
- Later: Netcode + Colocation + Shared Spatial Anchors; interpersonal distance + head-gaze logging.
