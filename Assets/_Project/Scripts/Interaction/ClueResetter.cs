using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Puts a grabbable clue back at its start position (relative to <see cref="CaseRoot"/>) when it falls
    /// below the floor or ends up more than <see cref="maxDistanceFromRoot"/> from CaseRoot
    /// (<see cref="ClueBounds"/>). Never while it is held. Optionally also on every session start, so each run
    /// begins with the clues in place. Resets during a run are logged as "clue_reset".
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
        private Vector3 _homeLocalPosition;
        private Quaternion _homeLocalRotation;
        private bool _hasHome;
        private float _nextCheck;

        private void Awake()
        {
            _clue = GetComponent<Clue>();
            _body = GetComponent<Rigidbody>();
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
            if (_clue.IsHeld || !_clue.HasLocalAuthority) return;   // team runs: only the controlling headset resets

            Vector3 local = caseRoot.transform.InverseTransformPoint(transform.position);
            if (!ClueBounds.IsOutOfBounds(local, maxDistanceFromRoot, belowFloorMargin)) return;

            ResetToHome();
            if (session != null) session.RequestClueReset(_clue.ClueId);
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
            transform.SetPositionAndRotation(root.TransformPoint(_homeLocalPosition), root.rotation * _homeLocalRotation);
        }
    }
}
