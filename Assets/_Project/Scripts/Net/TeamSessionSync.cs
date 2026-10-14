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
    /// (server only, Runtime/Core/NetworkObject.cs:2279).
    /// </remarks>
    public class TeamSessionSync : MonoBehaviour, ICaseAuthority
    {
        [SerializeField] private NetworkSessionLauncher launcher;
        [SerializeField] private SessionManager session;

        [Tooltip("Team-run status for the experimenter (e.g. partner refused to start), wire to StatusLabel.Show.")]
        public UnityEvent<string> onStatusChanged = new UnityEvent<string>();

        private readonly Dictionary<string, NetworkClue> _clues = new Dictionary<string, NetworkClue>();
        private string _partnerId;
        private bool _applyingRemoteEnd;

        private NetworkManager Nm => launcher != null ? launcher.Manager : null;
        private bool IsHost => Nm != null && Nm.IsListening && Nm.IsServer;

        /// <inheritdoc/>
        public bool IsActive => launcher != null && launcher.HasPeer;

        private void Awake()
        {
            if (launcher == null) launcher = GetComponent<NetworkSessionLauncher>();
            if (session == null) session = FindFirstObjectByType<SessionManager>();
        }

        private void OnEnable()
        {
            if (session != null)
            {
                session.Authority = this;
                session.SessionEnded += HandleSessionEnded;
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
            }
            if (Nm != null)
            {
                Nm.OnServerStarted -= RegisterHandler;
                Nm.OnClientStarted -= RegisterHandler;
            }
        }

        // ---- ICaseAuthority ----

        public void RequestStart()
        {
            if (!IsHost) { SendToHost(TeamMessage.ReqStart()); return; }
            if (!session.StartSession())
            {
                SetStatus("Team start refused on the host: " + session.LastStartError);
                return;
            }
            var c = session.Config;
            Broadcast(TeamMessage.Start(c.CaseCode, c.ConditionCode));
        }

        public void RequestArm(bool armed)
        {
            if (!IsHost) { SendToHost(TeamMessage.ReqArm(armed)); return; }
            if (session.ApplyArm(armed)) Broadcast(TeamMessage.Arm(armed));
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
                Nm.CustomMessagingManager.RegisterNamedMessageHandler(TeamMessage.Name, HandleMessage);
        }

        private void HandleMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string text);
            if (!TeamMessage.TryParse(text, out TeamMessage m))
            {
                Debug.LogWarning("[Team] Ignored a malformed message: " + text);
                return;
            }
            if (m.By != null) _partnerId = m.By;
            if (IsHost) HandleOnHost(sender, m); else HandleOnClient(m);
        }

        private void HandleOnHost(ulong sender, TeamMessage m)
        {
            switch (m.Kind)
            {
                case TeamMessageKind.ReqStart: RequestStart(); break;
                case TeamMessageKind.ReqArm: RequestArm(m.Armed); break;
                case TeamMessageKind.ReqBoardSuspect: RequestBoardSuspect(m.SuspectId, m.By); break;
                case TeamMessageKind.ReqBoardEvidence: RequestBoardEvidence(m.ClueId, m.Tagged, m.By); break;
                case TeamMessageKind.ReqEnd: RequestEnd(m.Reason); break;
                case TeamMessageKind.ReqFound: RequestClueFound(m.ClueId, m.By); break;
                case TeamMessageKind.ReqTag: RequestClueTagged(m.ClueId, m.Tagged, m.By); break;
                case TeamMessageKind.ReqSubmit: RequestSubmit(m.SuspectId, m.EvidenceIds, m.By); break;
                case TeamMessageKind.Contamination: session.ApplyPartnerContamination(m.ZoneId, m.Part, m.By); break;
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
                    if (!session.ApplyTeamStart(m.CaseCode, m.ConditionCode))
                        SetStatus("Host started the team run, but this headset couldn't: " + session.LastStartError);
                    break;
                case TeamMessageKind.Arm:
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
                default:
                    Debug.LogWarning("[Team] Client ignored " + m.Kind);
                    break;
            }
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
