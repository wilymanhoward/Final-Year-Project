using System.Collections.Generic;
using Meta.XR;
using UnityEngine;

namespace FYP.Detective.Meta
{
    /// <summary>
    /// Puts the scene back on the real floor without the Quest's floor settings (user, 2026-11-12: after the headset is
    /// taken off and handed over, the Quest's floor and the anchor can come back ~40 cm off). Runs 2 s after the headset
    /// is put on (<c>OVRManager.HMDMounted</c>, Scripts/OVRManager.cs:305), after a recenter and after every placement,
    /// and on the setup panel's "Fix floor" button (<see cref="FixFloor"/>).
    /// Depth: MRUK 207 <c>EnvironmentRaycastManager</c> (Core/Scripts/EnvironmentRaycastManager.cs: static
    /// <c>IsSupported</c>, <c>Raycast(Ray, out EnvironmentRaycastHit, float)</c>; OpenXR provider, needs the spatial data
    /// permission, independent of the occlusion toggle), enabled only while checking (it costs GPU). 9 rays 35-55 deg below
    /// the head's forward for 1.5 s, hits within 3 m (depth noise grows with distance and grazing angle; was 25-45 deg / 6 m,
    /// user 2026-11-13: floor a little low); the floor = <see cref="FloorEstimate.TryFindFloor"/>.
    /// Backup (button only, when depth fails): <see cref="TouchFloorDetector"/> - a hand held flat on the floor for 1 s.
    /// Moves CaseRoot through <see cref="SessionManager.CorrectFloor"/>. Added next to ScenePlacement by Set Up Headset UI.
    /// </summary>
    public class FloorCalibrator : MonoBehaviour
    {
        [SerializeField] private SessionManager session;
        [SerializeField] private PlayerRig rig;
        [SerializeField] private ScenePlacement placement;

        private const float AutoDelaySeconds = 2f;      // let tracking settle after put-on / recenter / placing
        private const float DepthTimeoutSeconds = 5f;   // includes the raycaster's start-up
        private const float SampleSeconds = 1.5f;
        private const float MaxRayDistance = 3f;
        private const float TouchTimeoutSeconds = 30f;
        private const float MinDownSlope = -0.34f;      // rays less than 20 deg below horizontal would hit walls

        private enum Mode { Idle, Depth, Touch }

        private Mode _mode;
        private float _modeStart, _firstHitAt, _autoAt = -1f;
        private bool _manual;
        private string _trigger = "";
        private EnvironmentRaycastManager _raycaster;
        private readonly List<float> _hits = new List<float>(1024);
        private readonly TouchFloorDetector[] _touch = { new TouchFloorDetector(), new TouchFloorDetector() };
        private readonly Vector3[] _joints = new Vector3[HandJointCsv.JointCount];
        private static readonly Vector3[] RayDirections = BuildRays();

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (rig == null) rig = FindFirstObjectByType<PlayerRig>();
            if (placement == null) placement = FindFirstObjectByType<ScenePlacement>();
        }

        private void OnEnable()
        {
            OVRManager.HMDMounted += HandleMounted;
            if (session == null) return;
            session.PlacementChanged += HandlePlacement;
            session.TrackingRecentered += HandleRecentered;
        }

        private void OnDisable()
        {
            OVRManager.HMDMounted -= HandleMounted;
            if (session != null)
            {
                session.PlacementChanged -= HandlePlacement;
                session.TrackingRecentered -= HandleRecentered;
            }
            StopRaycaster();
            _mode = Mode.Idle;
        }

        /// <summary>Setup panel button: depth check now; if that fails, asks for a hand on the floor.</summary>
        public void FixFloor()
        {
            if (!HasPlacement()) { Say("Place the scene first, then Fix floor"); return; }
            _manual = true;
            _trigger = "button";
            BeginDepth();
        }

        private void HandleMounted() => Schedule("mount");
        private void HandlePlacement(PlacementRecord _) => Schedule("placement");
        private void HandleRecentered() => Schedule("recenter");

        private void Schedule(string trigger)
        {
            if (_mode == Mode.Touch) return;   // someone is already touching the floor
            _autoAt = Time.unscaledTime + AutoDelaySeconds;
            _trigger = trigger;
        }

        private bool HasPlacement() => session != null && session.Placement.Method != PlacementMethod.None;

        private void Update()
        {
            if (_autoAt > 0f && Time.unscaledTime >= _autoAt && _mode == Mode.Idle)
            {
                _autoAt = -1f;
                if (HasPlacement()) { _manual = false; BeginDepth(); }
            }
            if (_mode == Mode.Depth) UpdateDepth();
            else if (_mode == Mode.Touch) UpdateTouch();
        }

