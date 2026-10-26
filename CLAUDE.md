# CLAUDE.md: FYP co-located MR detective game

Read this first in every session. Update **Current status** at the end of each session.

## Project context

- **What:** Final Year Project. A co-located Mixed Reality detective game for two Meta Quest 3
  headsets in the same room, used for an HCI user study.
- **Platform:** Meta Quest 3, Android build, passthrough MR (players see the real room).
- **Engine:** Unity **6000.3.25f1** (Unity 6.3 LTS), URP 17.3.0, Input System 1.20.0 (new Input System only,
  `activeInputHandler: 1`). XR stack: see **XR stack** below.
- **Final system (built, session 6):** 2 players see the same virtual crime scene in the same physical spot
  using Shared Spatial Anchors + Meta Multiplayer Building Blocks (Colocation block with
  "Use Colocation Session") + Unity Netcode for GameObjects. Design everything so it can become networked
  later: **all game state lives in one place, `CaseState`**, never scattered across objects.
- **Study design:** within-subjects, counterbalanced. Condition A = solo, Condition B = team of 2.
  Two different but equally difficult cases: Case X and Case Y.
- **Interaction (decided, session 3):** **hand tracking for participants.** Controllers stay in code only as an
  experimenter/fallback option. Meta Interaction SDK for grabbing. **Never write a custom grab system.** Hook into
  its events instead.
- **Physical room (user, session 6):** **5 x 5 m playable** (`PlayArea.HalfSize` = 2.5; was 4 x 4 m, before that a 3 x 3 m
  area inside a 4 x 4 m room). Furniture stands 0.15-0.3 m inside the edges, the middle stays open. Clues reset only past
  3.6 m from CaseRoot (`ClueBounds.DefaultMaxDistance`, just past the corners).
- **Time cap (decided, session 3):** **15 min**. Cases are designed to take about **10 min**.
- **Performance target:** stable 72+ fps on Quest 3.

### Measures the code must support
- **Completion time:** timer starts on experimenter trigger, stops on answer submit or the 15 min cap.
  Record whether the case was completed (= submitted before the cap).
- **Contamination errors:** head floor position (x,z) or a hand entering an invisible exclusion zone around
  evidence. Count **entries, not frames**, with a **1 s cooldown per zone**.
- **Reasoning errors:** wrong suspect / wrong evidence submitted on the evidence board.
- **Continuous log at 10 Hz:** timestamp, head position + rotation, left/right hand positions, plus discrete
  events (`clue_found`, `clue_tagged`, `error_contamination`, `error_reasoning`, `session_start`, `session_end`).
  Positions are relative to `CaseRoot` (Phase A).
- **Later (don't build yet):** interpersonal distance and shared head-gaze attention. Quest 3 has no eye
  tracking, so attention = head-forward raycast.

### Data
- CSV in `Application.persistentDataPath`
  (on Quest: `/sdcard/Android/data/com.xmum.fyp.detective/files/`). Package id: **`com.xmum.fyp.detective`**.
- Filename: `{pairCode}_{participantId}_{condition}_{case}_{yyyyMMdd_HHmmss}.csv`, plus a
  `..._summary.csv` with one row of totals per run.
- Flushed to disk every 1 s, on every event, and on app pause/focus loss/quit.
- **No names, only codes.** Codes are restricted to letters, digits and `-`.

## XR stack (checked session 3)

**Meta XR SDK (`OVRManager` / `OVRCameraRig`) running on the Unity OpenXR backend.** Not AR Foundation, not a mix.

| Package | Version | Notes |
|---|---|---|
| `com.meta.xr.sdk.all` | 207.0.0 | pulls core, interaction, interaction.ovr, mrutilitykit, haptics, platform (207.0.0), audio 85.0.0, voice 85.0.1 |
| `com.unity.xr.openxr` | 1.16.1 | Android loader = `OpenXRLoader`; enabled features: Meta XR Feature, Foveation, Subsampled Layout, SpaceWarp, Oculus Touch profile |
| `com.unity.xr.management` / `core-utils` / `hands` | 4.6.1 / 2.6.0 / 1.7.3 | dependencies only (Unity XR Hands OpenXR feature is OFF) |

| `com.unity.netcode.gameobjects` / `com.unity.transport` | 2.13.3 / 2.6.0 | installed session 6; Meta's `Meta.XR.MultiplayerBlocks.NGO(.Editor)` compile |

| `com.unity.xr.meta-openxr` | 2.5.1 | added session 6 (user-approved) for the Depth API on OpenXR; pulls AR Foundation 6.5.0 + XR Composition Layers 2.4.0; Android features "Meta Quest: Occlusion" + "Meta Quest: Session" enabled by FYP > Set Up Occlusion |

**Week 3 networking findings (session 5, read from the 207 source + packages.unity.com):**
- Meta's NGO multiplayer blocks need NGO >= 1.7.1 (`versionDefines` in
  `Scripts/BuildingBlocks/MultiplayerBlocks/NGO/Meta.XR.MultiplayerBlocks.NGO.asmdef`). NGO 3.x needs Unity 6000.7, so
  for 6000.3 the newest compatible is **NGO 2.13.3** (deps: `com.unity.transport` 2.6.0). Installed session 6.
- Colocation block with "Use Colocation Session" → `LocalMatchmaking` (`Shared/LocalMatchmaking/LocalMatchmaking.cs`:
  `OVRColocationSession` advertise/discover, Bluetooth + same Wi-Fi) **plus** `CustomMatchmaking`, whose NGO version
  (`NGO/CustomMatchmaking/CustomMatchmakingNGO.cs`) uses **UGS Authentication + Relay + Lobby** (internet + UGS project).
  With MRUK installed the block also defaults to MRUK space sharing (`shareSpaceToGuests`).
- **Shared anchors go through Meta's cloud** (confirmed in `Scripts/OVRAnchor/OVRAnchor.cs` `ShareResult`:
  `FailureNetworkTimeout`, `FailureNetworkRequestFailed` → check connectivity/firewalls; `FailureCloudStorageDisabled`
  → OS setting "Share Point Cloud Data" (Enhanced Spatial Services) off; sharing also needs
  `IMPORT_EXPORT_IOT_MAP_DATA`). So the Wi-Fi needs internet, and the label must explain these failures.
- User hardware (session 5): 2 Quest 3, developer mode, Enhanced Spatial Services on, Bluetooth not yet checked,
  campus Wi-Fi (risk: client isolation can block headset-to-headset traffic; test early, fallback = phone hotspot).
- Direct alternative with verified APIs: host `UnityTransport` direct IP + `OVRColocationSession.StartAdvertisementAsync`
  (metadata <= 1024 bytes: ip:port; returns group `Guid`) + `OVRSpatialAnchor.ShareAsync(anchors, groupUuid)`; guest
  `StartDiscoveryAsync` + `ColocationSessionDiscovered` (`Data.AdvertisementUuid`, `Data.Metadata`) → NGO client →
  `LoadUnboundSharedAnchorsAsync(groupUuid, list)`. No UGS, no Meta user ids.

**Package decisions (user, session 3):**
- **Netcode for GameObjects: approved, but install it in the Week 3 session**, not before.
- **MRUK: do not use in Week 2.** Placement is head-based + `OVRSpatialAnchor` only.

**Anchor / colocation API in this stack** (signatures checked in the installed 207 source):
- Local anchors: `OVRSpatialAnchor` component; `SaveAnchorAsync()`, `OVRSpatialAnchor.LoadUnboundAnchorsAsync(...)`,
  `OVRSpatialAnchor.EraseAnchorsAsync(...)`.
