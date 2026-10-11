using System;
using System.Collections.Generic;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Owns the <see cref="CaseState"/> and the session timer. Start/stop/reset,
    /// time cap, answer submission. Other components only read state and call
    /// the public methods here or on <see cref="State"/>.
    /// Put exactly one in the scene (e.g. on a "SessionSystems" GameObject).
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

        private SessionTimer _timer;
        private readonly PauseTracker _pauses = new PauseTracker();

        /// <summary>Why the last StartSession call was refused (null after a successful start).</summary>
        public string LastStartError { get; private set; }

        public CaseState State { get; } = new CaseState();
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

        private void Awake()
        {
            if (loadSavedConfig) ExperimentConfigStore.TryLoadInto(config);
            _timer = new SessionTimer(config.TimeCapSeconds);
        }

        private void Update()
        {
            if (!State.IsRunning) return;
            double now = Now;
            State.SetElapsed(_timer.Elapsed(now));
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

            LastStartError = null;
            ExperimentConfigStore.Save(config);
            _timer.CapSeconds = config.TimeCapSeconds;
            _pauses.Reset();
            _timer.Start(Now);
            StartUtc = DateTime.UtcNow;
            State.Begin();
            Debug.Log($"[SessionManager] Session started: {config.pairCode} {config.participantId} {config.ConditionCode} {config.CaseCode}");
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
        public bool SubmitAnswer(string suspectId, IReadOnlyList<string> evidenceIds, AnswerResult result)
        {
            if (!State.RecordSubmission(suspectId, evidenceIds, result)) return false;
            EndSession(SessionEndReason.Submitted);
            return true;
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
