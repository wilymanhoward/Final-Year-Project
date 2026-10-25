# AGENTS.md: FYP co-located MR detective game

Read this first in every session. Update **Current status** at the end of each session.

## Project context

- **What:** Final Year Project. A co-located Mixed Reality detective game for two Meta Quest 3
  headsets in the same room, used for an HCI user study.
- **Platform:** Meta Quest 3, Android build, passthrough MR (players see the real room).
- **Engine:** Unity **6000.3.25f1** (Unity 6.3 LTS), URP 17.3.0, Input System 1.20.0 (new Input System only,
  `activeInputHandler: 1`). XR stack: see **XR stack** below.
- **Final system (NOT built yet):** 2 players see the same virtual crime scene in the same physical spot
  using Shared Spatial Anchors + Meta Multiplayer Building Blocks (Colocation block with
  "Use Colocation Session") + Unity Netcode for GameObjects. Design everything so it can become networked
  later: **all game state lives in one place, `CaseState`**, never scattered across objects.
- **Study design:** within-subjects, counterbalanced. Condition A = solo, Condition B = team of 2.
  Two different but equally difficult cases: Case X and Case Y.
- **Interaction (decided, session 3):** **hand tracking for participants.** Controllers stay in code only as an
  experimenter/fallback option. Meta Interaction SDK for grabbing. **Never write a custom grab system.** Hook into
  its events instead.
- **Physical room (decided, session 3; confirm by measuring the lab):** **4 x 4 m** clear area with a
  **0.5 m safety margin** on every side, so the playable scene must fit inside the central **3 x 3 m**.
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
  Week 2 goal: positions relative to `CaseRoot` (not built yet, see audit).
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

Not installed: AR Foundation, `com.unity.xr.meta-openxr`, **Netcode for GameObjects**.

**Week 3 networking findings (session 5, read from the 207 source + packages.unity.com):**
- Meta's NGO multiplayer blocks need NGO >= 1.7.1 (`versionDefines` in
  `Scripts/BuildingBlocks/MultiplayerBlocks/NGO/Meta.XR.MultiplayerBlocks.NGO.asmdef`). NGO 3.x needs Unity 6000.7, so
  for 6000.3 the newest compatible is **NGO 2.13.3** (deps: `com.unity.transport` 2.6.0). Not installed yet (needs user OK).
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

## Rules for Codex
- Before using any Meta XR SDK class or method, verify it exists in the installed package source
  (`Library/PackageCache`) or Meta's official docs. Don't invent APIs. If unsure, say so.
- Don't upgrade or change package versions without asking.
- Codex can edit files but cannot operate the Unity Editor. Anything that needs the Editor goes into
  `SETUP_CHECKLIST.md` for the user. Codex *can* run Unity in batch mode when the Editor is closed.
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
  Interaction/ ZoneEntryTracker, FloorFootprint, ExclusionZone, Clue, TrackedPointGate, ClueBounds, ClueResetter
  UI/          EvidenceSelection, EvidenceBoard, ExperimenterPanel, StatusLabel, ClueTagButton
  Meta/        (FYP.Detective.Meta.asmdef) ScenePlacement, ExperimenterControllerShortcuts, HandTrackingMonitor,
               HeadsetPresenceMonitor
Editor/ (FYP.Detective.Editor.asmdef, Editor-only)
  MainSceneBuilder    menus: FYP > Create Main Scene / Add Controllers and Hands to Rig / Use Ghost Hands
  GrabSetupTool       menus: FYP > Make Selected Clues Grabbable / Make Selected Static Clues Pokeable
  ClueWiring          shared idempotent clue wiring (Clue, InteractableUnityEventWrapper, resetter, tag button)
  PlacementSetupTool  menu:  FYP > Set Up Case Root and Placement
Tests/ (FYP.Detective.Tests.asmdef, Editor-only, EditMode NUnit)
Art/Shaders/GhostHand.shader (URP, "FYP/GhostHand")   Art/Materials/HandGhost.mat
Scenes/Main.unity   Cases/{CaseX,CaseY}/CaseX|Y_Definition.asset (placeholder data)   Prefabs/ (empty)
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
- **Placement height check (session 5):** `SessionManager.PlacementHeightOffset` = CaseRoot y - TrackingSpace y
  (`floorReference`); start is refused if |offset| > 5 cm ("Placement height wrong (+0.62 m) - re-place").
  `ScenePlacement` subscribes `OVRManager.display.RecenteredPose` (`Scripts/OVRDisplay.cs:167`) → `SessionManager.
  ReportTrackingRecentered()`; Place/Load wait until tracking is stable for 3 s (`TrackingSettleGate`, launch counts as
  a change, max wait 10 s, "Waiting for tracking to settle..."); the height is checked after place/load and every 0.5 s
  before a run (label on change). Log rows: `placement_check` (when=session_start / after_recenter; height_m, ok,
  recenters) and `tracking_recentered` (count) during a run; the after-recenter check runs 1 s later.
