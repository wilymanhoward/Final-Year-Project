using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Gravity for a grabbable clue (user decision, session 6): a clue stays exactly where the scene put it until
    /// someone moves it; once it has been held and let go it is "loose" and falls (no throwing: the Interaction SDK's
    /// Grabbable has throw-on-release switched off by FYP > Build Case Scenes, and a no-bounce, high-friction material
    /// plus damping stop it rolling). Physics only runs while a session is running, on the headset that controls the
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

        private void HandleHoldEnded(Clue clue) => _loose = true;

        /// <summary>Stops the clue and makes it kinematic until it is held and let go again.</summary>
        public void MakeStill()
        {
            _loose = false;
            Apply(false);
        }

        private void FixedUpdate()
        {
            // Applied every step: the Interaction SDK restores the kinematic flag it saved at grab time when the
            // last hand lets go, which may happen before or after our HoldEnded handler.
            if (_clue.IsHeld) return;
            Apply(_loose && _clue.HasLocalAuthority && session != null && session.IsRunning);
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