- Shared anchors (group-based): `OVRSpatialAnchor.ShareAsync(anchors, Guid groupUuid)` →
  `OVRSpatialAnchor.LoadUnboundSharedAnchorsAsync(Guid groupUuid, List<UnboundAnchor>)` on the other headset.
- Colocation: `OVRColocationSession.StartAdvertisementAsync(ReadOnlySpan<byte>)` returns the group `Guid`;
  the other headset calls `OVRColocationSession.StartDiscoveryAsync()` and listens to the
  `OVRColocationSession.ColocationSessionDiscovered` event.
- Building Blocks available in core: `SpatialAnchorCore`, `SharedSpatialAnchorCore`,
  `MultiplayerBlocks/Shared/Colocation` (+ NGO and Photon Fusion variants). The NGO variant needs Netcode.
- MR Utility Kit (MRUK) is installed (part of `sdk.all`) but not used (see decisions above).

## Rules for Claude
- Before using any Meta XR SDK class or method, verify it exists in the installed package source
  (`Library/PackageCache`) or Meta's official docs. Don't invent APIs. If unsure, say so.
- Don't upgrade or change package versions without asking.
- Claude can edit files but cannot operate the Unity Editor. Anything that needs the Editor goes into
  `SETUP_CHECKLIST.md` for the user. Claude *can* run Unity in batch mode when the Editor is closed.
- Keep scripts small and single-purpose. Prefer plain C# classes for logic so it can be unit tested.
- Don't commit study data (`*.csv` is gitignored).
- **Git: commit and push directly to `main`** (user's standing instruction). No feature branches or PRs
  unless asked. Fetch first and fast-forward/merge; never force-push `main`.
- Files written from PowerShell must be BOM-free
  (`[IO.File]::WriteAllText(path, text, New-Object Text.UTF8Encoding($false))`), or Unity/JSON parsers can choke.

## Coding conventions
- Namespace: `FYP.Detective` for runtime code, `FYP.Detective.Tests` for tests,
  `FYP.Detective.EditorTools` for Editor tools.
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
- **No Meta SDK types in `FYP.Detective` (runtime).** Meta components call our public methods through
  UnityEvents wired in the Inspector. That keeps the core assembly compiling and tests running without the SDK.
  Runtime code that needs Meta types lives in **`FYP.Detective.Meta`** (`Scripts/Meta/`, namespace
  `FYP.Detective.Meta`, refs `FYP.Detective` + `Oculus.VR`). The Editor-only assembly `FYP.Detective.Editor` may
  reference Meta (`Oculus.VR(.Editor)`, `Oculus.Interaction(.OVR)(.Editor)`, `FYP.Detective.Meta`, `UnityEngine.UI`).
- Scene setup is done only through **FYP/... Editor menu items** (run by the user) or listed manual steps.
  Never hand-edit `.unity`/`.prefab`/`.asset` YAML. Menu tools must be idempotent.
- Every Meta API used must be verified in the installed 207 source; name the file in a comment.

## Architecture (Assets/_Project)

```
Scripts/ (FYP.Detective.asmdef, references Unity.InputSystem, UnityEngine.UI)
  Core/        StudyEnums, ExperimentConfig, CaseState, CaseDefinition (SO), AnswerChecker, PlayerRig,
               CaseRoot, PlacementMath, PlacementRecord (+PlacementMethod)
  Experiment/  SessionTimer, SessionManager, ExperimentConfigStore
  Logging/     PoseSample (+LogRow, LogEvents), CsvFormatter, CsvLogWriter, DataLogger,
               SessionMeta (+BuildInfo), SessionMetaBuilder
  Interaction/ ZoneEntryTracker, FloorFootprint, ExclusionZone, Clue, TrackedPointGate, ClueBounds, ClueResetter,
               ClueGravity, ClueRestDetector
  UI/          EvidenceSelection, EvidenceBoard, ExperimenterPanel, StatusLabel, ClueTagButton, UiOcclusion
  Meta/        (FYP.Detective.Meta.asmdef) ScenePlacement, ExperimenterControllerShortcuts, HandTrackingMonitor,
               HeadsetPresenceMonitor, DepthOcclusionMonitor (refs Meta.XR.EnvironmentDepth)
  Net/         (FYP.Detective.Net.asmdef, refs FYP.Detective + Unity.Netcode.Runtime + Unity.Networking.Transport)
               NetworkSessionLauncher (host / join / stop, status texts, net_* log rows)
               ColocationCoordinator (Phase 2: advertise/discover + share/load the CaseRoot anchor; refs FYP.Detective.Meta)
  Meta/MetaColocation (static wrapper over OVRColocationSession)   Core/ColocationProtocol (advert + anchor message, failure texts)
  Experiment/LanAddress (core: IPv4 parse, private-range check, LAN address pick)
Editor/ (FYP.Detective.Editor.asmdef, Editor-only)
  MainSceneBuilder    menus: FYP > Create Main Scene / Add Controllers and Hands to Rig / Use Ghost Hands
  GrabSetupTool       menus: FYP > Make Selected Clues Grabbable / Make Selected Static Clues Pokeable
  ClueWiring          shared idempotent clue wiring (Clue, InteractableUnityEventWrapper, resetter, tag button)
  PlacementSetupTool  menu:  FYP > Set Up Case Root and Placement
  BoundarySetupTool   menu:  FYP > Suppress Boundary (Passthrough)
  LocomotionDisableTool menu: FYP > Disable Locomotion
  NetworkSetupTool    menu:  FYP > Set Up Networking
  HeadsetUiTool       menu:  FYP > Set Up Headset UI
  EvidenceBoardTool   menu:  FYP > Set Up Evidence Board
  CaseSceneTool       menu:  FYP > Build Case Scenes (X + Y)
  OcclusionSetupTool  menu:  FYP > Set Up Occlusion (Depth API)
  CharacterSetupTool  menu:  FYP > Set Up Briefing Detective
  StudyBuildTool      menus: FYP > Prepare Study Build (debug off + readiness report) / Restore Debug Settings
tools/maya/build_props.py  (run with mayapy, Maya 2027): low-poly flat-shaded props, clue visuals, zone decals ->
                     Assets/_Project/Art/Props/*.fbx + Palette.png + BlobShadow.png; prints sizes + triangles
                     + tools/maya/scenes/<name>.ma per object (viewing only; the script is the source, a re-run overwrites)
Editor/PropImportSettings (import settings for Art/Props)   Editor/PropAssets (materials, Visual children, shells)
Scripts/Interaction/ClueHighlight (hover outline shell)
tools/receive_data.py  laptop receiver for DataUploader (python, stdlib; UDP beacon + HTTP PUT -> StudyData/)
tools/pull_data.sh   pulls *.csv/*.json (+ Unity log) from every connected headset to StudyData/<date>/<serial>/,
                     flags runs whose summary still says Interrupted (Git Bash; uses Windows paths for adb.exe)
docs/cases.md        case content (source of truth for CaseSpecs)   docs/device_test_checklist.md  session procedure
Tests/ (FYP.Detective.Tests.asmdef, Editor-only, EditMode NUnit)
Art/Shaders/GhostHand.shader (URP, "FYP/GhostHand"), OccludedProp / OccludedUI (Depth API)   Art/Materials/HandGhost.mat
Scenes/Main.unity   Cases/{CaseX,CaseY}/CaseX|Y_Definition.asset (generated from docs/cases.md)   Prefabs/ (empty)
```

Data flow:
`ExperimenterPanel → SessionManager (owns CaseState + SessionTimer) → events → DataLogger`
`Clue / ExclusionZone / EvidenceBoard → CaseState methods → CaseState events → DataLogger`
`PlayerRig` (CenterEyeAnchor / LeftHandAnchor / RightHandAnchor) is read by `DataLogger` and `ExclusionZone`.
`ScenePlacement → CaseRoot (pose / anchor child) + SessionManager.SetPlacement(PlacementRecord) → DataLogger`
`DataLogger` writes positions relative to `CaseRoot`, and `{base}_meta.json` (`SessionMeta`) at session start and end.

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
- **Pauses (user decision, session 4):** total time keeps running while the app is paused (headset off / background;
  Unity stops updating, so there are no samples). Paused time is tracked separately (`PauseTracker`, from
  `OnApplicationPause`): `active_s = elapsed_s - paused_s`, plus `paused_s` and `pause_count`, in the summary CSV and
  meta JSON. **Use active time for analysis.** `app_paused` / `app_resumed` event rows mark each gap. The time cap
  still applies to total time.
  **Headset off also counts as paused** (session 4): `HeadsetPresenceMonitor` (Meta asm) forwards
  `OVRManager.HMDUnmounted/HMDMounted` (proximity sensor) to `SessionManager.SetHeadsetWorn`; rows `hmd_unmounted` /
  `hmd_mounted`; samples keep being logged while it is off. `PauseTracker` has sources App + HeadsetOff and counts
  the **union**: overlapping sources = one pause, time counted once (`pause_count` = combined pauses).
  `SessionManager.PauseEnded(seconds)` drives the debug message. Headset already off at session start isn't counted.
- **No session without a summary:** while running, `_summary.csv` and `_meta.json` are written at start, every 5 s
  and on every pause with end reason **`Interrupted`**, then overwritten at the real end. `SessionEndReason`:
  Submitted, TimeCap, Aborted (experimenter), Interrupted (app closed mid-session, `OnApplicationQuit`). A file still
  saying Interrupted means the app was killed or crashed.
- **Start requires a placement** (`SessionManager.requirePlacement`, default on); the refusal reason is shown via
  `ExperimenterPanel.BuildStatus()` ("not started: Place the scene first").
- Log rows = `sample` (10 Hz) or `event`, in one file. Event rows also carry the current pose.
  Missing tracking = empty cells, never zeros. `head_yaw_deg`/`head_pitch_deg` are computed from the head
  forward vector (for future head-gaze analysis). `frame_dt_ms` lets you check fps from the log.
- **Placement (Phase A):** experimenter stands on the taped floor mark facing the front wall and presses
  Place Scene Here. CaseRoot = head (x,z) at floor height (TrackingSpace y; tracking origin = Floor Level, checked
  in `Main.unity`), rotation = head yaw only. Anchor flow: create `OVRSpatialAnchor` → `WhenLocalizedAsync` →
  `SaveAnchorAsync` → UUID in PlayerPrefs (`FYP.Detective.CaseAnchorUuid`); CaseRoot becomes a child of the anchor
  object. Load = `LoadUnboundAnchorsAsync` → `LocalizeAsync` → `BindTo`. Manual = same pose, no anchor. Clear =
  `EraseAnchorsAsync` + delete the PlayerPrefs key + CaseRoot back to origin. 15 s timeout → "Anchor failed - use
  Manual". Placement is refused while a session runs. Placement happens before a run (no log file open), so
  `placement_set`/`placement_loaded` is written right after `session_start` with `at_utc` = when it happened.
- **Shared anchor on the client's own floor (session 6 fix, user: "height wrong every time I join"):** the host's anchor
  sits at the host headset's floor estimate, which differs from the client's by a few cm, so the 5 cm height check failed
  on every join. `ScenePlacement.LoadSharedPlacement` now attaches CaseRoot with `CaseRoot.AttachToOnFloor` /
  `PlacementMath.FloorAlignedPose`: anchor x, z and heading, but this headset's floor height and level (status shows the
  correction in cm). The head-height floor guard still catches a badly calibrated headset.