- **Logged positions** (10 Hz samples and event rows) are CaseRoot-local; head rotation/yaw/pitch too.
  CSV columns are unchanged; `session_start` detail has `frame=CaseRoot|world` and the meta JSON `positionFrame`.
- **Evidence score** (meta JSON, to confirm with the user) = number of key evidence items submitted
  (`evidenceScore` of `evidenceScoreMax`); wrong items are `wrongEvidenceCount` (and reasoning errors).
- **Clue wiring (Phase B):** grabbable clue = Meta `AddGrabInteraction` + `Clue` + one `InteractableUnityEventWrapper`
  per HandGrab/Grab interactable (WhenSelect → `MarkFound` + `BeginHold`, WhenUnselect → `EndHold`) + `ClueResetter`
  + `ClueTagButton`. Static clue = flat object (readable side = local -Z, like a Quad) + transparent poke canvas
  (`AddPokeCanvasInteraction`) whose `PokeInteractable` WhenSelect → `MarkFound`. Clue id defaults to the object name.
- **Tagging:** after a clue is found, a 14 x 5.6 cm pokeable uGUI button floats 8 cm above it facing the player
  (yaw only) and calls `Clue.ToggleTagged` (logs clue_tagged / clue_untagged). Hidden when not running.
- **Clue reset:** a loose clue more than 3 m from CaseRoot or > 5 cm below the floor goes back to its start pose
  (relative to CaseRoot), never while held; logged `clue_reset`. Clues also go back home on every session start.
- **Hand tracking:** `HandTrackingMonitor` sets `PlayerRig.SetTracked(hand, OVRHand.IsTracked ||
  OVRInput.GetControllerPositionTracked)`. Untracked hands → empty cells in samples, ignored by zones, events
  `hand_tracking_lost` / `hand_tracking_regained` (part=LeftHand/RightHand). `TrackedPointGate` per point per zone:
  a point that (re)gains tracking already inside a zone is ignored until it has been outside, so tracking loss
  can't create an entry; a point already inside at session start isn't an entry either.
