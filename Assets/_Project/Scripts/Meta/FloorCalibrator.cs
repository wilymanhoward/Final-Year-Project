using UnityEngine;

namespace FYP.Detective.Meta
{
    /// <summary>
    /// Sets the floor by touch, without the Quest's floor settings (user, 2026-11-12: after the headset is handed over the
    /// Quest's floor, and the anchor with it, can come back ~40 cm off; 2026-11-19: the depth-sensor check sometimes got the
    /// height wrong and needed a placement first, which the floor guard then refused - so touch only, and before placing).
    /// Someone holds one hand flat on the floor for 1 s (<see cref="TouchFloorDetector"/>, hand joints from
    /// <see cref="HandJointSources"/>). The touched height goes to <see cref="ScenePlacement.SetTouchedFloor"/>, so
    /// placing (and its floor guard) uses it, and an existing scene is moved onto it through
    /// <see cref="SessionManager.CorrectFloor"/>. Starts from the setup panel's "Fix floor" button
    /// (<see cref="FixFloor"/>), and by itself when the headset is put on between runs (<c>OVRManager.HMDMounted</c>,
    /// Scripts/OVRManager.cs:305), with a prompt on the label. Added next to ScenePlacement by Set Up Headset UI.
    /// </summary>
    public class FloorCalibrator : MonoBehaviour
    {
        [SerializeField] private SessionManager session;
        [SerializeField] private PlayerRig rig;
        [SerializeField] private ScenePlacement placement;

        private const float TouchTimeoutSeconds = 30f;
        private const float MountDelaySeconds = 1.5f;     // let tracking settle after the headset goes on
        private const float PlacementDelaySeconds = 0.5f;  // CaseRoot sits on its anchor by then

        private bool _touching, _manual;
        private float _touchStart, _startAt = -1f, _applyAt = -1f;
        private string _trigger = "";
        private readonly TouchFloorDetector[] _touch = { new TouchFloorDetector(), new TouchFloorDetector() };
        private readonly Vector3[] _joints = new Vector3[HandJointCsv.JointCount];

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (rig == null) rig = FindFirstObjectByType<PlayerRig>();
            if (placement == null) placement = FindFirstObjectByType<ScenePlacement>();
        }

        private void OnEnable()
        {
            OVRManager.HMDMounted += HandleMounted;
            if (session != null) session.PlacementChanged += HandlePlacement;
        }

        private void OnDisable()
        {
            OVRManager.HMDMounted -= HandleMounted;
            if (session != null) session.PlacementChanged -= HandlePlacement;
            _touching = false;
        }

        /// <summary>Setup panel button: asks for a hand on the floor. Works before the scene is placed.</summary>
        public void FixFloor() => BeginTouch(true, "button");

        // Not during a run: a participant's flat hand on the floor while picking up a clue must not move the scene.
        private void HandleMounted()
        {
            if (session != null && session.IsRunning) return;
            _startAt = Time.unscaledTime + MountDelaySeconds;
        }

        // A new placement stands on FloorY already; this records the touched floor as checked for the height check
        // (and puts a loaded anchor, saved at another floor height, onto it).
        private void HandlePlacement(PlacementRecord _)
        {
            if (placement != null && placement.HasTouchedFloor) _applyAt = Time.unscaledTime + PlacementDelaySeconds;
        }

        private bool HasPlacement() => session != null && session.Placement.Method != PlacementMethod.None;

        private void Update()
        {
            if (_startAt > 0f && Time.unscaledTime >= _startAt)
            {
                _startAt = -1f;
                if (!_touching && (session == null || !session.IsRunning)) BeginTouch(false, "mount");
            }
            if (_applyAt > 0f && Time.unscaledTime >= _applyAt)
            {
                _applyAt = -1f;
                if (HasPlacement() && placement.HasTouchedFloor)
                    session.CorrectFloor(placement.FloorY, "touch", "placement", FloorEstimate.ManualCorrectionThreshold);
            }
            if (_touching) UpdateTouch();
        }

        private void BeginTouch(bool manual, string trigger)
        {
            foreach (var t in _touch) t.Reset();
            _touching = true;
            _manual = manual;
            _trigger = trigger;
            _touchStart = Time.unscaledTime;
            Say("Floor check: crouch and hold one hand flat on the floor for a second");
        }

        private void UpdateTouch()
        {
            if (Time.unscaledTime - _touchStart > TouchTimeoutSeconds)
            {
                _touching = false;
                if (_manual) Say("Floor not set (no hand on the floor) - press Fix floor to try again");
                return;
            }
            IHandJointSource source = HandJointSources.Current;
            if (source == null || rig == null || rig.Head == null || placement == null) return;
            for (int side = 0; side < 2; side++)
            {
                BodyPart part = side == 0 ? BodyPart.LeftHand : BodyPart.RightHand;
                if (!rig.IsTracked(part) || !source.TryGetJoints(part, _joints)) { _touch[side].Reset(); continue; }
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (Vector3 j in _joints) { lo = Mathf.Min(lo, j.y); hi = Mathf.Max(hi, j.y); }
                if (!_touch[side].Update(Time.unscaledTimeAsDouble, rig.Head.position.y, lo, hi, out float floorY)) continue;
                // Far from the Quest's own floor = a table or a knee, not the floor.
                if (Mathf.Abs(floorY - placement.TrackingFloorY) > FloorEstimate.MaxCorrection) { _touch[side].Reset(); continue; }
                _touching = false;
                Apply(floorY);
                return;
            }
        }

        private void Apply(float floorY)
        {
            placement.SetTouchedFloor(floorY);
            if (!HasPlacement())
            {
                Debug.Log($"[FloorCalibrator] Floor touched ({_trigger}) {floorY - placement.TrackingFloorY:+0.000;-0.000} m from the Quest's floor");
                Say("Floor set by touch - now place the scene");
                return;
            }
            float moved = session.CorrectFloor(floorY, "touch", _trigger, FloorEstimate.ManualCorrectionThreshold);
            Debug.Log($"[FloorCalibrator] Floor touched ({_trigger}): scene moved {moved:+0.000;-0.000} m");
            Say(moved != 0f
                ? "Floor fixed: scene moved " + Mathf.RoundToInt(Mathf.Abs(moved) * 100f) + " cm " + (moved > 0f ? "up" : "down") + " onto the floor you touched"
                : "Floor checked: the scene is on the floor you touched");
        }

        private void Say(string text)
        {
            if (placement != null) placement.SetStatus(text);
            else Debug.Log("[FloorCalibrator] " + text);
        }
    }
}
