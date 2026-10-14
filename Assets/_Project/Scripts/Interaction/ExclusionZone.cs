using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Invisible "don't step / don't touch" area around a piece of evidence.
    /// Each frame it projects the head and both hands onto the floor and tests
    /// them against a rectangle centred on this transform (position + yaw only;
    /// scale is ignored, use <see cref="size"/>). The zone counts as occupied if
    /// any tracked point is inside. Each outside -> inside transition is one
    /// contamination error, with a cooldown per zone (see <see cref="ZoneEntryTracker"/>).
    /// Only counts while a session is running. Untracked points are ignored, and a point that regains
    /// tracking while already inside doesn't count as entering (<see cref="TrackedPointGate"/>).
    /// </summary>
    public class ExclusionZone : MonoBehaviour
    {
        [Tooltip("Unique id written to the log, e.g. X_body_zone.")]
        [SerializeField] private string zoneId = "zone";

        [Tooltip("Floor footprint in metres: x = width (local X), y = depth (local Z).")]
        [SerializeField] private Vector2 size = new Vector2(1f, 1f);

        [SerializeField, Min(0f)] private float cooldownSeconds = 1f;
        [SerializeField] private bool checkHead = true;
        [SerializeField] private bool checkHands = true;

        [SerializeField] private SessionManager session;
        [SerializeField] private PlayerRig rig;

        [Header("Debug")]
        [Tooltip("Optional renderer (e.g. a flat quad) shown only when debug is on. Keep off for participants.")]
        [SerializeField] private Renderer debugVisual;
        [SerializeField] private bool showDebugVisual;

        private ZoneEntryTracker _tracker;
        private readonly TrackedPointGate _headGate = new TrackedPointGate();
        private readonly TrackedPointGate _leftGate = new TrackedPointGate();
        private readonly TrackedPointGate _rightGate = new TrackedPointGate();

        public string ZoneId => zoneId;
        public Vector2 Size => size;
        public int EntryCount => _tracker?.EntryCount ?? 0;
        public bool IsOccupied => _tracker != null && _tracker.IsInside;

        public bool ShowDebugVisual
        {
            get => showDebugVisual;
            set { showDebugVisual = value; ApplyDebugVisual(); }
        }

        private void Awake()
        {
            _tracker = new ZoneEntryTracker(cooldownSeconds);
            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (rig == null) rig = FindFirstObjectByType<PlayerRig>();
            ApplyDebugVisual();
        }

        private void OnEnable()
        {
            if (session != null) session.SessionStarted += HandleSessionStarted;
        }

        private void OnDisable()
        {
            if (session != null) session.SessionStarted -= HandleSessionStarted;
        }

        private void HandleSessionStarted()
        {
            _tracker.Reset();
            _headGate.Reset();
            _leftGate.Reset();
            _rightGate.Reset();
        }

        private void Update()
        {
            if (session == null || rig == null || !session.IsRunning) return;

            bool headIn = checkHead && IsPointInside(BodyPart.Head, _headGate);
            bool leftIn = checkHands && IsPointInside(BodyPart.LeftHand, _leftGate);
            bool rightIn = checkHands && IsPointInside(BodyPart.RightHand, _rightGate);
            bool inside = headIn || leftIn || rightIn;

            if (_tracker.Update(inside, SessionManager.Now))
            {
                BodyPart part = headIn ? BodyPart.Head : leftIn ? BodyPart.LeftHand : BodyPart.RightHand;
                session.RequestContamination(zoneId, part);
            }
        }

        private bool IsPointInside(BodyPart part, TrackedPointGate gate)
        {
            bool tracked = rig.IsAvailable(part);
            bool rawInside = tracked
                && FloorFootprint.Contains(rig.Get(part).position, transform.position, transform.eulerAngles.y, size);
            return gate.Update(tracked, rawInside);
        }

        private void ApplyDebugVisual()
        {
            if (debugVisual != null) debugVisual.enabled = showDebugVisual;
        }

        private void OnValidate()
        {
            size = new Vector2(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y));
            if (_tracker != null) _tracker.CooldownSeconds = cooldownSeconds;
            ApplyDebugVisual();
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = IsOccupied ? new Color(1f, 0.2f, 0.2f, 0.6f) : new Color(1f, 0.6f, 0f, 0.4f);
            var old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0f, 1f, 0f), new Vector3(size.x, 2f, size.y));
            Gizmos.DrawCube(Vector3.zero, new Vector3(size.x, 0.01f, size.y));
            Gizmos.matrix = old;
        }
    }
}