- **Debug messages:** `StatusLabel.showDebugMessages` (Inspector, default ON) shows "Resumed - paused X.X s" for 3 s
  when a pause (app and/or headset off) ends during a session. **Turn it OFF on `ExperimenterStatusLabel` before study builds.**
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
  HMDUnmounted` events (`Scripts/OVRManager.cs:305`, not used yet).
- `OVRInput.Get/GetDown(Button, Controller)` with `Button.One/Two/PrimaryThumbstick`, `Controller.LTouch/RTouch`
  (`Scripts/OVRInput.cs`). `OVRManager.TrackingOrigin.FloorLevel` (`Scripts/OVRManager.cs`).
- Project config: `anchorSupport = Enabled` (`Assets/Oculus/OculusProjectConfig.asset`), manifest has
  `com.oculus.permission.USE_ANCHOR_API`. `sharedAnchorSupport` is still 0 (needed in Week 3).
- Not yet verified: `OVRHand.IsTracked` behaviour for logging (do untracked hand anchors freeze at the last pose?).

## How to test
- Unity: Window > General > Test Runner > EditMode > Run All.
- Batch mode (Unity must be closed; results go to the gitignored `Logs/`):
  `"C:/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Unity.exe" -batchmode -projectPath "D:/Xiamen/FYP Project/FYP"
  -runTests -testPlatform EditMode -testResults "D:/Xiamen/FYP Project/FYP/Logs/EditModeResults.xml"
  -logFile "D:/Xiamen/FYP Project/FYP/Logs/EditModeTests.log"` (no `-quit`; `-runTests` exits by itself).

## Current status

_Last updated: session 4 (Week 2 Phase A, 2026-10-08). Week 2 work runs in phases A–E; stop after each phase
for the user's "next", commit only after the user confirms the phase works on the headset._

**Done (sessions 1–3)**
- Foundation (session 1): folder structure, 3 asmdefs, 20 runtime scripts, EditMode tests, `SETUP_CHECKLIST.md`,
  `.gitignore`/`.gitattributes` (LFS).
- XR runtime (session 2): Meta XR SDK 207 + Unity OpenXR 1.16.1 with the Android `OpenXRLoader` (fixed the app stuck
  on the loading screen). Passthrough via `PassthroughUnderlay`, floor-level tracking origin, splash off,
  `Mobile_RPAsset` tuned for Quest. Unused AI/visual-scripting packages removed (user approved).
- Rig (session 2): `Main.unity` built by **FYP > Create Main Scene**; **FYP > Add Controllers and Hands to Rig** adds
  `OVRControllerPrefab` (LTouch/RTouch) and `OVRHandPrefabBuildingBlock` (left/right) to the anchors.
- Ghost hands: **FYP > Use Ghost Hands** applies `HandGhost.mat` (shader `FYP/GhostHand`) and turns off the
  system-gesture material swap. Seen on the headset; the last outline fix (rim only on the silhouette edge) has not
  been re-confirmed by the user yet. `HandNeutral.mat` is a leftover from an earlier attempt (unused, can be deleted).
- Grab tool (commit 56b4577): **FYP > Make Selected Object Grabbable** calls `OVRQuickActionsAPI.AddOVRInteractionRig()`
  if no ISDK rig exists, then `QuickActionsAPI.AddGrabInteraction(target)`, then puts the ghost material on the ISDK
  hand meshes and saves the scene. Compiles (session 3 batch run); **not yet run/saved into `Main.unity`**.
- **Week 2 Phase A committed** (51f2d5b) after headset retest: refused start without placement, one `[Shortcuts]`
  line per press, pause events, Aborted/Interrupted sessions all with summary + meta. Finding: taking the headset off
  for ~15 s produced only a 1.4 s app pause (the app keeps running at first; samples continued), so pause time
  ≠ headset-off time → user approved headset-off detection (added in Phase B, see Pauses).
- **Phase B close-out (session 5):** 0.2 s hand-tracking log filter (`TrackingEventFilter`, events only; zones/samples
  raw). Floor-height incident diagnosed: anchor 257269b6 saved at y=0/yaw 44.7 (10:05:35) loaded at y=+0.62/yaw 90.2
  (10:13:50), 3.4 s after a burst of 3 "Recenter event detected" (`OVRDisplay.cs:150-153`,
  `OVRPlugin.GetLocalTrackingSpaceRecenterCount` changed) right after HMDMounted → tracking space (floor + yaw) changed
  between save and load. Physical position not provable from logs; safety check proposed. Tests 105/105.
- **Week 2 Phase B (session 4, in progress):** clue wiring in the menu tools, tag button, static poke clues, clue
  reset, hand-tracking monitor + `TrackedPointGate` in zones, new events. Not yet compiled in Unity/headset-tested.
- Grab tool applied to the test cube and saved (commit efd0564, includes `Assets/InteractionSDK/ComprehensiveInteractors.prefab`
  created by Meta's rig wizard). User confirmed grabbing works and the ghost hands look right on the headset.
- **Week 2 Phase A (session 4): CaseRoot + placement.** `CaseRoot`, `PlacementMath`, `PlacementRecord`,
  `SessionMeta(+Builder)`, `StatusLabel`; `FYP.Detective.Meta` assembly with `ScenePlacement` (anchor/load/manual/clear)
  and `ExperimenterControllerShortcuts` (A place, B load, X manual, hold Y clear, hold right stick start, hold left stick
  abort); menu **FYP > Set Up Case Root and Placement**. Extended (not rewritten): `DataLogger` (CaseRoot-local
  poses, placement event, meta JSON), `SessionManager` (`Placement`, `SetPlacement`, `EndUtc`), `CaseState`
  (`ContaminationCount(BodyPart)`), `AnswerResult` (`CorrectEvidenceIds`, `KeyEvidenceTotal`). Batch tests 77/77.
  Headset test 1: anchor created but `SaveAnchorAsync` returned `FailurePermissionInsufficient` (documented in
  `Scripts/OVRAnchor/OVRAnchor.cs` as "user has not granted all the required permissions"). The only runtime
  permission involved is Spatial data `com.oculus.permission.USE_SCENE`; the user enabled it in the headset settings
  (`dumpsys package` then showed `granted=true, USER_SET`). Fix: `ScenePlacement` checks
  `OVRPermissionsRequester.IsPermissionGranted(Permission.Scene)` before Place/Load and requests it with Unity's
  `UnityEngine.Android.Permission.RequestUserPermission(…, PermissionCallbacks)` (granted/denied/dismissed → status).
  Headset test 2 (logcat capture): placement, anchors, CaseRoot-local logging (head 0/0, yaw ~0 on the mark), meta,
  10 Hz while focused all OK. Problems found and fixed: the stick-click shortcuts misfired (left hold never aborted,
  59 repeated starts) → now physical `OVRInput.RawButton` A/B/X/Y/LThumbstick/RThumbstick with `Controller.Touch`,
  start = hold right stick, every shortcut logged as `[Shortcuts] …`; a session could start without placement → now
  refused; pauses left 51–92 s sample gaps unexplained → pause events + active time; a run left "Running" with no
  summary → snapshots marked Interrupted.
  User's own settings (kept, in the Phase A commit): package id `com.xmum.fyp.detective`,
  `sharedAnchorSupport`/`colocationSessionSupport` = Supported, `allowVisibilityMesh` = 1.
- **Tests (session 3): first run inside Unity, batch mode: 60/60 passed** (AnswerChecker 8, CaseState 5,
  CsvFormatter 18, CsvLogWriter 2, EvidenceSelection 1, ExperimentConfig 10, FloorFootprint 4, SessionTimer 3,
  ZoneEntryTracker 9). No compile errors or warnings from `Assets/_Project`.

**Week 2 audit (session 3, read-only)**
Goal: place scene → in-headset setup → start → find/tag clues → avoid protected evidence → submit → end; samples
(10 Hz, CaseRoot-local), events and meta logged; flush on pause.

| Step | Status | Gap |
|---|---|---|
| Place the scene in the room | Missing | No `CaseRoot`, placement or anchor code; scene has only rig, `SessionSystems`, test cube |
| In-headset setup panel | Partial | `ExperimenterPanel` actions exist, but its IMGUI window is invisible in the headset; no world-space panel/prefab |
| Start / timer / cap / end | Done | `SessionManager` + `SessionTimer` |
| Find clues | Partial | `Clue.MarkFound()` exists; no `Clue` in scene; nothing calls it (needs `InteractableUnityEventWrapper.WhenSelect`) |
| Tag clues | Partial | `Clue.MarkTagged/ToggleTagged` exist; no gesture/button calls them |
| Contamination zones | Partial | Logic done; no `ExclusionZone` in scene; possible stale hand anchors when tracking is lost (unverified) |
| Evidence board submit → end | Partial | `EvidenceBoard` + `AnswerChecker` done; no board/UI in scene; CaseX/Y data are placeholders |
| 10 Hz samples | Partial | World-space positions, not CaseRoot-local |
| Events | Done | all required events + `answer_submitted` |
| Meta file | Partial | Summary CSV per run + `experiment_config.json` (last config); no per-session meta JSON |
| Flushing | Done | 1 s, every event, `OnApplicationPause`, focus loss; session ended on quit |

Grab tool vs. "clue found": the tool doesn't add `InteractableUnityEventWrapper` or `Clue`, so grabbing fires
`WhenSelect` on the interactable but nothing reaches `CaseState`. Ghost hands are visual only (no effect on events).

**Open questions for the user**
1. Measure the lab to confirm the 4 x 4 m area (0.5 m margin → 3 x 3 m playable).
2. Delete template content (`TutorialInfo/`, `Readme.asset`, `Scenes/SampleScene`) and `HandNeutral.mat`? Currently kept.
3. Confirm the reasoning-error rule above.
4. OK to add Netcode for GameObjects (needed for Week 3 colocation)?
5. (Done, user) Package id renamed to `com.xmum.fyp.detective`.

**Next up (Week 2)**
- `CaseRoot` + scene placement (local `OVRSpatialAnchor`, optionally MRUK floor/table); log positions CaseRoot-local.
- Grab tool: also add `Clue` + `InteractableUnityEventWrapper` (`WhenSelect → Clue.MarkFound`) per interactable.
- In-headset experimenter/setup panel (world-space, ISDK poke or ray); clue tagging interaction.
- Evidence board UI in the scene; exclusion zones placed around protected evidence; Case X blockout inside 3 x 3 m.
- Hand-tracking validity (`OVRHand.IsTracked`, after verifying) so untracked hands aren't logged/counted.
- Per-session meta JSON; counterbalancing helper (Latin square for condition x case order).
- Later: Netcode + Colocation + Shared Spatial Anchors (Week 3); interpersonal distance + head-gaze logging.
