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

        /// <summary>Experimenter trigger. Returns false (and logs why) if it cannot start.</summary>
        public bool StartSession()
        {
            if (State.IsRunning)
            {
                Debug.LogWarning("[SessionManager] Session already running.");
                return false;
            }
            if (!config.Validate(out string error)) return RefuseStart("Invalid config: " + error);
            if (ActiveCase == null) return RefuseStart($"No CaseDefinition assigned for case {config.CaseCode}.");
            if (requirePlacement && Placement.Method == PlacementMethod.None)
                return RefuseStart("Place the scene first (A = anchor, X = manual).");
            if (!IsPlacementHeightOk) return RefuseStart(PlacementHeight.WrongMessage(PlacementHeightOffset));

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

        /// <summary>Experimenter start. Team mode: the host starts both headsets.</summary>
        public void RequestStart()
        {
            if (IsTeamMode) Authority.RequestStart();
            else StartSession();
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
        public void RequestClueReset(string clueId) => State.RecordClueReset(clueId, LocalActor);

        /// <summary>Answer from the evidence board. Team mode: the first submission (either partner) ends both runs.</summary>
        public void RequestSubmit(string suspectId, IReadOnlyList<string> evidenceIds)
        {
            if (!State.IsRunning || State.HasSubmitted) return;
            if (IsTeamMode) Authority.RequestSubmit(suspectId, evidenceIds, SelfId);
            else ApplySubmission(suspectId, evidenceIds, LocalActor);
        }

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
        public bool ApplyTeamStart(string caseCode, string conditionCode)
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
            return StartSession();
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
