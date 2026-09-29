# Setup checklist (manual Unity Editor steps)

Do these in order. Tick them off as you go. Claude can't operate the Editor, so everything here is yours.
Where Meta menu names differ slightly in your SDK version, use the closest match and tell Claude, so
this file can be corrected.

## 0. Before opening the project
- [ ] Fix the earlier "An Application Control policy has blocked this file" error (Smart App Control / unblock
      the Unity Editor folder); otherwise nothing compiles.
- [ ] Install Git LFS once on your PC: `git lfs install`.
- [ ] `git pull` this branch, then open the project in **Unity 6000.3.25f1**.

## 1. Confirm the foundation compiles (before touching Meta)
- [ ] Console shows no red errors. (Yellow CS0649 "never assigned" warnings on Inspector fields are harmless.)
- [ ] **Window > General > Test Runner > EditMode > Run All**. All tests should pass (60).
      If any fail inside Unity but not for Claude, copy the failure message into the next session.
- [ ] Commit the `.meta` files Unity generated for the new folders/scripts.

## 2. Meta XR SDK
- [ ] **Window > Package Manager**: check that **Meta XR All-in-One SDK** (`com.meta.xr.sdk.all`) is installed.
      The committed `Packages/manifest.json` currently does **not** contain it. If it's missing, install it
      (Package Manager > + > Add package by name > `com.meta.xr.sdk.all`, or from the Asset Store/My Assets).
- [ ] **Commit and push `Packages/manifest.json` and `Packages/packages-lock.json`** so Claude can see the exact version.
- [ ] Note the SDK version in `CLAUDE.md` (Current status).

## 3. Platform and XR
- [ ] **File > Build Profiles** > Android > **Switch Platform**.
- [ ] **Edit > Project Settings > XR Plug-in Management** > Android tab: enable the provider the Meta Project
      Setup Tool recommends for your SDK version (**OpenXR** with the *Meta Quest* feature group, or **Oculus**).
- [ ] **Meta > Tools > Project Setup Tool**: select Android, click **Fix All**, then **Apply All**. Repeat until
      nothing required is left. (It sets min API level, ARM64, IL2CPP, Vulkan, colour space, etc.)
- [ ] **Player Settings > Android**: set a real package name, e.g. `com.<yourname>.fypdetective`, and
      Company/Product name. (It is still the template default.)
- [ ] **Project Settings > Quality**: make sure the Android default quality level uses **Mobile_RPAsset**.
- [ ] In **Mobile_RPAsset**: HDR **off**, MSAA **4x**, Post-processing off unless needed (72 fps target).

## 4. Main scene
- [ ] Create `Assets/_Project/Scenes/Main.unity` and open it. Delete the default **Main Camera**.
- [ ] **Meta > Tools > Building Blocks**: add **Camera Rig**.
- [ ] Add **Passthrough** (check that the camera background becomes transparent/black and passthrough shows on device).
- [ ] Add **Hand Tracking** and/or **Controller Tracking** (default plan: both).
      On **OVRManager** (on the camera rig): *Quest Features > Hand Tracking Support* = **Controllers And Hands**.
- [ ] Lighting: remove the default Directional Light's shadows or set the shadow distance small (performance).

## 5. Core systems
- [ ] Select the camera rig root and **Add Component > PlayerRig**. It auto-fills
      *Head = CenterEyeAnchor*, *Left Hand = LeftHandAnchor*, *Right Hand = RightHandAnchor*. Check all three are set.
- [ ] Create an empty GameObject **SessionSystems** at the origin with:
  - [ ] **SessionManager**: set Pair/Participant codes, Condition, Case, Time Cap (15). Leave *Load Saved Config* on.
  - [ ] **DataLogger**: assign *Session* = SessionSystems, *Rig* = the PlayerRig. Sample rate 10, flush 1 s.
  - [ ] **ExperimenterPanel**: assign *Session*.

## 6. Case data
- [ ] In `Assets/_Project/Cases/CaseX`: **right-click > Create > FYP > Case Definition** → `CaseX_Definition`.
      Set *Case Id* = X, title, suspects (id + name), *Correct Suspect Id*, *Key Evidence Ids*.