        private void BeginDepth()
        {
            if (!EnvironmentRaycastManager.IsSupported || rig == null || rig.Head == null) { DepthFailed("depth sensor not available"); return; }
            if (_raycaster == null)
            {
                var go = new GameObject("FloorCheckRaycaster");
                go.transform.SetParent(transform, false);
                _raycaster = go.AddComponent<EnvironmentRaycastManager>();
            }
            _raycaster.enabled = true;
            _hits.Clear();
            _mode = Mode.Depth;
            _modeStart = Time.unscaledTime;
            _firstHitAt = -1f;
            if (_manual) Say("Checking the floor... look ahead at the floor");
        }

        private void UpdateDepth()
        {
            Transform head = rig.Head;
            foreach (Vector3 local in RayDirections)
            {
                Vector3 dir = head.rotation * local;
                if (dir.y > MinDownSlope) continue;
                if (!_raycaster.Raycast(new Ray(head.position, dir), out EnvironmentRaycastHit hit, MaxRayDistance)) continue;
                if (hit.normalConfidence > 0f && hit.normal.y < 0.7f) continue;   // not a horizontal surface
                _hits.Add(hit.point.y);
                if (_firstHitAt < 0f) _firstHitAt = Time.unscaledTime;
            }
            if (_firstHitAt >= 0f && Time.unscaledTime - _firstHitAt >= SampleSeconds)
            {
                StopRaycaster();
                float sceneY = session.CaseRootFloorY;
                if (FloorEstimate.TryFindFloor(_hits, out float floorY, out int support) &&
                    FloorEstimate.IsPlausible(sceneY, floorY, head.position.y))
                {
                    _mode = Mode.Idle;
                    Report("depth", session.CorrectFloor(floorY, "depth", _trigger, Threshold), support + " depth hits");
                }
                else DepthFailed(_hits.Count + " hits, no clear floor");
                return;
            }
            if (Time.unscaledTime - _modeStart > DepthTimeoutSeconds)
            {
                StopRaycaster();
                DepthFailed("no depth hits");
            }
        }

        private void DepthFailed(string why)
        {
            Debug.Log("[FloorCalibrator] Depth floor check failed: " + why);
            _mode = Mode.Idle;
            if (!_manual) return;   // automatic checks fail quietly; the button falls back to touch
            foreach (var t in _touch) t.Reset();
            _mode = Mode.Touch;
            _modeStart = Time.unscaledTime;
            Say("Floor: crouch and hold one hand flat on the floor for 1 s");
        }

        private void UpdateTouch()
        {
            if (Time.unscaledTime - _modeStart > TouchTimeoutSeconds)
            {
                _mode = Mode.Idle;
                Say("Floor not set (no hand on the floor) - press Fix floor to try again");
                return;
            }
            IHandJointSource source = HandJointSources.Current;
            if (source == null || rig == null || rig.Head == null) return;
            for (int side = 0; side < 2; side++)
            {
                BodyPart part = side == 0 ? BodyPart.LeftHand : BodyPart.RightHand;
                if (!rig.IsTracked(part) || !source.TryGetJoints(part, _joints)) { _touch[side].Reset(); continue; }
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (Vector3 j in _joints) { lo = Mathf.Min(lo, j.y); hi = Mathf.Max(hi, j.y); }
                if (!_touch[side].Update(Time.unscaledTimeAsDouble, rig.Head.position.y, lo, hi, out float floorY)) continue;
                if (Mathf.Abs(floorY - session.CaseRootFloorY) > FloorEstimate.MaxCorrection) { _touch[side].Reset(); continue; }
                _mode = Mode.Idle;
                Report("touch", session.CorrectFloor(floorY, "touch", _trigger, FloorEstimate.ManualCorrectionThreshold), "hand on the floor");
                return;
            }
        }

        // The Fix floor button applies even small differences; automatic checks only clear ones.
        private float Threshold => _manual ? FloorEstimate.ManualCorrectionThreshold : FloorEstimate.AutoCorrectionThreshold;

        private void Report(string method, float moved, string how)
        {
            Debug.Log($"[FloorCalibrator] {method} ({_trigger}, {how}): moved {moved:+0.000;-0.000} m");
            if (moved != 0f)
                Say("Floor fixed: scene moved " + Mathf.RoundToInt(Mathf.Abs(moved) * 100f) + " cm " + (moved > 0f ? "up" : "down") +
                    " onto the real floor (" + how + ")");
            else if (_manual) Say("Floor checked: the scene is on the real floor (" + how + ")");
        }

        private void Say(string text)
        {
            if (placement != null) placement.SetStatus(text);
            else Debug.Log("[FloorCalibrator] " + text);
        }

        private void StopRaycaster()
        {
            if (_raycaster != null) _raycaster.enabled = false;
        }

        private static Vector3[] BuildRays()
        {
            var rays = new List<Vector3>();
            foreach (float pitch in new[] { 35f, 45f, 55f })
                foreach (float yaw in new[] { -20f, 0f, 20f })
                    rays.Add(Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward);
            return rays.ToArray();
        }
    }
}