- **Placement height check (session 5):** `SessionManager.PlacementHeightOffset` = CaseRoot y - TrackingSpace y
  (`floorReference`); start is refused if |offset| > 5 cm ("Placement height wrong (+0.62 m) - re-place").
  `ScenePlacement` subscribes `OVRManager.display.RecenteredPose` (`Scripts/OVRDisplay.cs:167`) → `SessionManager.
  ReportTrackingRecentered()`; Place/Load wait until tracking is stable for 3 s (`TrackingSettleGate`, launch counts as
  a change, max wait 10 s, "Waiting for tracking to settle..."); the height is checked after place/load and every 0.5 s
  before a run (label on change). Log rows: `placement_check` (when=session_start / after_recenter; height_m, ok,
  recenters) and `tracking_recentered` (count) during a run; the after-recenter check runs 1 s later.
- **Logged positions** (10 Hz samples and event rows) are CaseRoot-local; head rotation/yaw/pitch too.
  CSV columns are unchanged; `session_start` detail has `frame=CaseRoot|world` and the meta JSON `positionFrame`.
- **Evidence score** (meta JSON, confirmed by the user, session 6) = number of key evidence items submitted
  (`evidenceScore` of `evidenceScoreMax`); wrong items are `wrongEvidenceCount` (and reasoning errors).
- **Clue wiring (Phase B):** grabbable clue = Meta `AddGrabInteraction` + `Clue` + one `InteractableUnityEventWrapper`
  per HandGrab/Grab interactable (WhenSelect → `MarkFound` + `BeginHold`, WhenUnselect → `EndHold`) + `ClueResetter`
  + `ClueTagButton`. Static clue = flat object (readable side = local -Z, like a Quad) + transparent poke canvas
  (`AddPokeCanvasInteraction`) whose `PokeInteractable` WhenSelect → `MarkFound`. Clue id defaults to the object name.
- **Tagging:** after a clue is found, a 14 x 5.6 cm pokeable uGUI button floats 8 cm above it facing the player
  (yaw only) and calls `Clue.ToggleTagged` (logs clue_tagged / clue_untagged). Hidden when not running.
- **Briefing detective (user, session 6):** after ARM the participant's card shows "Begin briefing"; tapping it puts
  the low-poly Inspector Hart (Maya parts Detective_Body/_Head/_Moustache/_Arm, offsets `DETECTIVE_*_AT` =
  `CharacterSetupTool`) 1.6 m in front of them on the floor; he speaks the lines of `Audio/Briefing/briefing_lines.csv`
  (order,file,case,condition,text; same structure for X/Y, `work_solo`/`work_team` by condition) with a speech bubble;
  procedural animation (user: all four): mouth part opens with loudness, eyes blink every 2.5-5 s, left hand lifts on every
  third line, right arm gestures, head nods, and a Rig pivot at the feet breathes / shifts weight. **Natural motion
  (user: "too robotic"):** every joint goes through a damped `Spring`; targets = Perlin noise + speech beats
  (`SpeechBeatDetector`: loudness jumps kick a nod and a hand beat) + one gesture per line (OpenPalm/Explain/Magnifier/
  Point/Relaxed, seeded) + gaze at the participant (head leads, body turns only past 22 deg); arms split at the elbow
  (Detective_Forearm/_ForearmL, `DETECTIVE_ELBOW_*_AT`). Parts: Body, Head (+Eyes,
  Mouth, Moustache), Arm, ArmL; character style = faceted lofts (`loft`/`limb`/`hand` in build_props.py). Card hidden while he talks, then Start +
  Replay. **Team runs (user, session 6): both players must tap Begin** (card: "Waiting for your partner to tap Begin");
  TeamMessage `ReqBriefingReady|by|x|z|yaw` -> host computes one shared spot (`BriefingSpot.Compute`, in front of the pair,
  CaseRoot-local) -> `BriefingGo|x|z|yaw|ax|az|bx|bz` -> both headsets start together, detective in the same place, facing
  the pair, head looking at player A (host) on even lines and B on odd lines; Replay stays local. **Start waits for the briefing** (`SessionManager.CanParticipantStart`), in team runs for both partners
  (TeamMessage `BriefingDone|by`, both directions); Start now still bypasses. Timer starts on Start (listening isn't
  completion time). Row `briefing` at session start (`started_utc;finished_utc;replays;heard;partner_heard`).
  Voice: user will generate an **online AI voice** (same base names, any of .wav/.mp3/.ogg, in Audio/Briefing, then rerun
  Set Up Briefing Detective); until then placeholders from `tools/make_briefing_tts.ps1` (Windows offline TTS, never
  overwrites existing clips). No audio = text only for an estimated time. Headset-untested.