- [ ] Same for `Cases/CaseY` → `CaseY_Definition` (Case Id = Y).
- [ ] Assign both to **SessionManager** (*Case X*, *Case Y*).
- [ ] Use simple, stable ids, prefixed per case: `X_knife`, `X_letter`, `X_suspect_butler`, …

## 7. Clue prefab (grab via Meta Interaction SDK, no custom grab code)
- [ ] Create a placeholder (e.g. a 10 cm cube), select it, and in **Building Blocks** add the **grab** block
      (named *Grab Interaction* / *Hand Grab* depending on version) so it gets `Grabbable` + grab interactable(s).
- [ ] Add **Clue** to the root: set *Clue Id* (must match the CaseDefinition), *Display Name*, *Is Key Evidence*.
- [ ] Add **InteractableUnityEventWrapper** (namespace `Oculus.Interaction`) next to each grab interactable
      (e.g. one for `HandGrabInteractable`, one for `GrabInteractable` if you use controllers):
  - [ ] *Interactable View* = that interactable.
  - [ ] **When Select** → + → drag the Clue object → `Clue.MarkFound()`.
- [ ] Optional: `Clue.onFound` → a small highlight/sound so players get feedback.
- [ ] Save as a prefab in `Assets/_Project/Prefabs/Clue.prefab`. Place instances in the scene.
- [ ] Tagging: decide how players tag (e.g. a poke button on the board per clue). Wire it to `Clue.MarkTagged()`
      or `Clue.ToggleTagged()`.

## 8. Exclusion zones
- [ ] For each protected piece of evidence: empty child GameObject `Zone_<id>` on the floor under the evidence.
- [ ] Add **ExclusionZone**: unique *Zone Id*, *Size* (width x depth in metres), cooldown 1 s,
      *Check Head* and *Check Hands* on. Only the transform's **Y rotation** matters; keep scale at 1.
- [ ] Optional debug visual: child **Quad** rotated flat, semi-transparent material, **remove its MeshCollider**,
      assign it to *Debug Visual*. Keep *Show Debug Visual* **off** for participants.
- [ ] The orange gizmo in the Scene view shows the footprint; check it doesn't block required walking paths.

## 9. Evidence board
- [ ] World-space **Canvas** (Render Mode = World Space, scale ≈ 0.001) with buttons for each suspect and each evidence item.
- [ ] Make it usable in XR with the Interaction SDK (ray and/or poke on a canvas, via the Building Blocks / Interaction SDK
      canvas setup for your version).
- [ ] Add **EvidenceBoard** to the canvas root, assign *Session*, *Min Evidence* = 1.
- [ ] Suspect buttons: `onClick` → `EvidenceBoard.SelectSuspect(string)` with the suspect id typed in.
- [ ] Evidence buttons: `onClick` → `EvidenceBoard.ToggleEvidence(string)` with the clue id.
- [ ] Submit button: `onClick` → `EvidenceBoard.Submit()`; `onCanSubmitChanged` → `Button.interactable` (dynamic bool).
- [ ] `onSelectionChanged` → a `TMP_Text.text` (dynamic string) to show the current choice.

## 10. Experimenter controls in the headset (optional but recommended)
The IMGUI window only shows in the Editor/Link. For standalone runs:
- [ ] Small world-space canvas, reachable by the experimenter, with buttons wired to `ExperimenterPanel`:
      `ToggleCondition`, `ToggleCase`, `NextParticipant`, `PreviousParticipant`, `NextPair`, `StartSession`,
      `AbortSession`, `ResetSession`; `onStatusChanged` → a `TMP_Text.text`.
- [ ] Or preset codes without rebuilding: edit `experiment_config.json` and
      `adb push experiment_config.json /sdcard/Android/data/<package id>/files/`.

## 11. Fit the room
- [ ] Keep all content inside a **4 x 4 m** area centred on the origin, with at least 0.5 m clearance from walls.
- [ ] Sketch the walking paths so exclusion zones are avoidable.

## 12. Editor test (no headset)
- [ ] Press Play. The Experimenter window appears top-left. **F5** start, **F6** abort, **F7** reset, **F8** hide window.
- [ ] Start, wait ~5 s, abort. Check the Console for `[DataLogger] Logging to ...` and open the CSV:
      header + ~10 rows/s + session_start/session_end events + a `_summary.csv`.

