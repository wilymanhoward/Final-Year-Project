# CLAUDE.md: FYP co-located MR detective game

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

Not installed: AR Foundation, `com.unity.xr.meta-openxr`.

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
  Interaction/ ZoneEntryTracker, FloorFootprint, ExclusionZone, Clue, TrackedPointGate, ClueBounds, ClueResetter
  UI/          EvidenceSelection, EvidenceBoard, ExperimenterPanel, StatusLabel, ClueTagButton
  Meta/        (FYP.Detective.Meta.asmdef) ScenePlacement, ExperimenterControllerShortcuts, HandTrackingMonitor,
               HeadsetPresenceMonitor
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

_Last updated: session 6 (2026-10-14). Week 2 work runs in phases A–E, Week 3 in phases; stop after each phase for
the user's headset test, commit only after the user confirms it works._

**Where we are (start here)**
- **Phase B is closed** (session 6). Next: Week 3 Phase 1 (NGO install, see "Next").
- EditMode tests: **111/111 pass** in batch mode (session 6, before and after the NGO install).
- **NGO 2.13.3 + Unity Transport 2.6.0 installed** (session 6); everything compiles incl. Meta's NGO blocks.
- **Week 3 Phase 1 done and headset-verified** (session 6): host `2G0YC5ZG970B12` (10.72.55.149) + client
  `2G0YC5ZG4M00Z6` (10.72.55.197) on **campus Wi-Fi connected in 0.4 s** (no client isolation); host showed
  Partner connected / Partner left. Tests 134/134. The client config has hostAddress 10.72.55.149 but still
  participantId P01 (give it its own id before team runs). Campus IPs can change (DHCP) - re-check before each test.
- **Week 3 Phase 2 done and headset-verified** (session 6): discovery over Bluetooth + campus Wi-Fi, anchor shared
  through Meta's cloud in ~1.3 s, client loaded it ("Host's placement loaded (shared anchor)") and the user confirmed
  **the cube and Quad line up in both headsets**. Tests 158/158. After the test: a guard so an older in-flight share
  can't overwrite a newer one (compiled in the Editor, not batch-tested). The host showed "Placement height wrong
  (+0.08 m)" after a recenter burst (headset taken off) - likely the known floor-estimate false alarm, but it blocks
  Start on the host until B reloads.
- Untracked in the repo root (the user's, not Claude's): `.agents/`, `.codex/`, `AGENTS.md`. Ask before touching.
- Headsets: `2G0YC5ZG970B12` (used for all session 6 tests) and `2G0YC5ZG4M00Z6` (not used since session 5; it has
  no build with the locomotion fix).

**Done**
- Sessions 1–3: foundation (asmdefs, pure logic + tests, logging), Meta XR SDK 207 on Unity OpenXR, passthrough,
  rig with controllers + hands, ghost-hand shader, Interaction SDK rig, status audit.
- **Week 2 Phase A** (`51f2d5b`, headset-verified): CaseRoot, anchor placement (save/load/manual/clear, Spatial data
  permission), CaseRoot-local logging, `_meta.json`, controller shortcuts (RawButton, logged), start requires a
  placement, pause/active time, Interrupted snapshots.
- **Week 2 Phase B** (`bf58f32`, `cb78eb7`, session 6 close-out): clue wiring, tag button, static poke clues, clue
  reset, hand-tracking monitor, headset-off pauses, placement height check + settle wait.
  Headset-verified in session 6: Quad poke → clue_found + tag button, tag/untag, settle wait on load, start refused
  without placement, left-stick abort (end reason Aborted, final summary/meta), hmd + app pause union.
  **Clue reset not seen on the headset** (cube only carried ~2.6 m; user accepted it – logic is unit-tested).