- **Clue sync fix (session 6, user: "picked-up item doesn't move in my partner's view"):** the gravity work gave clue
  Rigidbodies interpolation on every headset; on the non-owner the physics interpolation redrew the old pose over the
  NetworkTransform's writes, so the clue looked frozen. `ClueGravity.Update` now sets interpolation only where the clue is
  controlled (`Clue.HasLocalAuthority`), None on the partner's copy - Netcode's own NetworkRigidbody rule
  (NetworkRigidBodyBase.cs). NetworkRigidbody itself is NOT used (it would fight ClueGravity / ISDK over isKinematic).
- **Capped toss (user, session 6):** clues can be tossed, not flung: `ClueGravity` samples the held clue's position
  (`TossEstimator`, last 0.1 s) and on release gives it that velocity capped at 2 m/s, no spin (the ISDK's own throw stays
  off). Releases >= 0.6 m/s log `clue_thrown` (`<id>;speed=<raw m/s>;capped=0|1;by=`, logging only, never networked).
  Briefing tells participants not to run after objects (safety). Headset-untested.
- **Clue gravity (user, session 6):** a grabbable clue stays where the scene put it until it is held and let go; then it
  falls (`ClueGravity`): no throw (Grabbable `_throwWhenUnselected` off), no bounce, high friction, damping. Physics only
  while running, only on the controlling headset (team: owner; partner copy kinematic, follows NetworkTransform), never
  while held. Surfaces: invisible floor box per case (top = y 0) + solid box per furniture footprint up to its spec height
  (not the door). A dropped clue that comes to rest (< 5 cm/s for 0.5 s) within 5 cm of an exclusion zone (any height)
  goes home: `clue_reset` detail `<id>;reason=in_zone` (else `reason=out_of_bounds`), so retrieving it never forces a
  contamination entry. Headset-untested.
- **Clue reset:** a loose clue more than 3.6 m (was 3 m; 5 x 5 m room) from CaseRoot or > 5 cm below the floor goes back to its start pose
  (relative to CaseRoot), never while held; logged `clue_reset`. Clues also go back home on every session start.
- **Hand tracking:** `HandTrackingMonitor` sets `PlayerRig.SetTracked(hand, OVRHand.IsTracked ||
  OVRInput.GetControllerPositionTracked)`. Untracked hands → empty cells in samples, ignored by zones, events
  `hand_tracking_lost` / `hand_tracking_regained` (part=LeftHand/RightHand). `TrackedPointGate` per point per zone:
  a point that (re)gains tracking already inside a zone is ignored until it has been outside, so tracking loss
  can't create an entry; a point already inside at session start isn't an entry either.
- **Debug messages:** `StatusLabel.showDebugMessages` shows "Resumed - paused X.X s" for 3 s when a pause (app and/or
  headset off) ends during a session. `FYP > Prepare Study Build` turns it off (committed state), `FYP > Restore Debug
  Settings` turns it on.
- **Networking, Week 3 Phase 1 (session 6):** direct LAN, no relay. `Network` object = NetworkManager + UnityTransport
  (port 7777) + `NetworkSessionLauncher`. Host listens on 0.0.0.0 and shows its LAN IP on the label; the client joins
  `ExperimentConfig.hostAddress`:`netPort`, re-read from `experiment_config.json` on every join (set it with adb).
  Client gives up after 10 x 1 s attempts. Shortcuts (hold 1.5 s, ignored while the stick is clicked): right stick
  up = host, right stick down = join, left stick down = stop. Rows `net_peer_connected` / `net_peer_disconnected` /
  `net_stopped` (only while a run is open). NGO's `DefaultNetworkPrefabs.asset` (Meta Building Block prefabs) is NOT
  attached to our NetworkManager. `PlayerSettings.Android.forceInternetPermission` = true (set by the tool).
- **Colocation, Week 3 Phase 2 (session 6):** shortcuts now drive `ColocationCoordinator` (Host / Join / Stop).
  Host: Netcode host → `OVRColocationSession.StartAdvertisementAsync("FYPDET1|ip|port")` → group UUID →
  `ScenePlacement.ShareCurrentAnchorAsync(group)` (`OVRSpatialAnchor.ShareAsync(Guid)`), re-shared when the host
  re-places. Client: `StartDiscoveryAsync` + `ColocationSessionDiscovered` → join ip:port (20 s, then falls back to
  `hostAddress`) → named message `fyp.case_anchor_request` → host replies `fyp.case_anchor` ("group|anchor") →
  `ScenePlacement.LoadSharedPlacement` (`LoadUnboundSharedAnchorsAsync(group, [anchor], list)`, 30 s timeout) →
  placement method `shared_anchor`. Failure texts from `ColocationProtocol.ExplainAnchorFailure`; fallback = both
  headsets Manual (X) on the tape mark. Permissions USE_COLOCATION_DISCOVERY_API / IMPORT_EXPORT_IOT_MAP_DATA are
  added at build time by Meta's manifest patch (`OVRGradleGeneration.PatchAndroidManifest`).
- **Team-condition rules (user decisions, session 6, Week 3 Phase 3):** (1) found/tagged are **team-wide**, every row
  logs `by=<participantId>`, the summary has team totals plus own counts (`clues_found_self`, `clues_tagged_self`);
  (2) both grab the same clue → **last grabber wins** (ownership moves, `clue_grab_taken` logged); (3) **either partner
  submits once** - the first submission ends the run on both headsets, both CSVs record the answer and who submitted;
  (4) contamination **per person + team total** (each headset detects its own body, per-person per-zone cooldown,
  summary also has the team total). The host's Start/Abort/time cap control both headsets; each headset writes its
  own CSV. Solo runs (no network) behave exactly as before.
- **Condition is chosen explicitly (user, session 6; replaces the earlier auto-switch):** "Condition A / B" button on the
  setup panel. `CheckCanStart` (ARM and start) refuses B without a connected partner and A with one
  (`SessionManager.ConditionMismatch`). A team client takes condition B when the host arms. A partner disconnect during
  a run writes `incident` (`type=partner_disconnected;side=host|client`); the run keeps its condition.
