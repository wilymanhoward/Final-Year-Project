using System;
using System.Collections.Generic;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Owns the <see cref="CaseState"/> and the session timer. Start/stop/reset,
    /// time cap, answer submission. Other components only read state and call the Request* methods here.
    /// Team condition: while <see cref="Authority"/> is active (partner connected) every request is routed through it
    /// (host applies + mirrors); otherwise requests are applied locally. The Apply* methods are what the authority
    /// calls to change state. Put exactly one in the scene (e.g. on a "SessionSystems" GameObject).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SessionManager : MonoBehaviour
    {
        [SerializeField] private ExperimentConfig config = new ExperimentConfig();
        [SerializeField] private CaseDefinition caseX;
        [SerializeField] private CaseDefinition caseY;

        [Tooltip("Load the last used config from persistentDataPath on start (overrides the Inspector values).")]
        [SerializeField] private bool loadSavedConfig = true;

        [Tooltip("Refuse to start until the scene has been placed (anchor or manual). Turn off only for desk tests.")]
        [SerializeField] private bool requirePlacement = true;

        [Header("Placement height check")]
        [Tooltip("Case content root. Empty = found in the scene.")]
        [SerializeField] private CaseRoot caseRoot;
        [Tooltip("OVRCameraRig/TrackingSpace: with tracking origin Floor Level its height is the floor. Empty = found by name, else y 0.")]
        [SerializeField] private Transform floorReference;
        [Tooltip("Start is refused if CaseRoot is further than this from the floor.")]
        [SerializeField, Min(0.01f)] private float placementHeightTolerance = PlacementHeight.DefaultTolerance;

        private SessionTimer _timer;
        private readonly PauseTracker _pauses = new PauseTracker();

        /// <summary>Why the last StartSession call was refused (null after a successful start).</summary>
        public string LastStartError { get; private set; }

        public CaseState State { get; } = new CaseState();

        /// <summary>Team-condition router, set by the networking layer. Null or inactive = solo (apply locally).</summary>
        public ICaseAuthority Authority { get; set; }

        /// <summary>True while a partner is connected: requests go through <see cref="Authority"/>.</summary>
        public bool IsTeamMode => Authority != null && Authority.IsActive;

        /// <summary>This headset's participant id as sent over the network and written in by= fields.</summary>
        public string SelfId => ExperimentConfig.SanitizeCode(config.participantId);

        // Actor for changes made locally: our id in a team run, null (= no by= field) in a solo run.
        private string LocalActor => State.IsTeamRun ? SelfId : null;
        public ExperimentConfig Config => config;
        public CaseDefinition ActiveCase => config.caseId == CaseId.X ? caseX : caseY;
        public bool IsRunning => State.IsRunning;
        public DateTime StartUtc { get; private set; }
        public DateTime EndUtc { get; private set; }

        /// <summary>Where CaseRoot currently is and how it got there. Persists across runs.</summary>
        public PlacementRecord Placement { get; private set; } = PlacementRecord.None;

        /// <summary>Seconds since session start, live while running and frozen after it ends.</summary>
        public double ElapsedSeconds => _timer != null ? _timer.Elapsed(Now) : 0;

        /// <summary>Clock used for all session timing (unaffected by Time.timeScale).</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        public event Action SessionStarted;
        public event Action<SessionEndReason> SessionEnded;
        public event Action SessionReset;
        public event Action ConfigChanged;
        public event Action<PlacementRecord> PlacementChanged;

        /// <summary>Raised (only while running) when the app is paused / resumed. Pause stats are already in State.</summary>
        public event Action AppPaused;
        public event Action AppResumed;

        /// <summary>Raised (only while running) when the headset is taken off / put back on.</summary>
        public event Action HeadsetRemoved;
        public event Action HeadsetPutOn;

        /// <summary>
        /// Raised when a combined pause (app paused and/or headset off) ends, with its length in seconds.
        /// Overlapping sources count as one pause.
        /// </summary>
        public event Action<double> PauseEnded;

        /// <summary>Raised when the headset reset its tracking space (reported by the Meta adapter).</summary>
        public event Action TrackingRecentered;

        /// <summary>Raised when the depth occlusion status changes (reported by the Meta adapter).</summary>
        public event Action<DepthOcclusionStatus> DepthOcclusionChanged;

        /// <summary>Current depth occlusion status (Off until the Meta adapter reports).</summary>
        public DepthOcclusionStatus DepthOcclusion { get; private set; } = DepthOcclusionStatus.Off;

        /// <summary>Called by the Meta adapter (DepthOcclusionMonitor) with the current status; raises on change only.</summary>
        public void ReportDepthOcclusion(DepthOcclusionStatus status)
        {
            if (status == DepthOcclusion) return;
            DepthOcclusion = status;
            Debug.Log("[SessionManager] Depth occlusion: " + DepthOcclusionCode(status));
            DepthOcclusionChanged?.Invoke(status);
        }

        /// <summary>Log text of a <see cref="DepthOcclusionStatus"/> (status= of depth_occlusion rows).</summary>
        public static string DepthOcclusionCode(DepthOcclusionStatus status)
        {
            switch (status)
            {
                case DepthOcclusionStatus.Active: return "active";
                case DepthOcclusionStatus.Waiting: return "waiting";
                case DepthOcclusionStatus.NoPermission: return "no_permission";
                case DepthOcclusionStatus.Unsupported: return "unsupported";
                default: return "off";
            }
        }

        /// <summary>Tracking-space recenters reported since launch.</summary>
        public int RecenterCount { get; private set; }

        /// <summary>CaseRoot height above the tracking floor in metres (0 when there is no CaseRoot).</summary>
        public float PlacementHeightOffset
        {
            get
            {
                if (caseRoot == null) return 0f;
                float floorY = floorReference != null ? floorReference.position.y : 0f;
                return caseRoot.transform.position.y - floorY;
            }
        }

        /// <summary>True when nothing is placed or the placement sits on the floor (within tolerance).</summary>
        public bool IsPlacementHeightOk =>
            Placement.Method == PlacementMethod.None || PlacementHeight.IsOk(PlacementHeightOffset, placementHeightTolerance);

        /// <summary>Called by the Meta adapter when the tracking space is recentered.</summary>
        public void ReportTrackingRecentered()
        {
            RecenterCount++;
            Debug.Log($"[SessionManager] Tracking recentered (#{RecenterCount}); placement height {PlacementHeightOffset:+0.000;-0.000} m");
            TrackingRecentered?.Invoke();
        }

        private void Awake()
        {
            if (loadSavedConfig) ExperimentConfigStore.TryLoadInto(config);
            if (caseRoot == null) caseRoot = FindFirstObjectByType<CaseRoot>();
            if (floorReference == null)
            {
                var trackingSpace = GameObject.Find("TrackingSpace");
                if (trackingSpace != null) floorReference = trackingSpace.transform;
            }
            _timer = new SessionTimer(config.TimeCapSeconds);
        }

        private void Update()
        {
            if (!State.IsRunning) return;
            double now = Now;
            State.SetElapsed(_timer.Elapsed(now));
            // Headset off keeps Unity updating: keep the open pause current so Interrupted snapshots count it.
            if (_pauses.IsPaused) State.SetPauseStats(_pauses.PausedSeconds(now), _pauses.PauseCount);
            if (_timer.IsCapReached(now)) EndSession(SessionEndReason.TimeCap);
        }

        /// <summary>Replaces the config (only while no session is running) and saves it.</summary>
        public bool ApplyConfig(ExperimentConfig newConfig)
        {
            if (State.IsRunning || newConfig == null) return false;
            config = newConfig.Clone();
            ExperimentConfigStore.Save(config);
            ConfigChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Records a new placement of CaseRoot. Refused while a session is running, because logged
        /// CaseRoot-local positions would jump in the middle of a run.
        /// </summary>
        public bool SetPlacement(PlacementRecord record)
        {
            if (State.IsRunning || record == null) return false;
            Placement = record;
            Debug.Log($"[SessionManager] {record.EventName}: {record.FormatDetail()}");
            PlacementChanged?.Invoke(record);
            return true;
        }

        /// <summary>
        /// Why a run couldn't start right now (null if it could): config, condition vs. connection, case, placement,
        /// placement height. The condition is chosen explicitly (user decision, session 6): B needs a connected
        /// partner, A must have none.
        /// </summary>
        public string CheckCanStart()
        {
            if (State.IsRunning) return "A session is already running.";
            if (!config.Validate(out string error)) return "Invalid config: " + error;
            string mismatch = ConditionMismatch(config.condition, IsTeamMode);
            if (mismatch != null) return mismatch;
            if (ActiveCase == null) return $"No CaseDefinition assigned for case {config.CaseCode}.";
            if (requirePlacement && Placement.Method == PlacementMethod.None)
                return "Place the scene first (A = anchor, X = manual).";
            if (!IsPlacementHeightOk) return PlacementHeight.WrongMessage(PlacementHeightOffset);
            return null;
        }

        /// <summary>Null if the condition fits the connection, else the reason (pure, unit tested).</summary>
        public static string ConditionMismatch(StudyCondition condition, bool partnerConnected)
        {
            if (condition == StudyCondition.B && !partnerConnected)
                return "Condition B needs a connected partner (Host / Join first).";
            if (condition == StudyCondition.A && partnerConnected)
                return "Condition A is solo - stop the network first (or choose B).";
            return null;
        }

        /// <summary>Team client: take the host's case and condition B before arming. Not during a run.</summary>
        public void AdoptTeamSetup(string caseCode)
        {
            if (State.IsRunning) return;
            var c = config.Clone();
            c.condition = StudyCondition.B;
            if (Enum.TryParse(caseCode, out CaseId caseId)) c.caseId = caseId;
            if (c.condition == config.condition && c.caseId == config.caseId) return;
            Debug.Log($"[SessionManager] Team setup from the host: condition B, case {c.CaseCode}");
            ApplyConfig(c);
        }

        // ---- Arming (Phase C): the experimenter arms, the participant's Start card starts the run ----

        /// <summary>True when the experimenter has armed the next run and the participant may press Start.</summary>
        public bool IsArmed { get; private set; }

        /// <summary>Raised when <see cref="IsArmed"/> changes.</summary>
        public event Action<bool> ArmedChanged;

        /// <summary>Experimenter: arm (or disarm) the next run. Team mode: arms/disarms both headsets via the host.</summary>
        public void RequestArm(bool armed)
        {
            if (IsTeamMode) Authority.RequestArm(armed);
            else ApplyArm(armed);
        }

        /// <summary>Sets the armed state. Arming is refused (LastStartError says why) if the run couldn't start.</summary>
        public bool ApplyArm(bool armed)
        {
            if (armed)
            {
                string reason = CheckCanStart();
                if (reason != null) return RefuseStart(reason);
                LastStartError = null;
            }
            SetArmed(armed);
            return true;
        }

        /// <summary>Participant's Start button: starts the run only if the experimenter armed it.</summary>
        public void RequestParticipantStart()
        {
            if (!IsArmed || State.IsRunning) return;
            RequestStart(StartSource.ParticipantStart);
        }

        /// <summary>When the current arming happened (UTC); DateTime.MinValue when not armed.</summary>
        public DateTime ArmedUtc { get; private set; } = DateTime.MinValue;

        /// <summary>How the current/last run was started (logged in session_start and the meta file).</summary>
        public StartSource LastStartSource { get; private set; }

        /// <summary>Arming time of the current/last run (MinValue if it was started without being armed).</summary>
        public DateTime RunArmedUtc { get; private set; } = DateTime.MinValue;

        /// <summary>start_source code as written to logs.</summary>
        public static string StartSourceCode(StartSource source) =>
            source == StartSource.ParticipantStart ? "participant_start" : "start_now";

        private void SetArmed(bool armed)
        {
            if (IsArmed == armed) return;
            IsArmed = armed;
            ArmedUtc = armed ? DateTime.UtcNow : DateTime.MinValue;
            Debug.Log("[SessionManager] " + (armed ? "Armed - waiting for the participant's Start" : "Disarmed"));
            ArmedChanged?.Invoke(armed);
        }

        /// <summary>Experimenter trigger (or the participant's Start when armed). Returns false (and logs why) if it cannot start.</summary>
        public bool StartSession(StartSource source = StartSource.StartNow)
        {
            if (State.IsRunning)
            {
                Debug.LogWarning("[SessionManager] Session already running.");
                return false;
            }
            string refusal = CheckCanStart();
            if (refusal != null) return RefuseStart(refusal);

            RunArmedUtc = ArmedUtc;   // kept for the run_armed row; cleared by SetArmed(false)
            LastStartSource = source;
            SetArmed(false);
            LastStartError = null;
            ExperimentConfigStore.Save(config);
            _timer.CapSeconds = config.TimeCapSeconds;
            _pauses.Reset();
            _timer.Start(Now);
            StartUtc = DateTime.UtcNow;
            State.Begin(IsTeamMode, SelfId);
            Debug.Log($"[SessionManager] Session started: {config.pairCode} {config.participantId} {config.ConditionCode} {config.CaseCode}" +
                      (State.IsTeamRun ? " (team)" : ""));
            SessionStarted?.Invoke();
            return true;
        }

        /// <summary>Stops the timer and ends the run. Safe to call when not running.</summary>
        public void EndSession(SessionEndReason reason)
        {
            if (!State.IsRunning) return;
            double now = Now;
            double elapsed = _timer.Stop(now);
            _pauses.EndAll(now);   // closes a pause still open (e.g. quit while paused or with the headset off)
            State.SetPauseStats(_pauses.PausedSeconds(now), _pauses.PauseCount);
            EndUtc = DateTime.UtcNow;
            State.End(reason, elapsed);
            Debug.Log($"[SessionManager] Session ended: {reason}, {elapsed:F1}s");
            SessionEnded?.Invoke(reason);
        }

        /// <summary>Records a checked answer and ends the session (timer stops on submit).</summary>
        public bool SubmitAnswer(string suspectId, IReadOnlyList<string> evidenceIds, AnswerResult result, string by = null)
        {
            if (!State.RecordSubmission(suspectId, evidenceIds, result, by)) return false;
            EndSession(SessionEndReason.Submitted);
            return true;
        }

        // ---- Requests (components call these; routed through the authority in team mode) ----

        /// <summary>Experimenter start (or participant start). Team mode: the host starts both headsets.</summary>
        public void RequestStart(StartSource source = StartSource.StartNow)
        {
            if (IsTeamMode) Authority.RequestStart(source);
            else StartSession(source);
        }

        /// <summary>Experimenter abort. Team mode: ends the run on both headsets.</summary>
        public void RequestAbort()
        {
            if (IsTeamMode && State.IsRunning) Authority.RequestEnd(SessionEndReason.Aborted);
            else EndSession(SessionEndReason.Aborted);
        }

        public void RequestClueFound(string clueId)
        {
            if (!State.IsRunning || State.IsClueFound(clueId)) return;
            if (IsTeamMode) Authority.RequestClueFound(clueId, SelfId);
            else State.MarkClueFound(clueId, LocalActor);
        }

        public void RequestClueTagged(string clueId, bool tagged)
        {
            if (!State.IsRunning || State.IsClueTagged(clueId) == tagged) return;
            if (IsTeamMode) Authority.RequestClueTagged(clueId, tagged, SelfId);
            else ApplyClueTagged(clueId, tagged, LocalActor);
        }

        /// <summary>Contamination of this headset's own body: always counted here, and reported to the partner.</summary>
        public void RequestContamination(string zoneId, BodyPart part)
        {
            if (State.RecordContamination(zoneId, part) && IsTeamMode) Authority.ReportContamination(zoneId, part, SelfId);
        }

        /// <summary>A clue this headset controls was put back home (logging only, never networked).</summary>
        public void RequestClueReset(string clueId, ClueResetReason reason = ClueResetReason.OutOfBounds) =>
            State.RecordClueReset(clueId, LocalActor, ClueBounds.ReasonText(reason));

        /// <summary>Answer from the evidence board. Team mode: the first submission (either partner) ends both runs.</summary>
        public void RequestSubmit(string suspectId, IReadOnlyList<string> evidenceIds)
        {
            if (!State.IsRunning || State.HasSubmitted) return;
            if (IsTeamMode) Authority.RequestSubmit(suspectId, evidenceIds, SelfId);
            else ApplySubmission(suspectId, evidenceIds, LocalActor);
        }

        /// <summary>Evidence board: choose the suspect (team: shared board, through the host).</summary>
        public void RequestBoardSuspect(string suspectId)
        {
            if (!State.IsRunning || State.HasSubmitted || State.BoardSuspectId == suspectId) return;
            if (IsTeamMode) Authority.RequestBoardSuspect(suspectId, SelfId);
            else State.SetBoardSuspect(suspectId, LocalActor);
        }

        /// <summary>Evidence board: add/remove a tagged clue (team: shared board, through the host).</summary>
        public void RequestBoardEvidence(string clueId, bool selected)
        {
            if (!State.IsRunning || State.HasSubmitted || State.IsBoardEvidenceSelected(clueId) == selected) return;
            if (IsTeamMode) Authority.RequestBoardEvidence(clueId, selected, SelfId);
            else State.SetBoardEvidence(clueId, selected, LocalActor);
        }

        public bool ApplyBoardSuspect(string suspectId, string by) => State.SetBoardSuspect(suspectId, by);
        public bool ApplyBoardEvidence(string clueId, bool selected, string by) => State.SetBoardEvidence(clueId, selected, by);

        /// <summary>The partner grabbed a clue this headset was holding.</summary>
        public void ReportClueGrabTaken(string clueId, string by) => State.RecordClueGrabTaken(clueId, by);

        // ---- Apply (called by the authority on the host, and on the client when the host's message arrives) ----

        public bool ApplyClueFound(string clueId, string by) => State.MarkClueFound(clueId, by);

        public bool ApplyClueTagged(string clueId, bool tagged, string by) =>
            tagged ? State.MarkClueTagged(clueId, by) : State.UntagClue(clueId, by);

        public bool ApplyPartnerContamination(string zoneId, BodyPart part, string by) =>
            State.RecordPartnerContamination(zoneId, part, by);

        /// <summary>Checks the answer against the active case on this headset, records it and ends the run.</summary>
        public bool ApplySubmission(string suspectId, IReadOnlyList<string> evidenceIds, string by)
        {
            CaseDefinition caseDef = ActiveCase;
            if (caseDef == null)
            {
                Debug.LogError("[SessionManager] No active CaseDefinition; cannot check the answer.");
                return false;
            }
            var evidence = new List<string>(evidenceIds ?? Array.Empty<string>());
            return SubmitAnswer(suspectId, evidence, caseDef.Check(suspectId, evidence), by);
        }

        /// <summary>
        /// Client side of a team start: takes the host's case and condition for this run (saved to the config), then
        /// starts. Returns false (LastStartError says why) if this headset can't start.
        /// </summary>
        public bool ApplyTeamStart(string caseCode, string conditionCode, StartSource source)
        {
            if (State.IsRunning) return false;
            var c = config.Clone();
            if (Enum.TryParse(caseCode, out CaseId caseId)) c.caseId = caseId;
            if (Enum.TryParse(conditionCode, out StudyCondition condition)) c.condition = condition;
            if (c.caseId != config.caseId || c.condition != config.condition)
            {
                Debug.Log($"[SessionManager] Team start: using the host's case {caseCode} / condition {conditionCode}");
                ApplyConfig(c);
            }
            return StartSession(source);
        }

        /// <summary>Aborts a running session (logged as Aborted) and clears state for the next run.</summary>
        public void ResetSession()
        {
            EndSession(SessionEndReason.Aborted);
            _timer.Reset();
            State.Reset();
            SessionReset?.Invoke();
        }

        private bool RefuseStart(string reason)
        {
            LastStartError = reason;
            Debug.LogError("[SessionManager] Not started: " + reason);
            return false;
        }

        /// <summary>
        /// Called by the headset-presence adapter (HeadsetPresenceMonitor, from OVRManager.HMDMounted/HMDUnmounted).
        /// While the headset is off the run counts as paused; samples keep being logged.
        /// </summary>
        public void SetHeadsetWorn(bool worn)
        {
            if (!ChangePauseSource(PauseSource.HeadsetOff, !worn)) return;
            if (worn) HeadsetPutOn?.Invoke(); else HeadsetRemoved?.Invoke();
        }

        // App pause = app in background (Unity stops updating, the clock keeps running).
        // The total time keeps counting; paused time is tracked separately so active time can be analysed.
        private void OnApplicationPause(bool paused)
        {
            if (!ChangePauseSource(PauseSource.App, paused)) return;
            if (paused) AppPaused?.Invoke(); else AppResumed?.Invoke();
        }

        /// <summary>Updates one pause source and State's pause stats. False if not running or nothing changed.</summary>
        private bool ChangePauseSource(PauseSource source, bool active)
        {
            if (!State.IsRunning) return false;
            double now = Now;
            if (!_pauses.SetActive(source, active, now, out _, out bool pauseEnded)) return false;
            State.SetElapsed(_timer.Elapsed(now));
            State.SetPauseStats(_pauses.PausedSeconds(now), _pauses.PauseCount);
            Debug.Log($"[SessionManager] {source} {(active ? "on" : "off")}: pauses={_pauses.PauseCount}, paused_s={State.PausedSeconds:F1}");
            if (pauseEnded) PauseEnded?.Invoke(_pauses.LastPauseSeconds);
            return true;
        }

        private void OnApplicationQuit()
        {
            EndSession(SessionEndReason.Interrupted);
        }
    }
}
