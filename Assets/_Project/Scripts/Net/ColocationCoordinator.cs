using System;
using System.Threading.Tasks;
using FYP.Detective.Meta;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace FYP.Detective.Net
{
    /// <summary>
    /// Week 3 Phase 2: puts both headsets in the same session and the same physical scene.
    /// Host: start Netcode host → advertise "FYPDET1|ip|port" in a Meta colocation session (group UUID) → share
    /// the CaseRoot anchor to that group (again whenever the host re-places). Client: discover the advert
    /// (Bluetooth + same Wi-Fi) → join ip:port → ask the host for the anchor → load the shared anchor into CaseRoot.
    /// If discovery finds nothing, the client falls back to hostAddress from experiment_config.json; if the shared
    /// anchor fails, both headsets use Manual placement (X) on the tape mark.
    /// </summary>
    /// <remarks>
    /// Netcode 2.13.3, Runtime/Messaging/CustomMessageManager.cs: <c>RegisterNamedMessageHandler</c>,
    /// <c>SendNamedMessage(name, clientId, FastBufferWriter)</c>; <c>NetworkManager.CustomMessagingManager</c> exists only
    /// while listening (created in StartHost/StartClient), so handlers are registered right after starting.
    /// </remarks>
    public class ColocationCoordinator : MonoBehaviour
    {
        [SerializeField] private NetworkSessionLauncher launcher;
        [SerializeField] private ScenePlacement placement;
        [SerializeField] private SessionManager session;
        [Tooltip("Client stops looking for an advertised host after this long and tries the manual hostAddress.")]
        [SerializeField, Min(3f)] private float discoveryTimeoutSeconds = 20f;

        [Tooltip("Status for the experimenter, e.g. wire to StatusLabel.Show.")]
        public UnityEvent<string> onStatusChanged = new UnityEvent<string>();

        private Guid _group;          // colocation group (host: advertised; client: from the host's message)
        private Guid _sharedAnchor;   // host: anchor already shared to _group
        private bool _advertising;
        private bool _discovering;
        private float _discoveryDeadline = -1f;
        private int _op;              // bumped by every host/join/stop; stale async results are ignored

        private void Awake()
        {
            if (launcher == null) launcher = GetComponent<NetworkSessionLauncher>();
            if (placement == null) placement = FindFirstObjectByType<ScenePlacement>();
            if (session == null) session = FindFirstObjectByType<SessionManager>();
        }

        private void OnEnable()
        {
            MetaColocation.SessionDiscovered += HandleDiscovered;
            if (launcher != null)
            {
                launcher.ConnectedToHost += HandleConnectedToHost;
                launcher.NetworkStopped += HandleNetworkStopped;
            }
            if (session != null) session.PlacementChanged += HandlePlacementChanged;
        }

        private void OnDisable()
        {
            MetaColocation.SessionDiscovered -= HandleDiscovered;
            if (launcher != null)
            {
                launcher.ConnectedToHost -= HandleConnectedToHost;
                launcher.NetworkStopped -= HandleNetworkStopped;
            }
            if (session != null) session.PlacementChanged -= HandlePlacementChanged;
        }

        private void Update()
        {
            if (_discovering && Time.unscaledTime > _discoveryDeadline)
            {
                StopDiscovery();
                SetStatus($"No host found nearby in {discoveryTimeoutSeconds:F0} s - trying hostAddress from the config");
                JoinManualAddress();
            }
        }

        // ---- Public actions (controller shortcuts) ----

        /// <summary>Host + advertise + share the scene anchor.</summary>
        public async void Host()
        {
            if (!Ready()) return;
            int op = ++_op;
            if (!launcher.TryStartHost(out string ip)) return;
            RegisterHostHandlers();
            if (ip == null)
            {
                SetStatus("Hosting, but no Wi-Fi address found - discovery off");
                return;
            }

            var (group, error) = await MetaColocation.StartAdvertisingAsync(ColocationProtocol.EncodeAdvert(ip, launcher.Port));
            if (op != _op || !launcher.IsHosting)
            {
                if (group != Guid.Empty) _ = MetaColocation.StopAdvertisingAsync();
                return;
            }
            if (error != null)
            {
                SetStatus($"Hosting on {ip}, but not discoverable: {error}. Client can join by IP");
                return;
            }
            _group = group;
            _advertising = true;
            SetStatus($"Hosting on {ip} - discoverable nearby");
            await ShareAnchorAsync(op);
        }

        /// <summary>Find the host nearby, join it and load its scene anchor.</summary>
        public async void Join()
        {
            if (!Ready()) return;
            if (launcher.Manager.IsListening) { SetStatus("Network already running - stop it first"); return; }
            int op = ++_op;
            _group = Guid.Empty;
            string error = await MetaColocation.StartDiscoveryAsync();
            if (op != _op) return;
            if (error != null)
            {
                SetStatus("Discovery failed: " + error + " - trying hostAddress from the config");
                JoinManualAddress();
                return;
            }
            _discovering = true;
            _discoveryDeadline = Time.unscaledTime + discoveryTimeoutSeconds;
            SetStatus("Looking for the host nearby...");
        }

        /// <summary>Stops hosting/joining, advertising and discovery.</summary>
        public void Stop()
        {
            if (NotDuringRun()) return;
            _op++;
            StopDiscovery();
            StopAdvertising();
            if (launcher != null) launcher.StopNetwork();
        }

        // ---- Host ----

        private void RegisterHostHandlers()
        {
            launcher.Manager.CustomMessagingManager.RegisterNamedMessageHandler(
                ColocationProtocol.AnchorRequestName, (sender, _) => SendAnchorTo(sender));
        }

        private void HandlePlacementChanged(PlacementRecord record)
        {
            if (launcher != null && launcher.IsHosting && _group != Guid.Empty) _ = ShareAnchorAsync(_op);
        }

        private async Task ShareAnchorAsync(int op)
        {
            Guid anchor = placement.CurrentAnchorUuid;
            if (anchor == Guid.Empty)
            {
                _sharedAnchor = Guid.Empty;
                SetStatus("Host has no anchor - place with A (or both headsets use Manual X)");
                return;
            }
            if (anchor == _sharedAnchor)
            {
                SendAnchorToAll();
                return;
            }

            SetStatus("Sharing the scene anchor (Meta cloud)...");
            string error = await placement.ShareCurrentAnchorAsync(_group);
            if (op != _op || !launcher.IsHosting) return;
            // Re-placed while this share was in flight: the share of the newer anchor decides (shares can finish
            // in any order, so never let an older one overwrite _sharedAnchor).
            if (placement.CurrentAnchorUuid != anchor) return;
            if (error != null)
            {
                SetStatus("Anchor sharing failed: " + error + ". Fallback: both use Manual (X) on the tape mark");
                return;
            }
            _sharedAnchor = anchor;
            SetStatus("Scene anchor shared - partner can load it");
            SendAnchorToAll();
        }

        private void SendAnchorToAll()
        {
            NetworkManager nm = launcher.Manager;
            if (nm == null || !nm.IsServer) return;
            foreach (ulong id in nm.ConnectedClientsIds)
            {
                if (id != nm.LocalClientId) SendAnchorTo(id);
            }
        }

        private void SendAnchorTo(ulong clientId)
        {
            NetworkManager nm = launcher.Manager;
            if (_sharedAnchor == Guid.Empty || nm == null || !nm.IsServer || nm.CustomMessagingManager == null) return;
            string text = ColocationProtocol.FormatAnchorMessage(_group, _sharedAnchor);
            using (var writer = new FastBufferWriter(256, Allocator.Temp))
            {
                writer.WriteValueSafe(text, true);
                nm.CustomMessagingManager.SendNamedMessage(ColocationProtocol.AnchorMessageName, clientId, writer);
            }
            Debug.Log("[Colocation] Sent anchor to client " + clientId + ": " + text);
        }

        // ---- Client ----

        private void HandleDiscovered(Guid group, byte[] metadata)
        {
            if (!_discovering) return;
            if (!ColocationProtocol.TryDecodeAdvert(metadata, out string ip, out int port))
            {
                Debug.Log("[Colocation] Ignored a foreign advertisement " + group);
                return;
            }
            StopDiscovery();
            _group = group;
            SetStatus($"Found host {ip} - connecting");
            if (launcher.TryStartClient(ip, port)) RegisterClientHandlers();
        }

        private void JoinManualAddress()
        {
            launcher.StartClient();
            if (launcher.Manager.IsListening) RegisterClientHandlers();
        }

        private void RegisterClientHandlers()
        {
            launcher.Manager.CustomMessagingManager.RegisterNamedMessageHandler(
                ColocationProtocol.AnchorMessageName, HandleAnchorMessage);
        }

        private void HandleConnectedToHost()
        {
            NetworkManager nm = launcher.Manager;
            using (var writer = new FastBufferWriter(8, Allocator.Temp))
            {
                nm.CustomMessagingManager.SendNamedMessage(ColocationProtocol.AnchorRequestName, NetworkManager.ServerClientId, writer);
            }
        }

        private void HandleAnchorMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string text, true);
            if (!ColocationProtocol.TryParseAnchorMessage(text, out Guid group, out Guid anchor))
            {
                Debug.LogWarning("[Colocation] Bad anchor message: " + text);
                return;
            }
            _group = group;
            SetStatus("Host sent its scene anchor - loading");
            placement.LoadSharedPlacement(group, anchor);
        }

        // ---- Shared ----

        private void HandleNetworkStopped()
        {
            StopDiscovery();
            StopAdvertising();
            _group = Guid.Empty;
            _sharedAnchor = Guid.Empty;
        }

        private void StopDiscovery()
        {
            if (!_discovering) return;
            _discovering = false;
            _discoveryDeadline = -1f;
            _ = MetaColocation.StopDiscoveryAsync();
        }

        private void StopAdvertising()
        {
            if (!_advertising) return;
            _advertising = false;
            _ = MetaColocation.StopAdvertisingAsync();
        }

        private bool Ready()
        {
            if (NotDuringRun()) return false;
            if (launcher != null && launcher.Manager != null && placement != null) return true;
            SetStatus("Networking not set up (run FYP > Set Up Networking)");
            return false;
        }

        // The setup panel can now be opened during a run (to abort it): network changes then would turn a solo run into
        // a team run or cut the partner off mid-run, so they wait until the run is over.
        private bool NotDuringRun()
        {
            if (session == null || !session.IsRunning) return false;
            SetStatus("Run in progress - abort it first (Abort run, two taps), then change the network");
            return true;
        }

        private void SetStatus(string text)
        {
            Debug.Log("[Colocation] " + text);
            onStatusChanged.Invoke(text);
        }
    }
}
