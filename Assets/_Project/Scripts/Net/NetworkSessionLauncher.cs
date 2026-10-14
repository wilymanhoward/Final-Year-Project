using System.Globalization;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Events;

namespace FYP.Detective.Net
{
    /// <summary>
    /// Week 3 Phase 1: starts this headset as host or as a client over the LAN (direct IP, no relay) and reports
    /// the connection state as short status texts (wire <see cref="onStatusChanged"/> to StatusLabel.Show).
    /// The host listens on all interfaces and shows its LAN IP; the client joins
    /// <see cref="ExperimentConfig.hostAddress"/>, re-read from experiment_config.json on every join so an
    /// adb-pushed address works without a restart.
    /// </summary>
    /// <remarks>
    /// Verified in com.unity.netcode.gameobjects 2.13.3: <c>NetworkManager.StartHost/StartClient/Shutdown</c>,
    /// <c>OnClientConnectedCallback</c>, <c>OnClientDisconnectCallback</c>, <c>OnTransportFailure</c>,
    /// <c>IsListening/IsHost/IsServer/LocalClientId/DisconnectReason/ShutdownInProgress</c>
    /// (Runtime/Core/NetworkManager.cs); <c>UnityTransport.SetConnectionData(string, ushort, string)</c>,
    /// <c>MaxConnectAttempts</c>, <c>ConnectTimeoutMS</c> (Runtime/Transports/UTP/UnityTransport.cs; without a
    /// listen address the server only listens on localhost).
    /// </remarks>
    public class NetworkSessionLauncher : MonoBehaviour
    {
        public const string ListenOnAllInterfaces = "0.0.0.0";

        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private UnityTransport transport;
        [SerializeField] private SessionManager session;
        [SerializeField] private DataLogger logger;
        [Tooltip("Client gives up after this many 1 s connection attempts (transport default is 60).")]
        [SerializeField, Min(1)] private int clientConnectAttempts = 10;

        [Tooltip("Connection status for the experimenter, e.g. wire to StatusLabel.Show.")]
        public UnityEvent<string> onStatusChanged = new UnityEvent<string>();

        private bool _clientWasConnected;
        private string _joinAddress;

        /// <summary>Last status text shown.</summary>
        public string Status { get; private set; } = "Network off";

        /// <summary>True on the host while a partner (any remote client) is connected, and on a connected client.</summary>
        public bool HasPeer => networkManager != null && networkManager.IsListening
            && (networkManager.IsServer ? networkManager.ConnectedClientsIds.Count > 1 : networkManager.IsConnectedClient);

        private void Awake()
        {
            if (networkManager == null) networkManager = GetComponent<NetworkManager>();
            if (transport == null) transport = GetComponent<UnityTransport>();
            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (logger == null) logger = FindFirstObjectByType<DataLogger>();
        }

        private void OnEnable()
        {
            if (networkManager == null) return;
            networkManager.OnClientConnectedCallback += HandleClientConnected;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            networkManager.OnTransportFailure += HandleTransportFailure;
        }

        private void OnDisable()
        {
            if (networkManager == null) return;
            networkManager.OnClientConnectedCallback -= HandleClientConnected;
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            networkManager.OnTransportFailure -= HandleTransportFailure;
        }

        // ---- Public actions (controller shortcuts / UI) ----

        /// <summary>Starts this headset as host, listening on every interface at the configured port.</summary>
        public void StartHost()
        {
            if (!CanStart()) return;
            int port = CurrentConfig().netPort;
            transport.SetConnectionData("127.0.0.1", (ushort)port, ListenOnAllInterfaces);
            if (!networkManager.StartHost())
            {
                SetStatus("Host failed to start (port " + port + " in use?)");
                return;
            }
            string ip = LanAddress.FindLocalIpv4();
            SetStatus(ip != null
                ? $"Hosting on {ip}:{port} - waiting for partner"
                : $"Hosting on port {port} - no Wi-Fi address found");
        }

        /// <summary>Joins the host at hostAddress:netPort from experiment_config.json.</summary>
        public void StartClient()
        {
            if (!CanStart()) return;
            ExperimentConfig config = CurrentConfig();
            if (!LanAddress.TryParseIpv4(config.hostAddress, out string address))
            {
                SetStatus("No valid hostAddress in experiment_config.json");
                return;
            }
            _clientWasConnected = false;
            _joinAddress = address;
            transport.MaxConnectAttempts = clientConnectAttempts;
            transport.SetConnectionData(address, (ushort)config.netPort);
            if (!networkManager.StartClient())
            {
                SetStatus("Client failed to start");
                return;
            }
            SetStatus($"Connecting to {address}:{config.netPort}...");
        }

        /// <summary>Stops hosting / leaves the host.</summary>
        public void StopNetwork()
        {
            if (networkManager == null || !networkManager.IsListening) { SetStatus("Network off"); return; }
            networkManager.Shutdown();
            Log(LogEvents.NetStopped, null);
            SetStatus("Network stopped");
        }

        // ---- NGO callbacks ----

        private void HandleClientConnected(ulong clientId)
        {
            if (networkManager.IsServer)
            {
                if (clientId == networkManager.LocalClientId) return;   // the host's own client
                Log(LogEvents.NetPeerConnected, "client_id=" + clientId.ToString(CultureInfo.InvariantCulture));
                SetStatus("Partner connected (client " + clientId + ")");
            }
            else if (clientId == networkManager.LocalClientId)
            {
                _clientWasConnected = true;
                Log(LogEvents.NetPeerConnected, "host=" + _joinAddress);
                SetStatus("Connected to host " + _joinAddress);
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (networkManager.IsServer)
            {
                if (clientId == networkManager.LocalClientId) return;
                Log(LogEvents.NetPeerDisconnected, "client_id=" + clientId.ToString(CultureInfo.InvariantCulture));
                SetStatus("Partner left - waiting for partner");
                return;
            }

            string reason = networkManager.DisconnectReason;
            if (_clientWasConnected)
            {
                Log(LogEvents.NetPeerDisconnected, "host=" + _joinAddress + (string.IsNullOrEmpty(reason) ? "" : ";reason=" + reason));
                SetStatus("Lost the host" + (string.IsNullOrEmpty(reason) ? "" : ": " + reason));
            }
            else
            {
                SetStatus($"Could not reach {_joinAddress} - same Wi-Fi? host started? (client isolation blocks it)");
            }
            _clientWasConnected = false;
        }

        private void HandleTransportFailure()
        {
            Log(LogEvents.NetStopped, "reason=transport_failure");
            SetStatus("Network failed (transport) - restart host/client");
        }

        // ---- Helpers ----

        private bool CanStart()
        {
            if (networkManager == null || transport == null)
            {
                SetStatus("Networking not set up (run FYP > Set Up Networking)");
                return false;
            }
            if (networkManager.IsListening || networkManager.ShutdownInProgress)
            {
                SetStatus("Network already running - stop it first");
                return false;
            }
            return true;
        }

        /// <summary>Fresh copy of the config file (falls back to the session's config if there is no file).</summary>
        private ExperimentConfig CurrentConfig()
        {
            ExperimentConfig config = session != null ? session.Config.Clone() : new ExperimentConfig();
            ExperimentConfigStore.TryLoadInto(config);
            return config;
        }

        private void Log(string eventName, string detail)
        {
            if (logger != null) logger.LogEvent(eventName, detail);
        }

        private void SetStatus(string text)
        {
            Status = text;
            Debug.Log("[Net] " + text);
            onStatusChanged.Invoke(text);
        }
    }
}