- **Session 6 fixes:**
  - **Locomotion off** (`FYP > Disable Locomotion`): the ISDK rig's locomotion toggled crouch (-0.5 m rig move) on
    thumbstick click = our start/abort shortcuts → scene too high / floating. After the fix the head stays ~1.6–1.7 m
    above CaseRoot and placements are at y = 0.
  - **Boundary suppression** (`FYP > Suppress Boundary (Passthrough)`): the stationary boundary hid the scene ~2 m from
    the centre. At launch OVRManager logged "Cannot suppress boundary visibility" once; whether it then succeeded is
    unconfirmed (user didn't report). If the boundary still shows, set up a large roomscale boundary.
  - **Open pause counted in snapshots:** `SessionManager.Update` refreshes pause stats while a pause is open, so
    Interrupted summaries written with the headset off include the ongoing pause.
- **Known: `placement_check when=after_recenter` can be a false alarm.** In two runs after the headset was put back on
  it reported 0.27 / 0.447 m (ok=0) while the head stayed at the same height above CaseRoot – the Quest's floor estimate
  moved, not the anchor. During a run it is only logged. Treat CaseRoot-relative head height as the truth.
- Test-scene content: `CaseRoot/TestCube (delete me)` (grabbable clue `TestCube_delete_me`) and `CaseRoot/Quad`
  (static poke clue at 0.8/1.2/1.5, rotation 0 – a Quad faces local -Z, do NOT rotate it 180).
- Testing tip: the cable isn't needed during a test; the CSVs stay on the headset and `adb logcat -d` dumps the recent
  log afterwards (the buffer only holds the last minute or so of Unity output).

**Decisions made in session 5 (user)**
- Order: close Phase B → Week 3 networking → Week 2 Phase C (in-headset UI) and D (greybox cases) → E (wrap-up).
- **NGO 2.13.3 + Unity Transport 2.6.0 approved** (not installed yet). After installing: batch compile at once; if
  Meta's NGO assemblies fail to compile, revert the manifest immediately and tell the user.
- **Direct local connection approved** instead of Meta's UGS-based Colocation/CustomMatchmaking block: host
  UnityTransport on its LAN IP, IP:port in the `OVRColocationSession` advertisement, CaseRoot anchor shared with
  `OVRSpatialAnchor.ShareAsync(anchor, groupUuid)`, guest loads it with `LoadUnboundSharedAnchorsAsync(groupUuid)`.
  Shared anchors use Meta's cloud → network needs internet; show clear label messages for `FailureNetworkTimeout`,
  `FailureNetworkRequestFailed`, `FailureCloudStorageDisabled` ("Share Point Cloud Data"/Enhanced Spatial Services off).
- **Manual alignment fallback** for 2 headsets (both Manual-place on the same tape mark facing the same wall). If
  colocation isn't working by the end of Week 3, switch to the fallback and continue to Week 4.
- Hardware: 2 Quest 3, developer mode, Enhanced Spatial Services on, Bluetooth not yet checked, **campus Wi-Fi**
  (risk: client isolation / blocked ports → test first; backup = phone hotspot with internet).

**Next (in this order)**
1. ~~Batch tests~~ done. 
2. ~~**Week 3 Phase 1**~~ done (NGO install, LAN host/client with manual IP, campus Wi-Fi works).
3. ~~**Week 3 Phase 2**~~ done (discovery, shared CaseRoot anchor, failure texts, Manual fallback documented).
4. **Week 3 Phase 3:** network the shared state (`CaseState` authoritative on the host; clients send found / tag /
   submit requests; clue positions synced). Each headset still writes its own CSV (pairCode + participantId link them).
5. Then Week 2 Phase C (setup panel, participant Start, evidence board, end screen), D (greybox Case X/Y generator,
   room size in one constant), E (device test checklist, adb pull, wrap-up).

**Useful commands / tips**
- adb: `"/c/Program Files/Unity/Hub/Editor/6000.3.25f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"`.
  In Git Bash set `MSYS_NO_PATHCONV=1` before adb commands with `/sdcard/...` paths.
- Capture: `adb logcat -c` then `adb logcat -v time -s Unity:V > <file>` (run in background). The capture dies with
  exit 255 when the headset sleeps/disconnects; restart it.
- Pull data: `adb pull /sdcard/Android/data/com.xmum.fyp.detective/files/ ./StudyData/` (gitignored).
- Batch tests: see "How to test" (Unity must be closed; a batch run can take > 10 min after big changes).

**Open questions for the user**
1. Measure the lab to confirm 4 x 4 m (0.5 m margin → 3 x 3 m playable).
2. Delete template content (`TutorialInfo/`, `Readme.asset`, `Scenes/SampleScene`) and `HandNeutral.mat`?
3. Confirm the reasoning-error rule and the evidence-score definition.
4. Check Bluetooth on both headsets (needed for colocation discovery).