- **Phase C UI decisions (user, session 6):** experimenter **arms**, the participant taps **Start** on a briefing card
  (team: either partner's Start starts both; experimenter "Start now"/right stick still works as fallback); evidence
  board **fixed in the scene** (under CaseRoot); board lists **tagged clues only**; end screen is **neutral** (no
  correct/incorrect, so it can't influence the second condition).
- **Phase C1 (session 6):** `SetupPanel` (left controller menu button; status + 16 pokeable buttons: pair/participant
  -/+, case, place/load/manual, host/join/stop, arm/disarm/start now/abort; hidden during runs), `ParticipantCard`
  (`ParticipantCardModel`: armed → briefing + Start, running → hidden, ended → neutral text; re-placed in front of the
  head when out of view/reach for 1 s), arming in `SessionManager` (`RequestArm/ApplyArm/IsArmed/ArmedChanged`,
  `RequestParticipantStart`) synced with TeamMessage `ReqArm/Arm`. Built by `FYP > Set Up Headset UI`.
- **Phase C2 evidence board (session 6):** selection is game state in `CaseState` (`BoardSuspectId`,
  `BoardEvidenceIds`, `SetBoardSuspect/SetBoardEvidence`, only tagged clues, untag removes it, locked after submit),
  shared in team runs via TeamMessage `ReqBoardSuspect/BoardSuspect/ReqBoardEvidence/BoardEvidence`, logged as
  `board_suspect` / `board_evidence` (+by=). `EvidenceBoard` (logic) + `EvidenceBoardView` (4 suspect cards + 8 evidence
  prebuilt slots, Submit → "Submit? Yes/No", visible only while running) + `EvidenceBoardModel` (pure). Built by
  `FYP > Set Up Evidence Board` under CaseRoot (default = the case specs' spot: back wall (0, 1.35, -1.45), facing the
  start; rerun keeps a moved board's pose). Experimenter can also open the setup panel with the left menu button (launch hint on the label).
- **Start source + arming in the log (session 6):** `session_start` detail has `start_source=participant_start|start_now`
  (participant card Start vs. setup panel Start now / right stick / desktop); `run_armed` row (`at_utc` = when the
  experimenter armed) right after `session_start` when the run was armed; meta `startSource`, `armedUtc`. Team runs
  carry the source in TeamMessage `ReqStart|source` / `Start|case|cond|source`.
- **Phase D cases (session 6):** content in `docs/cases.md` (Case X "The Missing Design Award", culprit Grace Wong;
  Case Y "Death in the Melaka Room" (murder, own layout, not a mirror of X since the user relaxed it: body outline + blood pool, keris K3 beside the body, smashed clock K1, logbook K2, bloody footprints to the entrance, broken vase; headset-untested), K2 = blood forensic report on the reception table beside the pool / time of death 20:15-20:30 (replaces the key logbook), K3 = body forensic marker / cause, blood pool = zone 2, taped body outline = decoration; headset-untested), culprit Farid Rahman; 4 suspects, K1-K4 + R1-R3, 3 zones each). Code: `CaseSpec` types +
  `PlayArea` constants (HalfSize 1.5 m, heights floor/table 0.75/eye 1.5), `CaseSpecs.X/Y` (Y = X mirrored x -> -x),
  `CaseSpecValidator` (counts, height bands, inside area, no clue within 10 cm of a zone, red herrings -> distinct
  innocents, mean distance diff <= 0.15 m, mirror rule incl. display/desk/door moved). Layout: start mark = CaseRoot at
  the centre of the 3 x 3 m area facing the front wall; board at (0, -1.45) facing the start (mean clue distance: see the
  headset-test note below, now 1.22 m).
  `FYP > Build Case Scenes (X + Y)` builds Case_X/Case_Y (`CaseContent`: only the active case is shown; grabbable clues
  are soft-hidden because Netcode needs in-scene network objects active), updates the CaseDefinition assets (title,
  briefing, suspects with role/alibi, culprit, K1-K4), places the board, deactivates TestCube + Quad. Clue inspect text
  shows above its tag button; the briefing card shows the case story; the board has 4 suspect cards (name, role, alibi).
  TeamMessage `Arm` carries the host's case (client adopts case + condition B).
- **Grading (user, session 6):** `answer_correct` = correct culprit only; `perfect_submission` (summary column, meta
  `perfectSubmission`, `perfect=` in `answer_submitted`) = culprit + all key evidence + no red herrings. Reasoning errors
  and evidence score unchanged.
- **Phase D headset tests (session 6):** test 1: glass zone in front of the whole display made K1 unreachable without
  contamination (8 of 9 entries) -> validator reach rule; test 2: coffee in front of the desk blocked K2, crouching for K3
  swept a hand into the glass -> rule tightened (standing spot 0.45 m from the clue within +/-60 deg of its natural side,
  >= 30 cm body clearance from zones, reach line >= 5 cm from zones). Layout now: glass 0.36 x 0.4 at (-1.28, 0.42),
  coffee 0.40 x 0.5 at (0.30, 0.65) beside the desk, K1 (-0.75, 0.78), K3 (-0.70, 0.74), R1 (-0.50, 1.15); mean clue
  distance 1.22 m. Test 3: scene under the floor = the Quest's floor estimate ~0.3 m too low at placement (head 2.00 m
  above the scene; normal 1.58-1.70) -> **floor guard**: A / X refuse when the head is outside 1.20-1.95 m above the
  floor (press again within 5 s to override), loads warn, `placement_check` logs `head_m`. Test 4 (floor re-set in the Quest):
  head 1.56 m above the scene, ARM + participant Start logged, K1/K2/K4 taken with no glass/coffee entries; 7 entries were
  real steps onto the footprints. The floor guard itself wasn't in that build (headset-untested).
- **Room layout v2 (session 6, user: props weren't placed properly; then scaled to 5 x 5 m: furniture centres x1.25,
  clue/zone offsets kept):** numbers below are the 4 x 4 m version, see CaseSpecs for the 5 x 5 m ones; furniture against the edges,
  middle open. Case X: display case centrepiece at the front (-0.75, 1.55) facing the start; reception desk (K2) on the
  right (1.45, 0.45) facing the room; entrance in the back wall, back-right corner (1.35, -1.85, facing in; moved off the
  right edge because it looked like it opened onto the desk); office desk (K4) on the left (-1.45, -0.75);
  monitor (R3) on the left edge (-1.80, 0.25); evidence board at the back (0, -1.75). Clues/zones moved with their
  furniture (K1 -0.55/1.28, K3 -0.50/1.22, R1 -0.32/1.60, R2 1.70/-1.45; glass -1.05/1.08, coffee beside the desk's
  front end 1.45/1.22, footprints from the entrance towards the display 1.02/-1.32 yaw -32). Case Y mirrored. Mean clue
  distance 1.64 m. `PropSpec.Yaw` (front direction) replaces CaseSceneTool's role yaw table. Desk chairs removed.
- **Visual pass (user spec, session 6):** MR props only (no walls/ceilings). **Floors (user, session 6):** a semi-transparent (alpha 0.7,
  `Props_Floor`, queue 2950) 5 x 5 m floor per case, just under y 0: Case X 0.5 m concrete tiles, Case Y floorboards; the
  Case Y varnish zone decal is the same boards clipped to the zone (`Y_Z3_POSE` in build_props.py = CaseSpecs Y zone 3,
  keep in sync). Floors and the varnish decal are built in Unity coordinates and mirrored on export (`to_maya`). Maya path (mayapy 2027 available):
  `tools/maya/build_props.py` builds every mesh in code (metres, Y-up, one shared 32 x 32 (64-colour) palette texture via per-face
  UVs, all edges hard) and exports one FBX each (`<name>_Body`, optional `_Glass`, zone `_Alpha` outline). Conventions:
  props pivot on the floor, front +Z (**Unity's FBX import negates X** but keeps the look: in the script's Maya
  coordinates a viewer in front of a +Z face has +X on the right, of a -Z face +X on the left; use `mirrored()`, not a
  180 degree turn, to fix a left/right mistake) (CaseSceneTool turns them: display/desk 180, office 0, stand 90, door 0; Case Y
  negated); grabbable clue meshes fill their collider box from the bottom centre; static clues centred, readable -Z.
  Case X = modern studio (white/grey/black, teal/coral), Case Y = heritage gallery (wood, batik indigo/ochre/maroon).
  Heights: display case on plinth 1.63 (glass case 0.66 tall so K1 fits at eye height), desks/tables 0.75, monitor 1.59, noticeboard 1.62,
  door frames 2.0 / 2.1 (thin). Total ~15.1k triangles for both cases. Materials (`Art/Materials/Props`): URP Simple
  Lit palette (no specular, no shadows), transparent glass/outline, Unlit front-culled hover shell, Unlit blob shadow.
  `FYP > Build Case Scenes` keeps the functional objects (ids, colliders, grab/poke, tag buttons, zone triggers), removes
  the grey primitive's renderer and adds a "Visual" child (counter-scaled on clues), a `ClueHighlight` shell wired to
  WhenHover/WhenUnhover, blob shadows under furniture. **K1 (user, session 6)** = keypad alarm panel like the user's photo (12 x 18 cm,
  LCD with the time, 4 x 4 round light-blue keys, LED, cover flap open on the left) mounted on the display case's side glass
  at the front corner, lock side opposite the door hinge (X (-0.65, 1.81) facing +x; no post, nothing in the door's way). Clue collider boxes were resized so each slot
  has the same size range in both cases (e.g. R2 17 x 13 cm sketch vs 13 x 8 cm card). Greybox name labels removed.
  `Art/Materials/Greybox` is unused after a rebuild.
- **Data upload to the laptop (user, session 6):** `DataUploader` (next to DataLogger) sends every study file (`*.csv`,
  `*_meta.json`) to `tools/receive_data.py` on the laptop: the receiver broadcasts UDP `FYPDATA1|<port>` to 8766 every
  2 s (fallback `ExperimentConfig.dataReceiverAddress/dataReceiverPort`), headset PUTs
  `http://ip:8765/upload/quest-<id8>/<file>` with `X-Sha256`, receiver stores it in `StudyData/<date>/quest-<id8>/` and
  replies `OK <sha>`; only then the file is in `upload_ledger.txt` (name|size|ticks|sha; changed files go again).
  Never during a run, never deletes on the headset (pull_data.sh = backup); receiver keeps old versions
  (`.old-<time>`) and logs `StudyData/receiver_log.csv`. Plain HTTP on the LAN: `PlayerSettings.insecureHttpOption =
  AlwaysAllowed` (Set Up Networking) + `CleartextHttpBuildStep` (Gradle manifests). Status line on the setup panel.
  Protocol in `DataTransfer` (pure) - keep receive_data.py in sync. Headset-untested.
- **Setup panel v2 (user, session 6: "like a detective game, more user friendly"):** case-control desk (leather + brass,
  manila header with codes + tilted state stamp), **NEXT banner** (`SetupStatusModel.NextStep`: team connect -> placement ->
  ARM -> armed/running; that card gets a brass frame), case-file cards with red number stamps and wax-seal lights; card 1
  shows each value with [-]/[+] or Switch; ARM "show the briefing card", "Cancel ARM", "Start now (skip briefing)", Abort
  needs a second tap within 3 s. 780 x 650 mm at 0.58 m. Rebuilt by Set Up Headset UI.
- **Setup panel look (user, session 6: "too plain"):** 720 x 560 mm at 0.55 m: header (codes + run-state chip
  READY/ARMED/RUNNING mm:ss/ENDED) and four rounded cards 1 Participant / 2 Placement / 3 Network & data / 4 Run, each
  with a traffic light (`SetupStatusModel`: placement red until placed + height ok; network vs. condition like the ARM
  rule; data amber while files wait; run green/amber/red), status text and its buttons (ARM green, Start now blue,
  Abort red). `SetupPanel.RefreshSections`; the old single status text stays as fallback. Headset-untested.
  The ExperimenterStatusLabel has the same look (`PlacementSetupTool.StyleLabel`, in place, also run by Set Up Headset
  UI): rounded card, "EXPERIMENTER" header, accent bar coloured by `SetupStatusModel.MessageLight` (keywords: red
  problem / amber in progress / green).
- **Evidence board look (user, session 6: "like an actual evidence board"):** cork board in a wooden frame (mesh
  `EvidenceBoard` from build_props.py, readable side -Z, always visible, ledge with magnifier/markers) + transparent
  canvas 1100 x 740 mm (centre 1.30 m): 4 suspect Polaroids (silhouette, name/role/alibi, pin, "PRIME SUSPECT" stamp),
  8 pinned index cards ("PINNED" stamp), red strings chosen suspect -> chosen evidence (`EvidenceBoardView.UpdateStrings`,
  prebuilt Images), tape headers, notepad summary, red SUBMIT stamp. Logic unchanged. Headset-untested.
- **Occlusion on/off (user, session 6):** setup panel button "Occlusion: ON/OFF" (Placement & View card) toggles
  `ExperimentConfig.occlusionEnabled` (saved; between runs only); `DepthOcclusionMonitor` enables/disables the
  EnvironmentDepthManager (off stops depth sensing). Per headset, not synced - set both the same in team runs. Each run's
  `depth_occlusion` row records it (active / off).
- **Depth occlusion NOT WORKING YET (found session 6):** Meta's `EnvironmentDepthManager` on the OpenXR backend needs
  `com.unity.xr.meta-openxr` >= 2.1 (versionDefine OPEN_XR_META_2_1_OR_NEWER -> `DepthProviderOpenXR`) and the OpenXR feature
  "Meta Quest: Occlusion"; without it `IsSupported` is false (log `depth_occlusion status=unsupported`). Compatible version
  for OpenXR 1.16.1: **2.5.1** (pulls AR Foundation 6.5.0, XR Composition Layers 2.4.0); 2.6.1 needs OpenXR 1.18. **Installed
  (user OK)**; Set Up Occlusion enables Occlusion + Session on Android (Meta's rule, ProjectSetupDepthAPI.cs); Prepare Study
  Build fails without them. Headset-untested.
- **Depth occlusion (user, session 6):** Meta Depth API (`EnvironmentDepthManager`, core 207
  `Scripts/EnvironmentDepth/`), soft occlusion, hands removed from depth; needs the USE_SCENE (spatial data) permission
  (requested at startup; scene support Required). Every prop material is on `FYP/OccludedProp` (Art/Shaders: palette,
  Simple-Lit-like diffuse, Meta macros, premultiplied output, `_EnvironmentDepthBias` 0.02 props / 0.06 floor-level
  things against floor flicker, tune on the headset); world-space uGUI gets `FYP/OccludedUI` at start (`UiOcclusion`).
  `DepthOcclusionMonitor` (Meta asm) -> `SessionManager.ReportDepthOcclusion` -> row `depth_occlusion`
  (`when=session_start|change;status=active|waiting|no_permission|unsupported|off`). Same in both conditions.
  Built by `FYP > Set Up Occlusion (Depth API)`. Headset-untested.
- **Hand look:** participants see translucent dark "ghost" hands with a white rim (custom shader
  `FYP/GhostHand`: depth prepass + alpha-blended fresnel rim; tune Body Colour/Alpha and Outline Width/Sharpness
  on `HandGhost.mat`). The OVRHand system-gesture material swap is set to None.

### Verified Meta APIs (checked in the installed SDK 207 source, `Library/PackageCache`)
- `Oculus.Interaction.InteractableUnityEventWrapper` (MonoBehaviour): serialized `IInteractableView` field;
  UnityEvents `WhenHover`, `WhenUnhover`, `WhenSelect`, `WhenUnselect`, `WhenInteractorViewAdded/Removed`,
  `WhenSelectingInteractorViewAdded/Removed`. (The older `WhenInteractorsCountUpdated` /
  `WhenSelectingInteractorsCountUpdated` no longer exist in 207.)
- `Oculus.Interaction.Editor.QuickActions.QuickActionsAPI.AddGrabInteraction(GameObject)` (runs `GrabWizard`:
  kinematic, no-gravity `Rigidbody`, `Grabbable`, `HandGrabInteractable` + `GrabInteractable`, auto collider, adds
  grab interactors to the rig). It does **not** add an `InteractableUnityEventWrapper`.
- `Oculus.Interaction.OVR.Editor.QuickActions.OVRQuickActionsAPI.AddOVRInteractionRig()`: adds the comprehensive
  ISDK rig under `OVRCameraRig` and disables the OVRHand mesh visuals (ISDK `Oculus.Interaction.HandVisual` replaces them).
- `OVRMeshRenderer.SystemGestureBehavior { None, SwapMaterial }` (serialized `_systemGestureBehavior`,
  `_systemGestureMaterial`).
- `OVRCameraRig` child anchors: `TrackingSpace`, `CenterEyeAnchor`, `LeftHandAnchor`, `RightHandAnchor`,
  `LeftControllerAnchor`, `RightControllerAnchor`.
- `OVRCameraRig.prefab`, `PassthroughUnderlay.prefab`, `OVRManager.isInsightPassthroughEnabled`,
  `OVRManager.trackingOriginType` / `TrackingOrigin.FloorLevel`, `OVRProjectConfig` hand-tracking/passthrough settings.
- Anchors / colocation: see **XR stack** above. Used in Phase A (`Scripts/OVRSpatialAnchor.cs`):
  `AddComponent<OVRSpatialAnchor>()`, `WhenLocalizedAsync()`, `Uuid`, `SaveAnchorAsync()`,
  `LoadUnboundAnchorsAsync(IEnumerable<Guid>, List<UnboundAnchor>)`, `UnboundAnchor.Localized/LocalizeAsync()/
  TryGetPose/BindTo`, `EraseAnchorsAsync(IEnumerable<OVRSpatialAnchor>, IEnumerable<Guid>)`; results are
  `OVRResult<…>.Success/.Status` (`Scripts/Util/Async/OVRResult.cs`), awaitable `OVRTask` (`Scripts/Util/Async/OVRTask.cs`).
  Load pattern copied from `Scripts/BuildingBlocks/SpatialAnchorManagerBlockScripts/SpatialAnchorCoreBuildingBlock.cs`.
- Phase B (Interaction SDK, `com.meta.xr.sdk.interaction`): `InteractableUnityEventWrapper` (`Runtime/Scripts/Unity/
  InteractableUnityEventWrapper.cs`: serialized `_interactableView`, `WhenSelect`, `WhenUnselect`, `InjectInteractableView`);
  `HandGrabInteractable` (`Oculus.Interaction.HandGrab`), `GrabInteractable`, `PokeInteractable`, `PointableCanvas`,
  `PointableCanvasModule`; `QuickActionsAPI.AddPokeCanvasInteraction(GameObject, bool)` (`Editor/QuickActions/Scripts/
  QuickActionsAPI.cs`; adds GraphicRaycaster, PokeCanvasInteractable, poke interactors on the rig, EventSystem +
  PointableCanvasModule). There is NO public poke API for plain 3D objects (`PokeWizard` is internal), hence poke canvases.
  `OVRHand.IsTracked` (`com.meta.xr.sdk.core/Scripts/Util/OVRHand.cs:140`), `OVRInput.GetControllerPositionTracked`
  (`Scripts/OVRInput.cs:709`), `OVRInput.RawButton` + `Get/GetDown(RawButton, Controller)`, `OVRManager.HMDMounted/
  HMDUnmounted` events (`Scripts/OVRManager.cs:305`, used by HeadsetPresenceMonitor); `OVRManager.display.RecenteredPose` (`Scripts/OVRDisplay.cs:167`).
- `OVRInput.Get/GetDown(Button, Controller)` with `Button.One/Two/PrimaryThumbstick`, `Controller.LTouch/RTouch`
  (`Scripts/OVRInput.cs`). `OVRManager.TrackingOrigin.FloorLevel` (`Scripts/OVRManager.cs`).
- Project config: `anchorSupport = Enabled` (`Assets/Oculus/OculusProjectConfig.asset`), manifest has
  `com.oculus.permission.USE_ANCHOR_API`. `sharedAnchorSupport` and `colocationSessionSupport` = Supported (user, session 4).
- Boundary (session 6): `OVRProjectConfig.boundaryVisibilitySupport` (`Editor/OVRProjectConfig.cs:171`, adds
  `com.oculus.permission.BOUNDARY_VISIBILITY`) + `OVRManager.shouldBoundaryVisibilityBeSuppressed` (`Scripts/OVRManager.cs:1229`,
  honoured only while passthrough is on; refusal logs "Cannot suppress boundary visibility"). Set by BoundarySetupTool.
- Locomotion (session 6): the comprehensive ISDK rig brings `Oculus.Interaction.Locomotion.FirstPersonLocomotor` (moves the
  player origin; `_crouchHeightOffset = -0.5`) and `ControllerLocomotionSlideActions.prefab` toggles crouch on
  `ControllerButtonUsage.Primary2DAxisClick` (thumbstick click = our start/abort shortcuts). It moved TrackingSpace to
  y = -0.5 in a headset test. **Locomotion must stay off in MR** (LocomotionDisableTool deactivates the locomotor and the
  LocomotionController/HandInteractorGroup objects).
- Hand anchors when untracked: handled by HandTrackingMonitor (untracked = empty cells), so a frozen anchor pose is never logged.

## How to test
- Unity: Window > General > Test Runner > EditMode > Run All.
- Batch mode (Unity must be closed; results go to the gitignored `Logs/`):
  `"C:/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe" -batchmode -projectPath "D:/Xiamen/FYP Project/FYP"
  -runTests -testPlatform EditMode -testResults "D:/Xiamen/FYP Project/FYP/Logs/EditModeResults.xml"
  -logFile "D:/Xiamen/FYP Project/FYP/Logs/EditModeTests.log"` (no `-quit`; `-runTests` exits by itself).

## Current status

_Last updated: session 6 (2026-10-27). All planned phases are done (Week 2 A–E, Week 3 Phases 1–3) plus the visual pass,
clue gravity, floors, depth occlusion, data upload, the new evidence board look, the setup panel / case-file card /
status label redesign, capped toss, the briefing detective (ElevenLabs voice, natural motion, team-synchronised
briefing), the shared-anchor floor fix, the clue sync fix, the occlusion toggle, the Case Y murder scene, the Case X
trophy briefing and the shuffled suspect cards. Work in phases,
stop after each for the user's headset test, commit only after the user confirms it works._

**Where we are (start here)**
- `main` = `adb6baf` "Shuffle suspect cards on the evidence board, hand tracking Fast Motion tool" (after `f5b3404` Case Y murder
  scene / Case X trophy briefing; everything committed at the user's request **before** a full headset test). EditMode tests
  **259/259** (batch). Occlusion is OFF by
  default (`ExperimentConfig.occlusionEnabled`; scene set by Set Up Occlusion) - turn it on with the setup panel button. Packages: + `com.unity.xr.meta-openxr` 2.5.1 (AR Foundation
  6.5.0, XR Composition Layers 2.6.0). Setup order in Unity: Build Case Scenes -> Set Up Networking -> Set Up Evidence
  Board -> Set Up Headset UI -> Set Up Occlusion (Depth API) -> Set Up Briefing Detective -> Prepare Study Build.
- Headsets: `2G0YC5ZG4M00Z6` = **P01, host**; `2G0YC5ZG970B12` = **P02, client** (its experiment_config.json was set to
  P02). Campus Wi-Fi works headset-to-headset; IPs come from DHCP (discovery makes them irrelevant; `hostAddress` is
  only the fallback).
- Untracked in the repo root (the user's, not Claude's): `.agents/`, `.codex/`, `AGENTS.md`. `Assets/Resources/
  DevAgentSettings.asset` changes by itself (a dev tool's `serverAddress`); never commit it unless asked.
- **Case Y is a murder (user, session 6):** "Death in the Melaka Room", its own layout (no mirror rule any more; the validator
  keeps counts, bands, grab style, reachability, mean clue distance within 0.15 m of X). Body taped outline + blood pool
  (zone 1) at (0, 1.25), keris = K3 beside the body (green scarf strand), smashed wall clock = K1 (8:17), visitor logbook = K2,
  bloody footprints towards the entrance (zone 2), broken vase (zone 3), fallen chair; answer Farid Rahman. Full text:
  docs/cases.md. Case X stays the theft (user: the cases don't have to be near-identical).
- **Case X briefing (user, session 6):** `briefing_lines.csv` has an optional `cue` column; `display` on `X_story_01/02` makes the
  detective stand at one fixed spot 2 m right of the display (`DetectiveBriefing.DisplaySideOffset`), keep looking at the
  player and point with his right arm (aimed in `PointRotation`, elbow `PointElbowDegrees` 55 = Maya `POINT_FINGER_DOWN` 35
  below 90; the forearm mesh swaps to `Detective_ForearmPoint` with the index finger out) while `TrophyGhost` (under the X
  display, built by Build Case Scenes) blinks a gold trophy silhouette in the empty case. Case Y has no cue. Headset-untested
  (arm aim, finger direction, silhouette size). Voice clips needed: X_story_01/02 and Y_story_01..03 (supplied, attached).
- **Suspect cards are shuffled (user, session 6: the culprit was always leftmost):** `SuspectOrder.Shuffled` = seeded by pair code +
  case + condition (FNV-1a), so both headsets of a team agree without a message; the left-to-right order is logged as event row
  `board_order` (`left_to_right=<ids>`) at session start. The same pair/case/condition always gives the same order.
- **Grey "tracking-lost"-looking screen (user report, investigated):** a grey scrim with a clear window around the hands, drawn over the
  app. Not our rendering (it covers our floor too). Headset logs showed `numGuardians: 0` (no room-scale Guardian boundary saved) in
  boundaryless MR mode (the app suppresses the boundary) plus ~33 hand-tracking losses and mount/unmount flaps per 8 min run. Best
  guess: the Quest safety system. Remedy: draw a room-scale boundary on both headsets; if it still appears, A/B with boundary
  suppression off. Rarer after that (user). `FYP > Set Hand Tracking to Fast Motion (High)` was added and the config is at High (not
  proven to help; revert to Low if unwanted).
- Batch entries exist for `CaseSceneTool.BuildBatch` and `CharacterSetupTool.SetUpBatch` (Unity must be closed).
- Main.unity has the study setting (status-label debug messages off). For development: `FYP > Restore Debug Settings`.

**Next (in this order)**
1. **One full team session on both headsets** following `docs/device_test_checklist.md` (rebuild both first). It
   covers everything not yet headset-tested: Case Y (the new murder scene, read the clues once: logbook, clock, keris, email); shared evidence board in a team run; a fully
   correct submission (culprit + K1–K4 → `answer_correct 1`, evidence score 4); taking a clue from the partner's hand
   (`clue_grab_taken`); ARM/Start across two headsets; the floor guard ("Floor looks wrong…"); `head_m` in
   `placement_check`. New since then, also headset-untested: the visual pass (props, K1 keypad, desks, hanging
   noticeboard/monitor), floors (alpha 0.7, depth bias), clue gravity (drop, `clue_reset reason=in_zone`, team sync),
   depth occlusion (`depth_occlusion status=active`, 72 fps), data upload (`python tools/receive_data.py`, setup panel
   "0 waiting"), the evidence board look (readable at 1 m, poking tilted cards, red strings). Newest, also untested:
   occlusion now that the OpenXR Meta package is in (status=active? fps? the ON/OFF button), the briefing detective
   (both tap Begin, same spot for both, voice, gestures, the Start gate), clue sync (a picked-up clue moves in the
   partner's view), joining no longer says "height wrong" (status shows the cm correction), capped toss
   (`clue_thrown`), the new setup panel / case-file card / status label.
   Pull with `bash tools/pull_data.sh` (or the receiver) and check the rows listed in the checklist.
2. Answer the open questions below, then a pilot with real participants.
3. Later (not built, don't start without the user): interpersonal distance and shared head-gaze attention analysis.

**Headset-verified so far (session 6)**
- Placement + logging, pauses, abort, Interrupted snapshots; locomotion off; floor fixed after re-setting the Quest floor.
- Networking: LAN connect (0.4 s), colocation discovery + shared anchor (scene lines up), shared state (`by=P02` finds
  arrive on the host).
- UI: setup panel, ARM + participant Start (`start_source=participant_start`, `run_armed`), board submit graded right.
- Case X layout: clues reachable without zone entries after two layout fixes (see Phase D headset tests above).
- Not seen on the headset: clue reset (> 3 m), boundary suppression success, the items in Next 1.

**Known issues / notes**
- `placement_check when=after_recenter` can be a false alarm after the headset is put back on (the Quest's floor
  estimate moves, not the anchor; only logged during a run). Head height above CaseRoot (`head_m`) is the truth.
- A wrong Quest floor estimate puts the whole scene under/over the real floor; the floor guard refuses placement when
  the head is outside 1.20–1.95 m above the floor. Fix = re-set the floor in Quest settings.

**Useful commands / tips**
- adb: `"/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"`.
  In Git Bash set `MSYS_NO_PATHCONV=1` before adb commands with `/sdcard/...` paths; local paths for adb.exe must then
  be Windows-style (`D:/...`, `pwd -W`).
- Data: `bash tools/pull_data.sh` (all connected headsets → `StudyData/<date>/<serial>/`, gitignored).
- Logs: `adb logcat -d -v time -s Unity:V` after a test (the buffer only holds the last minute or so); a live capture
  dies with exit 255 when the headset sleeps/disconnects.
- Batch tests: see "How to test" (Unity must be closed; check `tasklist | grep -ci unity.exe` = 0 first).
- Python edit scripts: write them to the scratchpad and run with `python -I`; inline heredocs with `\n` in C# strings
  turn into real line breaks.

**Open questions for the user**
- None open (session 6: lab 4 x 4 m playable, template content deleted, grading split into answer_correct /
  perfect_submission).
