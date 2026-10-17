using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace FYP.Detective
{
    /// <summary>
    /// Sends the study files (run CSV, _summary.csv, _meta.json) to the experimenter's laptop over the local network
    /// (user decision, session 6), so they don't have to be pulled with a cable. The laptop runs tools/receive_data.py,
    /// which broadcasts where it is (<see cref="DataTransfer"/>); <see cref="ExperimentConfig.dataReceiverAddress"/> is
    /// the fallback. Uploads only while no run is going (shortly after a run ends, at start and every
    /// <see cref="scanIntervalSeconds"/>), one file at a time, and remembers a file as sent only after the laptop
    /// confirmed its SHA-256 (<see cref="UploadLedger"/>); anything not confirmed is retried on the next scan, including
    /// files of runs that were interrupted. Never deletes anything on the headset (tools/pull_data.sh stays the backup).
    /// Status is shown on the setup panel.
    /// </summary>
    public class DataUploader : MonoBehaviour
    {
        public const string LedgerFileName = "upload_ledger.txt";

        [SerializeField] private SessionManager session;
        [SerializeField, Min(5f)] private float scanIntervalSeconds = 30f;
        [SerializeField, Min(2)] private int requestTimeoutSeconds = 20;
        [Tooltip("A receiver found by its broadcast counts as present for this long after the last beacon.")]
        [SerializeField, Min(2f)] private float beaconTimeoutSeconds = 10f;

        private UdpClient _udp;
        private string _beaconHost;
        private int _beaconPort;
        private float _lastBeaconTime = -1000f;
        private UploadLedger _ledger;
        private string _device;
        private bool _busy;
        private float _nextScan;
        private int _waiting;
        private string _lastError;

        /// <summary>One line for the setup panel, e.g. "Data: laptop 192.168.1.20 - 12 sent, 0 waiting".</summary>
        public string StatusText { get; private set; } = "Data: starting";

        /// <summary>Study files on this headset the laptop hasn't confirmed yet (as of the last scan).</summary>
        public int WaitingCount => _waiting;

        private static string LedgerPath => Path.Combine(DataLogger.OutputDirectory, LedgerFileName);

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
            _device = DataTransfer.DeviceTag(SystemInfo.deviceUniqueIdentifier);
            try { _ledger = UploadLedger.Parse(File.Exists(LedgerPath) ? File.ReadAllText(LedgerPath) : null); }
            catch (Exception e)
            {
                Debug.LogWarning("[DataUploader] Could not read the upload ledger (files will be sent again): " + e.Message);
                _ledger = new UploadLedger();
            }
        }

        private void OnEnable()
        {
            if (session != null) session.SessionEnded += HandleSessionEnded;
            try
            {
                _udp = new UdpClient();
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DataTransfer.BeaconPort));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DataUploader] Can't listen for the laptop's broadcast (use dataReceiverAddress): " + e.Message);
                CloseUdp();
            }
            _nextScan = Time.unscaledTime + 5f;
        }

        private void OnDisable()
        {
            if (session != null) session.SessionEnded -= HandleSessionEnded;
            CloseUdp();
        }

        private void CloseUdp()
        {
            if (_udp == null) return;
            try { _udp.Close(); } catch (Exception) { /* closing anyway */ }
            _udp = null;
        }

        // The run's files are final once DataLogger has handled SessionEnded; send them a moment later.
        private void HandleSessionEnded(SessionEndReason reason) => _nextScan = Time.unscaledTime + 2f;

        private bool IsRunning => session != null && session.IsRunning;

        private void Update()
        {
            PollBeacon();
            if (_busy || Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + scanIntervalSeconds;
            if (IsRunning) return;   // never during a run: no network or file work while participants play
            if (TryGetReceiver(out string host, out int port)) StartCoroutine(UploadPending(host, port));
            else UpdateStatus(null);
        }

        private void PollBeacon()
        {
            if (_udp == null) return;
            try
            {
                while (_udp.Available > 0)
                {
                    IPEndPoint from = null;
                    byte[] data = _udp.Receive(ref from);
                    if (!DataTransfer.TryParseBeacon(Encoding.ASCII.GetString(data), out int port)) continue;
                    bool wasMissing = !HasFreshBeacon;
                    _beaconHost = from.Address.ToString();
                    _beaconPort = port;
                    _lastBeaconTime = Time.unscaledTime;
                    if (wasMissing)
                    {
                        Debug.Log($"[DataUploader] Laptop receiver found at {_beaconHost}:{_beaconPort}");
                        if (!_busy) _nextScan = Mathf.Min(_nextScan, Time.unscaledTime + 1f);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DataUploader] Broadcast receive failed: " + e.Message);
            }
        }

        private bool HasFreshBeacon => Time.unscaledTime - _lastBeaconTime <= beaconTimeoutSeconds && _beaconHost != null;

        private bool TryGetReceiver(out string host, out int port)
        {
            if (HasFreshBeacon) { host = _beaconHost; port = _beaconPort; return true; }
            var config = session != null ? session.Config : null;
            if (config != null && !string.IsNullOrEmpty(config.dataReceiverAddress))
            {
                host = config.dataReceiverAddress.Trim();
                port = config.dataReceiverPort;
                return true;
            }
            host = null; port = 0;
            return false;
        }

        private IEnumerator UploadPending(string host, int port)
        {
            _busy = true;
            string[] files = ListFiles();
            foreach (string path in files)
            {
                if (IsRunning) break;   // a run started: stop, the rest goes later
                string name = Path.GetFileName(path);
                if (!DataTransfer.IsStudyFile(name)) continue;
                FileInfo info;
                byte[] bytes;
                try
                {
                    info = new FileInfo(path);
                    if (_ledger.IsSent(name, info.Length, info.LastWriteTimeUtc.Ticks)) continue;
                    bytes = File.ReadAllBytes(path);
                }
                catch (Exception e)
                {
                    _lastError = name + ": " + e.Message;
                    continue;
                }
                string sha = DataTransfer.Sha256Hex(bytes);
                using (var request = new UnityWebRequest(DataTransfer.UploadUrl(host, port, _device, name), UnityWebRequest.kHttpVerbPUT))
                {
                    request.uploadHandler = new UploadHandlerRaw(bytes);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.timeout = requestTimeoutSeconds;
                    request.SetRequestHeader(DataTransfer.ShaHeader, sha);
                    yield return request.SendWebRequest();

                    if (DataTransfer.IsConfirmed(request.responseCode, request.downloadHandler.text, sha))
                    {
                        _ledger.Record(name, info.Length, info.LastWriteTimeUtc.Ticks, sha);
                        SaveLedger();
                        _lastError = null;
                        Debug.Log($"[DataUploader] Sent {name} ({bytes.Length} bytes) to {host}");
                    }
                    else
                    {
                        _lastError = !string.IsNullOrEmpty(request.error) ? request.error : "HTTP " + request.responseCode;
                        Debug.LogWarning($"[DataUploader] {name} not confirmed by {host}:{port}: {_lastError}");
                        if (request.result == UnityWebRequest.Result.ConnectionError) break;   // laptop gone: retry later
                    }
                }
            }
            UpdateStatus(host);
            _busy = false;
        }

        private static string[] ListFiles()
        {
            try { return Directory.GetFiles(DataLogger.OutputDirectory); }
            catch (Exception e)
            {
                Debug.LogWarning("[DataUploader] Can't list the data folder: " + e.Message);
                return Array.Empty<string>();
            }
        }

        private void SaveLedger()
        {
            try { File.WriteAllText(LedgerPath, _ledger.Format()); }
            catch (Exception e) { Debug.LogWarning("[DataUploader] Could not save the upload ledger: " + e.Message); }
        }

        private void UpdateStatus(string host)
        {
            _waiting = 0;
            foreach (string path in ListFiles())
            {
                string name = Path.GetFileName(path);
                if (!DataTransfer.IsStudyFile(name)) continue;
                try
                {
                    var info = new FileInfo(path);
                    if (!_ledger.IsSent(name, info.Length, info.LastWriteTimeUtc.Ticks)) _waiting++;
                }
                catch (Exception) { _waiting++; }
            }
            StatusText = host == null
                ? $"Data: laptop not found - {_waiting} file(s) waiting (run tools/receive_data.py)"
                : $"Data: laptop {host} - {_ledger.Count} sent, {_waiting} waiting" + (_lastError != null ? " (" + _lastError + ")" : "");
        }
    }
}
