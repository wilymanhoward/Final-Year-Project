using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Puts a grabbable clue back at its start position (relative to <see cref="CaseRoot"/>) when it falls
    /// below the floor or ends up more than <see cref="maxDistanceFromRoot"/> from CaseRoot, or when a dropped clue
    /// (<see cref="ClueGravity"/>) comes to rest in one of its case's exclusion zones, so retrieving it never forces
    /// a contamination error (<see cref="ClueBounds"/>, <see cref="ClueRestDetector"/>). Never while it is held.
    /// Optionally also on every session start, so each run begins with the clues in place. Resets during a run are
    /// logged as "clue_reset" with reason=out_of_bounds / in_zone.
    /// </summary>
    [RequireComponent(typeof(Clue))]
    public class ClueResetter : MonoBehaviour
    {
        [SerializeField] private CaseRoot caseRoot;
        [SerializeField] private SessionManager session;
        [SerializeField, Min(0.5f)] private float maxDistanceFromRoot = ClueBounds.DefaultMaxDistance;
        [SerializeField, Min(0f)] private float belowFloorMargin = ClueBounds.DefaultBelowFloorMargin;
        [SerializeField, Min(0.05f)] private float checkIntervalSeconds = 0.25f;
        [Tooltip("Put the clue back at its start position whenever a session starts.")]
        [SerializeField] private bool resetOnSessionStart = true;

        private Clue _clue;
        private Rigidbody _body;
        private ClueGravity _gravity;
        private ExclusionZone[] _zones = new ExclusionZone[0];
        private readonly ClueRestDetector _rest = new ClueRestDetector();
        private Vector3 _homeLocalPosition;
        private Quaternion _homeLocalRotation;
        private bool _hasHome;
        private float _nextCheck;

        private void Awake()
        {
            _clue = GetComponent<Clue>();
            _body = GetComponent<Rigidbody>();
            _gravity = GetComponent<ClueGravity>();
            var content = GetComponentInParent<CaseContent>(true);   // zones of this clue's own case only
            if (content != null) _zones = content.GetComponentsInChildren<ExclusionZone>(true);
            if (caseRoot == null) caseRoot = FindFirstObjectByType<CaseRoot>();
            if (session == null) session = FindFirstObjectByType<SessionManager>();
        }

        private void Start() => CaptureHome();

        private void OnEnable()
        {
            if (session != null) session.SessionStarted += HandleSessionStarted;
        }

        private void OnDisable()
        {
            if (session != null) session.SessionStarted -= HandleSessionStarted;
        }

        /// <summary>Stores the current pose (relative to CaseRoot) as the start position.</summary>
        public void CaptureHome()
        {
            if (caseRoot == null) return;
            Transform root = caseRoot.transform;
            _homeLocalPosition = root.InverseTransformPoint(transform.position);
            _homeLocalRotation = Quaternion.Inverse(root.rotation) * transform.rotation;
            _hasHome = true;
        }

        private void HandleSessionStarted()
        {
            if (resetOnSessionStart && !_clue.IsHeld && _clue.HasLocalAuthority) ResetToHome();
        }

        private void Update()
        {
            if (!_hasHome || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + checkIntervalSeconds;
            if (_clue.IsHeld || !_clue.HasLocalAuthority)   // team runs: only the controlling headset resets
            {
                _rest.Reset();
                return;
            }

            Vector3 local = caseRoot.transform.InverseTransformPoint(transform.position);
            if (ClueBounds.IsOutOfBounds(local, maxDistanceFromRoot, belowFloorMargin))
            {
                ResetToHome();
                if (session != null) session.RequestClueReset(_clue.ClueId, ClueResetReason.OutOfBounds);
                return;
            }

            if (_gravity == null || !_gravity.IsSimulated) { _rest.Reset(); return; }
            if (!_rest.Update(_gravity.Speed, Time.unscaledTimeAsDouble) || !InAnyZone()) return;
            ResetToHome();
            if (session != null) session.RequestClueReset(_clue.ClueId, ClueResetReason.InZone);
        }

        private bool InAnyZone()
        {
            foreach (ExclusionZone zone in _zones)
            {
                if (zone == null || !zone.isActiveAndEnabled) continue;
                Transform t = zone.transform;
                if (ClueBounds.IsInZone(transform.position, t.position, t.eulerAngles.y, zone.Size)) return true;
            }
            return false;
        }

        private void ResetToHome()
        {
            if (!_hasHome || caseRoot == null) return;
            Transform root = caseRoot.transform;
            if (_body != null && !_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
            if (_gravity != null) _gravity.MakeStill();   // kinematic first, so the teleport isn't interpolated
            transform.SetPositionAndRotation(root.TransformPoint(_homeLocalPosition), root.rotation * _homeLocalRotation);
            _rest.Reset();
        }
    }
}
