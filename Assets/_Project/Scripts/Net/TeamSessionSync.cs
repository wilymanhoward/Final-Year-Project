using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace FYP.Detective.Net
{
    /// <summary>
    /// Week 3 Phase 3: the team-condition <see cref="ICaseAuthority"/>. The host is authoritative: its own requests
    /// are applied at once and mirrored to the client; the client's requests go to the host, which applies and
    /// mirrors them back (so the client applies a change only when the host's message arrives). Messages are
    /// <see cref="TeamMessage"/> text in one Netcode named message. Also arbitrates clue ownership (last grabber wins).
    /// Each headset keeps writing its own CSV. A client that connects mid-run is not caught up (start runs after
    /// both are connected).
    /// </summary>
    /// <remarks>
    /// Netcode 2.13.3: <c>NetworkManager.OnServerStarted/OnClientStarted</c> (Runtime/Core/NetworkManager.cs:903/908),
    /// <c>CustomMessagingManager.RegisterNamedMessageHandler/SendNamedMessage</c>, <c>NetworkObject.ChangeOwnership</c>
    /// (server only, Runtime/Core/NetworkObject.cs:2279), <c>SendNamedMessage(..., NetworkDelivery)</c> (Runtime/Messaging/CustomMessageManager.cs:300).
    /// </remarks>
    public class TeamSessionSync : MonoBehaviour, ICaseAuthority
    {
        [SerializeField] private NetworkSessionLauncher launcher;
        [SerializeField] private SessionManager session;

        [Tooltip("Debug (one headset): with no partner connected, show this headset's own head and hands as the partner, " +
                 "turned to face you 1.5 m ahead. Toggle with FYP > Debug: Show Me As Partner; Prepare Study Build turns it off.")]
        [SerializeField] private bool showSelfAsPartner;

        [Tooltip("Team-run status for the experimenter (e.g. partner refused to start), wire to StatusLabel.Show.")]
        public UnityEvent<string> onStatusChanged = new UnityEvent<string>();

        private readonly Dictionary<string, NetworkClue> _clues = new Dictionary<string, NetworkClue>();
        private string _partnerId;
        // Team briefing (host): who has tapped Begin, with their pose; both -> BriefingGo to both headsets.
        private PlayerPose2D? _hostReady, _clientReady;
        private bool _applyingRemoteEnd;
        private PlayerRig _rig;
        private CaseRoot _caseRoot;
        private PartnerMarker _marker;
        private FYP.Detective.Meta.PartnerHands _hands;
        private float _nextPoseSend;
        private const float PoseInterval = 1f / 15f;
        private const string PoseMessageName = "fyp.pose";
        private readonly BodyPoseFrame _sendFrame = new BodyPoseFrame();
        private readonly BodyPoseFrame _receiveFrame = new BodyPoseFrame();
        private readonly byte[] _receiveBuffer = new byte[BodyPoseFrame.MaxBytes];

        private NetworkManager Nm => launcher != null ? launcher.Manager : null;
        private bool IsHost => Nm != null && Nm.IsListening && Nm.IsServer;

        /// <inheritdoc/>
        public bool IsActive => launcher != null && launcher.HasPeer;

        /// <summary>Debug switch (see the field's tooltip); set by the Editor menu.</summary>
        public bool ShowSelfAsPartner { get => showSelfAsPartner; set => showSelfAsPartner = value; }

        private void Awake()
        {
            if (launcher == null) launcher = GetComponent<NetworkSessionLauncher>();
            if (session == null) session = FindFirstObjectByType<SessionManager>();
            _rig = FindFirstObjectByType<PlayerRig>();
            _caseRoot = FindFirstObjectByType<CaseRoot>();
            _hands = FindFirstObjectByType<FYP.Detective.Meta.PartnerHands>();
            if (_hands == null) _hands = gameObject.AddComponent<FYP.Detective.Meta.PartnerHands>();
        }

        // Partner marker (user, session 6): send our head + hands (wrist + finger joints), CaseRoot-local so both headsets
        // agree, about 15 times a second while a partner is connected; binary, unreliable-sequenced (a lost frame is just
        // replaced by the next one, an old one is never shown after a newer one).
        private void Update()
        {
            bool selfTest = showSelfAsPartner && !IsActive;   // a real partner always wins
            if ((!IsActive && !selfTest) || _rig == null || _caseRoot == null || Time.unscaledTime < _nextPoseSend) return;
            _nextPoseSend = Time.unscaledTime + PoseInterval;
            Transform head = _rig.Head, root = _caseRoot.transform;
            _sendFrame.HasHead = _rig.IsAvailable(BodyPart.Head);
            if (_sendFrame.HasHead)
            {
                _sendFrame.HeadPosition = root.InverseTransformPoint(head.position);
                _sendFrame.HeadRotation = Quaternion.Inverse(root.rotation) * head.rotation;
            }
            _hands.Capture(_sendFrame, _rig);
            byte[] bytes = _sendFrame.Write(out int length);
            if (selfTest) { ShowSelf(bytes, length); return; }
            NetworkManager nm = Nm;
            if (nm == null || nm.CustomMessagingManager == null) return;
            if (IsHost)
            {
                foreach (ulong id in nm.ConnectedClientsIds) if (id != nm.LocalClientId) SendPose(nm, id, bytes, length);
            }
            else if (nm.IsConnectedClient) SendPose(nm, NetworkManager.ServerClientId, bytes, length);
        }

        private static void SendPose(NetworkManager nm, ulong clientId, byte[] bytes, int length)
        {
            using (var writer = new FastBufferWriter(length + 8, Allocator.Temp))
            {
                writer.WriteValueSafe(length);
                writer.WriteBytesSafe(bytes, length);
                nm.CustomMessagingManager.SendNamedMessage(PoseMessageName, clientId, writer, NetworkDelivery.UnreliableSequenced);
            }
        }

        // Debug: decode our own frame (so the codec is exercised too), turn it to face us 1.5 m ahead, show it.
        private void ShowSelf(byte[] bytes, int length)
        {
            if (!_sendFrame.HasHead || length > _receiveBuffer.Length) return;
            System.Array.Copy(bytes, _receiveBuffer, length);
            if (!_receiveFrame.TryRead(_receiveBuffer, length)) return;
            Vector3 ahead = _sendFrame.HeadRotation * Vector3.forward;
            ahead.y = 0f;
            if (ahead.sqrMagnitude < 1e-4f) ahead = Vector3.forward;
            Vector3 pivot = _sendFrame.HeadPosition + ahead.normalized * 0.75f;
            _receiveFrame.TurnAround(new Vector3(pivot.x, 0f, pivot.z));
            ShowReceived();
        }

        private void ShowReceived()
        {
            if (_marker == null) _marker = FindFirstObjectByType<PartnerMarker>();
            if (_marker != null && _receiveFrame.HasHead) _marker.Show(_receiveFrame.HeadPosition, _receiveFrame.HeadRotation);
            _hands.Show(_receiveFrame);
        }

        // Partner's pose frame -> head marker + hand copies. Malformed or oversized frames are dropped.
        private void HandlePoseMessage(ulong sender, FastBufferReader reader)
        {
            byte[] buffer = _receiveBuffer;
            int length;
            try
            {
                reader.ReadValueSafe(out length);
                if (length <= 0 || length > _receiveBuffer.Length) return;
                reader.ReadBytesSafe(ref buffer, length);
            }
            catch (System.OverflowException) { return; }   // shorter than it claims
            if (!_receiveFrame.TryRead(buffer, length)) return;
            ShowReceived();
        }

        private void OnEnable()
        {
            if (session != null)
            {
                session.Authority = this;
                session.SessionEnded += HandleSessionEnded;
                session.LocalBriefingFinished += HandleLocalBriefingFinished;
                session.ArmedChanged += HandleArmedChanged;
            }
            if (Nm != null)
            {
                Nm.OnServerStarted += RegisterHandler;
                Nm.OnClientStarted += RegisterHandler;
            }
        }

        private void OnDisable()
        {
            if (session != null)
            {
                if (ReferenceEquals(session.Authority, this)) session.Authority = null;
                session.SessionEnded -= HandleSessionEnded;
                session.LocalBriefingFinished -= HandleLocalBriefingFinished;
                session.ArmedChanged -= HandleArmedChanged;
            }
            if (Nm != null)
            {
                Nm.OnServerStarted -= RegisterHandler;
                Nm.OnClientStarted -= RegisterHandler;
            }
        }

        // ---- ICaseAuthority ----

        public void RequestStart(StartSource source)
        {
            if (!IsHost) { SendToHost(TeamMessage.ReqStart(source)); return; }
            if (!session.StartSession(source))
            {
                SetStatus("Team start refused on the host: " + session.LastStartError);
                return;
            }
            var c = session.Config;
            Broadcast(TeamMessage.Start(c.CaseCode, c.ConditionCode, source));
        }

        public void RequestArm(bool armed)
        {
            if (!IsHost) { SendToHost(TeamMessage.ReqArm(armed)); return; }
            if (session.ApplyArm(armed)) Broadcast(TeamMessage.Arm(armed, session.Config.CaseCode));
            else SetStatus("Arm refused on the host: " + session.LastStartError);
        }

        public void RequestEnd(SessionEndReason reason)
        {
            if (!IsHost) { SendToHost(TeamMessage.ReqEnd(reason)); return; }
            session.EndSession(reason);   // HandleSessionEnded mirrors it
        }

        public void RequestClueFound(string clueId, string by)
        {
            if (!TeamMessage.IsSendableId(clueId)) return;
            if (!IsHost) { SendToHost(TeamMessage.ReqFound(clueId, by)); return; }
            if (session.ApplyClueFound(clueId, by)) Broadcast(TeamMessage.Found(clueId, by));
        }

        public void RequestClueTagged(string clueId, bool tagged, string by)
        {
            if (!TeamMessage.IsSendableId(clueId)) return;
            if (!IsHost) { SendToHost(TeamMessage.ReqTag(clueId, tagged, by)); return; }
            if (session.ApplyClueTagged(clueId, tagged, by)) Broadcast(TeamMessage.Tag(clueId, tagged, by));
        }

        public void RequestSubmit(string suspectId, IReadOnlyList<string> evidenceIds, string by)
        {
            if (!TeamMessage.IsSendableId(suspectId)) return;
            if (!IsHost) { SendToHost(TeamMessage.ReqSubmit(by, suspectId, evidenceIds)); return; }
            if (session.ApplySubmission(suspectId, evidenceIds, by)) Broadcast(TeamMessage.Submit(by, suspectId, evidenceIds));
        }

        public void RequestBoardSuspect(string suspectId, string by)
        {
            if (suspectId != null && !TeamMessage.IsSendableId(suspectId)) return;
            if (!IsHost) { SendToHost(TeamMessage.ReqBoardSuspect(suspectId, by)); return; }
            if (session.ApplyBoardSuspect(suspectId, by)) Broadcast(TeamMessage.BoardSuspect(suspectId, by));
        }

        public void RequestBoardEvidence(string clueId, bool selected, string by)
        {
            if (!TeamMessage.IsSendableId(clueId)) return;
            if (!IsHost) { SendToHost(TeamMessage.ReqBoardEvidence(clueId, selected, by)); return; }
            if (session.ApplyBoardEvidence(clueId, selected, by)) Broadcast(TeamMessage.BoardEvidence(clueId, selected, by));
        }

        public void ReportContamination(string zoneId, BodyPart part, string by)
        {
            if (!TeamMessage.IsSendableId(zoneId)) return;
            string text = TeamMessage.Contamination(zoneId, part, by);
            if (IsHost) Broadcast(text); else SendToHost(text);
        }

        // ---- Clue ownership (called by NetworkClue) ----

        public void Register(NetworkClue clue) { if (clue != null) _clues[clue.ClueId] = clue; }
        public void Unregister(NetworkClue clue) { if (clue != null && _clues.TryGetValue(clue.ClueId, out var c) && c == clue) _clues.Remove(clue.ClueId); }

        /// <summary>A hand on this headset grabbed a clue the partner controls: take it over (last grabber wins).</summary>
        public void RequestOwnership(NetworkClue clue)
        {
            if (clue == null || !TeamMessage.IsSendableId(clue.ClueId)) return;
            if (IsHost) clue.NetworkObject.ChangeOwnership(Nm.LocalClientId);
            else SendToHost(TeamMessage.ReqOwnership(clue.ClueId));
        }

        /// <summary>The partner took a clue out of this headset's hand.</summary>
        public void ReportGrabTaken(string clueId) => session.ReportClueGrabTaken(clueId, _partnerId ?? "partner");

        // ---- Messages ----

        // CustomMessagingManager is recreated on every start; registering again just overwrites (host gets both events).
        private void RegisterHandler()
        {
            if (Nm != null && Nm.CustomMessagingManager != null)
            {
                Nm.CustomMessagingManager.RegisterNamedMessageHandler(TeamMessage.Name, HandleMessage);
                Nm.CustomMessagingManager.RegisterNamedMessageHandler(PoseMessageName, HandlePoseMessage);
            }
        }

        private void HandleMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string text);
            if (!TeamMessage.TryParse(text, out TeamMessage m))
            {
                Debug.LogWarning("[Team] Ignored a malformed message: " + text);
                return;
            }
            // The host relays our own actions back to us (by = our id); only the other id is the partner.
            if (m.By != null && m.By != session.SelfId) _partnerId = m.By;
            if (IsHost) HandleOnHost(sender, m); else HandleOnClient(m);
        }

        private void HandleOnHost(ulong sender, TeamMessage m)
        {
            switch (m.Kind)
            {
                case TeamMessageKind.ReqStart: RequestStart(m.Source); break;
                case TeamMessageKind.ReqArm: RequestArm(m.Armed); break;
                case TeamMessageKind.ReqBoardSuspect: RequestBoardSuspect(m.SuspectId, m.By); break;
                case TeamMessageKind.ReqBoardEvidence: RequestBoardEvidence(m.ClueId, m.Tagged, m.By); break;
                case TeamMessageKind.ReqEnd: RequestEnd(m.Reason); break;
                case TeamMessageKind.ReqFound: RequestClueFound(m.ClueId, m.By); break;
                case TeamMessageKind.ReqTag: RequestClueTagged(m.ClueId, m.Tagged, m.By); break;
                case TeamMessageKind.ReqSubmit: RequestSubmit(m.SuspectId, m.EvidenceIds, m.By); break;
                case TeamMessageKind.Contamination: session.ApplyPartnerContamination(m.ZoneId, m.Part, m.By); break;
                case TeamMessageKind.BriefingDone: session.SetPartnerBriefingDone(true); break;
                case TeamMessageKind.ReqBriefingReady:
                    _clientReady = m.Pose;
                    TryStartTeamBriefing();
                    break;
                case TeamMessageKind.ReqOwnership:
                    if (_clues.TryGetValue(m.ClueId, out NetworkClue clue) && clue.IsSpawned) clue.NetworkObject.ChangeOwnership(sender);
                    else Debug.LogWarning("[Team] Ownership request for unknown clue " + m.ClueId);
                    break;
                default:
                    Debug.LogWarning("[Team] Host ignored " + m.Kind);
                    break;
            }
        }

        private void HandleOnClient(TeamMessage m)
        {
            switch (m.Kind)
            {
                case TeamMessageKind.Start:
                    if (!session.ApplyTeamStart(m.CaseCode, m.ConditionCode, m.Source))
                    {
                        SetStatus("Host started the team run, but this headset couldn't: " + session.LastStartError);
                        SendToHost(TeamMessage.ReqEnd(SessionEndReason.Aborted));   // no one-sided team runs on the host
                    }
                    break;
                case TeamMessageKind.Arm:
                    if (m.Armed) session.AdoptTeamSetup(m.CaseCode);   // the host armed a team run: its case, condition B
                    if (!session.ApplyArm(m.Armed))
                        SetStatus("Host armed the team run, but this headset can't start: " + session.LastStartError);
                    break;
                case TeamMessageKind.End:
                    _applyingRemoteEnd = true;
                    try { session.EndSession(m.Reason); }
                    finally { _applyingRemoteEnd = false; }
                    break;
                case TeamMessageKind.Found: session.ApplyClueFound(m.ClueId, m.By); break;
                case TeamMessageKind.BoardSuspect: session.ApplyBoardSuspect(m.SuspectId, m.By); break;
                case TeamMessageKind.BoardEvidence: session.ApplyBoardEvidence(m.ClueId, m.Tagged, m.By); break;
                case TeamMessageKind.Tag: session.ApplyClueTagged(m.ClueId, m.Tagged, m.By); break;
                case TeamMessageKind.Submit: session.ApplySubmission(m.SuspectId, m.EvidenceIds, m.By); break;
                case TeamMessageKind.Contamination: session.ApplyPartnerContamination(m.ZoneId, m.Part, m.By); break;
                case TeamMessageKind.BriefingDone: session.SetPartnerBriefingDone(true); break;
                case TeamMessageKind.BriefingGo: session.ApplyBriefingGo(m.Spot); break;
                default:
                    Debug.LogWarning("[Team] Client ignored " + m.Kind);
                    break;
            }
        }

        /// <inheritdoc/>
        public void RequestBriefingReady(PlayerPose2D pose, string by)
        {
            if (!IsHost) { SendToHost(TeamMessage.ReqBriefingReady(by, pose)); return; }
            _hostReady = pose;
            TryStartTeamBriefing();
        }

        // Host: once both players tapped Begin, one shared spot for the detective, and both start at the same moment.
        private void TryStartTeamBriefing()
        {
            if (!_hostReady.HasValue || !_clientReady.HasValue) return;
            BriefingSpot spot = BriefingSpot.Compute(_hostReady.Value, _clientReady.Value);
            _hostReady = _clientReady = null;
            Broadcast(TeamMessage.BriefingGo(spot));
            session.ApplyBriefingGo(spot);
        }

        private void HandleArmedChanged(bool armed) => _hostReady = _clientReady = null;

        // Tell the partner this headset's briefing finished (both directions; their Start waits for it).
        private void HandleLocalBriefingFinished()
        {
            if (!IsActive) return;
            string msg = TeamMessage.BriefingDone(session.SelfId);
            if (IsHost) Broadcast(msg); else SendToHost(msg);
        }

        // Host: mirror every end except Submitted (the Submit message ends the client's run itself).
        private void HandleSessionEnded(SessionEndReason reason)
        {
            if (_applyingRemoteEnd || !IsHost || !IsActive || reason == SessionEndReason.Submitted) return;
            Broadcast(TeamMessage.End(reason));
        }

        private void Broadcast(string text)
        {
            NetworkManager nm = Nm;
            if (nm == null || !nm.IsServer || nm.CustomMessagingManager == null) return;
            foreach (ulong id in nm.ConnectedClientsIds)
            {
                if (id != nm.LocalClientId) Send(nm, id, text);
            }
        }

        private void SendToHost(string text)
        {
            NetworkManager nm = Nm;
            if (nm == null || !nm.IsConnectedClient || nm.CustomMessagingManager == null) return;
            Send(nm, NetworkManager.ServerClientId, text);
        }

        private static void Send(NetworkManager nm, ulong clientId, string text)
        {
            using (var writer = new FastBufferWriter(16 + text.Length * 2, Allocator.Temp))
            {
                writer.WriteValueSafe(text);
                nm.CustomMessagingManager.SendNamedMessage(TeamMessage.Name, clientId, writer);
            }
        }

        private void SetStatus(string text)
        {
            Debug.Log("[Team] " + text);
            onStatusChanged.Invoke(text);
        }
    }
}
