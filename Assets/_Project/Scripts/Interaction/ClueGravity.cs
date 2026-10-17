using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Gravity for a grabbable clue (user decision, session 6): a clue stays exactly where the scene put it until
    /// someone moves it; once it has been held and let go it is "loose" and falls. Capped toss (user decision, session
    /// 6): the release velocity is measured here from the clue's last positions while held (<see cref="TossEstimator"/>,
    /// at most <see cref="TossEstimator.DefaultMaxSpeed"/> m/s, no spin) - the Interaction SDK's own throw stays off, so
    /// the cap is exact - and a release faster than the toss threshold is logged as clue_thrown (speed, capped, by).
    /// A no-bounce, high-friction material plus damping stop it rolling. Physics only runs while a session is running, on the headset that controls the
    /// clue (team runs: the owner; the partner's copy stays kinematic and follows the NetworkTransform), and never
    /// while a hand holds it (the Interaction SDK keeps it kinematic then). Putting the clue back home
    /// (<see cref="ClueResetter"/>) makes it still again.
    /// </summary>
    [RequireComponent(typeof(Clue), typeof(Rigidbody))]
    public class ClueGravity : MonoBehaviour
    {
        [SerializeField] private SessionManager session;

        private Clue _clue;
        private Rigidbody _body;
        private bool _loose;
        private readonly TossEstimator _toss = new TossEstimator();
        private Vector3 _pendingVelocity;
        private bool _hasPendingVelocity;

        /// <summary>True once the clue was let go and not yet put back home.</summary>
        public bool IsLoose => _loose;

        /// <summary>True while physics moves the clue (loose, not held, controlled here, session running).</summary>
        public bool IsSimulated => _body != null && !_body.isKinematic;

        /// <summary>Current speed in m/s (0 while kinematic).</summary>
        public float Speed => IsSimulated ? _body.linearVelocity.magnitude : 0f;

        private void Awake()
        {
            _clue = GetComponent<Clue>();
            _body = GetComponent<Rigidbody>();
            if (session == null) session = FindFirstObjectByType<SessionManager>();
        }

        private void OnEnable() => _clue.HoldEnded += HandleHoldEnded;

        private void OnDisable()
        {
            _clue.HoldEnded -= HandleHoldEnded;
            Apply(false);
        }

        private void HandleHoldEnded(Clue clue)
        {
            _loose = true;
            _pendingVelocity = _toss.Release(out float rawSpeed, out bool isToss, out bool capped);
            _hasPendingVelocity = true;
            _toss.Clear();
            if (isToss && _clue.HasLocalAuthority && session != null && session.IsRunning)
                session.RequestClueThrown(_clue.ClueId, rawSpeed, capped);
        }

        // Positions while held, after the Interaction SDK moved the clue this frame.
        private void LateUpdate()
        {
            if (_clue.IsHeld) _toss.AddSample(Time.timeAsDouble, transform.position);
        }

        /// <summary>Stops the clue and makes it kinematic until it is held and let go again.</summary>
        public void MakeStill()
        {
            _loose = false;
            _hasPendingVelocity = false;
            Apply(false);
        }

        // Team runs (session 6 fix, user: "the item doesn't move in my partner's view"): Rigidbody interpolation only on
        // the headset that controls the clue. On the partner's copy the NetworkTransform writes the pose; with
        // interpolation on, the physics engine kept redrawing the old interpolated pose over it, so the clue looked frozen.
        // Same rule as Netcode's own NetworkRigidbody (NetworkRigidBodyBase.cs: non-authority -> interpolation None).
        private void Update()
        {
            if (_body == null) return;
            var wanted = _clue.HasLocalAuthority ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            if (_body.interpolation != wanted) _body.interpolation = wanted;
        }

        private void FixedUpdate()
        {
            // Applied every step: the Interaction SDK restores the kinematic flag it saved at grab time when the
            // last hand lets go, which may happen before or after our HoldEnded handler.
            if (_clue.IsHeld) return;
            bool simulate = _loose && _clue.HasLocalAuthority && session != null && session.IsRunning;
            Apply(simulate);
            if (!_hasPendingVelocity) return;
            _hasPendingVelocity = false;
            if (!simulate) return;   // let go outside a run: it just stays where it was released
            _body.linearVelocity = _pendingVelocity;
            _body.angularVelocity = Vector3.zero;
        }

        private void Apply(bool simulate)
        {
            if (_body == null || _body.isKinematic != simulate) return;
            if (!simulate)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
            _body.useGravity = true;
            _body.isKinematic = !simulate;
        }
    }
}