## 13. Build to Quest 3
- [ ] **File > Build Profiles**: add `Main.unity` (remove SampleScene), Android, *Development Build* on for the first tests.
- [ ] Build And Run with the headset connected (developer mode on).
- [ ] Check passthrough, grabbing (hands and controllers), clue_found in the log, and zone entries.
- [ ] Measure fps with the **OVR Metrics Tool** (or the `frame_dt_ms` column): target ≥ 72 fps (≤ 13.9 ms).
- [ ] Pull data: `adb pull /sdcard/Android/data/<package id>/files/ ./StudyData/` (the `StudyData/` folder is gitignored).

## 14. Commit
- [ ] Commit scenes, prefabs, CaseDefinition assets, ProjectSettings changes and all `.meta` files. Push.
- [ ] Tell Claude which steps were done and anything that differed from this list.

## Troubleshooting: app stuck on the loading screen on the Quest (diagnosed from the headset log)
Symptom: the Quest shows the dark Meta loading environment forever. `adb logcat` shows Unity starting fine and
endless `Waiting for available buffer SurfaceView[...UnityPlayerGameActivity]` lines, and **no OpenXR/VR session**.
Cause: no XR provider. `Assets/XR/XRGeneralSettingsPerBuildTarget.asset` has empty `Keys/Values`, and neither
`com.unity.xr.openxr` nor `com.unity.xr.oculus` is installed, so the app runs as a flat 2D window and never
starts a VR session. (Meta XR SDK 207 itself is installed.) Also, `SampleScene` (the only scene in Build Profiles)
has no OVRCameraRig/OVRManager. Fix, in the Editor:
- [ ] **Edit > Project Settings > XR Plug-in Management > Android tab (the Quest icon)**: tick **OpenXR**.
      Accept the install prompt (the Unity OpenXR Plugin; Meta recommends 1.15.x for SDK v74+; let Unity choose the
      version that matches 6000.3). Do the same on the desktop tab only if you want to test in Link.
- [ ] **XR Plug-in Management > OpenXR > Android tab > OpenXR Feature Groups**: tick **Meta XR** (Meta XR Feature,
      Meta XR Foveation, Meta XR Subsampled Layout on).
- [ ] **Meta > Tools > Project Setup Tool**: Android tab > **Fix All**, then **Apply All**.
- [ ] Put a camera rig in the scene you build: open `Assets/_Project/Scenes/Main.unity` (section 4), delete the
      default Main Camera, add the **Camera Rig** (and **Passthrough**) building blocks, then add that scene to
      **Build Profiles > Scene List** and remove `SampleScene`.
- [ ] Rebuild and install. On a good run the log contains OpenXR/`XR_` lines and the headset switches to your scene.
- [ ] Commit and push `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings/` and `Assets/XR/`.

## Troubleshooting: app takes a long time to launch on the Quest
Already fixed in the repo (pull first): Unity splash screen off; Android now uses only `Mobile_RPAsset`
(it was also bundling the PC render pipeline's shaders); HDR, terrain holes, LOD cross-fade, light cookies,
light layers and lens flares off (fewer shader variants), MSAA 4x, render scale 1.0, shadow distance 10 m.

Check these on your PC, in this order:
- [ ] **Is it actually a VR app?** If the app opens in a flat 2D window, XR isn't set up: do sections 2–3
      (Meta XR SDK + XR Plug-in Management + Project Setup Tool) and push `Packages/manifest.json`.
- [ ] **File > Build Profiles > Android**: untick **Autoconnect Profiler**, **Deep Profiling**,
      **Script Debugging** and especially **Wait For Managed Debugger**. Any of these can make the app sit on the
      loading screen waiting for a connection. For study builds, untick **Development Build** entirely.
- [ ] **The first launch after installing is always slower** (Vulkan shaders are compiled and cached on the device).
      Launch it a second time before judging. If the second launch is fast, this was the cause.
- [ ] Build Profiles > Android > **Run Device** = your Quest, and use **Build And Run** (not Patch And Run) for timing tests.
- [ ] Measure it: with the Quest connected, run
      `adb logcat -c` then launch the app, then `adb logcat -d -s Unity ActivityManager > launch_log.txt`
      and send `launch_log.txt` to Claude (it shows where the time goes).
