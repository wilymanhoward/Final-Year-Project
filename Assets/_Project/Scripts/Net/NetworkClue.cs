using Unity.Netcode;
using UnityEngine;

namespace FYP.Detective.Net
{
    /// <summary>
    /// Team condition: makes a grabbable <see cref="Clue"/> a shared object. Needs a NetworkObject and a
    /// NetworkTransform (Owner authority, In Local Space = position relative to CaseRoot, which lines up across the
    /// headsets while their world coordinates differ) on the same object - added by FYP > Set Up Networking.
    /// The owner drives the position; a local grab of a clue the partner owns asks for ownership (last grabber wins),
    /// and losing ownership while holding makes this headset's hand let go (logged as clue_grab_taken).
    /// While not spawned (solo run / network off) the clue behaves exactly as before.
    /// </summary>
    /// <remarks>
    /// Netcode 2.13.3 NetworkBehaviour (Runtime/Core/NetworkBehaviour.cs): <c>OnNetworkSpawn/OnNetworkDespawn</c>,
    /// <c>OnOwnershipChanged(ulong previous, ulong current)</c> (:946), <c>IsOwner</c>, <c>IsSpawned</c>.
    /// </remarks>
    [RequireComponent(typeof(Clue))]
    public class NetworkClue : NetworkBehaviour
    {
        private Clue _clue;
        private TeamSessionSync _sync;

        public string ClueId => _clue != null ? _clue.ClueId : null;

        private void Awake()
        {
            _clue = GetComponent<Clue>();
            _sync = FindFirstObjectByType<TeamSessionSync>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _clue.HoldBegan += HandleHoldBegan;
            UpdateAuthority();
            if (_sync != null) _sync.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            _clue.HoldBegan -= HandleHoldBegan;
            _clue.HasLocalAuthority = true;
            if (_sync != null) _sync.Unregister(this);
            base.OnNetworkDespawn();
        }

        protected override void OnOwnershipChanged(ulong previous, ulong current)
        {
            base.OnOwnershipChanged(previous, current);
            bool hadIt = previous == NetworkManager.LocalClientId;
            UpdateAuthority();
            if (hadIt && !IsOwner && _clue.IsHeld)
            {
                _clue.ForceRelease();
                if (_sync != null) _sync.ReportGrabTaken(_clue.ClueId);
            }
        }

        private void HandleHoldBegan(Clue clue)
        {
            if (IsSpawned && !IsOwner && _sync != null) _sync.RequestOwnership(this);
        }

        private void UpdateAuthority() => _clue.HasLocalAuthority = !IsSpawned || IsOwner;
    }
}
